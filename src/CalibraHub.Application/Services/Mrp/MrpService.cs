using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Mrp;

/// <summary>
/// MRP motoru — Faz 2 (2026-08-29). Açık satış siparişi satırlarından net ihtiyaç hesaplar,
/// malzeme kartındaki kırılım politikasına göre gruplar ve planlı iş emirleri üretir.
///
/// <para><b>Sanal tahsis:</b> koşu, eldeki stoğu / açık arzı ihtiyaçlara in-memory dağıtır ve
/// bu dağıtımı HİÇBİR YERE yazmaz. Bir sonraki koşu sıfırdan hesaplar; kalıcı olan tek şey
/// açık iş emirleri ve sipariş bağlarıdır. Kullanıcının FİİLEN yaptığı rezervasyonlar ise
/// talebi düşürür — rezerve miktar için iş emri açılmaz.</para>
///
/// <para><b>Havuz paylaşımı KRİTİK:</b> <c>_available</c> / <c>_openWoSupply</c> /
/// <c>_openPoSupply</c> koşu boyunca TEK örnektir ve tahsis ettikçe azalır. Her ihtiyaç için
/// yeniden okunsaydı aynı 100 kg hammadde üç ayrı emre birden "yeterli" görünürdü.</para>
///
/// <para><b>Sessiz atlama YASAK</b> (CLAUDE.md #3): talebi sıfırlanan, üretilemeyen, deposu
/// çözülemeyen her satır <c>MrpRunLine</c> olarak gerekçesiyle raporlanır.</para>
/// </summary>
public sealed class MrpService : IMrpService
{
    private readonly IMrpRepository _repo;
    private readonly IWorkOrderService _workOrders;
    private readonly IWorkOrderRepository _workOrderRepo;
    private readonly IRoutingService _routings;
    private readonly IOperationMachineTimeService _machineTimes;
    private readonly IMachineCalendarRepository _calendar;
    private readonly ILogisticsConfigurationRepository _logisticsConfig;
    /// <summary>Satın Alma Talebi önerisi (Faz 4) — opsiyonel; kayıtlı değilse öneri atlanır ve gerekçe bildirilir.</summary>
    private readonly IDocumentService? _documents;
    private readonly IDocumentTypeRepository? _documentTypes;
    private readonly IAuditTrailService? _audit;
    private readonly ILogger<MrpService>? _logger;

    public MrpService(
        IMrpRepository repo,
        IWorkOrderService workOrders,
        IWorkOrderRepository workOrderRepo,
        IRoutingService routings,
        IOperationMachineTimeService machineTimes,
        IMachineCalendarRepository calendar,
        ILogisticsConfigurationRepository logisticsConfig,
        IDocumentService? documents = null,
        IDocumentTypeRepository? documentTypes = null,
        IAuditTrailService? audit = null,
        ILogger<MrpService>? logger = null)
    {
        _repo = repo;
        _workOrders = workOrders;
        _workOrderRepo = workOrderRepo;
        _routings = routings;
        _machineTimes = machineTimes;
        _calendar = calendar;
        _logisticsConfig = logisticsConfig;
        _documents = documents;
        _documentTypes = documentTypes;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MrpOpenOrderLineDto>> ListOpenOrderLinesAsync(
        int? documentId, string? search, CancellationToken ct)
        => _repo.ListOpenSalesOrderLinesAsync(null, documentId, search, ct);

    /// <summary>Reçete patlatma derinlik sınırı — ExplodeBOMAsync ile aynı (döngü/derin ağaç koruması).</summary>
    private const int MaxBomLevel = 20;

    /// <summary>Bu kadar günden eski, uygulanmamış (Draft) koşular önizleme başında silinir.</summary>
    private const int StaleDraftRunDays = 30;

    /// <summary>Koşu satırının yanında taşınan görüntü alanları (DB'de saklanmaz — tek kaynak Items).</summary>
    private readonly record struct NodeExtra(
        string Code, string Name, string? UnitCode, string Policy, int? LocationId, string? TargetNo);

    // ── Planlama grubu: aynı iş emrinde birleşecek talep kümesi ────────────────────────
    private sealed class PlanGroup
    {
        public required int ItemId { get; init; }
        public int? ConfigId { get; init; }
        public string ItemCode { get; init; } = string.Empty;
        public string ItemName { get; init; } = string.Empty;
        public string? UnitCode { get; init; }
        public int? UnitId { get; init; }
        public int? LocationId { get; init; }
        public string SplitPolicy { get; init; } = WorkOrderSplitPolicyCatalog.Default;
        public bool IsProducible { get; init; }
        public int Level { get; init; }
        public decimal Gross { get; set; }
        public DateTime? DueDate { get; set; }
        /// <summary>Bu emrin planlanan başlangıcı = bileşenlerinin hazır olması gereken an.</summary>
        public DateTime? PlannedStartForChildren { get; set; }
        public List<MrpPegDto> Pegs { get; } = [];
    }

    /// <summary>
    /// Gruplama anahtarı — malzemenin KENDİ politikasına göre. Alt seviyede de aynı üç seçenek
    /// geçerlidir; "sipariş"/"sipariş satırı" alt seviyede KÖK satış siparişini işaret eder,
    /// böylece her alt emirde kaynak sipariş izi korunur (kullanıcı kuralı 3).
    /// </summary>
    private static string GroupKey(WorkOrderSplitPolicy policy, int itemId, int? configId, int documentId, int lineId)
        => policy switch
        {
            WorkOrderSplitPolicy.Cumulative => $"C|{itemId}|{configId}",
            WorkOrderSplitPolicy.PerOrder   => $"O|{itemId}|{configId}|{documentId}",
            _                               => $"L|{itemId}|{configId}|{lineId}",
        };

    /// <summary>İki mesajı birleştirir (biri boşsa diğeri) — uyarılar birbirini ezmesin.</summary>
    private static string? Join(string? a, string? b)
        => string.IsNullOrWhiteSpace(a) ? b : string.IsNullOrWhiteSpace(b) ? a : a + " " + b;

    /// <inheritdoc />
    public async Task<MrpPreviewResult> PreviewAsync(MrpPreviewRequest request, int? userId, CancellationToken ct)
    {
        var lineIds = (request?.LineIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList();
        if (lineIds.Count == 0)
            return Fail("En az bir sipariş satırı seçilmelidir.");

        // Terk edilmiş önizlemeleri temizle (uygulanmamış Draft koşular). Applied olanlar
        // "bu emir neden açıldı" izidir, dokunulmaz. Temizlik başarısız olursa koşu devam eder.
        try { await _repo.PurgeStaleDraftRunsAsync(StaleDraftRunDays, ct); }
        catch (Exception ex) { _logger?.LogWarning(ex, "[MRP] Eski taslak koşular temizlenemedi."); }

        var lines = await _repo.ListOpenSalesOrderLinesAsync(lineIds, null, null, ct);
        if (lines.Count == 0)
            return Fail("Seçilen satırlar artık açık değil (teslim edilmiş veya belge iptal edilmiş olabilir).");

        var records = new List<MrpRunLineRecord>();
        var nodeExtras = new List<NodeExtra>();

        // ── A) TALEP (seviye 0) ───────────────────────────────────────────────────────
        // demand = açık miktar − o satıra fiilen rezerve edilmiş − zaten iş emrine tahsis edilmiş
        var groups = new Dictionary<string, PlanGroup>(StringComparer.Ordinal);

        foreach (var l in lines)
        {
            var demand = l.OpenQuantity - l.ReservedQuantity - l.AllocatedQuantity;

            if (demand <= 0.0001m)
            {
                records.Add(Info(l, MrpActionTypes.CoveredByStock, 0m,
                    $"Talep kalmadı — açık {F(l.OpenQuantity)}, rezerve {F(l.ReservedQuantity)}, iş emrine tahsis {F(l.AllocatedQuantity)}."));
                nodeExtras.Add(new NodeExtra(l.ItemCode, l.ItemName, l.UnitCode, l.SplitPolicy, l.LocationId, null));
                continue;
            }

            if (!l.IsProducible)
            {
                records.Add(Info(l, MrpActionTypes.Shortage, demand,
                    "Malzeme üretilebilir tipte değil (Mamul/Yarı Mamul) — iş emri açılamaz."));
                nodeExtras.Add(new NodeExtra(l.ItemCode, l.ItemName, l.UnitCode, l.SplitPolicy, l.LocationId, null));
                continue;
            }

            if (l.LocationId is not > 0)
            {
                records.Add(Info(l, MrpActionTypes.Shortage, demand,
                    "Depo belirlenemedi (sipariş kaleminde ve başlığında depo yok) — stok karşılaştırması yapılamaz."));
                nodeExtras.Add(new NodeExtra(l.ItemCode, l.ItemName, l.UnitCode, l.SplitPolicy, l.LocationId, null));
                continue;
            }

            // ── C) GRUPLAMA — malzemenin KENDİ politikası belirler ──
            var policy = WorkOrderSplitPolicyCatalog.Parse(l.SplitPolicy);
            var key = GroupKey(policy, l.ItemId, l.ConfigId, l.DocumentId, l.LineId);

            if (!groups.TryGetValue(key, out var g))
            {
                g = new PlanGroup
                {
                    ItemId = l.ItemId,
                    ConfigId = l.ConfigId,
                    ItemCode = l.ItemCode,
                    ItemName = l.ItemName,
                    UnitCode = l.UnitCode,
                    UnitId = l.UnitId,
                    LocationId = l.LocationId,
                    SplitPolicy = policy.ToString(),
                    IsProducible = true,
                    Level = 0,
                };
                groups[key] = g;
            }
            g.Gross += demand;
            // Grubun teslim tarihi EN ERKEN olanıdır — birleşen emir, en acil siparişe yetişmeli.
            if (l.DeliveryDate is not null && (g.DueDate is null || l.DeliveryDate < g.DueDate))
                g.DueDate = l.DeliveryDate;
            g.Pegs.Add(new MrpPegDto(l.DocumentId, l.DocumentNumber, l.LineId, demand));
        }

        // ── ARZ HAVUZLARI (koşu boyunca TEK örnek, tahsis ettikçe azalır) ─────────────
        // Seviyeler arasında PAYLAŞILIR: yarı mamulün stoğu hem mamul talebini hem başka bir
        // üstün talebini karşılayabilir; her seviyede sıfırdan okunsaydı aynı stok birden çok
        // kez "yeterli" görünürdü.
        var available = new Dictionary<(int ItemId, int LocationId), decimal>();
        var openWoSupply = new Dictionary<int, decimal>();
        var openPoSupply = new Dictionary<int, decimal>();
        var mergeCandidates = new Dictionary<int, List<MrpOpenWorkOrderRow>>();
        var supplyLoaded = new HashSet<int>();             // malzeme bazında "havuz okundu"
        var availLoaded = new HashSet<(int, int)>();       // (malzeme, depo) bazında

        async Task EnsureSupplyAsync(IEnumerable<PlanGroup> gs)
        {
            var newItems = gs.Select(x => x.ItemId).Where(id => supplyLoaded.Add(id)).Distinct().ToList();
            var newKeys = gs.Where(x => x.LocationId is > 0)
                            .Select(x => (x.ItemId, x.LocationId!.Value))
                            .Where(k => availLoaded.Add(k)).Distinct().ToList();

            if (newKeys.Count > 0)
                foreach (var r in await _repo.GetAvailabilityAsync(newKeys, ct))
                    available[(r.ItemId, r.LocationId)] = Math.Max(0m, r.Available);

            if (newItems.Count > 0)
            {
                var openWoRows = await _repo.GetOpenWorkOrdersAsync(newItems, ct);
                foreach (var w in openWoRows)
                {
                    // Arz = emrin henüz ÜRETİLMEMİŞ ve henüz bir siparişe BAĞLANMAMIŞ kısmı.
                    // İkisinin küçüğü: üretilmiş kısım stoğa girmiştir (available'da sayılı),
                    // bağlanmış kısım başka siparişin ihtiyacını karşılar.
                    var usable = Math.Max(0m, Math.Min(w.RemainingQuantity, w.UnpeggedQuantity));
                    if (usable > 0m)
                        openWoSupply[w.ItemId] = openWoSupply.TryGetValue(w.ItemId, out var acc) ? acc + usable : usable;

                    if (!mergeCandidates.TryGetValue(w.ItemId, out var cl))
                        mergeCandidates[w.ItemId] = cl = [];
                    cl.Add(w);
                }
                foreach (var kv in mergeCandidates)
                    kv.Value.Sort((a, b) =>
                    {
                        var c = (a.PlannedEndDate ?? DateTime.MaxValue).CompareTo(b.PlannedEndDate ?? DateTime.MaxValue);
                        return c != 0 ? c : a.WorkOrderId.CompareTo(b.WorkOrderId);
                    });

                foreach (var r in await _repo.GetOpenPurchaseSupplyAsync(newItems, ct))
                    openPoSupply[r.ItemId] = openPoSupply.TryGetValue(r.ItemId, out var p) ? p + r.OpenQuantity : r.OpenQuantity;
            }
        }

        // ── TARİHLEME (Faz 3) ────────────────────────────────────────────────────────
        var leadTime = new MrpLeadTimeCalculator(_workOrderRepo, _routings, _machineTimes, _calendar, _logger);
        var walker = await leadTime.GetWalkerAsync(ct);

        var mergeUsed = new Dictionary<int, decimal>();     // workOrderId → bu koşuda eklenen
        var visitedItems = new HashSet<int>();              // döngüsel reçete koruması

        // ── SEVİYE DÖNGÜSÜ (Faz 4) ───────────────────────────────────────────────────
        // Her seviye planlandıktan sonra, açılması kararlaştırılan emirlerin reçete bileşenleri
        // bir sonraki seviyenin talebi olur. Ara seviyede NET hesaplanır: yarı mamulün eldeki
        // stoğu, onun altındaki hammadde ihtiyacını düşürür (düzleştirilmiş patlatmanın
        // yapamadığı şey — bkz. ExplodeBOMAsync neden kullanılmadı).
        var current = groups.Values.ToList();
        for (var level = 0; level < MaxBomLevel && current.Count > 0; level++)
        {
            await EnsureSupplyAsync(current);

            // EDD (en erken teslim önce): kıt stok en acil siparişe gitmeli.
            var ordered = current
                .OrderBy(g => g.DueDate ?? DateTime.MaxValue)
                .ThenBy(g => g.ItemCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var planned = new List<(PlanGroup Group, int RecordIndex, decimal Net)>();

            foreach (var g in ordered)
            {
                var locKey = (g.ItemId, g.LocationId!.Value);
                var onHandPool = available.TryGetValue(locKey, out var av) ? av : 0m;
                var woPool     = openWoSupply.TryGetValue(g.ItemId, out var wp) ? wp : 0m;
                var poPool     = openPoSupply.TryGetValue(g.ItemId, out var pp) ? pp : 0m;

                var remaining  = g.Gross;
                var onHandUsed = Math.Min(remaining, onHandPool); remaining -= onHandUsed;
                var woUsed     = Math.Min(remaining, woPool);     remaining -= woUsed;
                var poUsed     = Math.Min(remaining, poPool);     remaining -= poUsed;

                available[locKey]      = onHandPool - onHandUsed;
                openWoSupply[g.ItemId] = woPool - woUsed;
                openPoSupply[g.ItemId] = poPool - poUsed;

                var supplyUsed = woUsed + poUsed;
                var net = Math.Round(Math.Max(0m, remaining), 4);
                var pegJson = JsonSerializer.Serialize(g.Pegs);

                if (net <= 0.0001m)
                {
                    records.Add(new MrpRunLineRecord(
                        Id: 0, Level: g.Level, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                        ActionType: MrpActionTypes.CoveredByStock,
                        GrossQuantity: g.Gross, OnHandApplied: onHandUsed, OpenSupplyApplied: supplyUsed,
                        NetQuantity: 0m, PlannedStartDate: null, PlannedEndDate: g.DueDate,
                        TargetWorkOrderId: null, PegJson: pegJson, CreatedWorkOrderId: null,
                        CreatedDocumentId: null,
                        Message: $"İhtiyaç mevcut arzdan karşılanıyor — eldeki {F(onHandUsed)}, açık arz {F(supplyUsed)}.",
                        LocationId: g.LocationId));
                    nodeExtras.Add(new NodeExtra(g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, null));
                    continue;
                }

                // ── Üretilemeyen malzeme: Satın Alma Talebi önerilir (kullanıcı kuralı 4) ──
                if (!g.IsProducible)
                {
                    records.Add(new MrpRunLineRecord(
                        Id: 0, Level: g.Level, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                        ActionType: MrpActionTypes.PurchaseRequest,
                        GrossQuantity: g.Gross, OnHandApplied: onHandUsed, OpenSupplyApplied: supplyUsed,
                        NetQuantity: net, PlannedStartDate: null, PlannedEndDate: g.DueDate,
                        TargetWorkOrderId: null, PegJson: pegJson, CreatedWorkOrderId: null,
                        CreatedDocumentId: null,
                        Message: "Satın alınması gereken malzeme — Satın Alma Talebi önerildi.",
                        LocationId: g.LocationId));
                    nodeExtras.Add(new NodeExtra(g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, null));
                    continue;
                }

                // ── Mevcut açık emre bağlama (kullanıcı kuralı 7) ──
                MrpOpenWorkOrderRow? target = null;
                if (mergeCandidates.TryGetValue(g.ItemId, out var cands))
                {
                    target = cands.FirstOrDefault(w =>
                        w.ConfigId == g.ConfigId
                        && w.RemainingQuantity > 0.0001m
                        && (w.UnpeggedQuantity - (mergeUsed.TryGetValue(w.WorkOrderId, out var mu) ? mu : 0m)) > 0.0001m
                        && (w.PlannedEndDate is null || g.DueDate is null || w.PlannedEndDate <= g.DueDate));
                }

                // ── TARİHLEME ──
                DateTime? plannedStart = null;
                string? dateMessage = null;
                if (g.DueDate is null)
                {
                    dateMessage = "Teslim tarihi yok — planlanan bitiş boş bırakıldı.";
                }
                else
                {
                    var lt = await leadTime.ResolveMinutesAsync(g.ItemId, g.ConfigId, net, ct);
                    if (lt.Minutes is not > 0m) { plannedStart = g.DueDate; dateMessage = lt.Reason; }
                    else if (!walker.HasCalendar)
                    {
                        plannedStart = g.DueDate;
                        dateMessage = "Çalışma takvimi tanımlı değil — başlangıç tarihi bitişe eşitlendi "
                                    + $"(hesaplanan üretim süresi {F(lt.Minutes.Value / 60m)} saat).";
                    }
                    else
                    {
                        plannedStart = walker.WalkBackward(g.DueDate.Value, lt.Minutes.Value);
                        // Geçmişe düşen başlangıç SESSİZCE bugüne çekilmez.
                        if (plannedStart < DateTime.Now.Date)
                            dateMessage = $"Geç kalınmış: hesaplanan başlangıç geçmişte ({plannedStart:dd.MM.yyyy}).";
                    }
                }

                if (target is not null)
                {
                    mergeUsed[target.WorkOrderId] = (mergeUsed.TryGetValue(target.WorkOrderId, out var prev) ? prev : 0m) + net;
                    var mergeMsg = target.Status == 1
                        ? "Yayımlanmış emre ekleniyor — operasyon süreleri yeniden hesaplanmalı."
                        : null;
                    records.Add(new MrpRunLineRecord(
                        Id: 0, Level: g.Level, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                        ActionType: MrpActionTypes.MergeWorkOrder,
                        GrossQuantity: g.Gross, OnHandApplied: onHandUsed, OpenSupplyApplied: supplyUsed,
                        NetQuantity: net, PlannedStartDate: plannedStart, PlannedEndDate: g.DueDate,
                        TargetWorkOrderId: target.WorkOrderId, PegJson: pegJson,
                        CreatedWorkOrderId: null, CreatedDocumentId: null,
                        Message: Join(mergeMsg, dateMessage), LocationId: g.LocationId));
                    nodeExtras.Add(new NodeExtra(g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, target.DocumentNumber));
                    g.PlannedStartForChildren = plannedStart;
                    planned.Add((g, records.Count - 1, net));
                    continue;
                }

                records.Add(new MrpRunLineRecord(
                    Id: 0, Level: g.Level, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                    ActionType: MrpActionTypes.NewWorkOrder,
                    GrossQuantity: g.Gross, OnHandApplied: onHandUsed, OpenSupplyApplied: supplyUsed,
                    NetQuantity: net, PlannedStartDate: plannedStart, PlannedEndDate: g.DueDate,
                    TargetWorkOrderId: null, PegJson: pegJson,
                    CreatedWorkOrderId: null, CreatedDocumentId: null,
                    Message: dateMessage, LocationId: g.LocationId));
                nodeExtras.Add(new NodeExtra(g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, null));
                g.PlannedStartForChildren = plannedStart;
                planned.Add((g, records.Count - 1, net));
            }

            // ── F) BİR SONRAKİ SEVİYE: planlanan emirlerin reçete bileşenleri ─────────
            current = await BuildChildDemandsAsync(planned, level, visitedItems, records, nodeExtras, ct);
        }

        // ── Koşuyu SAKLA (Draft) ─────────────────────────────────────────────────────
        var runId = await _repo.CreateRunAsync(
            request!.SourceScope ?? "Selected", lineIds, records, userId, ct);

        var stored = await _repo.GetRunLinesAsync(runId, ct);
        var nodes = BuildNodes(stored, nodeExtras);
        return new MrpPreviewResult(true, null, runId, nodes, Summarize(nodes, lineIds.Count));
    }

    /// <summary>
    /// Planlanan emirlerin reçetelerini patlatıp bir sonraki seviyenin taleplerini üretir.
    ///
    /// <para><b>Neden ExplodeBOMAsync kullanılmıyor:</b> o metot tüm seviyeleri ItemId bazında
    /// DÜZLEŞTİRİR (LogisticsConfigurationService:4004-4033) — ara seviyede net hesaplanamaz,
    /// yani yarı mamulün eldeki stoğu altındaki hammadde ihtiyacını düşürmez ve hammadde
    /// sistematik olarak fazla çıkar. Ayrıca ConfigId düşer; iş emri ve rota seçimi ona bakar.
    /// Burada aynı veri kaynağı (GetBOMComponentLines*) ve aynı miktar formülü kullanılır,
    /// ama seviye korunur.</para>
    ///
    /// <para>Bileşen talebi, ÜST grubun peg'leri oranında bölünür → alt emirde de "hangi
    /// siparişten ne kadar" bilgisi korunur (kullanıcı kuralı 3).</para>
    /// </summary>
    private async Task<List<PlanGroup>> BuildChildDemandsAsync(
        List<(PlanGroup Group, int RecordIndex, decimal Net)> planned,
        int level,
        HashSet<int> visitedItems,
        List<MrpRunLineRecord> records,
        List<NodeExtra> nodeExtras,
        CancellationToken ct)
    {
        var next = new Dictionary<string, PlanGroup>(StringComparer.Ordinal);
        if (planned.Count == 0) return [];

        // Bileşen satırlarını malzeme bazında bir kez oku (aynı mamul birden çok grupta olabilir).
        var bomCache = new Dictionary<int, IReadOnlyCollection<BOMComponentLineRow>>();

        foreach (var (g, _, net) in planned)
        {
            if (net <= 0m) continue;

            // Döngüsel reçete koruması: bir malzeme aynı koşuda ikinci kez patlatılmaz.
            // Döngüde koşu DURMAZ; o dal gerekçesiyle kesilir (CLAUDE.md #3).
            if (!visitedItems.Add(g.ItemId))
            {
                records.Add(new MrpRunLineRecord(
                    Id: 0, Level: g.Level, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                    ActionType: MrpActionTypes.Shortage, GrossQuantity: net, OnHandApplied: 0m,
                    OpenSupplyApplied: 0m, NetQuantity: 0m, PlannedStartDate: null, PlannedEndDate: null,
                    TargetWorkOrderId: null, PegJson: null, CreatedWorkOrderId: null, CreatedDocumentId: null,
                    Message: "Reçete döngüsü veya tekrar eden malzeme — bu dalın patlatması kesildi.",
                    LocationId: g.LocationId));
                nodeExtras.Add(new NodeExtra(g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, null));
                continue;
            }

            if (!bomCache.TryGetValue(g.ItemId, out var comps))
            {
                try { comps = await _logisticsConfig.GetBOMComponentLinesAsync(g.ItemId, ct); }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[MRP] Reçete okunamadı. ItemId={ItemId}", g.ItemId);
                    comps = [];
                }
                bomCache[g.ItemId] = comps;
            }
            if (comps.Count == 0) continue;   // reçetesiz mamul: alt seviye yok (normal durum)

            var compInfos = await _repo.GetItemInfoAsync(comps.Select(c => c.ItemId).ToList(), ct);
            var infoById = compInfos.ToDictionary(x => x.ItemId);

            foreach (var c in comps)
            {
                if (c.Quantity <= 0m) continue;
                if (!infoById.TryGetValue(c.ItemId, out var info)) continue;

                // ExplodeBOMAsync ile AYNI formül: qty × satır miktarı × (1 + fire).
                var childTotal = net * c.Quantity * (1 + c.ScrapRatio);
                if (childTotal <= 0m) continue;

                var childPolicy = WorkOrderSplitPolicyCatalog.Parse(info.SplitPolicy);

                // Üst grubun peg'leri oranında böl → alt emirde kaynak sipariş izi korunur.
                foreach (var peg in g.Pegs)
                {
                    var share = g.Gross > 0m ? peg.Quantity / g.Gross : 1m;
                    var qty = Math.Round(childTotal * share, 4);
                    if (qty <= 0m) continue;

                    var key = GroupKey(childPolicy, c.ItemId, c.ConfigId, peg.RootDocumentId, peg.RootLineId);
                    if (!next.TryGetValue(key, out var cg))
                    {
                        cg = new PlanGroup
                        {
                            ItemId = c.ItemId,
                            ConfigId = c.ConfigId,
                            ItemCode = info.Code,
                            ItemName = info.Name,
                            UnitCode = info.UnitCode,
                            UnitId = info.UnitId,
                            // Alt üretim, üstün tüketim deposunda planlanır (üstün deposu).
                            LocationId = g.LocationId,
                            SplitPolicy = childPolicy.ToString(),
                            IsProducible = ItemTypeCatalog.IsProducible(info.TypeId),
                            Level = level + 1,
                        };
                        next[key] = cg;
                    }
                    cg.Gross += qty;
                    // Bileşen, ÜSTÜN BAŞLADIĞI anda hazır olmalı → alt emrin bitişi üstün başlangıcı.
                    // Kümülede birden çok üst varsa EN ERKEN olan belirler.
                    var childDue = g.PlannedStartForChildren;
                    if (childDue is not null && (cg.DueDate is null || childDue < cg.DueDate))
                        cg.DueDate = childDue;
                    cg.Pegs.Add(new MrpPegDto(peg.RootDocumentId, peg.RootDocumentNumber, peg.RootLineId, qty));
                }
            }
        }

        return next.Values.ToList();
    }
    /// <inheritdoc />
    public async Task<MrpPreviewResult> GetRunAsync(int runId, CancellationToken ct)
    {
        var run = await _repo.GetRunAsync(runId, ct);
        if (run is null) return Fail("Koşu bulunamadı.");
        var stored = await _repo.GetRunLinesAsync(runId, ct);
        // Malzeme kod/adı gibi görüntü alanları koşuda saklanmaz (tek kaynak Items) —
        // geri okumada boş gelir; ekran zaten yeni önizlemeyle çalışır.
        var nodes = BuildNodes(stored, null);
        return new MrpPreviewResult(true, null, runId, nodes, Summarize(nodes, 0));
    }

    /// <inheritdoc />
    public async Task<MrpApplyResult> ApplyAsync(MrpApplyRequest request, int? userId, CancellationToken ct)
    {
        var run = await _repo.GetRunAsync(request.RunId, ct);
        if (run is null)
            return new MrpApplyResult(false, "Koşu bulunamadı.", request.RunId, [], [], [], null);
        if (run.Value.Status != MrpRunStatus.Draft)
            return new MrpApplyResult(false,
                run.Value.Status == MrpRunStatus.Applied
                    ? "Bu koşu zaten uygulanmış. Yeni bir önizleme çalıştırın."
                    : "Bu koşu iptal edilmiş. Yeni bir önizleme çalıştırın.",
                request.RunId, [], [], [], null);

        var lines = await _repo.GetRunLinesAsync(request.RunId, ct);
        // Seviye sırası ZORUNLU: alt emirler üst emirlere peg'lenecek, üst emrin Id'si önce
        // oluşmuş olmalı. GetRunLinesAsync zaten ORDER BY Level, Id döner — burada garanti altına
        // alınır ki sorgu sırası değişirse bağlar sessizce kopmasın.
        var actionable = lines
            .Where(l => l.ActionType is MrpActionTypes.NewWorkOrder or MrpActionTypes.MergeWorkOrder)
            .OrderBy(l => l.Level).ThenBy(l => l.Id)
            .ToList();
        var shortages = lines.Where(l => l.ActionType == MrpActionTypes.PurchaseRequest).ToList();
        if (actionable.Count == 0 && shortages.Count == 0)
            return new MrpApplyResult(false, "Bu koşuda oluşturulacak iş emri yok.", request.RunId, [], [], [], null);

        // Koşuyu ÖNCE Applied yap: koşullu UPDATE (Status=0 iken) çift tıklamayı/iki sekmeyi
        // burada keser. Sonra yazsaydık iki istek de emirleri açar, ikisi de "başarılı" derdi.
        if (!await _repo.TryMarkRunAppliedAsync(request.RunId, null, userId, ct))
            return new MrpApplyResult(false, "Bu koşu başka bir işlemde uygulandı.", request.RunId, [], [], [], null);

        var created = new List<MrpCreatedWorkOrderDto>();
        var merged = new List<MrpCreatedWorkOrderDto>();
        var warnings = new List<string>();

        // Malzeme kod/adı TEK toplu sorguda — koşudaki her malzeme (alt seviyedeki yarı mamul
        // ve hammadde dahil; bunlar açık sipariş satırlarında görünmez).
        var allItemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var itemInfo = (await _repo.GetItemInfoAsync(allItemIds, ct))
            .ToDictionary(x => x.ItemId, x => (x.Code, x.Name));

        // Seviye 1+ emirlerini üst emirlere bağlamak için: her uygulanan satırın ürettiği emir
        // ve o satırın kök sipariş satırları. Alt emir, ÜST SEVİYEDEKİ kök satırları KESİŞEN
        // emirlere peg'lenir — böylece "hangi mamul için üretiliyor" izi kurulur.
        var appliedByLevel = new Dictionary<int, List<(int WorkOrderId, HashSet<int> RootLines, List<MrpPegDto> Pegs)>>();
        var pegInputs = new List<WorkOrderPegInput>();

        foreach (var l in actionable)
        {
            var (code, name) = itemInfo.TryGetValue(l.ItemId, out var info) ? info : ($"#{l.ItemId}", string.Empty);
            try
            {
                var pegs = string.IsNullOrWhiteSpace(l.PegJson)
                    ? []
                    : JsonSerializer.Deserialize<List<MrpPegDto>>(l.PegJson!) ?? [];
                var sources = pegs
                    .Select(p => new MrpWorkOrderSourceLine(p.RootDocumentId, p.RootLineId, p.Quantity))
                    .ToList();

                var woId = await _workOrders.CreateFromMrpAsync(new CreateWorkOrderFromMrpRequest(
                    ItemId: l.ItemId,
                    ConfigId: l.ConfigId,
                    Quantity: l.NetQuantity,
                    UnitId: null,
                    LocationId: l.LocationId,
                    PlannedStartDate: l.PlannedStartDate,
                    PlannedEndDate: l.PlannedEndDate,
                    Sources: sources,
                    TargetWorkOrderId: l.ActionType == MrpActionTypes.MergeWorkOrder ? l.TargetWorkOrderId : null,
                    MrpRunId: request.RunId), ct);

                await _repo.SetRunLineResultAsync(l.Id, woId, null, null, ct);
                await _repo.SetWorkOrderMrpRunAsync(woId, request.RunId, ct);

                // ── Alt/üst iş emri bağı (Faz 4) ──
                // Seviye 0 emirleri WorkOrderSource ile satış satırına bağlıdır (CreateFromMrpAsync
                // yazdı). Seviye 1+ emirleri ayrıca WorkOrderPeg ile ÜST emre bağlanır: bir alt
                // emrin birden çok üstü olabilir (kümüle politika), bu yüzden tek kolon değil
                // bağ tablosu kullanılır.
                var rootLines = pegs.Select(p => p.RootLineId).ToHashSet();
                if (l.Level > 0 && appliedByLevel.TryGetValue(l.Level - 1, out var parents))
                {
                    foreach (var parent in parents.Where(p => p.RootLines.Overlaps(rootLines)))
                    {
                        foreach (var peg in pegs.Where(p => parent.RootLines.Contains(p.RootLineId)))
                        {
                            pegInputs.Add(new WorkOrderPegInput(
                                WorkOrderId: woId,
                                ParentWorkOrderId: parent.WorkOrderId,
                                ParentComponentId: null,
                                RootDocumentId: peg.RootDocumentId,
                                RootLineId: peg.RootLineId,
                                Quantity: peg.Quantity,
                                Level: l.Level,
                                MrpRunId: request.RunId));
                        }
                    }
                }
                if (!appliedByLevel.TryGetValue(l.Level, out var lvl))
                    appliedByLevel[l.Level] = lvl = [];
                lvl.Add((woId, rootLines, pegs));

                var dto = new MrpCreatedWorkOrderDto(woId, null, l.ItemId, code, name, l.NetQuantity);
                if (l.ActionType == MrpActionTypes.MergeWorkOrder) merged.Add(dto); else created.Add(dto);
            }
            catch (Exception ex)
            {
                // Bir satırın düşmesi diğerlerini iptal etmez, ama SESSİZ de geçilmez:
                // gerekçe hem koşu satırına hem kullanıcıya döner, sunucuya loglanır.
                _logger?.LogError(ex, "[MRP] Koşu {RunId} satır {LineId} uygulanamadı (Item {ItemId}).",
                    request.RunId, l.Id, l.ItemId);
                var msg = $"{code} — iş emri açılamadı: {ex.Message}";
                warnings.Add(msg);
                await _repo.SetRunLineResultAsync(l.Id, null, null, msg, ct);
            }
        }

        // Peg'ler emirler açıldıktan SONRA toplu yazılır (tek transaction).
        if (pegInputs.Count > 0)
        {
            try { await _repo.AddPegsAsync(pegInputs, userId, ct); }
            catch (Exception ex)
            {
                // Emirler açıldı ama alt/üst bağ kurulamadı — iş emirleri geçerli, yalnız ağaç
                // görünümü eksik kalır. Sessizce yutulmaz: loglanır + kullanıcıya uyarı.
                _logger?.LogError(ex, "[MRP] Koşu {RunId} — iş emri ağacı bağları yazılamadı.", request.RunId);
                warnings.Add("İş emirleri açıldı ancak alt/üst emir bağları yazılamadı — ağaç görünümü eksik olabilir.");
            }
        }

        // ── Satın Alma Talebi (kullanıcı kuralı 4) ────────────────────────────────────
        int? purchaseDocId = null;
        if (request.CreatePurchaseRequest && shortages.Count > 0)
        {
            var (docId, prWarning) = await TryCreatePurchaseRequestAsync(request.RunId, shortages, userId, ct);
            purchaseDocId = docId;
            if (prWarning is not null) warnings.Add(prWarning);
            if (docId is > 0)
                foreach (var s in shortages)
                    await _repo.SetRunLineResultAsync(s.Id, null, docId, null, ct);
        }

        try
        {
            _audit?.LogInsert("MrpRun", request.RunId, $"MRP #{request.RunId}",
                detail: $"{created.Count} yeni iş emri, {merged.Count} mevcut emre ekleme"
                        + (purchaseDocId is > 0 ? ", 1 satın alma talebi" : "")
                        + (warnings.Count > 0 ? $", {warnings.Count} uyarı" : ""));
        }
        catch { /* audit yazımı koşuyu asla bozmaz */ }

        return new MrpApplyResult(true, null, request.RunId, created, merged, warnings, purchaseDocId);
    }

    /// <summary>
    /// Eksik (üretilemeyen) malzemeler için TEK bir Satın Alma Talebi belgesi üretir.
    ///
    /// <para>Belge türü <c>satin_alma_talebi</c> (kullanıcı kararı — İhtiyaç Kaydı adımı
    /// atlanır). Aynı malzemenin farklı koşu satırları TEK kaleme toplanır. Cari yoktur
    /// (talep aşamasında tedarikçi henüz belli değil), fiyat 0'dır.</para>
    ///
    /// <para>Belge oluşturulamazsa iş emirleri GERİ ALINMAZ — talep bir öneridir, üretim planı
    /// ondan bağımsız geçerlidir. Hata gerekçesiyle kullanıcıya döner.</para>
    /// </summary>
    private async Task<(int? DocumentId, string? Warning)> TryCreatePurchaseRequestAsync(
        int runId, IReadOnlyList<MrpRunLineRecord> shortages, int? userId, CancellationToken ct)
    {
        if (_documents is null || _documentTypes is null)
            return (null, "Satın Alma Talebi servisi kullanılamıyor — belge oluşturulmadı.");

        try
        {
            var type = await _documentTypes.GetByCodeAsync("satin_alma_talebi", ct);
            if (type is null || type.Id <= 0)
                return (null, "'Satın Alma Talebi' belge tipi tanımlı değil — belge oluşturulmadı.");

            var byItem = shortages
                .Where(s => s.NetQuantity > 0m)
                .GroupBy(s => s.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.NetQuantity) })
                .ToList();
            if (byItem.Count == 0) return (null, null);

            var saveLines = byItem.Select(x => new SaveDocumentLineRequest(
                Id: null, ItemId: x.ItemId, UnitId: null, Quantity: x.Qty,
                UnitPrice: 0m, DiscountRate: 0m, CombinationId: null, LocationId: null,
                Notes: null, CombinationDetails: null, TrackCombinations: false,
                NotesPinned: false, RevisedFromId: null)).ToList();

            // İhtiyaç tarihi = en erken gereken tarih (planlanan başlangıçların en küçüğü).
            var needDate = shortages
                .Select(s => s.PlannedStartDate ?? s.PlannedEndDate)
                .Where(d => d is not null).OrderBy(d => d).FirstOrDefault();

            var req = new SaveDocumentRequest(
                Id: null, DocumentDate: DateTime.Today, ValidUntil: null,
                ContactId: null, ContactName: null, ContactAddress: null, SalesRepId: null,
                CurrencyId: 1, DiscountRate: 0m, TaxRate: 20m,
                PaymentTerms: null, DeliveryTerms: null, DeliveryAddress: null,
                Notes: $"MRP koşusu #{runId} — otomatik ihtiyaç önerisi",
                Lines: saveLines, ContactCode: null, DocumentTypeId: type.Id,
                DeliveryDate: needDate, DeliveryDays: null, RequesterPersonnelId: null);

            var (ok, error, saved, _) = await _documents.SaveQuoteAsync(req, userId, null, ct);
            if (!ok || saved is null)
                return (null, "Satın Alma Talebi oluşturulamadı: " + (error ?? "bilinmeyen hata"));
            return (saved.Id, null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MRP] Koşu {RunId} — Satın Alma Talebi oluşturulamadı.", runId);
            return (null, "Satın Alma Talebi oluşturulamadı (ayrıntı sunucu logunda).");
        }
    }

    /// <inheritdoc />
    public Task DiscardAsync(int runId, int? userId, CancellationToken ct)
        => _repo.DiscardRunAsync(runId, userId, ct);

    // ── Yardımcılar ───────────────────────────────────────────────────────────────────

    private static string F(decimal v) => v.ToString("0.####");

    private static MrpPreviewResult Fail(string error)
        => new(false, error, 0, [], new MrpPreviewSummaryDto(0, 0, 0, 0, 0, 0));

    /// <summary>Aksiyon üretmeyen sipariş satırı için gerekçeli koşu satırı (sessiz atlama yok).</summary>
    private static MrpRunLineRecord Info(MrpOpenOrderLineDto l, string action, decimal gross, string message)
        => new(
            Id: 0, Level: 0, ParentRunLineId: null, ItemId: l.ItemId, ConfigId: l.ConfigId,
            ActionType: action, GrossQuantity: gross, OnHandApplied: 0m, OpenSupplyApplied: 0m,
            NetQuantity: 0m, PlannedStartDate: null, PlannedEndDate: l.DeliveryDate,
            TargetWorkOrderId: null,
            PegJson: JsonSerializer.Serialize(new[] { new MrpPegDto(l.DocumentId, l.DocumentNumber, l.LineId, gross) }),
            CreatedWorkOrderId: null, CreatedDocumentId: null, Message: message, LocationId: l.LocationId);

    private static IReadOnlyList<MrpPreviewNodeDto> BuildNodes(
        IReadOnlyList<MrpRunLineRecord> stored,
        IReadOnlyList<NodeExtra>? extras)
    {
        var nodes = new List<MrpPreviewNodeDto>(stored.Count);
        for (var i = 0; i < stored.Count; i++)
        {
            var s = stored[i];
            var e = extras is not null && i < extras.Count ? extras[i] : default;
            var pegs = string.IsNullOrWhiteSpace(s.PegJson)
                ? []
                : JsonSerializer.Deserialize<List<MrpPegDto>>(s.PegJson!) ?? [];
            nodes.Add(new MrpPreviewNodeDto(
                RunLineId: s.Id,
                Level: s.Level,
                ParentRunLineId: s.ParentRunLineId,
                ItemId: s.ItemId,
                ItemCode: e.Code ?? string.Empty,
                ItemName: e.Name ?? string.Empty,
                ConfigId: s.ConfigId,
                UnitCode: e.UnitCode,
                LocationId: e.LocationId ?? s.LocationId,
                LocationName: null,
                ActionType: s.ActionType,
                SplitPolicy: e.Policy ?? WorkOrderSplitPolicyCatalog.Default,
                GrossQuantity: s.GrossQuantity,
                OnHandApplied: s.OnHandApplied,
                OpenSupplyApplied: s.OpenSupplyApplied,
                NetQuantity: s.NetQuantity,
                PlannedStartDate: s.PlannedStartDate,
                PlannedEndDate: s.PlannedEndDate,
                TargetWorkOrderId: s.TargetWorkOrderId,
                TargetWorkOrderNumber: e.TargetNo,
                Message: s.Message,
                Pegs: pegs));
        }
        return nodes;
    }

    private static MrpPreviewSummaryDto Summarize(IReadOnlyList<MrpPreviewNodeDto> nodes, int selectedLineCount)
        => new(
            NewWorkOrderCount:   nodes.Count(n => n.ActionType == MrpActionTypes.NewWorkOrder),
            MergeWorkOrderCount: nodes.Count(n => n.ActionType == MrpActionTypes.MergeWorkOrder),
            CoveredByStockCount: nodes.Count(n => n.ActionType == MrpActionTypes.CoveredByStock),
            ShortageCount:       nodes.Count(n => n.ActionType == MrpActionTypes.Shortage),
            PurchaseRequestCount: nodes.Count(n => n.ActionType == MrpActionTypes.PurchaseRequest),
            SelectedLineCount:   selectedLineCount);
}

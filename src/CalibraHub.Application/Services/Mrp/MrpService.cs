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
    private readonly IAuditTrailService? _audit;
    private readonly ILogger<MrpService>? _logger;

    public MrpService(
        IMrpRepository repo,
        IWorkOrderService workOrders,
        IWorkOrderRepository workOrderRepo,
        IRoutingService routings,
        IOperationMachineTimeService machineTimes,
        IMachineCalendarRepository calendar,
        IAuditTrailService? audit = null,
        ILogger<MrpService>? logger = null)
    {
        _repo = repo;
        _workOrders = workOrders;
        _workOrderRepo = workOrderRepo;
        _routings = routings;
        _machineTimes = machineTimes;
        _calendar = calendar;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MrpOpenOrderLineDto>> ListOpenOrderLinesAsync(
        int? documentId, string? search, CancellationToken ct)
        => _repo.ListOpenSalesOrderLinesAsync(null, documentId, search, ct);

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
        public decimal Gross { get; set; }
        public DateTime? DueDate { get; set; }
        public List<MrpPegDto> Pegs { get; } = [];
    }

    /// <inheritdoc />
    public async Task<MrpPreviewResult> PreviewAsync(MrpPreviewRequest request, int? userId, CancellationToken ct)
    {
        var lineIds = (request?.LineIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList();
        if (lineIds.Count == 0)
            return Fail("En az bir sipariş satırı seçilmelidir.");

        var lines = await _repo.ListOpenSalesOrderLinesAsync(lineIds, null, null, ct);
        if (lines.Count == 0)
            return Fail("Seçilen satırlar artık açık değil (teslim edilmiş veya belge iptal edilmiş olabilir).");

        var records = new List<MrpRunLineRecord>();
        var nodeExtras = new List<(int ItemId, string Code, string Name, string? UnitCode, string Policy, int? LocationId, string? TargetNo)>();

        // ── A) TALEP ──────────────────────────────────────────────────────────────────
        // demand = açık miktar − o satıra fiilen rezerve edilmiş − zaten iş emrine tahsis edilmiş
        var groups = new Dictionary<string, PlanGroup>(StringComparer.Ordinal);

        foreach (var l in lines)
        {
            var demand = l.OpenQuantity - l.ReservedQuantity - l.AllocatedQuantity;

            if (demand <= 0.0001m)
            {
                records.Add(Info(l, MrpActionTypes.CoveredByStock, 0m,
                    $"Talep kalmadı — açık {F(l.OpenQuantity)}, rezerve {F(l.ReservedQuantity)}, iş emrine tahsis {F(l.AllocatedQuantity)}."));
                nodeExtras.Add((l.ItemId, l.ItemCode, l.ItemName, l.UnitCode, l.SplitPolicy, l.LocationId, null));
                continue;
            }

            if (!l.IsProducible)
            {
                records.Add(Info(l, MrpActionTypes.Shortage, demand,
                    "Malzeme üretilebilir tipte değil (Mamul/Yarı Mamul) — iş emri açılamaz."));
                nodeExtras.Add((l.ItemId, l.ItemCode, l.ItemName, l.UnitCode, l.SplitPolicy, l.LocationId, null));
                continue;
            }

            if (l.LocationId is not > 0)
            {
                records.Add(Info(l, MrpActionTypes.Shortage, demand,
                    "Depo belirlenemedi (sipariş kaleminde ve başlığında depo yok) — stok karşılaştırması yapılamaz."));
                nodeExtras.Add((l.ItemId, l.ItemCode, l.ItemName, l.UnitCode, l.SplitPolicy, l.LocationId, null));
                continue;
            }

            // ── C) GRUPLAMA — malzemenin KENDİ politikası belirler ──
            var policy = WorkOrderSplitPolicyCatalog.Parse(l.SplitPolicy);
            var key = policy switch
            {
                WorkOrderSplitPolicy.Cumulative => $"C|{l.ItemId}|{l.ConfigId}",
                WorkOrderSplitPolicy.PerOrder   => $"O|{l.ItemId}|{l.ConfigId}|{l.DocumentId}",
                _                               => $"L|{l.ItemId}|{l.ConfigId}|{l.LineId}",
            };

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
                };
                groups[key] = g;
            }
            g.Gross += demand;
            // Grubun teslim tarihi EN ERKEN olanıdır — birleşen emir, en acil siparişe yetişmeli.
            if (l.DeliveryDate is not null && (g.DueDate is null || l.DeliveryDate < g.DueDate))
                g.DueDate = l.DeliveryDate;
            g.Pegs.Add(new MrpPegDto(l.DocumentId, l.DocumentNumber, l.LineId, demand));
        }

        // ── B) ARZ HAVUZLARI (tek örnek, azaltılarak kullanılır) ──────────────────────
        var itemIds = groups.Values.Select(g => g.ItemId).Distinct().ToList();
        var availKeys = groups.Values
            .Where(g => g.LocationId is > 0)
            .Select(g => (g.ItemId, g.LocationId!.Value))
            .Distinct().ToList();

        var availRows = await _repo.GetAvailabilityAsync(availKeys, ct);
        var available = availRows.ToDictionary(r => (r.ItemId, r.LocationId), r => Math.Max(0m, r.Available));

        var openWoRows = await _repo.GetOpenWorkOrdersAsync(itemIds, ct);
        // Arz olarak sayılan: emrin henüz ÜRETİLMEMİŞ ve henüz bir siparişe BAĞLANMAMIŞ kısmı.
        // İkisinin küçüğü alınır: üretilmiş kısım stoğa zaten girmiştir (available'da sayılı),
        // bağlanmış kısım başka siparişin ihtiyacını karşılar.
        var openWoSupply = new Dictionary<int, decimal>();
        foreach (var w in openWoRows)
        {
            var usable = Math.Max(0m, Math.Min(w.RemainingQuantity, w.UnpeggedQuantity));
            if (usable <= 0m) continue;
            openWoSupply[w.ItemId] = openWoSupply.TryGetValue(w.ItemId, out var acc) ? acc + usable : usable;
        }

        var openPoRows = await _repo.GetOpenPurchaseSupplyAsync(itemIds, ct);
        var openPoSupply = openPoRows
            .GroupBy(r => r.ItemId)
            .ToDictionary(g2 => g2.Key, g2 => g2.Sum(x => x.OpenQuantity));

        // ── E) TARİHLEME hazırlığı (Faz 3) ───────────────────────────────────────────
        // Süre rota operasyonlarından hesaplanır, geriye kaydırma çalışma takvimi üzerinden.
        var leadTime = new MrpLeadTimeCalculator(_workOrderRepo, _routings, _machineTimes, _calendar, _logger);
        var walker = await leadTime.GetWalkerAsync(ct);

        // ── D) NET İHTİYAÇ + AKSİYON ─────────────────────────────────────────────────
        // EDD (en erken teslim önce): kıt stok en acil siparişe gitmeli.
        var ordered = groups.Values
            .OrderBy(g => g.DueDate ?? DateTime.MaxValue)
            .ThenBy(g => g.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Merge adaylarını malzeme bazında sıraya koy (en erken biten önce) — deterministik önizleme.
        var mergeCandidates = openWoRows
            .GroupBy(w => w.ItemId)
            .ToDictionary(
                gr => gr.Key,
                gr => gr.OrderBy(w => w.PlannedEndDate ?? DateTime.MaxValue).ThenBy(w => w.WorkOrderId).ToList());
        var mergeUsed = new Dictionary<int, decimal>();   // workOrderId → bu koşuda eklenen miktar

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

            available[locKey] = onHandPool - onHandUsed;
            openWoSupply[g.ItemId] = woPool - woUsed;
            openPoSupply[g.ItemId] = poPool - poUsed;

            var supplyUsed = woUsed + poUsed;
            var net = Math.Round(Math.Max(0m, remaining), 4);
            var pegJson = JsonSerializer.Serialize(g.Pegs);

            if (net <= 0.0001m)
            {
                records.Add(new MrpRunLineRecord(
                    Id: 0, Level: 0, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                    ActionType: MrpActionTypes.CoveredByStock,
                    GrossQuantity: g.Gross, OnHandApplied: onHandUsed, OpenSupplyApplied: supplyUsed,
                    NetQuantity: 0m, PlannedStartDate: null, PlannedEndDate: g.DueDate,
                    TargetWorkOrderId: null, PegJson: pegJson, CreatedWorkOrderId: null,
                    CreatedDocumentId: null,
                    Message: $"İhtiyaç mevcut arzdan karşılanıyor — eldeki {F(onHandUsed)}, açık arz {F(supplyUsed)}.",
                    LocationId: g.LocationId));
                nodeExtras.Add((g.ItemId, g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, null));
                continue;
            }

            // ── Mevcut açık emre bağlama (kullanıcı kuralı 7) ──
            // (a) üretilmemiş miktar var, (b) bağlılar düşünce açık kalıyor, (c) teslim tarihi uyuyor.
            MrpOpenWorkOrderRow? target = null;
            if (mergeCandidates.TryGetValue(g.ItemId, out var cands))
            {
                target = cands.FirstOrDefault(w =>
                    w.ConfigId == g.ConfigId
                    && w.RemainingQuantity > 0.0001m
                    && (w.UnpeggedQuantity - (mergeUsed.TryGetValue(w.WorkOrderId, out var mu) ? mu : 0m)) > 0.0001m
                    && (w.PlannedEndDate is null || g.DueDate is null || w.PlannedEndDate <= g.DueDate));
            }

            if (target is not null)
            {
                mergeUsed[target.WorkOrderId] = (mergeUsed.TryGetValue(target.WorkOrderId, out var prev) ? prev : 0m) + net;
                records.Add(new MrpRunLineRecord(
                    Id: 0, Level: 0, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                    ActionType: MrpActionTypes.MergeWorkOrder,
                    GrossQuantity: g.Gross, OnHandApplied: onHandUsed, OpenSupplyApplied: supplyUsed,
                    NetQuantity: net, PlannedStartDate: null, PlannedEndDate: g.DueDate,
                    TargetWorkOrderId: target.WorkOrderId, PegJson: pegJson,
                    CreatedWorkOrderId: null, CreatedDocumentId: null,
                    Message: target.Status == 1
                        ? "Yayımlanmış emre ekleniyor — operasyon süreleri yeniden hesaplanmalı."
                        : null,
                    LocationId: g.LocationId));
                nodeExtras.Add((g.ItemId, g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, target.DocumentNumber));
                continue;
            }

            // ── TARİHLEME (Faz 3) ──
            // Bitiş = kaynak siparişin (grup içindeki en erken) teslim tarihi.
            // Başlangıç = bitişten, operasyon sürelerinden hesaplanan üretim süresi kadar
            // GERİYE — kapalı saatler ve tatiller atlanarak.
            DateTime? plannedStart = null;
            string? dateMessage = null;

            if (g.DueDate is null)
            {
                dateMessage = "Siparişte teslim tarihi yok — planlanan bitiş boş bırakıldı.";
            }
            else
            {
                var lt = await leadTime.ResolveMinutesAsync(g.ItemId, g.ConfigId, net, ct);
                if (lt.Minutes is not > 0m)
                {
                    plannedStart = g.DueDate;                  // süre yok → aynı gün
                    dateMessage = lt.Reason;
                }
                else if (!walker.HasCalendar)
                {
                    plannedStart = g.DueDate;
                    dateMessage = "Çalışma takvimi tanımlı değil — başlangıç tarihi bitişe eşitlendi "
                                + $"(hesaplanan üretim süresi {F(lt.Minutes.Value / 60m)} saat).";
                }
                else
                {
                    plannedStart = walker.WalkBackward(g.DueDate.Value, lt.Minutes.Value);
                    // Geçmişe düşen başlangıç SESSİZCE bugüne çekilmez — emir yine planlanır,
                    // kullanıcı gecikmeyi görüp kararı kendisi verir.
                    if (plannedStart < DateTime.Now.Date)
                        dateMessage = "Geç kalınmış: hesaplanan başlangıç geçmişte "
                                    + $"({plannedStart:dd.MM.yyyy}).";
                }
            }

            records.Add(new MrpRunLineRecord(
                Id: 0, Level: 0, ParentRunLineId: null, ItemId: g.ItemId, ConfigId: g.ConfigId,
                ActionType: MrpActionTypes.NewWorkOrder,
                GrossQuantity: g.Gross, OnHandApplied: onHandUsed, OpenSupplyApplied: supplyUsed,
                NetQuantity: net,
                PlannedStartDate: plannedStart, PlannedEndDate: g.DueDate,
                TargetWorkOrderId: null, PegJson: pegJson,
                CreatedWorkOrderId: null, CreatedDocumentId: null,
                Message: dateMessage,
                LocationId: g.LocationId));
            nodeExtras.Add((g.ItemId, g.ItemCode, g.ItemName, g.UnitCode, g.SplitPolicy, g.LocationId, null));
        }

        // ── Koşuyu SAKLA (Draft) ─────────────────────────────────────────────────────
        var runId = await _repo.CreateRunAsync(
            request!.SourceScope ?? "Selected", lineIds, records, userId, ct);

        var stored = await _repo.GetRunLinesAsync(runId, ct);
        var nodes = BuildNodes(stored, nodeExtras);
        return new MrpPreviewResult(true, null, runId, nodes, Summarize(nodes, lineIds.Count));
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
        var actionable = lines
            .Where(l => l.ActionType is MrpActionTypes.NewWorkOrder or MrpActionTypes.MergeWorkOrder)
            .ToList();
        if (actionable.Count == 0)
            return new MrpApplyResult(false, "Bu koşuda oluşturulacak iş emri yok.", request.RunId, [], [], [], null);

        // Koşuyu ÖNCE Applied yap: koşullu UPDATE (Status=0 iken) çift tıklamayı/iki sekmeyi
        // burada keser. Sonra yazsaydık iki istek de emirleri açar, ikisi de "başarılı" derdi.
        if (!await _repo.TryMarkRunAppliedAsync(request.RunId, null, userId, ct))
            return new MrpApplyResult(false, "Bu koşu başka bir işlemde uygulandı.", request.RunId, [], [], [], null);

        var created = new List<MrpCreatedWorkOrderDto>();
        var merged = new List<MrpCreatedWorkOrderDto>();
        var warnings = new List<string>();

        // Malzeme kod/adı tek sorguda (uyarı metinleri ve sonuç listesi için).
        var itemInfo = (await _repo.ListOpenSalesOrderLinesAsync(null, null, null, ct))
            .GroupBy(x => x.ItemId)
            .ToDictionary(x => x.Key, x => (x.First().ItemCode, x.First().ItemName));

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

        try
        {
            _audit?.LogInsert("MrpRun", request.RunId, $"MRP #{request.RunId}",
                detail: $"{created.Count} yeni iş emri, {merged.Count} mevcut emre ekleme"
                        + (warnings.Count > 0 ? $", {warnings.Count} uyarı" : ""));
        }
        catch { /* audit yazımı koşuyu asla bozmaz */ }

        return new MrpApplyResult(true, null, request.RunId, created, merged, warnings, null);
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
        IReadOnlyList<(int ItemId, string Code, string Name, string? UnitCode, string Policy, int? LocationId, string? TargetNo)>? extras)
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

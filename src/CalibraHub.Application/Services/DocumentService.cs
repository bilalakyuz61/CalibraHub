using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval.EntityTypes;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repo;
    private readonly IFinanceService _financeService;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly IDocumentSourceRepository _docSourceRepo;
    private readonly IDocumentNumberService? _docNumberService;
    private readonly IApprovalFlowService? _approvalFlowService;
    private readonly ICompanyParameterService? _companyParameters;
    private readonly IDecimalSettingService? _decimalSettings;
    private readonly IAuditTrailService? _audit;
    private readonly IWorkOrderRepository? _workOrders;
    private readonly ILogisticsConfigurationRepository? _itemLocks;
    private readonly IPriceListService? _priceListService;
    /// <summary>Tekliften siparişe dönüşümde stok rezervasyonu (opsiyonel — kayıtlı değilse
    /// ReserveStock talebi net hata ile reddedilir, sessizce rezervasyonsuz sipariş üretilmez).</summary>
    private readonly IStockReservationRepository? _stockReservations;
    private readonly ILogger<DocumentService>? _logger;
    private const string DefaultSalesQuoteTypeCode = "satis_teklifi";
    private const string DefaultSalesOrderTypeCode = "satis_siparisi";
    private static readonly IReadOnlyDictionary<int, decimal> EmptyWorkOrderAllocations = new Dictionary<int, decimal>();

    public DocumentService(
        IDocumentRepository repo,
        IFinanceService financeService,
        IDocumentTypeRepository documentTypeRepo,
        IDocumentSourceRepository docSourceRepo,
        IDocumentNumberService? docNumberService = null,
        IApprovalFlowService? approvalFlowService = null,
        ICompanyParameterService? companyParameters = null,
        IDecimalSettingService? decimalSettings = null,
        IAuditTrailService? audit = null,
        IWorkOrderRepository? workOrders = null,
        ILogisticsConfigurationRepository? itemLocks = null,
        IPriceListService? priceListService = null,
        ILogger<DocumentService>? logger = null,
        IStockReservationRepository? stockReservations = null)
    {
        _stockReservations = stockReservations;
        _repo = repo;
        _financeService = financeService;
        _documentTypeRepo = documentTypeRepo;
        _docSourceRepo = docSourceRepo;
        _docNumberService = docNumberService;
        _approvalFlowService = approvalFlowService;
        _companyParameters = companyParameters;
        _decimalSettings = decimalSettings;
        _audit = audit;
        _workOrders = workOrders;
        _itemLocks = itemLocks;
        _priceListService = priceListService;
        _logger = logger;
    }

    /// <summary>
    /// 2026-07-20 — İş emri↔sipariş miktar guard'ı. Belgenin satırlarına WorkOrderSource ile
    /// tahsis edilmiş miktar (sourceLineId → toplam), TEK sorguda (N+1'den kaçınmak için
    /// belge başına bir kez çağrılır). <see cref="_workOrders"/> kayıtlı değilse (üretim modülü
    /// devre dışı senaryosu) boş sözlük döner — guard sessizce no-op olur. Repo çağrısı KENDİSİ
    /// try/catch İLE SARILMAZ: gerçek bir hata (ör. tablo/kolon adı sorunu) olursa yukarı fırlar,
    /// guard'ın "hata oldu, sanki tahsis yokmuş gibi devam et" şeklinde sessizce atlanması KURALA
    /// AYKIRI olurdu (bkz. CLAUDE.md "sessiz kırık" kural #2/#3).
    /// </summary>
    private Task<IReadOnlyDictionary<int, decimal>> GetWorkOrderAllocationsAsync(int documentId, CancellationToken ct)
        => _workOrders?.GetAllocatedQuantitiesForDocumentAsync(documentId, ct)
           ?? Task.FromResult(EmptyWorkOrderAllocations);

    /// <summary>
    /// Kalem miktar tabanı (floor) — kullanıcı kararı (2026-07-20): karşılanan (consumed),
    /// türetilmiş belge satırı toplamı (derivedQty — irsaliye/sevkiyat SourceLineId zinciri) ve
    /// iş emri tahsisi (woQty — WorkOrderSource) AYRI kaynaklardır ve TOPLANMAZ; üçünden BÜYÜK
    /// OLANI taban olur. Örnek: sipariş 100, iş emri tahsisi 90, teslimat 95 → floor = max(90,95)
    /// = 95 (185 değil). SaveQuoteAsync / GetLineLocksAsync / GetDeleteBlockReasonAsync AYNI bu
    /// formülü kullanır — üçü ayrı ayrı hesaplarsa sapabilir (kaydet-sonra-hata / sil-sonra-hata).
    /// </summary>
    private static decimal ComputeLineFloor(decimal consumed, decimal derivedQty, decimal woQty)
        => Math.Max(Math.Max(consumed, derivedQty), woQty);

    /// <summary>Floor=0 iken kilit yok; floor&gt;0 iken üç kaynaktan hangisinin payı en büyükse
    /// kullanıcı mesajı onu anlatır (eşitlikte öncelik: iş emri &gt; türetilmiş &gt; karşılanan).</summary>
    private enum LineFloorSource { None, Consumed, Derived, WorkOrder }

    private static LineFloorSource DominantLineFloorSource(decimal consumed, decimal derivedQty, decimal woQty)
    {
        if (consumed <= 0 && derivedQty <= 0 && woQty <= 0) return LineFloorSource.None;
        if (woQty >= derivedQty && woQty >= consumed) return LineFloorSource.WorkOrder;
        if (derivedQty >= consumed) return LineFloorSource.Derived;
        return LineFloorSource.Consumed;
    }

    /// <summary>
    /// Seq 1073 Part B, 4 moda genişletildi Seq 1078 (PageComment) — FixedPackage DIŞINDAKİ 3
    /// kit fiyat modunun (FixedComponent/ListPackage/ListComponent) birim fiyatını SERVER-TRUTH
    /// olarak hesaplar. Dönen sözlük <paramref name="lineRequests"/> içindeki DİZİN (index) →
    /// hesaplanan birim fiyat şeklindedir (KitItemId DEĞİL — review Bulgu #1: aynı kit birden
    /// fazla satırda geçebilir ve satırlardan biri dondurulmuş/diğeri dondurulmamış olabilir;
    /// ItemId bazlı anahtarlama bu durumda çakışırdı).
    ///
    /// Mod başına kaynak:
    ///   FixedPackage   → bu sözlüğe HİÇ girmez (sunucu ezmez, client'ın/kayıtlı FixedPrice değeri kullanılır).
    ///   FixedComponent → kitUnitPrice = Σ(bileşenin kit tanımındaki ELLE UnitPrice'ı × miktar).
    ///                    Fiyat listesi ÇAĞRISI YOK — tamamen ItemKitLine.UnitPrice'tan (veya
    ///                    donmuşsa DocumentLineKitComponent.UnitPrice'tan) gelir.
    ///   ListPackage    → kitUnitPrice = kit'in KENDİ ItemId'si için Genel Liste/cari fiyatı
    ///                    (bileşenlere hiç bakılmaz).
    ///   ListComponent  → kitUnitPrice = Σ(bileşenin Genel Liste/cari fiyatı × miktar) — eski "RollUp".
    ///
    /// Kaynak seçimi (Bulgu #1 — fiyat≠teslim ayrışmasını önler, FixedComponent/ListComponent için):
    /// bir satır ZATEN dondurulmuş bir kit snapshot'ı taşıyorsa (<see cref="IDocumentRepository.GetExistingKitSnapshotsAsync"/>
    /// ile tespit — freeze-on-first ile AYNI eşleşme kuralı: snapshot'taki KitItemId == satırın
    /// güncel ItemId'si) bileşen listesi o DONMUŞ içerikten (<see cref="IDocumentRepository.GetKitSnapshotAsync"/>)
    /// alınır — irsaliye/patlatma da aynı donmuş içerikten üretildiği için fiyat ile teslim aynı
    /// kaynağı paylaşır. FixedComponent'te donmuş satırın ELLE fiyatı da (DocumentLineKitComponent.UnitPrice)
    /// donduğu andaki değerdir (kit tanımı sonradan revize edilse bile bu belge etkilenmez).
    /// ListComponent'te ise donan yalnız MİKTAR/bileşen kümesidir — fiyat her save'de yine CANLI
    /// Genel Liste'den yeniden çözülür (dış kaynak zaman içinde değişir). Henüz dondurulmamış
    /// satırlarda (yeni satır veya kit değişmiş) canlı aktif içerik
    /// (<see cref="IDocumentRepository.GetActiveKitContentsAsync"/>) kullanılır — snapshot bloğuyla
    /// (SaveQuoteAsync, kit snapshot bölümü) TUTARLI.
    ///
    /// Fiyat listesi gerektiren key'ler (ListComponent bileşenleri + ListPackage kit'in kendisi)
    /// TEK <see cref="IPriceListService.ResolveLinePricesAsync"/> çağrısıyla (flat batch, N+1 yok)
    /// belge para birimi + cari + tarih + <paramref name="direction"/> bağlamında çözülür.
    ///
    /// Review Bulgu #2/#5 (silent-loss, CLAUDE.md kural #3) TÜM modlara uygulanır: bir kit'in
    /// bileşenlerinden TEK BİRİ bile fiyatlanamazsa (ListComponent: liste fiyatı yok; FixedComponent:
    /// elle UnitPrice girilmemiş) veya ListPackage'da kit'in kendi fiyatı çözülemezse o satır
    /// dictionary'e HİÇ girmez — eksik/yanlış bir kısmi toplam asla yazılmaz, çağıran taraf
    /// mevcut/istemci UnitPrice'ı korur. Fiyatlanamayan durum LogWarning ile loglanır (sessiz kayıp değil).
    /// </summary>
    /// <param name="resolvedComponentPrices">
    /// ListComponent kitlerinde fiyat listesinden ÇÖZÜLEN bileşen birim fiyatları
    /// ((ComponentItemId, ConfigId) → fiyat). 2026-08-22: bu sözlük snapshot'a
    /// yazılarak bileşen fiyatları belgeyle birlikte DONDURULUR — kit dökümünde
    /// "fiyat kit detayından geliyorsa birim fiyatlar görünsün" isteği için.
    /// FİYATLAMAYI ETKİLEMEZ: ListComponent dalı fiyatı yine CANLI priceByKey'den
    /// okur, snapshot'taki UnitPrice'a hiç bakmaz (bkz. aşağıdaki foreach).
    /// </param>
    private async Task<IReadOnlyDictionary<int, decimal>> ResolveKitServerTruthPricesAsync(
        IReadOnlyList<SaveDocumentLineRequest> lineRequests, int? contactId, int currencyId,
        PriceDirection direction, DateTime date, CancellationToken ct,
        Dictionary<(int ItemId, int? ConfigId), decimal>? resolvedComponentPrices = null)
    {
        var result = new Dictionary<int, decimal>();
        if (_priceListService is null || lineRequests.Count == 0) return result;

        var itemIds = lineRequests.Select(l => l.ItemId).Distinct().ToArray();
        var kitContents = await _repo.GetActiveKitContentsAsync(itemIds, ct);
        // FixedPackage kit'ler bu sözlüğe hiç girmez — sunucu ezmez.
        var liveKitByItem = kitContents
            .Where(k => k.PriceMode != KitPriceMode.FixedPackage && k.Components.Count > 0)
            .ToDictionary(k => k.KitItemId);
        if (liveKitByItem.Count == 0) return result;

        // Hangi (mevcut) satırlar zaten dondurulmuş bir kit snapshot'ı taşıyor? (LineId → KitItemId)
        var existingLineIds = lineRequests
            .Where(l => l.Id is > 0)
            .Select(l => l.Id!.Value)
            .Distinct()
            .ToArray();
        var frozenKitByLineId = existingLineIds.Length == 0
            ? new Dictionary<int, int>()
            : (await _repo.GetExistingKitSnapshotsAsync(existingLineIds, ct))
                .ToDictionary(x => x.LineId, x => x.KitItemId);

        // ListPackage satırları — kit'in KENDİ fiyatı, bileşenlere bakılmaz.
        var listPackageLines = new Dictionary<int, PriceEntryKey>();
        // FixedComponent/ListComponent satırları — bileşen listesi (dondurulmuş ya da canlı) + mod.
        var componentModeLines = new Dictionary<int, (string Mode, List<(int CompItemId, int? ConfigId, decimal Qty, decimal? UnitPrice)> Comps)>();

        for (var i = 0; i < lineRequests.Count; i++)
        {
            var ln = lineRequests[i];
            if (!liveKitByItem.TryGetValue(ln.ItemId, out var liveKit)) continue; // kit değil / FixedPackage

            if (liveKit.PriceMode == KitPriceMode.ListPackage)
            {
                listPackageLines[i] = new PriceEntryKey(ln.ItemId, ln.CombinationId);
                continue;
            }

            var isFrozenForThisKit = ln.Id is > 0
                && frozenKitByLineId.TryGetValue(ln.Id.Value, out var frozenKitItemId)
                && frozenKitItemId == ln.ItemId;

            List<(int CompItemId, int? ConfigId, decimal Qty, decimal? UnitPrice)> comps;
            if (isFrozenForThisKit)
            {
                var snap = await _repo.GetKitSnapshotAsync(ln.Id!.Value, ct);
                comps = snap.Select(s => (CompItemId: s.ComponentItemId, s.ConfigId, Qty: s.Quantity, s.UnitPrice)).ToList();
            }
            else
            {
                comps = liveKit.Components.Select(c => (CompItemId: c.ComponentItemId, c.ConfigId, Qty: c.Quantity, c.UnitPrice)).ToList();
            }
            if (comps.Count > 0) componentModeLines[i] = (liveKit.PriceMode, comps);
        }
        if (listPackageLines.Count == 0 && componentModeLines.Count == 0) return result;

        // Fiyat listesinden çözülmesi gereken key'ler: ListComponent bileşenleri + ListPackage kit'in
        // kendisi. FixedComponent bileşenlerinin key'leri buraya GİRMEZ (elle fiyat, liste çağrısı yok).
        var listComponentKeys = componentModeLines
            .Where(kv => kv.Value.Mode == KitPriceMode.ListComponent)
            .SelectMany(kv => kv.Value.Comps)
            .Select(c => new PriceEntryKey(c.CompItemId, c.ConfigId));
        var allKeys = listComponentKeys.Concat(listPackageLines.Values).Distinct().ToArray();

        var priceByKey = new Dictionary<(int, int?), decimal>();
        if (allKeys.Length > 0)
        {
            var resolved = await _priceListService.ResolveLinePricesAsync(
                new ResolveLinePricesRequest(contactId, currencyId, direction, date, allKeys), ct);
            priceByKey = resolved
                .Where(r => r.Price.HasValue)
                .ToDictionary(r => (r.ItemId, r.ConfigId), r => r.Price!.Value);
        }

        // Cozumlenen bilesen fiyatlarini cagirana ver (snapshot dondurmasi icin).
        if (resolvedComponentPrices is not null)
        {
            foreach (var kv in priceByKey) resolvedComponentPrices[kv.Key] = kv.Value;
        }

        // ── ListPackage: kit'in KENDİ liste fiyatı ──
        foreach (var (idx, key) in listPackageLines)
        {
            if (priceByKey.TryGetValue((key.ItemId, key.ConfigId), out var kitPrice))
            {
                result[idx] = kitPrice;
            }
            else
            {
                _logger?.LogWarning(
                    "Kit ListPackage fiyatlama: kit ItemId={KitItemId} icin Genel Liste/cari fiyati " +
                    "bulunamadi (satir index={LineIndex}); mevcut/istemci birim fiyati korunuyor.",
                    key.ItemId, idx);
            }
        }

        // ── FixedComponent / ListComponent: bileşen toplamı ──
        foreach (var (idx, entry) in componentModeLines)
        {
            var mode = entry.Mode;
            decimal sum = 0m;
            var allResolved = true;
            foreach (var comp in entry.Comps)
            {
                decimal compPrice;
                if (mode == KitPriceMode.FixedComponent)
                {
                    if (!comp.UnitPrice.HasValue)
                    {
                        allResolved = false;
                        _logger?.LogWarning(
                            "Kit FixedComponent fiyatlama: bileşen ItemId={ComponentItemId} (ConfigId={ConfigId}) " +
                            "icin kit tanımında elle birim fiyat girilmemiş (satir index={LineIndex}, Kit ItemId={KitItemId}).",
                            comp.CompItemId, comp.ConfigId, idx, lineRequests[idx].ItemId);
                        continue;
                    }
                    compPrice = comp.UnitPrice.Value;
                }
                else // ListComponent
                {
                    if (!priceByKey.TryGetValue((comp.CompItemId, comp.ConfigId), out compPrice))
                    {
                        allResolved = false;
                        _logger?.LogWarning(
                            "Kit ListComponent fiyatlama: bileşen ItemId={ComponentItemId} (ConfigId={ConfigId}) icin " +
                            "Genel Liste/cari fiyati bulunamadi (satir index={LineIndex}, ItemId={KitItemId}).",
                            comp.CompItemId, comp.ConfigId, idx, lineRequests[idx].ItemId);
                        continue;
                    }
                }
                sum += compPrice * comp.Qty;
            }
            // Bulgu #2/#5 — TEK bileşen bile fiyatsızsa yanlış (eksik) toplam YAZILMAZ; satır
            // dictionary'e hiç girmez, çağıran taraf mevcut/istemci UnitPrice'ı korur.
            if (allResolved)
                result[idx] = sum;
            else
                _logger?.LogWarning(
                    "Kit {Mode} fiyatlama: satir index={LineIndex} (Kit ItemId={KitItemId}) en az bir " +
                    "bileşeni fiyatlanamadığı icin fiyat UYGULANMADI; mevcut/istemci birim fiyati korunuyor.",
                    mode, idx, lineRequests[idx].ItemId);
        }
        return result;
    }

    // ── İşlem logu (audit trail) yardımcıları ──────────────────────────────
    // Belgenin audit entity kodu = DocumentType.Code ("satis_siparisi", "alis_talebi"...).
    // Liste ekranlarının İşlemler menüsündeki "İşlem Logu" öğesi aynı kodla sorgular
    // (/AuditLog?entity=<kod>&recordId=<id>). 2026-07-19: kayıt içindeki "Değişiklik
    // Geçmişi" sekmesi kaldırıldı, erişim listeye taşındı — kod sözleşmesi DEĞİŞMEDİ.

    private async Task<string> ResolveAuditEntityAsync(int? documentTypeId, CancellationToken ct)
    {
        if (documentTypeId is > 0)
        {
            try
            {
                var dt = await _documentTypeRepo.GetByIdAsync(documentTypeId.Value, ct);
                if (!string.IsNullOrWhiteSpace(dt?.Code)) return dt!.Code;
            }
            catch { /* audit için tip çözümlenemedi — genel koda düş */ }
        }
        return "belge";
    }

    /// <summary>
    /// 2026-07-20 — Miktar/bağlantı guard'ı (consumed/derived/iş emri tahsisi) bir kaydı
    /// reddettiğinde "başarısız işlem" olarak loglar (CLAUDE.md audit kuralı: reddi logla).
    /// Ayrı bir Insert/Update/Delete DEĞİL — kayıt hiç değişmedi; bu yüzden AuditActions.Event
    /// (serbest olay) kullanılır. Audit yazımı iş akışını asla bozmaz (_audit null-conditional +
    /// AuditTrailService'in kendi iç try/catch'i zaten var).
    /// </summary>
    private async Task LogGuardRejectionAsync(int? documentTypeId, int documentId, string reason, CancellationToken ct)
    {
        if (_audit is null) return;
        var entity = await ResolveAuditEntityAsync(documentTypeId, ct);
        _audit.LogEvent(AuditActions.Event,
            detail: $"Kayıt reddedildi (miktar/bağlantı guard'ı): {reason}",
            entity: entity, entityId: documentId);
    }

    /// <summary>
    /// PageComment Seq 1068 — "KDV Dahil" net-out yardımcısı. IsVatIncluded=true iken satırda
    /// girilen UnitPrice KDV dahil (brüt) kabul edilir; hesaplamalarda (LineTotal/SubTotal)
    /// kullanılacak net birim fiyatı header TaxRate ile geri hesaplar. Switch kapalıyken veya
    /// TaxRate 0/negatifse no-op (UnitPrice zaten net kabul edilir). Persist edilen
    /// DocumentLine.UnitPrice kullanıcının girdiği ham (brüt) değerdir — yalnız hesap bu neti kullanır.
    /// Clone/dönüştürme akışlarında da (SaveQuoteAsync ile) aynı sonucu vermesi için tek kaynak.
    /// </summary>
    private static decimal NetUnitPrice(decimal unitPrice, decimal taxRate, bool isVatIncluded)
    {
        if (!isVatIncluded || taxRate <= 0) return unitPrice;
        var divisor = 1m + (taxRate / 100m);
        return divisor == 0 ? unitPrice : unitPrice / divisor;
    }

    /// <summary>Header diff snapshot'ı — yalnızca kullanıcı tarafından düzenlenen alanlar
    /// (türetilmiş SubTotal/TaxAmount/DiscountAmount gürültü olmasın diye dışarıda).</summary>
    private static object SnapHeader(Document d) => new
    {
        d.DocumentDate,
        d.ValidUntil,
        d.DeliveryDate,
        d.DeliveryDays,
        d.ContactName,
        d.ContactAddress,
        d.SalesRepId,
        d.RequesterPersonnelId,
        d.LocationId,
        d.CurrencyId,
        d.ExchangeRate,
        d.IsVatIncluded,
        d.RateDate,
        d.SourceDocumentNo,
        d.DiscountRate,
        d.TaxRate,
        d.GrandTotal,
        d.PaymentTerms,
        d.DeliveryTerms,
        d.DeliveryAddress,
        d.Notes,
    };

    /// <summary>
    /// Kalem satırları diff'i — eklenen/silinen/değişen satırları alan değişikliği
    /// listesine çevirir (yalnızca değişenler). Eşleştirme iki geçişlidir:
    ///   1) Satır Id eşleşmesi (normal upsert — Id korunur).
    ///   2) Id ile eşleşmeyenler içerik anahtarıyla (ItemId + CombinationId, LineNo sıralı)
    ///      eşleştirilir — bazı ekranlar satır Id'sini göndermediği için SaveLinesAsync
    ///      DELETE+INSERT çalışır ve Id değişir; bu geçiş olmadan basit bir miktar
    ///      düzeltmesi "silindi + eklendi" olarak raporlanırdı.
    /// </summary>
    private static List<AuditFieldChange> BuildLineChanges(
        IReadOnlyCollection<DocumentLine> oldLines, IReadOnlyCollection<DocumentLine> newLines)
    {
        var changes = new List<AuditFieldChange>();
        var oldById = oldLines.ToDictionary(l => l.Id);
        var newById = newLines.ToDictionary(l => l.Id);

        static string LineName(DocumentLine l) =>
            l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId);
        static string LineSummary(DocumentLine l) =>
            $"{AuditDiff.Normalize(l.Quantity)} {l.UnitCode ?? "birim"} × {AuditDiff.Normalize(l.UnitPrice)}";
        static string ContentKey(DocumentLine l) =>
            l.ItemId + "|" + (l.CombinationId?.ToString() ?? "");

        // 1. geçiş — Id eşleşmesi
        var pairs = new List<(DocumentLine Old, DocumentLine New)>();
        var unmatchedOld = new List<DocumentLine>();
        foreach (var o in oldLines)
        {
            if (newById.TryGetValue(o.Id, out var n)) pairs.Add((o, n));
            else unmatchedOld.Add(o);
        }
        var unmatchedNew = newLines.Where(n => !oldById.ContainsKey(n.Id)).ToList();

        // 2. geçiş — içerik anahtarı eşleşmesi (aynı malzeme+kombinasyon, LineNo sırasıyla bire bir)
        var newByKey = unmatchedNew
            .GroupBy(ContentKey)
            .ToDictionary(g => g.Key, g => new Queue<DocumentLine>(g.OrderBy(l => l.LineNo)));
        var removedLines = new List<DocumentLine>();
        foreach (var o in unmatchedOld.OrderBy(l => l.LineNo))
        {
            if (newByKey.TryGetValue(ContentKey(o), out var q) && q.Count > 0)
                pairs.Add((o, q.Dequeue()));
            else
                removedLines.Add(o);
        }
        var addedLines = newByKey.Values.SelectMany(q => q).OrderBy(l => l.LineNo);

        foreach (var o in removedLines)
            changes.Add(new AuditFieldChange($"Line[{o.Id}]", $"Kalem Silindi — {LineName(o)}", LineSummary(o), null));

        foreach (var n in addedLines)
            changes.Add(new AuditFieldChange($"Line[{n.Id}]", $"Kalem Eklendi — {LineName(n)}", null, LineSummary(n)));

        foreach (var (o, n) in pairs)
        {
            var name = LineName(n);
            void AddIfChanged(string field, string label, object? oldVal, object? newVal)
            {
                var os = AuditDiff.Normalize(oldVal);
                var ns = AuditDiff.Normalize(newVal);
                if (!string.Equals(os ?? "", ns ?? "", StringComparison.Ordinal))
                    changes.Add(new AuditFieldChange($"Line[{n.Id}].{field}", $"{name} · {label}", os, ns));
            }
            AddIfChanged("Quantity", "Miktar", o.Quantity, n.Quantity);
            AddIfChanged("UnitPrice", "Birim Fiyat", o.UnitPrice, n.UnitPrice);
            AddIfChanged("DiscountRate", "İskonto %", o.DiscountRate, n.DiscountRate);
            AddIfChanged("Unit", "Birim", o.UnitCode, n.UnitCode);
            AddIfChanged("Location", "Lokasyon", o.LocationName ?? o.LocationCode, n.LocationName ?? n.LocationCode);
            AddIfChanged("Combination", "Kombinasyon", o.CombinationCode, n.CombinationCode);
            AddIfChanged("Notes", "Not", o.Notes, n.Notes);
        }
        return changes;
    }

    /// <summary>
    /// Belge hesaplarında kullanılacak etkili ondalık ayarı. Servis kayıtlı
    /// değilse (Worker vb.) fallback (2,2,2,2,4) — davranış asla bloklanmaz.
    /// </summary>
    private async Task<EffectiveDecimalsDto> ResolveDecimalsAsync(string formCode, CancellationToken ct)
    {
        if (_decimalSettings is null) return EffectiveDecimalsDto.Fallback(formCode);
        try { return await _decimalSettings.GetEffectiveAsync(formCode, ct); }
        catch { return EffectiveDecimalsDto.Fallback(formCode); }
    }

    /// <summary>
    /// Faz P — Yeni belge numarası türetme: önce DocumentNumberRule kontrol et,
    /// kural yoksa eski default davranışa fallback (repo.GetNextDocumentNumberAsync).
    /// Yeni belge tipleri için admin DocumentNumberRule tanımlayınca otomatik etkin olur.
    /// </summary>
    private async Task<string> ResolveNextDocumentNumberAsync(
        int documentTypeId, int? contactId, int? userId, DateTime issueDate, CancellationToken ct)
    {
        if (_docNumberService is not null)
        {
            // ContactGroupId resolve — Contact var ise grubunu çek
            int? contactGroupId = null;
            if (contactId is > 0)
            {
                var contact = await _financeService.GetContactByIdAsync(contactId.Value, ct);
                contactGroupId = contact?.ContactGroupId;
            }
            var ctx = new DocumentNumberContext(
                DocumentTypeId: documentTypeId,
                ContactId:      contactId,
                ContactGroupId: contactGroupId,
                UserId:         userId,
                BranchId:       null,             // ileride
                IssueDate:      issueDate);
            var generated = await _docNumberService.GenerateNextAsync(ctx, ct);
            if (!string.IsNullOrWhiteSpace(generated)) return generated;
        }

        // Fallback: kural yok → eski sayaç (TKL{yyMM}{seq}) davranışı korunur (geriye uyum)
        return await _repo.GetNextDocumentNumberAsync(ct);
    }

    private async Task<int?> ResolveDefaultQuoteTypeIdAsync(CancellationToken ct)
    {
        var type = await _documentTypeRepo.GetByCodeAsync(DefaultSalesQuoteTypeCode, ct);
        return type?.Id;
    }

    private async Task<int?> ResolveDefaultOrderTypeIdAsync(CancellationToken ct)
    {
        var type = await _documentTypeRepo.GetByCodeAsync(DefaultSalesOrderTypeCode, ct);
        return type?.Id;
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct)
    {
        var quotes = await _repo.GetAllAsync(search, status, ct);
        return quotes.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.CurrencyId, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            null,
            q.CurrencyCode, q.CurrencySymbol
        )).ToArray();
    }

    /// <summary>Bir cariye ait tum tekliflerin ozet listesi (cari karti "Verilen Teklifler" sekmesi icin).</summary>
    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesByContactAsync(int contactId, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(null, null, ct);
        return all.Where(q => q.ContactId == contactId)
            .Select(q => new DocumentListItemDto(
                q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
                q.ContactName, q.CurrencyId, q.GrandTotal,
                q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
                q.ContactId,
                q.DocumentTypeId,
                q.CurrencyCode, q.CurrencySymbol
            )).ToArray();
    }

    /// <summary>Bir cariye ait tum hareketler (belge tipi + tarih aralgi filtresi).</summary>
    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetMovementsByContactAsync(
        int contactId, int? documentTypeId, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(null, null, ct);
        var q = all.Where(d => d.ContactId == contactId);
        if (documentTypeId is > 0) q = q.Where(d => d.DocumentTypeId == documentTypeId);
        if (fromDate.HasValue)     q = q.Where(d => d.DocumentDate >= fromDate.Value.Date);
        if (toDate.HasValue)       q = q.Where(d => d.DocumentDate <= toDate.Value.Date);
        return q.OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Id)
            .Select(d => new DocumentListItemDto(
                d.Id, d.DocumentNumber, d.DocumentDate, d.ValidUntil,
                d.ContactName, d.CurrencyId, d.GrandTotal,
                d.Status.ToString(), d.RevisionNo, d.IsActive, d.LineCount,
                d.ContactId,
                d.DocumentTypeId,
                d.CurrencyCode, d.CurrencySymbol
            )).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct)
    {
        var docs = await _repo.GetByTypeAsync(typeCode, search, status, ct);
        return docs.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.CurrencyId, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            q.DocumentTypeId,
            q.CurrencyCode, q.CurrencySymbol,
            q.RequesterPersonnelId, q.RequesterPersonnelName,
            FulfillPending: q.FulfillPending,
            FulfillPartial: q.FulfillPartial,
            FulfillFull:    q.FulfillFull
        )).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct)
    {
        // document_source tablosu yoksa olustur — modal/repo NOT EXISTS subquery'sinin
        // calismasi icin garanti gerekir. Idempotent.
        await _docSourceRepo.EnsureSchemaAsync(ct);
        var docs = await _repo.GetConvertibleQuotesAsync(fromDate, toDate, contactId, search, ct);
        return docs.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.CurrencyId, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            q.DocumentTypeId,
            q.CurrencyCode, q.CurrencySymbol
        )).ToArray();
    }

    public async Task<DocumentDto?> GetQuoteByIdAsync(int id, CancellationToken ct)
    {
        var q = await _repo.GetByIdAsync(id, ct);
        return q == null ? null : MapDto(q);
    }

    public Task<IReadOnlyCollection<DocumentLineKitComponent>> GetKitLineComponentsAsync(int documentLineId, CancellationToken ct)
        => _repo.GetKitSnapshotAsync(documentLineId, ct);

    public async Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(int documentId, CancellationToken ct)
    {
        var lines = await _repo.GetLinesAsync(documentId, ct);
        var result = new List<DocumentLineDto>(lines.Count);
        foreach (var ln in lines)
        {
            var details = await _repo.GetLineDetailsAsync(ln.Id, ct);
            var detailDtos = details
                .Select(d => new DocumentLineDetailDto(
                    d.Id, d.QuoteLineId, d.FeatureName, d.ValueCode, d.ValueName, d.Description, d.LineOrder))
                .ToList();
            result.Add(MapLineDto(ln, detailDtos));
        }
        return result;
    }

    /// <summary>
    /// Bağlantı-kilitli satırlar — SaveQuoteAsync'teki koruma ile AYNI taban formülü:
    /// floor = max(karşılanan miktar, türetilmiş aktif satır miktar toplamı, iş emri tahsisi)
    /// — bkz. <see cref="ComputeLineFloor"/> (2026-07-20: iş emri tahsisi eklendi). Kilitli
    /// olmayan (floor yok) satırlar döndürülmez. UI önden silme engeli + miktar tabanı için kullanır.
    /// </summary>
    public async Task<IReadOnlyList<DocumentLineLockDto>> GetLineLocksAsync(int documentId, CancellationToken ct)
    {
        var result = new List<DocumentLineLockDto>();
        if (documentId <= 0) return result;
        var lines = await GetQuoteLinesAsync(documentId, ct);
        if (lines.Count == 0) return result;

        var derivedAgg = await _repo.GetDerivedLineAggregatesAsync(documentId, ct);
        var woAlloc = await GetWorkOrderAllocationsAsync(documentId, ct);
        foreach (var ln in lines)
        {
            // consumed: karşılanan miktar. İhtiyaç dışı türlerde Fulfilled* zaten 0 (domain
            // invariant) → SaveQuoteAsync'teki isPurchaseRequest-gate ile aynı sonucu verir.
            var consumed   = ln.FulfilledFromStock + ln.FulfilledByPurchase;
            var hasDerived = derivedAgg.TryGetValue(ln.Id, out var da);
            var derivedQty = hasDerived ? da.QtySum : 0m;
            var woQty      = woAlloc.TryGetValue(ln.Id, out var wq) ? wq : 0m;
            var source = DominantLineFloorSource(consumed, derivedQty, woQty);
            if (source == LineFloorSource.None) continue;

            var floor  = ComputeLineFloor(consumed, derivedQty, woQty);
            var reason = source switch
            {
                LineFloorSource.WorkOrder => $"Bu kalemden {woQty:0.##} birim iş emrine tahsis edildiği için silinemez.",
                LineFloorSource.Derived   => $"Bu kalemden türetilmiş {da.Count} belge satırı olduğu için silinemez.",
                _                         => $"Bu kalem {consumed:0.##} birim karşılandığı için silinemez.",
            };
            result.Add(new DocumentLineLockDto(ln.Id, floor, reason));
        }
        return result;
    }

    /// <summary>Istemciden gelen base64 ROWVERSION damgasini cozer; bozuksa null (kontrol atlanir).</summary>
    private static byte[]? ParseRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); } catch (FormatException) { return null; }
    }

    /// <summary>Durum kilidi mesajinda kullanilan Turkce durum etiketi.</summary>
    private static string DocumentStatusText(DocumentStatus status) => status switch
    {
        DocumentStatus.Approved  => "onaylanmis",
        DocumentStatus.Converted => "donusturulmus",
        DocumentStatus.Cancelled => "iptal edilmis",
        DocumentStatus.Revised   => "revize edilmis",
        _                        => status.ToString(),
    };

    public async Task<(bool Success, string? Error, DocumentDto? Quote, bool ApprovalStarted)> SaveQuoteAsync(
        SaveDocumentRequest request, int? createdById, string? startedByUser, CancellationToken ct,
        IReadOnlyCollection<PendingFulfillmentEntry>? fulfillmentEntries = null)
    {
        // ── Cari cozumleme ─────────────────────────────────────
        // contact_id otorite kaynaktir; client ContactName gondermiyor (label),
        // ID cozumlendikten sonra Contact'tan live AccountTitle alinir (display/rapor icin).
        int? resolvedContactId = request.ContactId;
        string? resolvedContactName = request.ContactName;

        if (!resolvedContactId.HasValue && !string.IsNullOrWhiteSpace(request.ContactCode))
        {
            // Koda gore tek satir cek (search param'i ile filtreli — tum cari listesini yuklemez)
            var code = request.ContactCode.Trim();
            var matches = await _financeService.GetContactsAsync(null, code, ct);
            var exact = matches.FirstOrDefault(a =>
                a.IsActive && string.Equals(a.AccountCode, code, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                resolvedContactId = exact.Id;
                resolvedContactName = exact.AccountTitle;
            }
        }
        else if (resolvedContactId.HasValue)
        {
            // ID set edilmisse tek satirlik PK sorgusu — full list yuklemekten cok daha hizli.
            var live = await _financeService.GetContactByIdAsync(resolvedContactId.Value, ct);
            if (live != null && live.IsActive)
                resolvedContactName = live.AccountTitle;
        }

        // 2026-05-23: İhtiyaç Kaydı (alis_talebi) bir IC belge — tedarikci/musteri
        // bu asamada belli degil. Cari Kod zorunlulugu sadece diger belge tiplerinde geçerli.
        var isPurchaseRequest = false;
        // 2026-08-22 DUZELTME: Satin Alma Talebi (satin_alma_talebi) de AYNI sinifta bir
        // IC belgedir — tedarikci henuz belli degil; /Purchase/CreatePurchaseDemand zaten
        // bilincli olarak ContactId/ContactName NULL gonderiyor. Muafiyet listesi yalniz
        // 'alis_talebi' icerdigi icin bu uc HER CAGRIDA "Cari (musteri) zorunludur" ile
        // reddediliyordu — yani Ihtiyac Kaydi'ndan Satin Alma Talebi uretmek HIC
        // calismiyordu. (Fonksiyon testi bu hatayi acikca gosterdi.)
        var isInternalProcurementDoc = false;
        CalibraHub.Domain.Entities.DocumentType? documentType = null;
        if (request.DocumentTypeId.HasValue)
        {
            documentType = await _documentTypeRepo.GetByIdAsync(request.DocumentTypeId.Value, ct);
            isPurchaseRequest = string.Equals(documentType?.Code, "alis_talebi", StringComparison.OrdinalIgnoreCase);
            isInternalProcurementDoc = isPurchaseRequest
                || string.Equals(documentType?.Code, "satin_alma_talebi", StringComparison.OrdinalIgnoreCase);
        }
        if (!isInternalProcurementDoc && !resolvedContactId.HasValue && string.IsNullOrWhiteSpace(resolvedContactName))
            return (false, "Cari (musteri) zorunludur. Kalem eklemeden once cari seciniz.", null, false);
        // 2026-06-01: İhtiyaç Kaydı (alis_talebi) için Talep Eden personel zorunlu —
        // onay akışı + raporlama bu personel üzerinden ilerler. Frontend de aynı
        // kontrolü uyguluyor ama API'ye direkt POST atılırsa burada yakalanır.
        if (isPurchaseRequest && (!request.RequesterPersonnelId.HasValue || request.RequesterPersonnelId.Value <= 0))
            return (false, "İhtiyaç Kaydı için 'Talep Eden' personel seçilmelidir.", null, false);
        if (request.Lines.Count == 0)
            return (false, "En az bir satir eklenmeli.", null, false);

        // Kombinasyon takibi acik olan stok icin kombinasyon ID zorunlu
        // Kombinasyon zorunlu kontrolü — mesajda satır numarası (opak ItemId değil).
        // TrackCombinations bayrağı payload'dan gelir; frontend bunu stok kartı (master)
        // değerinden doldurur (bkz. DocumentEdit save map, 2026-07-08 hizalama).
        //
        // Miktar sıfır/negatif satır engeli (2026-07-19) — proje genelinde yerleşik kural
        // (bkz. CalibroDocumentTools, BomImportHandler, InventoryCountImportHandler,
        // WorkOrderService — hepsi "miktar > 0" uygular). Burada eksikti: istemci ekranı
        // miktarı boş bırakırsa satır Quantity=0 ile sessizce kaydoluyordu. SaveDocumentRequestValidator
        // (FluentValidation) aynı kuralı taşır ama otomatik doğrulama yalnız [ApiController]
        // action'larında devreye girer (bkz. Program.cs yorumu) — Sales/PurchaseController klasik
        // MVC controller'dır, o katman bu isteği hiç görmez. Asıl garanti bu yüzden serviste.
        var lineList = request.Lines.ToList();
        for (var i = 0; i < lineList.Count; i++)
        {
            var ln = lineList[i];
            if (ln.Quantity <= 0)
                return (false, $"{i + 1}. kalemde miktar sıfırdan büyük olmalı.", null, false);
            if (ln.TrackCombinations && (!ln.CombinationId.HasValue || ln.CombinationId.Value <= 0))
            {
                return (false, $"{i + 1}. kalemde kombinasyon takibi açık; kombinasyon seçilmelidir.", null, false);
            }
        }

        // Malzeme Belge Kilitleri (ItemDocumentLock) — sert kapı (2026-08-01, üretimdeki
        // WorkOrderService.CreateAsync/CreateFromSalesLineAsync ile aynı desen — bkz. o dosyadaki
        // "Malzeme Belge Kilitleri" yorumu). DocumentType.Code (satis_teklifi/satis_siparisi/
        // satis_irsaliyesi/alis_talebi/alis_teklifi/alis_siparisi/satin_alma_talebi/alis_irsaliyesi)
        // ItemDocumentLock.DocType ile birebir aynı kod sözlüğünü paylaşır (ItemDocumentLockTypes),
        // ayrı bir eşleme gerekmez. Tek sorgu: bu belge tipi için kilitli TÜM itemId'ler önce
        // çekilir, sonra kalem listesiyle bellek içinde kesişim alınır (N+1 yok).
        if (_itemLocks is not null && ItemDocumentLockTypes.IsValidCode(documentType?.Code))
        {
            var lockedItemIdSet = (await _itemLocks.GetLockedItemIdsByDocTypeAsync(documentType!.Code, ct)).ToHashSet();
            if (lockedItemIdSet.Count > 0)
            {
                var lockedInLines = lineList.Select(l => l.ItemId).Distinct()
                    .Where(id => lockedItemIdSet.Contains(id)).ToList();
                if (lockedInLines.Count > 0)
                {
                    var lockedItems = await _itemLocks.GetItemsByIdsAsync(lockedInLines, ct);
                    var labels = lockedInLines.Select(id =>
                        lockedItems.FirstOrDefault(x => x.Id == id)?.Code ?? $"#{id}");
                    var docLabel = ItemDocumentLockTypes.LabelFor(documentType.Code);
                    var msg = $"{string.Join(", ", labels)} malzemesi '{docLabel}' için kilitli — " +
                        "Malzeme Belge Kilitleri ekranından kilidi kaldırın.";
                    return (false, msg, null, false);
                }
            }
        }

        request = request with { ContactId = resolvedContactId, ContactName = resolvedContactName };

        var isNew = !request.Id.HasValue || request.Id.Value == 0;

        // ── Bağlantı bütünlüğü koruması (güncelleme) ───────────────────────────
        // Kaynak kalem, bağlantıyı bozacak şekilde düzenlenemez/silinemez:
        //  1) İhtiyaç zinciri: karşılanan (FulfilledFromStock+ByPurchase) miktar tabandır.
        //  2) Dönüşüm zinciri (teklif→sipariş vb.): başka AKTİF belgede SourceLineId ile
        //     referans alınan kalem silinemez; miktarı türetilmiş toplamın altına inemez.
        //  3) İş emri tahsisi (2026-07-20): WorkOrderSource ile bu kaleme tahsis edilmiş miktar
        //     da tabandır. Üç kaynak (consumed/derived/woAlloc) AYRIDIR ve TOPLANMAZ — floor
        //     üçünden BÜYÜK OLANI (bkz. ComputeLineFloor). Ör. sipariş 100, iş emri tahsisi 90,
        //     teslimat 95 → floor = max(90,95) = 95 (185 değil).
        // Bağlantıyı etkilemeyen düzenlemeler (not, fiyat, artırma...) serbesttir.
        if (!isNew && request.Id.HasValue)
        {
            var derivedAgg = await _repo.GetDerivedLineAggregatesAsync(request.Id.Value, ct);
            var woAlloc = await GetWorkOrderAllocationsAsync(request.Id.Value, ct);
            if (isPurchaseRequest || derivedAgg.Count > 0 || woAlloc.Count > 0)
            {
                var existingLines = await GetQuoteLinesAsync(request.Id.Value, ct);
                var incomingById = request.Lines
                    .Where(l => l.Id.HasValue && l.Id.Value > 0)
                    .GroupBy(l => l.Id!.Value)
                    .ToDictionary(g => g.Key, g => g.First());
                foreach (var ex in existingLines)
                {
                    var consumed = isPurchaseRequest ? ex.FulfilledFromStock + ex.FulfilledByPurchase : 0m;
                    var hasDerived = derivedAgg.TryGetValue(ex.Id, out var da);
                    var derivedQty = hasDerived ? da.QtySum : 0m;
                    var woQty = woAlloc.TryGetValue(ex.Id, out var wq) ? wq : 0m;
                    var source = DominantLineFloorSource(consumed, derivedQty, woQty);
                    if (source == LineFloorSource.None) continue;
                    var floor = ComputeLineFloor(consumed, derivedQty, woQty);

                    var name = ex.MaterialName ?? ex.MaterialCode ?? $"#{ex.Id}";
                    var reason = source switch
                    {
                        LineFloorSource.WorkOrder => $"{woQty:0.##} birim iş emrine tahsis edildiği",
                        LineFloorSource.Derived   => $"bu kalemden türetilmiş {da.Count} belge satırı olduğu",
                        _                         => $"{consumed:0.##} birim karşılandığı",
                    };
                    if (!incomingById.TryGetValue(ex.Id, out var inc))
                    {
                        var msg = $"'{name}' kalemi {reason} için silinemez. Önce bağlantılı belgeleri geri alın.";
                        await LogGuardRejectionAsync(request.DocumentTypeId, request.Id.Value, msg, ct);
                        return (false, msg, null, false);
                    }
                    if (inc.Quantity < floor)
                    {
                        var msg = $"'{name}' kalemi {reason} için miktarı {floor:0.##} altına düşürülemez (girilen: {inc.Quantity:0.##}).";
                        await LogGuardRejectionAsync(request.DocumentTypeId, request.Id.Value, msg, ct);
                        return (false, msg, null, false);
                    }
                }
            }

            // ── Karşılama defteri koruması (2026-07-20, Madde 1) ─────────────────
            // Bu belge (satın alma siparişi/talebi) bir İhtiyaç Kaydı satırını karşılıyorsa
            // (DocumentLineFulfillment.RefDocId = bu belge), malzeme bazında miktar defterdeki
            // katkısının altına düşürülemez — aksi halde belge daha az mal hareket ettirir/daha
            // az satın alır ama defter eski (yüksek) toplamı göstermeye devam eder. Yukarıdaki
            // guard'ın (kaynak taraf, isPurchaseRequest) TERSİ: burada bu belge alis_talebi
            // OLMADIĞI için (RefDocId hiçbir zaman kendi Id'si olamaz) doğal olarak no-op kalır,
            // yalnız karşılayan belge türlerinde (alis_siparisi/satin_alma_talebi) devreye girer.
            var fulfillmentFloor = await _repo.GetFulfillmentContributionByItemAsync(request.Id.Value, ct);
            if (fulfillmentFloor.Count > 0)
            {
                var incomingQtyByItem = request.Lines
                    .GroupBy(l => l.ItemId)
                    .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
                foreach (var (itemId, floorInfo) in fulfillmentFloor)
                {
                    var incomingQty = incomingQtyByItem.GetValueOrDefault(itemId);
                    if (incomingQty < floorInfo.FloorQty)
                    {
                        var label = floorInfo.MaterialName ?? floorInfo.MaterialCode ?? $"#{itemId}";
                        return (false,
                            $"{label} malzemesi bu belgede {floorInfo.FloorQty:0.##} birim ihtiyaç karşılıyor; " +
                            $"miktarı {floorInfo.FloorQty:0.##} altına düşürülemez (girilen: {incomingQty:0.##}).",
                            null, false);
                    }
                }
            }
        }
        var effectiveTypeIdForNumber = request.DocumentTypeId ?? await ResolveDefaultQuoteTypeIdAsync(ct) ?? 0;
        var quoteNumber = isNew
            ? await ResolveNextDocumentNumberAsync(
                documentTypeId: effectiveTypeIdForNumber,
                contactId:      request.ContactId,
                userId:         null,    // current user — controller catch eder, ileride pass
                issueDate:      request.DocumentDate,
                ct)
            : "";

        // Satirlari hesapla — ondalik ayari form bazinda cozumlenir (Ayarlar → Ondalik Ayarlari)
        var decimals = await ResolveDecimalsAsync(
            isPurchaseRequest ? FormCodes.PurchaseRequest : FormCodes.SalesQuote, ct);
        var lineRequests = request.Lines.ToArray();

        // Seq 1073 Part B, 4 moda genişletildi Seq 1078 — FixedComponent/ListPackage/ListComponent
        // kit satırlarının birim fiyatı burada server-truth olarak çözülür (pricing loop'undan
        // ÖNCE — subTotal/taxAmount/grandTotal bu değeri yansıtsın). Yön (review Bulgu #4): bu
        // belge İhtiyaç Kaydı (alis_talebi) ise Purchase, aksi halde Sales — SaveQuoteAsync hem
        // satış hem satın alma belgelerini işler, sabit 's' YANLIŞTI. Sözlük ItemId DEĞİL,
        // lineRequests DİZİN'i ile anahtarlanır (Bulgu #1 — aynı kit birden fazla satırda farklı
        // dondurma durumunda olabilir).
        // 2026-08-22: cozulen BILESEN fiyatlari da toplanir — asagida kit snapshot'ina
        // yazilip belgeyle birlikte dondurulur (kit dokumunde birim fiyat gosterimi).
        var kitComponentPrices = new Dictionary<(int ItemId, int? ConfigId), decimal>();
        var kitServerPrices = await ResolveKitServerTruthPricesAsync(
            lineRequests,
            resolvedContactId,
            request.CurrencyId > 0 ? request.CurrencyId : 1,
            isPurchaseRequest ? PriceDirection.Purchase : PriceDirection.Sales,
            request.DocumentDate, ct, kitComponentPrices);

        decimal subTotal = 0;
        var lineEntities = new List<DocumentLine>(lineRequests.Length);
        int lineNo = 1;
        for (var lineIdx = 0; lineIdx < lineRequests.Length; lineIdx++)
        {
            var ln = lineRequests[lineIdx];
            // FixedComponent/ListPackage/ListComponent kit satırıysa hesaplanan server-truth fiyat
            // kullanılır (istemci değeri ezilir); FixedPackage modu / kit-olmayan satırlarda
            // dictionary boş → ln.UnitPrice korunur.
            var isServerTruth = kitServerPrices.TryGetValue(lineIdx, out var serverPrice);
            var effectiveUnitPrice = isServerTruth ? serverPrice : ln.UnitPrice;

            var lineDiscountMultiplier = 1m - (ln.DiscountRate / 100m);
            // PageComment Seq 1068 — IsVatIncluded=true iken UnitPrice brüt kabul edilir;
            // LineTotal/SubTotal header TaxRate ile geri hesaplanan net birim üzerinden hesaplanır.
            // Review Bulgu #3 (Seq 1073) + Seq 1078 genişlemesi — server-truth kit fiyatı ZATEN net
            // (Genel Liste veya kit tanımındaki elle fiyat, ikisi de KDV hariç); net-out ikinci kez
            // uygulanırsa KDV kadar EKSİK hesaplanır. Bu yüzden net-out SADECE istemci-girişli
            // (server-truth OLMAYAN — FixedPackage/kit-dışı) satırlara uygulanır.
            var netUnitPrice = isServerTruth
                ? effectiveUnitPrice
                : NetUnitPrice(effectiveUnitPrice, request.TaxRate, request.IsVatIncluded);
            var lineTotal = decimals.RoundAmount(ln.Quantity * netUnitPrice * lineDiscountMultiplier);
            subTotal += lineTotal;
            lineEntities.Add(new DocumentLine
            {
                // DocumentId 0 — UpsertAsync sonrasi quote.Id ile set edecegiz.
                // Id dolu gelirse repository UPDATE uygular; yoksa INSERT ederek yeni IDENTITY alir.
                Id = ln.Id ?? 0,
                LineNo = lineNo++,
                ItemId = ln.ItemId,
                UnitId = ln.UnitId,
                Quantity = decimals.RoundQuantity(ln.Quantity),
                UnitPrice = decimals.RoundUnitPrice(effectiveUnitPrice),
                DiscountRate = decimals.RoundRate(ln.DiscountRate),
                LineTotal = lineTotal,
                CombinationId = ln.CombinationId,
                LocationId = ln.LocationId,
                Notes = ln.Notes,
                NotesPinned = ln.NotesPinned,
                RevisedFromId = ln.RevisedFromId,
            });
        }

        var discountAmount = decimals.RoundAmount(subTotal * (request.DiscountRate / 100m));
        var afterDiscount = subTotal - discountAmount;
        var taxAmount = decimals.RoundAmount(afterDiscount * (request.TaxRate / 100m));
        var grandTotal = decimals.RoundAmount(afterDiscount + taxAmount);

        // DocumentTypeId null ise varsayilan 'satis_teklifi' turune bagla
        var effectiveDocumentTypeId = request.DocumentTypeId ?? await ResolveDefaultQuoteTypeIdAsync(ct);

        // İşlem logu için eski durum snapshot'ları (yalnızca güncellemede ve audit aktifken)
        object? auditOldHeader = null;
        IReadOnlyCollection<DocumentLine>? auditOldLines = null;

        Document quote;
        if (isNew)
        {
            quote = new Document
            {
                DocumentNumber = quoteNumber,
                DocumentTypeId = effectiveDocumentTypeId,
                DocumentDate = request.DocumentDate,
                ValidUntil = request.ValidUntil,
                DeliveryDate = request.DeliveryDate,    // Faz M
                DeliveryDays = request.DeliveryDays,    // Faz M
                ContactId = request.ContactId,
                ContactName = request.ContactName,
                ContactAddress = request.ContactAddress,
                SalesRepId = request.SalesRepId,
                RequesterPersonnelId = request.RequesterPersonnelId,
                LocationId = request.LocationId,
                CurrencyId = request.CurrencyId > 0 ? request.CurrencyId : 1,
                ExchangeRate = request.ExchangeRate > 0 ? request.ExchangeRate : 1m,
                IsVatIncluded = request.IsVatIncluded,
                // TRY belgede kur 1 sabittir → kur tarihi anlamsız, NULL bırakılır.
                RateDate = (request.CurrencyId > 0 ? request.CurrencyId : 1) == 1 ? null : request.RateDate,
                // PageComment Seq 1106 — dış sistem belge no (yalnız içe aktarım doldurur; UI'dan null gelir).
                SourceDocumentNo = string.IsNullOrWhiteSpace(request.SourceDocumentNo)
                    ? null : request.SourceDocumentNo.Trim(),
                SubTotal = Math.Round(subTotal, 4),
                DiscountRate = request.DiscountRate,
                DiscountAmount = discountAmount,
                TaxRate = request.TaxRate,
                TaxAmount = taxAmount,
                GrandTotal = Math.Round(grandTotal, 4),
                PaymentTerms = request.PaymentTerms,
                DeliveryTerms = request.DeliveryTerms,
                DeliveryAddress = request.DeliveryAddress,
                Notes = request.Notes,
                CreatedById = createdById,
                Status = DocumentStatus.Draft
            };
        }
        else
        {
            var existing = await _repo.GetByIdAsync(request.Id!.Value, ct)
                    ?? throw new InvalidOperationException("Teklif bulunamadi.");

            // ── Durum kilidi (2026-08-27) ────────────────────────────────────
            // Document.IsEditable() kurali domain'de yaziliydi ama HICBIR yerden
            // cagrilmiyordu (olu kod): Approved / Converted / Cancelled / Revised bir
            // belge bu yoldan yeniden kaydedilip ezilebiliyordu. Kural burada zorlanir
            // — teslimat/rezervasyon/uretim akislari SaveQuoteAsync'ten GECMEZ (dogrudan
            // repository'ye yazarlar), dolayisiyla onlar etkilenmez.
            // Bypass yok: ice aktarim mevcut belgeyi zaten guncellemiyor (atliyor),
            // AI araclari ve diger cagiranlar da bu kurala tabidir.
            if (!existing.IsEditable())
                return (false,
                    $"Bu belge {DocumentStatusText(existing.Status)} durumunda oldugu icin duzenlenemez.",
                    null, false);

            // GetByIdAsync zaten line_count'u tek sorguda getiriyor — tekrar lines cekmeye gerek yok.
            if (existing.LineCount > 0 && existing.ContactId != request.ContactId)
                return (false, "Kalem girilmis belgenin cari kodu degistirilemez.", null, false);

            // Iyimser eszamanlilik: KARSILASTIRILACAK damga ISTEMCIDEN gelen olmali.
            // GetByIdAsync'in getirdigi (guncel) damga kullanilirsa kontrol her zaman
            // kendisiyle eslesir ve hicbir cakismayi yakalamaz.
            existing.RowVersion = ParseRowVersion(request.RowVersion);

            // İşlem logu: mutasyondan ÖNCE eski header + kalem snapshot'ı al
            if (_audit is not null)
            {
                auditOldHeader = SnapHeader(existing);
                try { auditOldLines = await _repo.GetLinesAsync(request.Id!.Value, ct); }
                catch { auditOldLines = null; }
            }

            existing.DocumentTypeId = request.DocumentTypeId ?? existing.DocumentTypeId ?? effectiveDocumentTypeId;
            existing.DocumentDate = request.DocumentDate;
            existing.ValidUntil = request.ValidUntil;
            existing.DeliveryDate = request.DeliveryDate;    // Faz M
            existing.DeliveryDays = request.DeliveryDays;    // Faz M
            existing.ContactId = request.ContactId;
            existing.ContactName = request.ContactName;
            existing.ContactAddress = request.ContactAddress;
            existing.SalesRepId = request.SalesRepId;
            existing.RequesterPersonnelId = request.RequesterPersonnelId;
            existing.LocationId = request.LocationId;
            existing.CurrencyId = request.CurrencyId > 0 ? request.CurrencyId : 1;
            existing.ExchangeRate = request.ExchangeRate > 0 ? request.ExchangeRate : 1m;
            existing.IsVatIncluded = request.IsVatIncluded;
            // TRY belgede kur 1 sabittir → kur tarihi anlamsız, NULL bırakılır.
            existing.RateDate = (request.CurrencyId > 0 ? request.CurrencyId : 1) == 1 ? null : request.RateDate;
            // PageComment Seq 1106 — dış sistem belge no yalnız DOLU gelirse yazılır. Normal ekran
            // kaydında (null) mevcut değer KORUNUR; aksi halde kullanıcının elle kaydettiği bir
            // belge, içe aktarımın kaynak eşlemesini sessizce silerdi (tekrar mükerrer üretir).
            if (!string.IsNullOrWhiteSpace(request.SourceDocumentNo))
                existing.SourceDocumentNo = request.SourceDocumentNo.Trim();
            existing.SubTotal = Math.Round(subTotal, 4);
            existing.DiscountRate = request.DiscountRate;
            existing.DiscountAmount = discountAmount;
            existing.TaxRate = request.TaxRate;
            existing.TaxAmount = taxAmount;
            existing.GrandTotal = Math.Round(grandTotal, 4);
            existing.PaymentTerms = request.PaymentTerms;
            existing.DeliveryTerms = request.DeliveryTerms;
            existing.DeliveryAddress = request.DeliveryAddress;
            existing.Notes = request.Notes;
            existing.UpdatedAt = DateTime.Now;
            quote = existing;
        }

        var savedId = await _repo.UpsertAsync(quote, ct);

        // 0 = UPDATE hicbir satiri etkilemedi → damga tutmadi (belgeyi aradaki surede
        // baska biri kaydetti). Yeni kayitta bu durum olusmaz (SCOPE_IDENTITY).
        if (!isNew && savedId == 0)
            return (false,
                "Bu belgeyi siz açtıktan sonra başka bir kullanıcı kaydetmiş. "
                + "Değişiklikleriniz kaybolmasın diye kayıt yapılmadı — sayfayı yenileyip tekrar deneyin.",
                null, false);

        // Upsert sonrasi yeni Id'yi entity'ye ata (init-only oldugu icin yeni instance)
        if (isNew)
        {
            quote = new Document
            {
                Id = savedId,
                DocumentNumber = quote.DocumentNumber,
                DocumentTypeId = quote.DocumentTypeId,
                DocumentDate = quote.DocumentDate,
                ValidUntil = quote.ValidUntil,
                DeliveryDate = quote.DeliveryDate,    // Faz M
                DeliveryDays = quote.DeliveryDays,    // Faz M
                ContactId = quote.ContactId,
                ContactName = quote.ContactName,
                ContactAddress = quote.ContactAddress,
                ContactCode = quote.ContactCode,
                SalesRepId = quote.SalesRepId,
                RequesterPersonnelId = quote.RequesterPersonnelId,
                CurrencyId = quote.CurrencyId,
                ExchangeRate = quote.ExchangeRate,
                IsVatIncluded = quote.IsVatIncluded,
                RateDate = quote.RateDate,
                SourceDocumentNo = quote.SourceDocumentNo,
                SubTotal = quote.SubTotal,
                DiscountRate = quote.DiscountRate,
                DiscountAmount = quote.DiscountAmount,
                TaxRate = quote.TaxRate,
                TaxAmount = quote.TaxAmount,
                GrandTotal = quote.GrandTotal,
                PaymentTerms = quote.PaymentTerms,
                DeliveryTerms = quote.DeliveryTerms,
                DeliveryAddress = quote.DeliveryAddress,
                Status = quote.Status,
                RevisionNo = quote.RevisionNo,
                ParentDocumentId = quote.ParentDocumentId,
                Notes = quote.Notes,
                CreatedById = quote.CreatedById,
                CreatedAt = quote.CreatedAt,
                UpdatedAt = quote.UpdatedAt,
                IsActive = quote.IsActive,
                LineCount = quote.LineCount,
            };
        }

        // Satirlari kaydet — DocumentId'yi yeni Id ile set et.
        // ÖNEMLI: Id'yi de kopyala — aksi halde UPSERT'te tum satirlar INSERT olarak
        // algilanir, eski Id'ler silinir ve WidgetTra kayitlari orphan kalir.
        var finalLines = lineEntities.Select(ln => new DocumentLine
        {
            Id = ln.Id,
            DocumentId = quote.Id,
            LineNo = ln.LineNo,
            ItemId = ln.ItemId,
            UnitId = ln.UnitId,
            Quantity = ln.Quantity,
            UnitPrice = ln.UnitPrice,
            DiscountRate = ln.DiscountRate,
            LineTotal = ln.LineTotal,
            CombinationId = ln.CombinationId,
            LocationId = ln.LocationId,
            Notes = ln.Notes,
            NotesPinned = ln.NotesPinned,
            RevisedFromId = ln.RevisedFromId,
        }).ToArray();

        // 2026-07-20 (Madde 2): fulfillmentEntries verilmişse (bu belge bir İhtiyaç Kaydı'nı
        // karşılıyorsa) kalem yazımıyla AYNI transaction'da deftere yazılır — atomiklik.
        await _repo.SaveLinesAsync(quote.Id, finalLines, ct, createdById, fulfillmentEntries);

        // ── Kit snapshot (Faz 2) — kit satirlarini o anki aktif ItemKit icerigiyle DONDUR ──
        // Bir kit belge kalemine eklendiginde icerigi satira snapshot'lanir; kit sonradan revize
        // edilse bile bu belge eski icerigi tasir. Freeze-on-first: ayni satirda ayni kit zaten
        // snapshot'landiysa korunur (kit revizyonu gecmis belgeyi etkilemez). Item baska bir kite
        // degistiyse yeniden snapshot alinir. Faz 3 irsaliye patlatmasi bu snapshot'tan uretir.
        var savedForKit = await _repo.GetLinesAsync(quote.Id, ct);
        var kitContents = await _repo.GetActiveKitContentsAsync(
            savedForKit.Select(l => l.ItemId).Distinct(), ct);
        if (kitContents.Count > 0)
        {
            var kitByItem = kitContents.ToDictionary(k => k.KitItemId);
            var kitLines = savedForKit.Where(l => kitByItem.ContainsKey(l.ItemId)).ToList();
            var existing = (await _repo.GetExistingKitSnapshotsAsync(kitLines.Select(l => l.Id), ct))
                .ToDictionary(x => x.LineId, x => x.KitItemId);
            foreach (var line in kitLines)
            {
                if (existing.TryGetValue(line.Id, out var snappedKit) && snappedKit == line.ItemId)
                    continue; // freeze — ayni satirda ayni kit zaten donduruldu
                var src = kitByItem[line.ItemId];
                /* 2026-08-22 (kullanici karari "B"): bilesen birim fiyatlari da DONDURULUR.
                   FixedComponent'te fiyat zaten kit tanimindan (ItemKitLine.UnitPrice) gelir;
                   ListComponent'te ise snapshot'a yalniz miktar yaziliyordu ve kit dokumunde
                   fiyat sutunlari bos kaliyordu. Artik kaydetme anindaki cozulmus liste
                   fiyati snapshot'a yazilir.
                   FIYATLAMA DEGISMEZ: ListComponent dali kit fiyatini yine CANLI cozer,
                   snapshot'taki UnitPrice'a bakmaz (bkz. ResolveKitServerTruthPricesAsync).
                   GECMIS BELGELER etkilenmez — eski snapshot'lar oldugu gibi kalir. */
                var compsToSave = src.Components;
                if (kitComponentPrices.Count > 0)
                {
                    compsToSave = src.Components
                        .Select(c => c.UnitPrice.HasValue
                            ? c
                            : (kitComponentPrices.TryGetValue((c.ComponentItemId, c.ConfigId), out var cp)
                                ? c with { UnitPrice = cp }
                                : c))
                        .ToList();
                }
                await _repo.ReplaceKitSnapshotAsync(line.Id, src.KitItemId, src.VersionNo, compsToSave, ct);
            }
        }

        // İhtiyaç Kaydı → türetilen belge köprüsü. Sadece yeni belgede (isNew) ve
        // FromRequestId verilmişse çalışır; mevcut belge güncellemelerinde atlenir.
        if (isNew && request.FromRequestId.HasValue && request.FromRequestId.Value > 0)
        {
            await _docSourceRepo.EnsureSchemaAsync(ct);
            await _docSourceRepo.AddAsync(quote.Id, request.FromRequestId.Value, ct);
        }

        // Satir detaylarini (ozellik-deger-aciklama) kaydet — herhangi bir satirda
        // detay varsa line ID'leri icin ek sorgu at. Cogu teklif detaysiz (no-op save)
        // bu yol hic calismaz ve 1 RT + N RT kazanilir.
        var hasAnyDetails = lineRequests.Any(r => r.CombinationDetails != null && r.CombinationDetails.Count > 0);
        if (hasAnyDetails)
        {
            var savedLines = await _repo.GetLinesAsync(quote.Id, ct);
            var byLineNo = savedLines.ToDictionary(l => l.LineNo);
            for (int i = 0; i < lineRequests.Length; i++)
            {
                var reqLine = lineRequests[i];
                var lineNoForThis = i + 1;
                if (!byLineNo.TryGetValue(lineNoForThis, out var savedLine)) continue;

                var details = (reqLine.CombinationDetails ?? new List<SaveQuoteLineDetailItem>())
                    .Select((d, idx) => new DocumentLineDetail
                    {
                        QuoteLineId = savedLine.Id,
                        FeatureName = d.FeatureName,
                        ValueCode = d.ValueCode,
                        ValueName = d.ValueName,
                        Description = d.Description,
                        LineOrder = d.LineOrder > 0 ? d.LineOrder : (idx + 1),
                    })
                    .ToList();

                await _repo.SaveLineDetailsAsync(savedLine.Id, details, ct);
            }
        }

        // Yeni belgede aktif bir onay akışı varsa otomatik başlat.
        // Hata akışı durdurmaz — belge zaten kaydedildi, akış başlatılamazsa sessizce geçer.
        var approvalStarted = false;
        if (isNew && _approvalFlowService is not null)
        {
            try
            {
                // Belge tipinden spesifik kind çözümle (SalesQuote/PurchaseOrder/...).
                // Spesifik akışlar da otomatik tetiklenir; tip çözümlenemezse wildcard'a düşer.
                var kind = DocumentEntityTypes.WildcardKind;
                if (quote.DocumentTypeId.HasValue)
                {
                    var docType = await _documentTypeRepo.GetByIdAsync(quote.DocumentTypeId.Value, ct);
                    kind = DocumentEntityTypes.ResolveKind(docType?.Code);
                }

                // Şirket parametresi: belge türü bazında onay kapalıysa otomatik başlatma.
                // Parametre tanımsızsa açık kabul edilir (geriye uyum). Manuel "Onaya Gönder"
                // yolu bu parametreden etkilenmez — açık kullanıcı niyeti her zaman geçerli.
                var approvalEnabled = true;
                if (_companyParameters is not null && kind != DocumentEntityTypes.WildcardKind)
                {
                    // Ana anahtar: onay sistemi bu belge türünde kullanılmıyorsa hiçbir onay davranışı devreye girmez.
                    var useApproval = await _companyParameters.GetBoolAsync(
                        ApprovalParameters.FormCode, ApprovalParameters.UseKey(kind), ct) ?? true;

                    approvalEnabled = useApproval
                        && (await _companyParameters.GetBoolAsync(
                                ApprovalParameters.FormCode, ApprovalParameters.EnabledKey(kind), ct) ?? true);
                }

                if (approvalEnabled)
                {
                    var flow = await _approvalFlowService.MatchFlowAsync(
                        kind, quote.GrandTotal, null, null, ct);
                    if (flow is not null)
                    {
                        await _approvalFlowService.StartAsync(
                            new StartApprovalRequest(
                                DocumentId:      quote.Id,
                                FlowId:          flow.Id,
                                StartedBy:       startedByUser ?? "system",
                                StartedByUserId: createdById),
                            ct);
                        approvalStarted = true;
                    }
                }
            }
            catch
            {
                // Akış başlatma hatası belge kaydını iptal etmez
            }
        }

        // ── İşlem logu — yeni kayıt: Insert; güncelleme: yalnızca değişen alanlar ──
        if (_audit is not null)
        {
            try
            {
                var auditEntity = await ResolveAuditEntityAsync(quote.DocumentTypeId, ct);
                if (isNew)
                {
                    // İlk değer dökümü: başlık alanları + kalem listesi ("boş → değer")
                    var insertedLines = await _repo.GetLinesAsync(quote.Id, ct);
                    var lineSnapshot = insertedLines.Select(l => new AuditFieldChange(
                        $"Line[{l.Id}]",
                        $"Kalem — {l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId)}",
                        null,
                        $"{AuditDiff.Normalize(l.Quantity)} {l.UnitCode ?? "birim"} × {AuditDiff.Normalize(l.UnitPrice)}")).ToList();
                    _audit.LogInsert(auditEntity, quote.Id, quote.DocumentNumber,
                        detail: $"{finalLines.Length} kalem · Genel Toplam {AuditDiff.Normalize(quote.GrandTotal)}",
                        snapshot: SnapHeader(quote),
                        extraChanges: lineSnapshot);
                }
                else
                {
                    var changes = new List<AuditFieldChange>();
                    if (auditOldHeader is not null)
                        changes.AddRange(AuditDiff.Compute(auditOldHeader, SnapHeader(quote), auditEntity));
                    if (auditOldLines is not null)
                    {
                        var newLinesForAudit = await _repo.GetLinesAsync(quote.Id, ct);
                        changes.AddRange(BuildLineChanges(auditOldLines, newLinesForAudit));
                    }
                    _audit.LogChanges(auditEntity, quote.Id, quote.DocumentNumber, changes);
                }
            }
            catch { /* audit yazımı belge kaydını asla bozmaz */ }
        }

        // Damgayi TAZELE: kaydettikten sonra ROWVERSION degisti. Elimizdeki entity hala
        // istemciden gelen (eski) damgayi tasiyor; yenilemezsek ayni ekrandan yapilan
        // IKINCI kayit her seferinde "baskasi degistirdi" hatasi verirdi.
        var savedDto = MapDto(quote);
        try
        {
            var fresh = await _repo.GetByIdAsync(quote.Id, ct);
            if (fresh?.RowVersion is { Length: > 0 } freshRv)
                savedDto = savedDto with { RowVersion = Convert.ToBase64String(freshRv) };
        }
        catch (Exception rvEx)
        {
            // Damga tazelenemezse kayit yine de basarilidir; istemci sayfayi yenileyince duzelir.
            _logger?.LogWarning(rvEx, "Kayit sonrasi surum damgasi tazelenemedi (belge {Id}).", quote.Id);
        }

        return (true, null, savedDto, approvalStarted);
    }

    /// <summary>
    /// Silme ön-kontrolü — silinebilirse null, silinemezse gerekçe mesajı.
    /// <see cref="DeleteQuoteAsync"/> ve UI precheck endpoint'i (tek kaynak) buradan çağırır.
    /// </summary>
    public async Task<string?> GetDeleteBlockReasonAsync(int id, CancellationToken ct)
    {
        // 1) Karşılanmış kalem içeren belge silinemez (İhtiyaç Kaydı zinciri koruması).
        //    Diğer belge türlerinde Fulfilled* = 0 olduğundan bu kontrol no-op'tur.
        var lines = await GetQuoteLinesAsync(id, ct);
        if (lines.Any(l => l.FulfilledFromStock + l.FulfilledByPurchase > 0))
            return "Bu belge karşılanmış kalem(ler) içerdiği için silinemez. Önce karşılama belgelerini geri alın.";

        // 2) İş emrine tahsis edilmiş kalem içeren belge silinemez (2026-07-20 — iş emri↔sipariş
        //    miktar guard'ı, SaveQuoteAsync/GetLineLocksAsync ile AYNI kaynak: WorkOrderSource).
        //    Yalnızca AKTİF (iptal edilmemiş) iş emirleri sayılır — bkz. GetWorkOrderAllocationsAsync.
        //    Kalemi silmek miktarı 0'a indirmekle eşdeğerdir; tahsis varsa 0 daima taban altıdır.
        var woAlloc = await GetWorkOrderAllocationsAsync(id, ct);
        if (woAlloc.Count > 0 && woAlloc.Values.Any(q => q > 0))
        {
            var names = lines
                .Where(l => woAlloc.TryGetValue(l.Id, out var q) && q > 0)
                .Select(l => l.MaterialName ?? l.MaterialCode ?? $"#{l.Id}")
                .Distinct()
                .ToList();
            // İsim listesi boş çıkabilir (ör. tahsisli satır sonradan belgeden silindiyse —
            // WorkOrderSource.SourceLineId artık mevcut lines'ta yok) — jenerik ifadeye düş,
            // boş "..." (2026-07-20 review Bulgu 4).
            var label = names.Count > 0 ? string.Join(", ", names) : "bir veya daha fazla kalem";
            return $"Bu belgede iş emrine tahsis edilmiş kalem(ler) var: {label}. " +
                   "Kaynak belge silinemez; önce ilgili iş emirlerini iptal edin.";
        }

        // 3) Bu belgeden türetilmiş AKTİF belge varsa (DocumentSource: teklif→sipariş,
        //    İhtiyaç→talep/fiş...) kaynak silinemez — bağlantı bozulur. Türetilenler
        //    silinirse (soft-delete) kaynak yeniden silinebilir hale gelir.
        //    İSTİSNA (2026-07-20 review Bulgu 1 — DÜZELTİLDİ): "is_emri" türündeki türevler bu
        //    listeye HİÇ eklenmez. Sebep: iş emri Document'ını soft-delete eden bir yol yok —
        //    ChangeStatusAsync(Cancelled) yalnız WorkOrder.Status'u değiştirir, Document.IsActive
        //    hep 1 kalır. Bu kontrol status'a bakmadığı için Cancelled bir iş emri bile kaynağı
        //    KALICI olarak bloklardı — madde 2 (Status-farkında, WorkOrderSource üzerinden) zaten
        //    doğru kapsamı sağlıyor; iş emri türevleri için TEK doğru kaynak odur.
        var derivedIds = await _docSourceRepo.GetDerivedDocumentIdsAsync(id, ct);
        if (derivedIds.Count > 0)
        {
            var activeNos = new List<string>();
            foreach (var did in derivedIds)
            {
                var d = await GetQuoteByIdAsync(did, ct);
                if (d is not { IsActive: true }) continue;

                string? typeCode = null;
                if (d.DocumentTypeId is > 0)
                {
                    var dt = await _documentTypeRepo.GetByIdAsync(d.DocumentTypeId.Value, ct);
                    typeCode = dt?.Code;
                }
                if (string.Equals(typeCode, "is_emri", StringComparison.OrdinalIgnoreCase)) continue;

                activeNos.Add(d.DocumentNumber);
            }
            if (activeNos.Count > 0)
                return $"Bu belgeden türetilmiş belge(ler) var: {string.Join(", ", activeNos)}. " +
                       "Kaynak belge silinemez; önce türetilmiş belgeleri silin/iptal edin.";
        }

        return null;
    }

    public async Task<(bool Ok, string? Error)> DeleteQuoteAsync(int id, CancellationToken ct)
    {
        // Silme engelleri (karşılanmış kalem / iş emri tahsisi / türetilmiş aktif belge) — UI
        // precheck ile aynı kaynak. Engelliyse silme + reddi logla (başarısız işlem).
        // NOT: documentTypeId burada bilinmiyor (belge henüz okunmadı) — LogGuardRejectionAsync
        // null ile çağrılır, ResolveAuditEntityAsync generic "belge" koduna düşer. Bunun için
        // ayrı bir GetByIdAsync round-trip'i eklemedik (yalnız kozmetik etiket kazandırırdı) —
        // hem gereksiz DB çağrısı hem onu saracak bir try/catch (CLAUDE.md kural #2: boş catch
        // yasak) gerektirirdi. LogGuardRejectionAsync zaten hiçbir zaman fırlatmaz (iç
        // ResolveAuditEntityAsync + AuditTrailService.Enqueue kendi try/catch'lerine sahip).
        var blockReason = await GetDeleteBlockReasonAsync(id, ct);
        if (blockReason is not null)
        {
            await LogGuardRejectionAsync(null, id, blockReason, ct);
            return (false, blockReason);
        }

        // İşlem logu için silinen belgenin kimliğini + kalem dökümünü silmeden ÖNCE al
        Document? docForAudit = null;
        IReadOnlyCollection<DocumentLineDto> lines = System.Array.Empty<DocumentLineDto>();
        if (_audit is not null)
        {
            try { docForAudit = await _repo.GetByIdAsync(id, ct); } catch { }
            try { lines = await GetQuoteLinesAsync(id, ct); } catch { }
        }

        // İşlem logu (Madde 3, 2026-07-20): bu belge bir İhtiyaç Kaydı satırını karşılıyorsa,
        // silmeden ÖNCE etkilenen satırların eski durumunu yakala — ters çevirme DeleteAsync'in
        // İÇİNDE (aynı transaction'da) yapıldığı için "sonra"sını ayrıca DB'den okuyup diff'leriz
        // (bkz. LogFulfillmentAuditAsync). Salt-okunur ön-kontrol; ReverseByDocumentAsync'in
        // davranışına dokunmaz.
        IReadOnlyDictionary<int, DocumentLineDto>? oldFulfillmentLines = null;
        if (_audit is not null)
        {
            try
            {
                var affectedIds = await _repo.GetFulfillmentAffectedLineIdsAsync(id, ct);
                if (affectedIds.Count > 0)
                    oldFulfillmentLines = await GetLinesSnapshotAsync(affectedIds, ct);
            }
            catch { oldFulfillmentLines = null; }
        }

        // NOT: karşılama defterinin ters çevrilmesi (bu belge bir İhtiyaç Kaydı'nı
        // karşılıyorduysa katkısını geri alma) DeleteAsync'in İÇİNDE, aynı transaction'da
        // yapılır — bkz. SqlDocumentRepository.DeleteAsync. Burada ayrı bir çağrı yok ki
        // "silindi ama geri düşüm yapılmadı" yarım durumu oluşmasın.
        await _repo.DeleteAsync(id, ct);

        if (_audit is not null && docForAudit is not null)
        {
            var auditEntity = await ResolveAuditEntityAsync(docForAudit.DocumentTypeId, ct);
            // Silinen belgenin kalem dökümü snapshot olarak loglanır — "ne kayboldu" izlenebilir.
            var snapshot = lines.Select(l => new AuditFieldChange(
                $"Line[{l.Id}]",
                $"Kalem — {l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId)}",
                $"{AuditDiff.Normalize(l.Quantity)} {l.UnitCode ?? "birim"} × {AuditDiff.Normalize(l.UnitPrice)}",
                null)).ToList();
            _audit.LogDelete(auditEntity, id, docForAudit.DocumentNumber,
                detail: $"{lines.Count} kalem · Genel Toplam {AuditDiff.Normalize(docForAudit.GrandTotal)}",
                snapshot: snapshot);
        }

        // İşlem logu (Madde 3): ters çevirmenin İhtiyaç Kaydı tarafındaki etkisi — ayrı
        // belge(ler)e (İhtiyaç Kaydı) yazılır, silinen belgenin kendi log kaydından bağımsız.
        if (oldFulfillmentLines is { Count: > 0 })
        {
            var detail = docForAudit is not null
                ? $"Karşılayan belge silindi (#{docForAudit.DocumentNumber})"
                : $"Karşılayan belge silindi (#{id})";
            await LogFulfillmentAuditAsync(oldFulfillmentLines, detail, ct);
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ChangeStatusAsync(int id, string newStatus, CancellationToken ct)
    {
        if (!Enum.TryParse<DocumentStatus>(newStatus, out _))
            return (false, "Gecersiz durum.");

        // İşlem logu için eski durumu değişiklikten ÖNCE oku
        Document? docForAudit = null;
        if (_audit is not null)
        {
            try { docForAudit = await _repo.GetByIdAsync(id, ct); } catch { }
        }

        await _repo.UpdateStatusAsync(id, newStatus, ct);

        if (_audit is not null && docForAudit is not null &&
            !string.Equals(docForAudit.Status.ToString(), newStatus, StringComparison.OrdinalIgnoreCase))
        {
            var auditEntity = await ResolveAuditEntityAsync(docForAudit.DocumentTypeId, ct);
            _audit.LogChanges(auditEntity, id, docForAudit.DocumentNumber,
                [new AuditFieldChange("Status", "Durum", docForAudit.Status.ToString(), newStatus)]);
        }
        return (true, null);
    }

    public Task<string> GetNextDocumentNumberAsync(CancellationToken ct) => _repo.GetNextDocumentNumberAsync(ct);

    private static DocumentDto MapDto(Document q) => new(
        q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
        q.ContactId, q.ContactName, q.ContactAddress,
        q.SalesRepId,
        q.CurrencyId, q.SubTotal, q.DiscountRate, q.DiscountAmount,
        q.TaxRate, q.TaxAmount, q.GrandTotal,
        q.PaymentTerms, q.DeliveryTerms, q.DeliveryAddress,
        q.Status.ToString(), q.RevisionNo, q.ParentDocumentId, q.Notes,
        q.CreatedById, q.CreatedAt, q.UpdatedAt, q.IsActive,
        q.ContactCode,
        q.DocumentTypeId,
        q.DeliveryDate,        // Faz M
        q.DeliveryDays,        // Faz M
        q.CurrencyCode, q.CurrencySymbol,
        q.RequesterPersonnelId, q.RequesterPersonnelName,
        q.LocationId, q.LocationName,
        q.ExchangeRate, q.IsVatIncluded, q.RateDate,
        q.SourceDocumentNo,
        q.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : null);

    /// <summary>
    /// Satir revizyonu — repository katmanina delege eder. Widget degerlerinin
    /// kopyalanmasi bu service sinifinda DEGIL (WidgetService farkli), controller
    /// akisinda yapilir.
    /// </summary>
    public Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct)
        => _repo.ReviseLineAsync(parentLineId, description, ct);

    /// <summary>Bkz. IDocumentService.CopyDocumentAsync KDoc'u.</summary>
    public async Task<(bool Success, string? Error, DocumentDto? NewDocument)> CopyDocumentAsync(
        int sourceId, int? createdById, string? startedByUser, CancellationToken ct)
    {
        var source = await GetQuoteByIdAsync(sourceId, ct);
        if (source is null) return (false, "Kopyalanacak belge bulunamadı.", null);

        var sourceLines = await GetQuoteLinesAsync(sourceId, ct);
        if (sourceLines.Count == 0) return (false, "Kalemsiz belge kopyalanamaz.", null);

        var lineRequests = sourceLines
            .OrderBy(l => l.LineNo)
            .Select(l => new SaveDocumentLineRequest(
                Id: null,
                ItemId: l.ItemId,
                UnitId: l.UnitId,
                Quantity: l.Quantity,
                UnitPrice: l.UnitPrice,
                DiscountRate: l.DiscountRate,
                CombinationId: l.CombinationId,
                LocationId: l.LocationId,
                Notes: l.Notes,
                CombinationDetails: l.CombinationDetails?.Select(d => new SaveQuoteLineDetailItem(
                    d.FeatureName, null, d.ValueCode, d.ValueName, d.Description, d.LineOrder)).ToList(),
                TrackCombinations: l.CombinationId.HasValue,
                NotesPinned: l.NotesPinned,
                RevisedFromId: null,
                Serials: null))
            .ToList();

        var request = new SaveDocumentRequest(
            Id: null,
            DocumentDate: DateTime.Today,
            ValidUntil: source.ValidUntil,
            ContactId: source.ContactId,
            ContactName: source.ContactName,
            ContactAddress: source.ContactAddress,
            SalesRepId: source.SalesRepId,
            CurrencyId: source.CurrencyId,
            DiscountRate: source.DiscountRate,
            TaxRate: source.TaxRate,
            PaymentTerms: source.PaymentTerms,
            DeliveryTerms: source.DeliveryTerms,
            DeliveryAddress: source.DeliveryAddress,
            Notes: source.Notes,
            Lines: lineRequests,
            ContactCode: source.ContactCode,
            DocumentTypeId: source.DocumentTypeId,
            DeliveryDate: source.DeliveryDate,
            DeliveryDays: source.DeliveryDays,
            RequesterPersonnelId: source.RequesterPersonnelId,
            FromRequestId: null,
            LocationId: source.LocationId);

        var (success, error, newDoc, _) = await SaveQuoteAsync(request, createdById, startedByUser, ct);
        return (success, error, newDoc);
    }

    /// <summary>
    /// Satirin bagli oldugu belge Id'si — repository'ye delege eder. Yetkilendirme
    /// amacli (satir-bazli endpoint'te izin BELGE tipinden cozulur).
    /// </summary>
    public Task<int?> GetDocumentIdByLineAsync(int lineId, CancellationToken ct)
        => _repo.GetDocumentIdByLineAsync(lineId, ct);

    // ── Karşılama defteri — audit yardımcıları (Madde 3, 2026-07-20) ───────────────

    /// <summary>Bkz. IDocumentService.GetFulfillmentAffectedLineIdsAsync KDoc'u.</summary>
    public Task<IReadOnlyList<int>> GetFulfillmentAffectedLineIdsAsync(int refDocId, CancellationToken ct)
        => _repo.GetFulfillmentAffectedLineIdsAsync(refDocId, ct);

    /// <summary>Bkz. IDocumentService.GetLinesSnapshotAsync KDoc'u.</summary>
    public async Task<IReadOnlyDictionary<int, DocumentLineDto>> GetLinesSnapshotAsync(
        IReadOnlyCollection<int> lineIds, CancellationToken ct)
    {
        var result = new Dictionary<int, DocumentLineDto>();
        if (lineIds is null || lineIds.Count == 0) return result;

        // Satırlar farklı belgelere ait olabilir — önce hangi belgeye ait olduklarını çöz,
        // sonra belge başına TEK GetQuoteLinesAsync ile hepsini birden çek (N+1 belge bazında,
        // satır bazında değil).
        var docIds = new HashSet<int>();
        var lineToDoc = new Dictionary<int, int>();
        foreach (var lineId in lineIds.Distinct())
        {
            var docId = await _repo.GetDocumentIdByLineAsync(lineId, ct);
            if (docId is > 0)
            {
                lineToDoc[lineId] = docId.Value;
                docIds.Add(docId.Value);
            }
        }

        foreach (var docId in docIds)
        {
            var lines = await GetQuoteLinesAsync(docId, ct);
            foreach (var l in lines)
                if (lineToDoc.TryGetValue(l.Id, out var d) && d == docId)
                    result[l.Id] = l;
        }
        return result;
    }

    /// <summary>Karşılama durumu (FulfillmentStatus) → Türkçe etiket.</summary>
    private static string FulfillmentStatusLabel(int status) => status switch
    {
        1 => "Kısmen Karşılandı",
        2 => "Karşılandı",
        3 => "Kapatıldı",
        _ => "Açık",
    };

    /// <summary>Bkz. IDocumentService.LogFulfillmentAuditAsync KDoc'u.</summary>
    public async Task LogFulfillmentAuditAsync(
        IReadOnlyDictionary<int, DocumentLineDto> oldLinesById, string detail, CancellationToken ct)
    {
        if (_audit is null || oldLinesById is null || oldLinesById.Count == 0) return;
        try
        {
            foreach (var grp in oldLinesById.Values.GroupBy(l => l.DocumentId))
            {
                var docId = grp.Key;
                var newLines = (await GetQuoteLinesAsync(docId, ct)).ToDictionary(l => l.Id);
                var changes = new List<AuditFieldChange>();
                foreach (var oldLn in grp)
                {
                    // Satır silinmiş olabilir (nadiren) — o durumda yeni durumu yok, atla.
                    if (!newLines.TryGetValue(oldLn.Id, out var newLn)) continue;

                    var oldQty = oldLn.FulfilledFromStock + oldLn.FulfilledByPurchase;
                    var newQty = newLn.FulfilledFromStock + newLn.FulfilledByPurchase;
                    if (oldQty == newQty && oldLn.FulfillmentStatus == newLn.FulfillmentStatus) continue;

                    var label = newLn.MaterialName ?? newLn.MaterialCode ?? ("#" + newLn.ItemId);
                    changes.Add(new AuditFieldChange(
                        $"Line[{newLn.Id}].Fulfillment",
                        $"{label} · Karşılama",
                        $"{AuditDiff.Normalize(oldQty)} ({FulfillmentStatusLabel(oldLn.FulfillmentStatus)})",
                        $"{AuditDiff.Normalize(newQty)} ({FulfillmentStatusLabel(newLn.FulfillmentStatus)})"));
                }
                if (changes.Count == 0) continue;

                var doc = await GetQuoteByIdAsync(docId, ct);
                _audit.LogChanges("alis_talebi", docId, doc?.DocumentNumber, changes, detail: detail);
            }
        }
        catch { /* audit yazımı işlem akışını asla bozmaz */ }
    }

    /// <summary>
    /// Tekliflerden cari bazli siparis(ler) uretir.
    ///   1) Tum kaynak teklifleri yukle + dogrula (Approved + henuz consume edilmemis)
    ///   2) ContactId bazinda grupla — her grup icin tek bir Document (type=satis_siparisi) olustur
    ///   3) Tum gruptaki teklifin satirlarini siparise clone et (LineNo resequence + Notes'a kaynak ekle)
    ///   4) Kombinasyon detaylarini ayrica kopyala
    ///   5) document_source koprusu kayit ekle, kaynak teklif statusunu Converted yap
    /// Service-level transaction yok; document_source UNIQUE INDEX cift insert riskini engeller.
    /// </summary>
    /// <summary>
    /// Rezervasyon ön kontrolünün blok mesajı — kullanıcı hangi malzemenin hangi depoda ne kadar
    /// eksik olduğunu tek bakışta görür. Kit satırlarında eksik olan BİLEŞEN gösterilir; hangi
    /// kitten geldiği parantez içinde belirtilir (yoksa kullanıcı "bu malzeme teklifte yok" der).
    /// </summary>
    private static string BuildShortageMessage(IReadOnlyList<StockShortageDto> shortages)
    {
        var lines = shortages.Select(s =>
        {
            var name = string.IsNullOrWhiteSpace(s.ItemCode) ? s.ItemName : $"{s.ItemCode} — {s.ItemName}";
            var via  = string.IsNullOrWhiteSpace(s.SourceItemName) ? string.Empty : $" (kit: {s.SourceItemName})";
            var loc  = string.IsNullOrWhiteSpace(s.LocationName) ? $"#{s.LocationId}" : s.LocationName;
            return $"• {name}{via} — {loc}: gerekli {s.Required:0.####}, kullanılabilir {s.Available:0.####} " +
                   $"(eksik {(s.Required - s.Available):0.####})";
        });
        return "Stok rezervasyonu yapılamadığı için sipariş oluşturulmadı — yetersiz stok:\n"
             + string.Join("\n", lines);
    }

    /// <summary>
    /// Dönüşüm ortasında rezervasyon başarısız olursa oluşan sipariş(ler)i geri alır. Kaynak
    /// teklifler bu noktada HENÜZ "Converted" yapılmadığı için tek yapılacak iş siparişleri
    /// silmektir (satırlar + rezervasyonlar cascade/servis içinde temizlenir). Silme hatası
    /// yutulmaz; loglanır — çağıran zaten kullanıcıya blok mesajı döner.
    /// </summary>
    private async Task RollbackConvertedOrdersAsync(IEnumerable<int> orderIds, CancellationToken ct)
    {
        foreach (var id in orderIds.Where(x => x > 0).Distinct())
        {
            try { await _repo.DeleteAsync(id, ct); }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Rezervasyon başarısız olduktan sonra sipariş {OrderId} geri alınamadı — taslak sipariş elle silinmeli.", id);
            }
        }
    }

    public async Task<CreateOrdersFromQuotesResult> CreateOrdersFromQuotesAsync(
        CreateOrdersFromQuotesRequest req, int? createdById, CancellationToken ct)
    {
        if (req.QuoteIds == null || req.QuoteIds.Count == 0)
            return new CreateOrdersFromQuotesResult(false, "En az bir teklif secilmelidir.", 0, Array.Empty<int>());

        await _docSourceRepo.EnsureSchemaAsync(ct);

        var orderTypeId = await ResolveDefaultOrderTypeIdAsync(ct);
        if (!orderTypeId.HasValue)
            return new CreateOrdersFromQuotesResult(false, "'satis_siparisi' belge tipi bulunamadi.", 0, Array.Empty<int>());

        var quoteTypeId = await ResolveDefaultQuoteTypeIdAsync(ct);

        // ── 1) Yukle + dogrula ──
        var quotes = new List<(Document Quote, IReadOnlyCollection<DocumentLine> Lines)>(req.QuoteIds.Count);
        foreach (var qid in req.QuoteIds.Distinct())
        {
            var doc = await _repo.GetByIdAsync(qid, ct);
            if (doc == null)
                return new CreateOrdersFromQuotesResult(false, $"Teklif #{qid} bulunamadi.", 0, Array.Empty<int>());
            if (!doc.IsActive)
                return new CreateOrdersFromQuotesResult(false, $"Teklif {doc.DocumentNumber} pasif.", 0, Array.Empty<int>());
            if (doc.Status != DocumentStatus.Approved)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} onaylanmamis (durum: {doc.Status}). Sadece Approved teklifler siparise donusturulebilir.", 0, Array.Empty<int>());
            if (quoteTypeId.HasValue && doc.DocumentTypeId.HasValue && doc.DocumentTypeId != quoteTypeId)
                return new CreateOrdersFromQuotesResult(false,
                    $"Belge {doc.DocumentNumber} satis teklifi degil.", 0, Array.Empty<int>());
            if (!doc.ContactId.HasValue)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} icin cari bos. Cari secilmeden siparise donusturulemez.", 0, Array.Empty<int>());
            if (await _docSourceRepo.IsSourceConsumedAsync(qid, ct))
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} zaten siparise donusturulmus.", 0, Array.Empty<int>());

            var lines = await _repo.GetLinesAsync(qid, ct);
            if (lines.Count == 0)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} icin satir yok — bos teklif siparise donusturulemez.", 0, Array.Empty<int>());
            quotes.Add((doc, lines));
        }

        // ── 1b) Stok rezervasyonu ON KONTROLU (2026-08-29) ──
        // Rezervasyon istendiyse HICBIR SEY OLUSTURULMADAN once yeterlilik denetlenir: tum
        // tekliflerin TUM satirlari tek seferde degerlendirilir (ayni malzemeyi isteyen iki
        // kalem toplanir), yetersiz varsa islem blok halinde kesilir ve hangi malzemenin ne
        // kadar eksik oldugu tek tek bildirilir. Kismi donusturme (bir kismi rezerveli) YOK.
        if (req.ReserveStock)
        {
            if (_stockReservations is null)
                return new CreateOrdersFromQuotesResult(false,
                    "Stok rezervasyonu servisi kullanılamıyor — rezervasyonlu sipariş oluşturulamadı.", 0, Array.Empty<int>());

            var quoteLineIds = quotes.SelectMany(t => t.Lines).Select(l => l.Id).Where(id => id > 0).ToArray();
            var shortages = await _stockReservations.CheckLinesAvailabilityAsync(quoteLineIds, null, ct);
            if (shortages.Count > 0)
                return new CreateOrdersFromQuotesResult(false, BuildShortageMessage(shortages), 0, Array.Empty<int>());
        }

        // ── 2) Cari bazli grupla ──
        var groups = quotes.GroupBy(t => t.Quote.ContactId!.Value).ToArray();
        var orderIds = new List<int>(groups.Length);
        // Kaynak teklif durumu/koprusu — TUM siparisler kurulduktan SONRA yazilir (bkz. adim 5).
        var pendingFinalize = new List<(int OrderId, string OrderNumber, Document Order,
            List<(Document Quote, IReadOnlyCollection<DocumentLine> Lines)> Quotes)>();

        foreach (var grp in groups)
        {
            var first = grp.First().Quote;

            // Yeni siparis numarasi — Faz P: kurala göre türet, yoksa eski fallback
            var orderTypeIdForNumber = await ResolveDefaultOrderTypeIdAsync(ct) ?? 0;
            var orderNumber = await ResolveNextDocumentNumberAsync(
                documentTypeId: orderTypeIdForNumber,
                contactId:      first.ContactId,
                userId:         null,
                issueDate:      DateTime.Now,
                ct);

            // Tutar hesabi: subtotal = tum gruptaki line'larin line_total toplami
            var allLines = grp.SelectMany(t => t.Lines).ToArray();
            var subTotal = allLines.Sum(l => l.LineTotal);
            var discountRate = first.DiscountRate;
            var taxRate = first.TaxRate;
            var discountAmount = Math.Round(subTotal * (discountRate / 100m), 4);
            var afterDiscount = subTotal - discountAmount;
            var taxAmount = Math.Round(afterDiscount * (taxRate / 100m), 4);
            var grandTotal = afterDiscount + taxAmount;

            var order = new Document
            {
                DocumentNumber = orderNumber,
                DocumentTypeId = orderTypeId,
                DocumentDate = req.OrderDate,
                ValidUntil = null,
                ContactId = first.ContactId,
                ContactName = first.ContactName,
                ContactAddress = first.ContactAddress,
                SalesRepId = first.SalesRepId,
                CurrencyId = first.CurrencyId,
                // PageComment Seq 1067/1068 — kur + KDV Dahil kaynak teklifin header'ından taşınır.
                // LineTotal'ler zaten kaynak satırdan aynen kopyalanıyor (aşağıda), o toplamlar
                // kaynak kaydedilirken NetUnitPrice ile netlenmişti — burada yeniden net-out
                // uygulanmaz (çift netleme sapması yaratmasın diye).
                ExchangeRate = first.ExchangeRate,
                IsVatIncluded = first.IsVatIncluded,
                RateDate = first.RateDate,
                SubTotal = Math.Round(subTotal, 4),
                DiscountRate = discountRate,
                DiscountAmount = discountAmount,
                TaxRate = taxRate,
                TaxAmount = taxAmount,
                GrandTotal = Math.Round(grandTotal, 4),
                PaymentTerms = first.PaymentTerms,
                DeliveryTerms = first.DeliveryTerms,
                DeliveryAddress = first.DeliveryAddress,
                Notes = grp.Count() > 1
                    ? $"Kaynak teklifler: {string.Join(", ", grp.Select(t => t.Quote.DocumentNumber))}"
                    : $"Kaynak teklif: {first.DocumentNumber}",
                CreatedById = createdById,
                Status = DocumentStatus.Draft,
            };

            var newOrderId = await _repo.UpsertAsync(order, ct);

            // ── 3) Satirlari clone et (LineNo resequence + Notes'a kaynak ekle) ──
            var clonedLines = new List<DocumentLine>();
            // Her clone'un kaynak QuoteLine.Id'sini hatirla — detail kopyasinda kullanilacak
            var lineSourceMap = new List<int>();
            int lineNo = 1;
            foreach (var (quote, lines) in grp)
            {
                foreach (var src in lines)
                {
                    var sourceTag = $"[Kaynak: {quote.DocumentNumber}]";
                    var combinedNotes = string.IsNullOrWhiteSpace(src.Notes)
                        ? sourceTag
                        : $"{src.Notes}\n{sourceTag}";

                    clonedLines.Add(new DocumentLine
                    {
                        Id = 0, // INSERT
                        DocumentId = newOrderId,
                        LineNo = lineNo++,
                        ItemId = src.ItemId,
                        UnitId = src.UnitId,
                        Quantity = src.Quantity,
                        UnitPrice = src.UnitPrice,
                        DiscountRate = src.DiscountRate,
                        LineTotal = src.LineTotal,
                        CombinationId = src.CombinationId,
                        LocationId = src.LocationId,
                        Notes = combinedNotes,
                        NotesPinned = src.NotesPinned,
                        // RevisedFromId YOK — siparis revizyonu farkli bir senaryo
                        // SourceLineId — kalem bazli kaynak iz (sipariş satiri ↔ teklif satiri)
                        SourceLineId = src.Id,
                    });
                    lineSourceMap.Add(src.Id);
                }
            }

            await _repo.SaveLinesAsync(newOrderId, clonedLines, ct);

            // ── 4) Kombinasyon detaylarini kopyala ──
            // SaveLinesAsync sonrasi yeni Id'leri yukleyip line_no -> new id eslestirmesi yap
            var savedLines = await _repo.GetLinesAsync(newOrderId, ct);
            // savedLines line_no'ya gore sirali; lineSourceMap da line_no sirasiyla insert edilmisti.
            var savedByLineNo = savedLines.OrderBy(l => l.LineNo).ToArray();
            for (int i = 0; i < savedByLineNo.Length && i < lineSourceMap.Count; i++)
            {
                var sourceLineId = lineSourceMap[i];
                var destLineId = savedByLineNo[i].Id;
                var details = await _repo.GetLineDetailsAsync(sourceLineId, ct);
                if (details.Count == 0) continue;
                var clonedDetails = details.Select((d, idx) => new DocumentLineDetail
                {
                    QuoteLineId = destLineId,
                    FeatureName = d.FeatureName,
                    ValueCode = d.ValueCode,
                    ValueName = d.ValueName,
                    Description = d.Description,
                    LineOrder = d.LineOrder > 0 ? d.LineOrder : (idx + 1),
                }).ToList();
                await _repo.SaveLineDetailsAsync(destLineId, clonedDetails, ct);
            }

            // ── 4b) Kit snapshot taşıma (Faz 1.5, 2026-08-03) — "donmuş içerik + dönüşüm düzeltmesi" ──
            // Kaynak teklif satırı zaten dondurulmuş bir kit içeriği taşıyorsa (freeze-on-first,
            // SaveQuoteAsync) o AYNI donmuş içerik yeni sipariş satırına taşınır — kit sonradan revize
            // edilse bile fiyat/patlatma teklifteki ile TUTARLI kalır (Yükleme Planlama'nın kit
            // rezervasyonu + Faz 3 irsaliye patlatması bu satırın snapshot'ından okur). Kaynakta
            // snapshot yoksa (kit teklif kaydından SONRA tanımlandıysa) canlı aktif kit içeriğinden
            // dondurulur — lazy freeze; item gerçekte kit DEĞİLSE (Components boş) no-op.
            for (int i = 0; i < savedByLineNo.Length && i < lineSourceMap.Count; i++)
            {
                var sourceLineId = lineSourceMap[i];
                var destLine = savedByLineNo[i];
                var sourceSnapshot = await _repo.GetKitSnapshotAsync(sourceLineId, ct);
                if (sourceSnapshot.Count > 0)
                {
                    var firstSnap = sourceSnapshot.First();
                    // UnitPrice tasinir (Seq 1078) — FixedComponent modda donmus elle bilesen fiyati
                    // olmadan tasinirsa hedef satirda fiyat kaybolurdu (silent-loss, CLAUDE.md kural #3).
                    var comps = sourceSnapshot
                        .Select(s => new KitSnapshotComponentDto(s.ComponentItemId, s.ConfigId, s.Quantity, s.UnitPrice))
                        .ToList();
                    await _repo.ReplaceKitSnapshotAsync(destLine.Id, firstSnap.KitItemId, firstSnap.KitVersionNo, comps, ct);
                }
                else
                {
                    var liveKit = (await _repo.GetActiveKitContentsAsync(new[] { destLine.ItemId }, ct)).FirstOrDefault();
                    if (liveKit != null && liveKit.Components.Count > 0)
                        await _repo.ReplaceKitSnapshotAsync(destLine.Id, liveKit.KitItemId, liveKit.VersionNo, liveKit.Components, ct);
                }
            }

            // ── 4c) Stok rezervasyonu (hepsi-ya-hic) ──
            // Kaynak teklifin durumu/koprusu HENUZ yazilmadi (adim 5 donguden SONRAYA alindi):
            // burada bir sey ters giderse olusan siparisleri silmek yeterli — teklifler ellenmemis
            // ve kopru satiri yazilmamis olur, yani gercekten "blok halinde kesme" saglanir.
            if (req.ReserveStock && _stockReservations is not null)
            {
                var reserveLines = savedByLineNo
                    .Where(l => l.Quantity > 0m)
                    .Select(l => new CreateReservationLineRequest(l.Id, l.Quantity))
                    .ToList();
                if (reserveLines.Count > 0)
                {
                    var rsv = await _stockReservations.CreateReservationsAsync(
                        new CreateReservationRequest(reserveLines, null, null,
                            $"Tekliften dönüşüm: {orderNumber}", AllOrNothing: true),
                        createdById, ct);
                    if (!rsv.Ok)
                    {
                        // On kontrol gecmisti → arada baska bir islem stogu tuketti (yaris) ya da
                        // kalemin deposu cozulemedi. Olusan TUM siparisleri geri al.
                        await RollbackConvertedOrdersAsync(orderIds.Append(newOrderId), ct);
                        var reasons = rsv.Skipped.Select(s =>
                        {
                            var ln = savedByLineNo.FirstOrDefault(x => x.Id == s.OrderLineId);
                            var label = ln is null ? $"Satır #{s.OrderLineId}" : $"{ln.MaterialCode} — {ln.MaterialName}";
                            return $"• {label}: {s.Reason}";
                        });
                        return new CreateOrdersFromQuotesResult(false,
                            "Stok rezervasyonu kurulamadığı için sipariş oluşturulmadı:\n" + string.Join("\n", reasons),
                            0, Array.Empty<int>());
                    }
                }
            }

            pendingFinalize.Add((newOrderId, orderNumber, order, grp.ToList()));
            orderIds.Add(newOrderId);
        }

        // ── 5) document_source koprusu + status update ──
        // Donguden SONRA: tum siparisler (ve varsa rezervasyonlari) basariyla kuruldugunda
        // kaynak teklifler "Converted" yapilir. Onceden dongu icindeydi; toplu donusturmede
        // ikinci grup rezervasyonda takilirsa birinci grubun teklifi ZATEN cevrilmis oluyordu.
        foreach (var (newOrderId, orderNumber, order, grpList) in pendingFinalize)
        {
            foreach (var (quote, _) in grpList)
            {
                await _docSourceRepo.AddAsync(newOrderId, quote.Id, ct);
                var prevStatus = quote.Status;
                quote.Status = DocumentStatus.Converted;
                quote.UpdatedAt = DateTime.Now;
                await _repo.UpsertAsync(quote, ct);
                _audit?.LogChanges(
                    await ResolveAuditEntityAsync(quote.DocumentTypeId, ct), quote.Id, quote.DocumentNumber,
                    [new AuditFieldChange("Status", "Durum", prevStatus.ToString(), DocumentStatus.Converted.ToString())],
                    detail: $"Siparişe dönüştürüldü: {orderNumber}");
            }

            _audit?.LogInsert(
                await ResolveAuditEntityAsync(orderTypeId, ct), newOrderId, orderNumber,
                detail: $"Tekliften dönüştürüldü: {string.Join(", ", grpList.Select(t => t.Quote.DocumentNumber))}",
                snapshot: SnapHeader(order));
        }

        return new CreateOrdersFromQuotesResult(true, null, orderIds.Count, orderIds);
    }

    /// <summary>
    /// Genel belge dönüşümü (satın alma zinciri). CreateOrdersFromQuotesAsync ile aynı desen
    /// ama kaynak/hedef tür kodları + Approved/Cari zorunluluğu parametrik. Satış akışı ayrı
    /// tutulur (o metoda dokunulmaz) — burada talep→teklif ve teklif→sipariş dönüşümü yönetilir.
    /// </summary>
    public async Task<CreateOrdersFromQuotesResult> ConvertDocumentsAsync(
        IReadOnlyCollection<int> sourceIds, string sourceTypeCode, string targetTypeCode,
        bool requireApproved, bool requireContact, DateTime targetDate, int? createdById, CancellationToken ct)
    {
        if (sourceIds == null || sourceIds.Count == 0)
            return new CreateOrdersFromQuotesResult(false, "En az bir belge seçilmelidir.", 0, Array.Empty<int>());

        await _docSourceRepo.EnsureSchemaAsync(ct);

        var targetType = await _documentTypeRepo.GetByCodeAsync(targetTypeCode, ct);
        if (targetType == null)
            return new CreateOrdersFromQuotesResult(false, $"'{targetTypeCode}' belge tipi bulunamadı.", 0, Array.Empty<int>());
        var sourceType = await _documentTypeRepo.GetByCodeAsync(sourceTypeCode, ct);

        // ── 1) Yükle + doğrula ──
        var srcs = new List<(Document Doc, IReadOnlyCollection<DocumentLine> Lines)>(sourceIds.Count);
        foreach (var sid in sourceIds.Distinct())
        {
            var doc = await _repo.GetByIdAsync(sid, ct);
            if (doc == null)
                return new CreateOrdersFromQuotesResult(false, $"Belge #{sid} bulunamadı.", 0, Array.Empty<int>());
            if (!doc.IsActive)
                return new CreateOrdersFromQuotesResult(false, $"{doc.DocumentNumber} pasif.", 0, Array.Empty<int>());
            if (sourceType != null && doc.DocumentTypeId.HasValue && doc.DocumentTypeId != sourceType.Id)
                return new CreateOrdersFromQuotesResult(false, $"{doc.DocumentNumber} beklenen belge türünde değil.", 0, Array.Empty<int>());
            if (requireApproved && doc.Status != DocumentStatus.Approved)
                return new CreateOrdersFromQuotesResult(false,
                    $"{doc.DocumentNumber} onaylanmamış (durum: {doc.Status}). Sadece onaylı belgeler dönüştürülebilir.", 0, Array.Empty<int>());
            if (requireContact && !doc.ContactId.HasValue)
                return new CreateOrdersFromQuotesResult(false,
                    $"{doc.DocumentNumber} için cari (tedarikçi) boş. Cari seçilmeden dönüştürülemez.", 0, Array.Empty<int>());
            if (await _docSourceRepo.IsSourceConsumedAsync(sid, ct))
                return new CreateOrdersFromQuotesResult(false, $"{doc.DocumentNumber} zaten dönüştürülmüş.", 0, Array.Empty<int>());
            var lines = await _repo.GetLinesAsync(sid, ct);
            if (lines.Count == 0)
                return new CreateOrdersFromQuotesResult(false, $"{doc.DocumentNumber} için satır yok — boş belge dönüştürülemez.", 0, Array.Empty<int>());
            srcs.Add((doc, lines));
        }

        // ── 2) Cari bazlı grupla (cari yoksa -1 grubu) ──
        var groups = srcs.GroupBy(t => t.Doc.ContactId ?? -1).ToArray();
        var newIds = new List<int>(groups.Length);

        foreach (var grp in groups)
        {
            var first = grp.First().Doc;
            var number = await ResolveNextDocumentNumberAsync(
                documentTypeId: targetType.Id, contactId: first.ContactId, userId: null, issueDate: DateTime.Now, ct);

            var allLines = grp.SelectMany(t => t.Lines).ToArray();
            var subTotal = allLines.Sum(l => l.LineTotal);
            var discountRate = first.DiscountRate;
            var taxRate = first.TaxRate;
            var discountAmount = Math.Round(subTotal * (discountRate / 100m), 4);
            var afterDiscount = subTotal - discountAmount;
            var taxAmount = Math.Round(afterDiscount * (taxRate / 100m), 4);
            var grandTotal = afterDiscount + taxAmount;

            var newDoc = new Document
            {
                DocumentNumber = number,
                DocumentTypeId = targetType.Id,
                DocumentDate = targetDate,
                ContactId = first.ContactId,
                ContactName = first.ContactName,
                ContactAddress = first.ContactAddress,
                SalesRepId = first.SalesRepId,
                CurrencyId = first.CurrencyId,
                // PageComment Seq 1067/1068 — kur + KDV Dahil kaynak belgenin header'ından taşınır.
                // LineTotal'ler kaynak satırdan aynen kopyalanır (aşağıda) — kaynak kaydedilirken
                // NetUnitPrice ile zaten netlenmişti, burada yeniden net-out uygulanmaz.
                ExchangeRate = first.ExchangeRate,
                IsVatIncluded = first.IsVatIncluded,
                RateDate = first.RateDate,
                SubTotal = Math.Round(subTotal, 4),
                DiscountRate = discountRate,
                DiscountAmount = discountAmount,
                TaxRate = taxRate,
                TaxAmount = taxAmount,
                GrandTotal = Math.Round(grandTotal, 4),
                PaymentTerms = first.PaymentTerms,
                DeliveryTerms = first.DeliveryTerms,
                DeliveryAddress = first.DeliveryAddress,
                LocationId = first.LocationId,
                Notes = grp.Count() > 1
                    ? $"Kaynak belgeler: {string.Join(", ", grp.Select(t => t.Doc.DocumentNumber))}"
                    : $"Kaynak belge: {first.DocumentNumber}",
                CreatedById = createdById,
                Status = DocumentStatus.Draft,
            };

            var newId = await _repo.UpsertAsync(newDoc, ct);

            // ── 3) Satırları klonla (SourceLineId ile) ──
            var clonedLines = new List<DocumentLine>();
            var lineSourceMap = new List<int>();
            int lineNo = 1;
            foreach (var (doc, lines) in grp)
                foreach (var src in lines)
                {
                    var tag = $"[Kaynak: {doc.DocumentNumber}]";
                    var combinedNotes = string.IsNullOrWhiteSpace(src.Notes) ? tag : $"{src.Notes}\n{tag}";
                    clonedLines.Add(new DocumentLine
                    {
                        Id = 0,
                        DocumentId = newId,
                        LineNo = lineNo++,
                        ItemId = src.ItemId,
                        UnitId = src.UnitId,
                        Quantity = src.Quantity,
                        UnitPrice = src.UnitPrice,
                        DiscountRate = src.DiscountRate,
                        LineTotal = src.LineTotal,
                        CombinationId = src.CombinationId,
                        LocationId = src.LocationId,
                        Notes = combinedNotes,
                        NotesPinned = src.NotesPinned,
                        SourceLineId = src.Id,
                    });
                    lineSourceMap.Add(src.Id);
                }

            await _repo.SaveLinesAsync(newId, clonedLines, ct);

            // ── 4) Kombinasyon detaylarını kopyala ──
            var savedLines = (await _repo.GetLinesAsync(newId, ct)).OrderBy(l => l.LineNo).ToArray();
            for (int i = 0; i < savedLines.Length && i < lineSourceMap.Count; i++)
            {
                var details = await _repo.GetLineDetailsAsync(lineSourceMap[i], ct);
                if (details.Count == 0) continue;
                var cloned = details.Select((d, idx) => new DocumentLineDetail
                {
                    QuoteLineId = savedLines[i].Id,
                    FeatureName = d.FeatureName,
                    ValueCode = d.ValueCode,
                    ValueName = d.ValueName,
                    Description = d.Description,
                    LineOrder = d.LineOrder > 0 ? d.LineOrder : (idx + 1),
                }).ToList();
                await _repo.SaveLineDetailsAsync(savedLines[i].Id, cloned, ct);
            }

            // ── 5) DocumentSource köprüsü + kaynak Converted ──
            foreach (var (doc, _) in grp)
            {
                await _docSourceRepo.AddAsync(newId, doc.Id, ct);
                var prev = doc.Status;
                doc.Status = DocumentStatus.Converted;
                doc.UpdatedAt = DateTime.Now;
                await _repo.UpsertAsync(doc, ct);
                _audit?.LogChanges(
                    await ResolveAuditEntityAsync(doc.DocumentTypeId, ct), doc.Id, doc.DocumentNumber,
                    [new AuditFieldChange("Status", "Durum", prev.ToString(), DocumentStatus.Converted.ToString())],
                    detail: $"Dönüştürüldü: {number}");
            }
            _audit?.LogInsert(
                await ResolveAuditEntityAsync(targetType.Id, ct), newId, number,
                detail: $"Dönüştürüldü: {string.Join(", ", grp.Select(t => t.Doc.DocumentNumber))}",
                snapshot: SnapHeader(newDoc));

            newIds.Add(newId);
        }

        return new CreateOrdersFromQuotesResult(true, null, newIds.Count, newIds);
    }

    private static DocumentLineDto MapLineDto(DocumentLine ln, IReadOnlyList<DocumentLineDetailDto>? details = null) => new(
        ln.Id, ln.DocumentId, ln.LineNo,
        ln.ItemId, ln.MaterialCode, ln.MaterialName,
        ln.UnitId, ln.UnitCode, ln.UnitName,
        ln.Quantity, ln.UnitPrice, ln.DiscountRate, ln.LineTotal,
        ln.CombinationId, ln.CombinationCode,
        ln.LocationId, ln.LocationCode, ln.LocationName,
        ln.Notes, details,
        NotesPinned:         ln.NotesPinned,
        RevisedFromId:       ln.RevisedFromId,
        SourceLineId:        ln.SourceLineId,
        FulfilledFromStock:  ln.FulfilledFromStock,
        FulfilledByPurchase: ln.FulfilledByPurchase,
        FulfillmentStatus:   ln.FulfillmentStatus,
        KitParentLineId:     ln.KitParentLineId,
        IsKit:               ItemTypeCatalog.IsKit(ln.ItemTypeId));
}

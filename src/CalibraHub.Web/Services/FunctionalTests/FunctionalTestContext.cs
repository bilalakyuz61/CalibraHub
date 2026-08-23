using CalibraHub.Application.Abstractions.Persistence;

namespace CalibraHub.Web.Services.FunctionalTests;

/// <summary>
/// Senaryolar arası paylaşılan çalışma zamanı durumu — "kayıt sözleşmesi" (bkz. CLAUDE.md /
/// görev talimatı madde 3). Bir senaryo ürettiği Id'yi <see cref="Set"/> ile burada bırakır,
/// bağımlı senaryolar <see cref="GetInt"/>/<see cref="Get{T}"/> ile okur. Anahtar isimleri bu
/// dosyanın en altında (Keys sınıfı) belgelenir — üretim/kalite senaryolarını yazacak ikinci
/// ajan SEED_MASTER_DATA çıktısını buradan tüketmelidir (yeniden malzeme/cari/lokasyon
/// oluşturmasın).
/// </summary>
public sealed class FunctionalTestContext
{
    private readonly Dictionary<string, object?> _bag = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _documentTypeCache = new(StringComparer.OrdinalIgnoreCase);

    public FunctionalTestContext(FunctionalTestHttpClient http, IDocumentTypeRepository documentTypes)
    {
        Http = http;
        DocumentTypes = documentTypes;
    }

    public FunctionalTestHttpClient Http { get; }

    /// <summary>
    /// Belge tipi kodu → DocumentTypeId çözümü. Bu tek noktada Application repository'sine
    /// doğrudan erişilir (statik metadata okuma — mutasyon değil, HTTP'de bunu döndüren bir
    /// uç yok). Asıl kaydetme/dönüştürme aksiyonları yine SaveDocument/ConvertPurchaseDocJson
    /// gibi gerçek HTTP uçlarından geçer; bu yalnız o çağrıların gövdesine konacak Id'yi bulur.
    /// </summary>
    public async Task<int?> ResolveDocumentTypeIdAsync(string code, CancellationToken ct)
    {
        if (_documentTypeCache.TryGetValue(code, out var cached)) return cached;
        var dt = await DocumentTypes.GetByCodeAsync(code, ct);
        if (dt == null) return null;
        _documentTypeCache[code] = dt.Id;
        return dt.Id;
    }

    private IDocumentTypeRepository DocumentTypes { get; }

    public void Set(string key, object? value) => _bag[key] = value;

    public bool Has(string key) => _bag.ContainsKey(key);

    public int GetInt(string key) => _bag.TryGetValue(key, out var v) && v is int i ? i : 0;

    public string? GetString(string key) => _bag.TryGetValue(key, out var v) ? v as string : null;

    public T? Get<T>(string key) => _bag.TryGetValue(key, out var v) && v is T t ? t : default;

    /// <summary>
    /// SEED_MASTER_DATA + ana senaryoların bıraktığı bağlam anahtarları — sabit sözleşme.
    /// Yeni senaryo eklerken burada tanımlı bir anahtarı okumak/yazmak istiyorsan aynı ismi
    /// kullan (yeniden icat etme); yeni bir veri üretiyorsan buraya XML yorumuyla ekle.
    /// </summary>
    public static class Keys
    {
        // ── SEED_MASTER_DATA çıktısı ──
        public const string UnitId = "unitId";
        public const string Item1Id = "item1Id";
        public const string Item1Code = "item1Code";
        public const string Item2Id = "item2Id";
        public const string Item2Code = "item2Code";
        public const string ContactId = "contactId";
        public const string Location1Id = "location1Id";
        public const string Location2Id = "location2Id";
        public const string PersonnelId = "personnelId";
        public const string CurrencyId = "currencyId";

        // ── STOCK_IN_RECEIPT çıktısı ──
        public const string StockInDocId = "stockInDocId";

        // ── NEED_RECORD_CREATE çıktısı ──
        public const string NeedDocId = "needDocId";
        public const string NeedLineTransferId = "needLineTransferId"; // Depo transferi ile karşılanacak satır
        public const string NeedLineStockIssueId = "needLineStockIssueId"; // Ambar çıkış fişi ile karşılanacak satır
        public const string NeedLinePurchaseId = "needLinePurchaseId"; // Satın alma zinciri ile karşılanacak satır

        // ── Satın alma zinciri ──
        public const string PurchaseDemandDocId = "purchaseDemandDocId";
        public const string PurchaseQuoteDocId = "purchaseQuoteDocId";
        public const string PurchaseOrderDocId = "purchaseOrderDocId";

        // ── Satış zinciri ──
        public const string SalesQuoteDocId = "salesQuoteDocId";
        public const string SalesOrderDocId = "salesOrderDocId";
    }
}

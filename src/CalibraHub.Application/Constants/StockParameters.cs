namespace CalibraHub.Application.Constants;

/// <summary>
/// Stok modülü şirket parametresi anahtarları (formCode = STOCK).
/// Admin → Parametreler → Stok sekmesinden yönetilir.
///
/// Anahtar formatı: STOCK_EFFECT_{DocTypeCode} — ilgili belge türünün
/// DocumentLine hareketlerinin (MovementType) stok bakiyesi hesabına dahil
/// edilip edilmeyeceği (Bool). Parametre tanımsızsa DAHİL kabul edilir
/// (geriye uyum: parametre yokken mevcut bakiye davranışı değişmez).
/// </summary>
public static class StockParameters
{
    public const string FormCode = "STOCK";

    public const string EffectKeyPrefix = "STOCK_EFFECT_";

    public static string EffectKey(string docTypeCode) => EffectKeyPrefix + docTypeCode;

    /// <summary>
    /// Stok hareketi (DocumentLine.MovementType) üretebilen belge türleri —
    /// parametre ekranındaki switch listesi. Code = DocumentType.Code.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Label, string Description)> MovementCapableTypes =
    [
        ("depo_giris",    "Depo Girişi",    "Doğrudan stok giriş fişleri (+)"),
        ("depo_cikis",    "Depo Çıkışı",    "Ambar çıkış / sarf fişleri (−). İhtiyaç karşılama çıkışları da bu türdendir."),
        ("depo_transfer", "Depo Transferi", "Lokasyonlar arası transfer hareketleri (+/−)"),
        ("sayim",         "Sayım Fişi",     "Envanter sayım farkları (+/−)"),
        ("is_emri",       "İş Emri",        "Üretim malzeme sarfı (−) ve üretim çıktısı (+)"),
    ];
}

/// <summary>
/// İhtiyaç karşılama merkezi parametre anahtarları (formCode = PURCHASE_FULFILLMENT).
/// Admin → Parametreler → İhtiyaç Kayıtları sekmesinden yönetilir.
/// </summary>
public static class FulfillmentParameters
{
    public const string FormCode = "PURCHASE_FULFILLMENT";

    /// <summary>"SPECIFIC" (belirli depolar) | "ITEM_DEFAULT" (stok kartındaki varsayılan depo).</summary>
    public const string LocationModeKey = "FULFILLMENT_LOCATION_MODE";

    /// <summary>SPECIFIC modda kullanılacak virgülle ayrılmış Location Id listesi.</summary>
    public const string LocationIdsKey = "FULFILLMENT_LOCATION_IDS";

    /// <summary>
    /// Karşılama aksiyonları (depodan karşıla / transfer / çıkış fişi / satın alma
    /// talebi-siparişi) yalnızca Onaylı ihtiyaç kayıtlarında çalışsın (Bool, default true).
    /// İhtiyaç Kaydı türünde onay tetikleme kapalıysa
    /// (APPROVAL_ENABLED_PurchaseRequest = false) bu şart uygulanmaz.
    /// </summary>
    public const string RequireApprovalKey = "FULFILLMENT_REQUIRE_APPROVAL";
}

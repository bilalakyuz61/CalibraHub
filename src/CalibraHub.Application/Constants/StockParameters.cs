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

    // ── Eksi bakiye kontrolü (negative balance control) ──
    // İki katmanlı çözümleme: Şirket ana anahtarı KAPALI → hiç kontrol yok.
    // AÇIK → lokasyonun (deponun) kendi AllowNegativeBalance switch'i. Ayar yoksa (null)
    // varsayılan ENGELLE; izin yalnızca deponun switch'i açıkça açıksa verilir.
    // İhlalde ENGELLE (kayıt olmaz). Tarih bazlı: hareket tarihinden itibaren hiçbir
    // noktada bakiye negatife düşmemeli (ileriye dönük zincirleme kontrol).

    /// <summary>Eksi bakiye kontrolü ana anahtarı (Bool, default false=kapalı). Kapalıyken hiç kontrol yapılmaz — mevcut davranış korunur.</summary>
    public const string NegBalanceControlKey = "NEG_BALANCE_CONTROL";

    /// <summary>
    /// KALDIRILDI (2026-07-07): şirket geneli "varsayılan izin" parametresi. Artık her depo
    /// kendi switch'ini taşır (Lokasyon Tanımlamaları); ayar yoksa varsayılan engelle.
    /// Sabit yalnızca eski kayıtların temizliği (SaveStockParametersJson DeleteAsync) için tutulur.
    /// </summary>
    public const string NegBalanceAllowDefaultKey = "NEG_BALANCE_ALLOW_DEFAULT";

    /// <summary>Satış siparişi stok bakiyesini rezervasyonla etkiler mi (Bool, default false). Açıkken açık sipariş miktarları kullanılabilir bakiyeyi düşürür (Faz 2).</summary>
    public const string SalesOrderAffectsStockKey = "SALES_ORDER_AFFECTS_STOCK";

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
    /// Karşılamada asgari stok korunsun (Bool, default false). Açıkken "Depodan Karşıla"
    /// dağıtımı ve stok bakiyesi görünümü asgari seviyeleri düşer: depo bazında asgari
    /// (ItemLocation.MinStock) ilgili depo bakiyesinden, genel asgari (Items.MinStock)
    /// toplam kullanılabilir havuzdan. Kapalıyken tüm fiziksel bakiye kullanılabilir.
    /// </summary>
    public const string RespectMinStockKey = "FULFILLMENT_RESPECT_MIN_STOCK";

    /// <summary>
    /// KALDIRILDI (2026-07-08): ayrı "karşılamada onay şartı" parametresi. Karşılama artık
    /// doğrudan İhtiyaç Kaydı onay tetiklemesine (APPROVAL_ENABLED_PurchaseRequest) bağlıdır:
    /// onay açıksa yalnızca Onaylı belgeler karşılanır, kapalıysa tümü. Sabit yalnızca eski
    /// DB kayıtlarının artık okunmadığını belgelemek için tutulur; hiçbir runtime kodu tüketmez.
    /// </summary>
    public const string RequireApprovalKey = "FULFILLMENT_REQUIRE_APPROVAL";
}

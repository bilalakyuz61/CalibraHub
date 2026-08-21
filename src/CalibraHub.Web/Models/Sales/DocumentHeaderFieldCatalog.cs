namespace CalibraHub.Web.Models.Sales;

/// <summary>
/// Belge ÜST BİLGİ (header) sabit alan kataloğu — Form Davranış Katmanı (2026-08-05).
///
/// Ekran markup'ı sabittir (DocumentEdit.cshtml); bu katalog o markup'taki alanların
/// KOD-TANIMLI envanteridir: davranış editörü (Alan Yönetimi → Standart Alanlar) bu
/// listeyi gösterir, runtime uygulayıcı (formBehavior.js) DOM'da `data-field-key`
/// attribute'u ile eşler. Reflection YOK — DocumentEdit'e yeni sabit alan eklenirse
/// buraya da satır eklenir (data-field-key attribute'u ile birlikte).
///
/// Locked=true → Görünür kapatılamaz (veri girişinin çekirdeği: belge no/tarih/cari/
/// para birimi). Zorunluluk yine düzenlenebilir (mevcut kod-zorunlulukları korunur).
/// </summary>
public static class DocumentHeaderFieldCatalog
{
    /// <param name="Key">Davranış anahtarı (FormFieldBehavior.FieldKey + data-field-key)</param>
    /// <param name="Label">Admin UI'da görünen ad (ekrandaki etiketin karşılığı)</param>
    /// <param name="Tab">Bulunduğu sekme key'i (sqe-tab: general/lines/conditions/notes)</param>
    /// <param name="DataType">Kural scope tip dönüşümü: text/date/numeric/boolean/currencyCode</param>
    /// <param name="Locked">true → gizlenemez</param>
    /// <param name="Movable">
    /// false → alan BASKA SEKMEYE TASINAMAZ. Sebep: markup'ta bagimsiz bir hucre
    /// degildir (ör. Genel İskonto % — toplamlar bloguna orulu iki &lt;span&gt;).
    /// Editorde surukleme tutamagi kapali gosterilir; sessizce hicbir sey yapmamak
    /// yerine nedeni kullaniciya soylenir.
    /// </param>
    public sealed record HeaderField(string Key, string Label, string Tab, string DataType, bool Locked,
        bool Movable = true);

    public sealed record HeaderTab(string Key, string Label, bool Locked);

    /// <summary>Sekme kataloğu — DocumentEdit sqe-tab-nav ile birebir.</summary>
    public static readonly IReadOnlyList<HeaderTab> Tabs =
    [
        new("general",     "Genel Bilgiler",    true),
        new("lines",       "Kalem Bilgileri",   true),
        new("conditions",  "Koşullar",          false),
        new("notes",       "Notlar",            false),
        new("widgets",     "Ek Alanlar",        false),
        new("attachments", "Ekli Dosyalar",     false),
        new("lineage",     "İlişkili Belgeler", false),
    ];

    // Ticari belge ortak alanları (satış + satın alma; İhtiyaç Kaydı hariç).
    private static readonly IReadOnlyList<HeaderField> Common =
    [
        new("quoteNumber",     "Belge No",             "general",    "text",         true),
        new("quoteDate",       "Tarih",                "general",    "date",         true),
        new("validUntil",      "Gün / Tarih",          "general",    "date",         false),
        new("currency",        "Para Birimi",          "general",    "currencyCode", true),
        // 2026-08-08: Kur ve Kur Tarihi, Para Birimi satırının İÇİNDE gömülüydü — kart
        // düzeninde tek hücrede alt alta çıkıyor ve ayrı yerleştirilemiyorlardı. Ayrı
        // katalog alanı olarak tanımlandılar: kendi hücreleri olur, Standart Alanlar'dan
        // bağımsız bölüme/şeride taşınabilirler. (Görünürlük yine dövize bağlı: TRY'de gizli.)
        new("exchangeRate",    "Kur",                  "general",    "numeric",      false),
        new("rateDate",        "Kur Tarihi",           "general",    "date",         false),
        new("vatIncluded",     "KDV Dahil",            "general",    "boolean",      false),
        new("customer",        "Cari / Tedarikçi",     "general",    "text",         true),
        new("salesRep",        "Temsilci",             "general",    "text",         false),
        // Movable:false — toplamlar blogunun icine orulu (bagimsiz hucre degil), tasinamaz.
        new("discountRate",    "Genel İskonto %",      "lines",      "numeric",      false, Movable: false),
        new("paymentTerms",    "Ödeme Koşulu",         "conditions", "text",         false),
        new("deliveryTerms",   "Teslimat",             "conditions", "text",         false),
        new("deliveryAddress", "Teslimat Adresi",      "conditions", "text",         false),
        new("notes",           "Notlar",               "notes",      "text",         false),
    ];

    // İhtiyaç Kaydı (alis_talebi): fiyat/cari alanları ekranda zaten yok;
    // Talep Eden + Lokasyon bu tipe özgü. Talep Eden mevcut kodda zorunlu → locked.
    private static readonly IReadOnlyList<HeaderField> PurchaseRequest =
    [
        new("quoteNumber",     "Belge No",        "general",    "text", true),
        new("quoteDate",       "Tarih",           "general",    "date", true),
        new("validUntil",      "Gün / Tarih",     "general",    "date", false),
        new("requester",       "Talep Eden",      "general",    "text", true),
        new("location",        "Lokasyon",        "general",    "text", false),
        new("paymentTerms",    "Ödeme Koşulu",    "conditions", "text", false),
        new("deliveryTerms",   "Teslimat",        "conditions", "text", false),
        new("deliveryAddress", "Teslimat Adresi", "conditions", "text", false),
        new("notes",           "Notlar",          "notes",      "text", false),
    ];

    /// <summary>
    /// Header form kodundan alan listesi. Desteklenmeyen form → null (davranış
    /// katmanı o form için kapalı — fail-open).
    /// </summary>
    public static IReadOnlyList<HeaderField>? Resolve(string? headerFormCode)
    {
        var code = (headerFormCode ?? "").Trim();
        if (code.Length == 0) return null;
        if (string.Equals(code, "PURCHASE_REQUEST_EDIT", StringComparison.OrdinalIgnoreCase))
            return PurchaseRequest;
        // DocumentTypeFormMap'teki tüm Header form kodları ortak listeyi kullanır.
        return FindDocTypeByHeaderFormCode(code) is not null ? Common : null;
    }

    /// <summary>Header form kodu → belge tipi (DocumentTypeFormMap tersine, Header üzerinden).</summary>
    public static string? FindDocTypeByHeaderFormCode(string? headerFormCode)
    {
        var code = (headerFormCode ?? "").Trim();
        if (code.Length == 0) return null;
        foreach (var docType in new[]
        {
            "satis_teklifi", "satis_siparisi", "satis_irsaliyesi",
            "alis_talebi", "alis_teklifi", "alis_siparisi", "satin_alma_talebi", "alis_irsaliyesi",
        })
        {
            if (string.Equals(DocumentTypeFormMap.Resolve(docType).Header, code, StringComparison.OrdinalIgnoreCase))
                return docType;
        }
        return null;
    }
}

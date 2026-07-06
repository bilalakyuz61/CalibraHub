namespace CalibraHub.Application.Security;

/// <summary>
/// 2026-06-08 — Form içi buton/aksiyon yetkileri için kod-side registry.
///
/// **Amaç:** Standart CRUD (VIEW/CREATE/EDIT_OWN/EDIT_ALL/DELETE_OWN/DELETE_ALL) dışında
/// kalan form-içi özel butonların (Onayla, Reddet, Kopyala, Excel'e Aktar, Tekrar Hesapla
/// vb.) yetkilendirilebilmesini sağlamak. Bu registry'de tanımlı her (FormCode, ButtonKey)
/// çifti startup'ta <c>PermissionDef</c> tablosuna <c>BUTTON:&lt;KEY&gt;</c> action koduyla
/// upsert edilir; sonrasında Yetki Yönetimi ekranında ilgili formun altında diğer aksiyonlarla
/// birlikte listelenir, ayrı ayrı verilip alınabilir.
///
/// **Eklemek için:** Bu sözlüğe yeni satır eklemek yeterli — discovery bir sonraki başlangıçta
/// otomatik seed eder. Razor tarafında <c>PermissionServiceExtensions.CanInvokeAsync</c>
/// helper'ı ile butonu show/hide edersiniz:
/// <code>
/// @if (await PermService.CanInvokeAsync(uid, role, dept, "DOCUMENT_NEED", "APPROVE", ct))
/// {
///     &lt;button&gt;Onayla&lt;/button&gt;
/// }
/// </code>
///
/// **Naming kuralı:** Button key UPPER_SNAKE_CASE; FormCode ile çakışmasın, eyleme dair açıklayıcı.
/// </summary>
public static class FormButtonCatalog
{
    /// <summary>Action prefix — PermissionDef.ActionCode'a bu prefix ile yazılır.</summary>
    public const string ActionPrefix = "BUTTON:";

    /// <summary>
    /// FormCode → form içindeki özel butonların listesi.
    ///
    /// Buradaki kayıtlar startup discovery sırasında <c>BUTTON:&lt;Key&gt;</c> action koduyla
    /// seed edilir. Yeni form/buton eklerken yalnızca bu dict'e satır eklemek yeterli.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<FormButton>> Buttons
        = new Dictionary<string, IReadOnlyList<FormButton>>(StringComparer.OrdinalIgnoreCase)
        {
            // ── İhtiyaç kaydı (DOCUMENT_NEED) ────────────────────────────────────
            ["DOCUMENT_NEED"] = new FormButton[]
            {
                new("APPROVE",        "Onayla"),
                new("REJECT",         "Reddet"),
                new("REVISE",         "Revizyon İste"),
                new("CONVERT_TO_PO",  "Satınalma Siparişine Dönüştür"),
            },

            // ── Satınalma Siparişi (PURCHASE_ORDER_EDIT) ─────────────────────────
            ["PURCHASE_ORDER_EDIT"] = new FormButton[]
            {
                new("APPROVE",  "Onayla"),
                new("CANCEL",   "İptal Et"),
                new("DUPLICATE","Kopyala"),
                new("EXPORT",   "Excel'e Aktar"),
                new("PRINT",    "Yazdır"),
            },

            // ── Satış Siparişi (SALES_ORDER_EDIT) ────────────────────────────────
            ["SALES_ORDER_EDIT"] = new FormButton[]
            {
                new("APPROVE",  "Onayla"),
                new("CANCEL",   "İptal Et"),
                new("DUPLICATE","Kopyala"),
                new("EXPORT",   "Excel'e Aktar"),
                new("PRINT",    "Yazdır"),
                new("SEND_MAIL","Mail Gönder"),
            },

            // ── İş Emri (WORK_ORDER_EDIT) ────────────────────────────────────────
            ["WORK_ORDER_EDIT"] = new FormButton[]
            {
                new("START",       "Başlat"),
                new("PAUSE",       "Duraklat"),
                new("COMPLETE",    "Tamamla"),
                new("CANCEL",      "İptal Et"),
                new("RECALCULATE", "Tekrar Hesapla"),
            },

            // ── BOM (BOM_EDIT) ───────────────────────────────────────────────────
            ["BOM_EDIT"] = new FormButton[]
            {
                new("DUPLICATE","Çoğalt"),
                new("EXPORT",   "Excel'e Aktar"),
                new("IMPORT",   "Excel'den Yükle"),
            },

            // ── İhtiyaç Kaydı (PURCHASE_REQUEST) — parametrik yetkiler ─────────────
            // Talep eden seçimi kapsamı. PURCHASE_REQUEST_EDIT excluded (FormName="Üst Bilgi")
            // olduğundan buton seed'i PURCHASE_REQUEST (FormName="İhtiyaç Kaydı") altında yapılır.
            ["PURCHASE_REQUEST"] = new FormButton[]
            {
                new("CREATE_ON_BEHALF",          "Başkası adına ihtiyaç girebilir"),
                new("CREATE_ON_BEHALF_DEPT_ONLY","Sadece departmanındaki personel için"),
            },

            // ── Şirket Ayarları (COMPANY_SETTINGS) ───────────────────────────────
            // Tek form altında tab erişim yetkileri + tab-içi test/eylem butonları.
            // 2026-06-08 — Eskiden ayrı form (MAIL_SETTINGS, INTEGRATOR_SETTINGS, ERP_SETTINGS,
            // ADDRESSES_SETTINGS, WHATSAPP_SETTINGS, AI_SETTINGS) olarak duran her bir tab
            // burada BUTTON:TAB_* aksiyonu olarak konsolide edildi. Yetki Yönetimi UI'da artık
            // tek "Şirket Ayarları" satırı görünür, altında tab + buton izinleri sıralanır.
            ["COMPANY_SETTINGS"] = new FormButton[]
            {
                // Tab erişim izinleri — kullanıcı görmek için ilgili TAB_* iznine sahip olmalı.
                // (Genel Bilgiler tab'ı VIEW iznine bağlı; ayrı TAB_GENERAL gerekmez.)
                new("TAB_MAIL",        "Sekme: Mail Ayarları"),
                new("TAB_INTEGRATOR",  "Sekme: Entegratör Ayarları"),
                // TAB_ADDRESSES kaldırıldı (2026-07-06) — Adres Kataloğu sekmesi UI'dan
                // çıkarıldı; şehir/ilçe ileride ayrı tanımlama ekranı olacak. Mevcut
                // PermissionDef satırı initializer migration'ında pasifleştirilir.
                new("TAB_WHATSAPP",    "Sekme: WhatsApp"),
                new("TAB_AI",          "Sekme: Yapay Zeka"),

                // Tab içindeki özel eylem butonları (test bağlantı, senkronize vb.)
                new("MAIL_TEST_CONNECTION",        "Mail: Bağlantıyı Test Et"),
                new("MAIL_SEND_TEST",              "Mail: Test Maili Gönder"),
                new("INTEGRATOR_TEST_CONNECTION",  "Entegratör: Bağlantıyı Test Et"),
                new("INTEGRATOR_SYNC_NOW",         "Entegratör: Şimdi Senkronize Et"),
                new("AI_TEST_CONNECTION",          "Yapay Zeka: Provider Test"),
                new("WHATSAPP_TEST_CONNECTION",    "WhatsApp: Bağlantıyı Test Et"),
                new("WHATSAPP_SEND_TEST_MSG",      "WhatsApp: Test Mesajı Gönder"),
            },
        };

    /// <summary>Verilen ActionCode 'BUTTON:' prefix taşıyor mu?</summary>
    public static bool IsButtonAction(string actionCode)
        => actionCode != null && actionCode.StartsWith(ActionPrefix, StringComparison.Ordinal);

    /// <summary>FormCode + ButtonKey → "BUTTON:KEY" action kodu.</summary>
    public static string BuildActionCode(string buttonKey)
        => $"{ActionPrefix}{buttonKey?.Trim().ToUpperInvariant()}";
}

/// <summary>Form içindeki tek bir özel buton tanımı.</summary>
/// <param name="Key">UPPER_SNAKE_CASE eyleme dair anahtar (örn. APPROVE).</param>
/// <param name="Label">Yetki Yönetimi ekranında gösterilen kullanıcı dostu etiket.</param>
public sealed record FormButton(string Key, string Label);

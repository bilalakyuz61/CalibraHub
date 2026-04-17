namespace CalibraHub.Application.Ui;

public sealed record UiCatalogLanguage(string Code, string DisplayName);

public sealed record UiCatalogTheme(string Code, string DisplayName);

public sealed record UiCatalogLabel(string Key, IReadOnlyDictionary<string, string> Texts);

public sealed record UiCatalogForm(string FormKey, string DisplayName, IReadOnlyCollection<UiCatalogLabel> Labels);

public static class UiCatalog
{
    public const string DefaultLanguageCode = "tr-TR";
    public const string DefaultThemeCode = "dark";

    private static readonly UiCatalogLanguage[] Languages =
    [
        new("tr-TR", "Turkce (TR)"),
        new("en-US", "English (US)")
    ];

    // Sistem sadece iki ana temayi destekler: light (gunduz) ve dark (gece).
    // Eski renkli tema kodlari (graphite, sky, vb.) NormalizeThemeCode tarafindan
    // otomatik olarak default (dark) temasina donusturulur.
    private static readonly UiCatalogTheme[] Themes =
    [
        new("light", "Gunduz"),
        new("dark",  "Gece"),
    ];

    private static readonly IReadOnlyDictionary<string, UiCatalogForm> Forms =
        new Dictionary<string, UiCatalogForm>(StringComparer.OrdinalIgnoreCase)
        {
            ["shared.layout"] = Form(
                "shared.layout",
                "Genel Yerlesim",
                Label("login_button", "Giris", "Sign in"),
                Label("profile_password", "Profil Bilgileri / Sifre", "Profile / Password"),
                Label("logout_button", "Cikis Yap", "Sign out"),
                Label("system_status", "Sistem Durumu: Hazir", "System Status: Ready"),
                Label("active_company", "Aktif Sirket", "Active Company"),
                Label("user_label", "Kullanici", "User"),
                Label("collaboration_button", "Mesajlar", "Messages"),
                Label("collaboration_online_users", "Aktif Kullanicilar", "Online Users"),
                Label("collaboration_conversation", "Mesajlasma", "Conversation"),
                Label("collaboration_no_users", "Aktif kullanici bulunmuyor.", "No active users."),
                Label("collaboration_no_conversation", "Mesajlasmak icin bir kullanici secin.", "Select a user to start messaging."),
                Label("collaboration_message_placeholder", "Mesajinizi yazin...", "Write your message..."),
                Label("collaboration_send", "Gonder", "Send"),
                Label("collaboration_status_connected", "Canli baglanti hazir", "Realtime connection ready"),
                Label("collaboration_status_connecting", "Baglanti kuruluyor", "Connecting"),
                Label("collaboration_status_disconnected", "Baglanti kesildi", "Connection lost"),
                Label("collaboration_lock_owned", "Bu kaydi su anda siz duzenliyorsunuz.", "You are currently editing this record."),
                Label("collaboration_lock_readonly", "Bu kayit su anda {user} tarafindan duzenlenmektedir.", "This record is currently being edited by {user}."),
                Label("collaboration_request_unlock", "Lutfen {record} kaydini kapatir misin?", "Can you please close the {record} record?"),
                Label("collaboration_message_user", "Mesaj Gonder", "Message User"),
                Label("collaboration_select_user", "Kullanici Secin", "Select User"),
                Label("collaboration_incoming_toast", "{user} size yeni bir mesaj gonderdi.", "{user} sent you a new message."),
                Label("collaboration_connection_lost_toast", "Canli isbirligi baglantisi kesildi.", "Realtime collaboration connection was lost."),
                Label("collaboration_connection_restored_toast", "Canli isbirligi baglantisi tekrar kuruldu.", "Realtime collaboration connection was restored."),
                Label("delete_confirm_title", "Kayit silinsin mi?", "Delete this record?"),
                Label("delete_confirm_text", "Bu islem geri alinamaz. Silmek uzere oldugunuz kayit:", "This action cannot be undone. Record to be deleted:"),
                Label("delete_confirm_cancel", "Vazgec", "Cancel"),
                Label("delete_confirm_submit", "Sil", "Delete")),
            ["shared.main_menu"] = Form(
                "shared.main_menu",
                "Ana Sol Menu",
                Label("collapse_menu", "Menuyu Daralt", "Collapse menu"),
                Label("search_placeholder", "Menude ara...", "Search menu..."),
                Label("favorites_title", "Hizli Erisim", "Quick Access"),
                Label("home", "Ana Sayfa", "Home"),
                Label("approval_processes", "Onay Surecleri", "Approval Processes"),
                Label("electronic_documents", "Elektronik Belgeler", "Electronic Documents"),
                Label("e_invoice", "e-Fatura", "e-Invoice"),
                Label("e_archive", "e-Arsiv", "e-Archive"),
                Label("e_dispatch", "e-Irsaliye", "e-Dispatch"),
                Label("logistics", "Lojistik", "Logistics"),
                Label("purchase", "Satin Alma", "Purchase"),
                Label("sales", "Satis", "Sales"),
                Label("product_configuration", "Urun Konfigurasyonu", "Product Configuration"),
                Label("items", "Malzeme Kartlari", "Material Cards"),
                Label("fixed_definitions", "Sabit Tanimlamalar", "Fixed Definitions"),
                Label("locations", "Lokasyon Tanimlamalari", "Warehouse Locations"),
                Label("measure_units", "Ölçü Birimleri", "Measure Units"),
                Label("settings", "Ayarlar", "Settings"),
                Label("company_definition", "Sirket Tanimlama", "Company Definition"),
                Label("integrator_settings", "Entegrator Ayarlari", "Integrator Settings"),
                Label("mail_settings", "Mail Ayarlari", "Mail Settings"),
                Label("erp_settings", "ERP Baglanti Ayarlari", "ERP Connection Settings"),
                Label("appearance", "Dil Etiketleri", "Language Labels"),
                Label("material_card_design", "Malzeme Karti Tasarimi", "Material Card Design"),
                Label("screen_design_settings", "Ekran Tasarim Ayarlari", "Screen Design Settings"),
                Label("system_log_report", "Sistem Log Raporu", "System Log Report")),
            ["shared.admin_menu"] = Form(
                "shared.admin_menu",
                "Admin Sol Menu",
                Label("collapse_menu", "Menuyu Daralt", "Collapse menu"),
                Label("search_placeholder", "Menude ara...", "Search menu..."),
                Label("favorites_title", "Hizli Erisim", "Quick Access"),
                Label("management", "Yonetim", "Management"),
                Label("panel", "Panel", "Panel"),
                Label("general_settings", "Ayarlar Genel", "General Settings"),
                Label("main_home", "Genel Ana Sayfa", "Main Home"),
                Label("settings", "Ayarlar", "Settings"),
                Label("system_settings", "Sistem Ayarlari", "System Settings"),
                Label("company_definition", "Sirket Tanimlama", "Company Definition"),
                Label("integrator_settings", "Entegrator Ayarlari", "Integrator Settings"),
                Label("mail_settings", "Mail Ayarlari", "Mail Settings"),
                Label("erp_settings", "ERP Baglanti Ayarlari", "ERP Connection Settings"),
                Label("appearance", "Dil Etiketleri", "Language Labels"),
                Label("views", "Gorunumler", "Views"),
                Label("items", "Malzeme Kartlari", "Material Cards"),
                Label("screen_design_settings", "Ekran Tasarim Ayarlari", "Screen Design Settings"),
                Label("log", "Log", "Logs"),
                Label("system_log_report", "Sistem Log Raporu", "System Log Report"),
                Label("definitions", "Tanimlar", "Definitions"),
                Label("organization", "Organizasyon", "Organization"),
                Label("departments", "Departman Tanimlama", "Department Definition"),
                Label("users", "Kullanici Tanimlama", "User Definition"),
                Label("roles", "Rol Tanimlama", "Role Definition")),
            ["shared.dashboard_sidebar"] = Form(
                "shared.dashboard_sidebar",
                "Dashboard Kenar Cubugu",
                Label("workspace_eyebrow", "Finans Calisma Alani", "Finance Workspace"),
                Label("subtitle", "Operasyon, arsiv sagligi ve raporlama icin aydinlik dashboard kabugu.", "Light dashboard shell for operations, archive health, and reporting."),
                Label("search_placeholder", "Calisma alaninda ara", "Search workspace"),
                Label("dashboard", "Dashboard", "Dashboard"),
                Label("invoices", "Faturalar", "Invoices"),
                Label("archive", "Arsiv", "Archive"),
                Label("reports", "Raporlar", "Reports"),
                Label("today", "Bugun", "Today"),
                Label("sla_value", "%97,4 onay SLA", "97.4% approval SLA"),
                Label("sla_copy", "Fatura onaylari, arsiv senkronu ve rapor uretimi hedef esiklerde ilerliyor.", "Invoice approvals, archive sync, and report generation are all within target thresholds."),
                Label("open_live_queue", "Canli kuyrugu ac", "Open live queue"),
                Label("appearance_link", "Form etiketlerini duzenle", "Edit form labels")),
            ["home.dashboard"] = Form(
                "home.dashboard",
                "Ana Dashboard",
                Label("overview", "Genel Bakis", "Overview"),
                Label("title", "Finansal operasyonlara tek ekranda hakim olun", "Financial operations at a glance"),
                Label("intro", "Fatura akisini, arsiv hazirligini ve raporlama sinyallerini tek bir temiz calisma alanindan izleyin.", "Monitor invoice throughput, archive readiness, and reporting signals from one clean workspace."),
                Label("live_sync", "Canli senkron saglikli", "Live sync healthy"),
                Label("review_invoices", "Faturalari incele", "Review invoices"),
                Label("invoices_processed", "Islenen faturalar", "Invoices processed"),
                Label("pending_approval", "Bekleyen onay", "Pending approval"),
                Label("archive_compliance", "Arsiv uyumu", "Archive compliance"),
                Label("reporting_accuracy", "Rapor dogrulugu", "Reporting accuracy"),
                Label("compared_previous", "Onceki operasyon donemine gore.", "Compared with the previous operational cycle."),
                Label("under_sla", "4s SLA altinda", "Under 4h SLA"),
                Label("purchase_exceptions", "Bekleyen kalemlerin cogu satin alma istisnalari.", "Most waiting items are purchase-side exceptions."),
                Label("on_target", "Hedefte", "On target"),
                Label("archive_completed", "Tum export paketleri son saat icinde tamamlandi.", "All export batches completed within the last hour."),
                Label("stable", "Stabil", "Stable"),
                Label("mapping_review", "Yalnizca birkac esleme finans incelemesi bekliyor.", "Only a few mappings require finance team review."),
                Label("invoices_section", "Faturalar", "Invoices"),
                Label("weekly_throughput", "Haftalik hacim", "Weekly throughput"),
                Label("this_week", "Bu hafta", "This week"),
                Label("processed_volume", "Islenen hacim", "Processed volume"),
                Label("processed_amount", "48,2K belge", "48.2K documents"),
                Label("throughput_copy", "Satin alma ve satis belgelerinde ayni gun tamamlama orani yukselmis durumda.", "Clean approval flow with a higher same-day completion rate across purchase and sales documents."),
                Label("reports_section", "Raporlar", "Reports"),
                Label("recent_financial_activity", "Son finansal hareketler", "Recent financial activity"),
                Label("open_reports", "Raporlari ac", "Open reports"),
                Label("document_column", "Belge", "Document"),
                Label("owner_column", "Sorumlu", "Owner"),
                Label("status_column", "Durum", "Status"),
                Label("updated_column", "Guncelleme", "Updated"),
                Label("approved", "Onaylandi", "Approved"),
                Label("needs_review", "Inceleme gerekli", "Needs review"),
                Label("queued", "Kuyrukta", "Queued"),
                Label("delivered", "Teslim edildi", "Delivered"),
                Label("archive_section", "Arsiv", "Archive"),
                Label("distribution_pipeline", "Dagitim hatti", "Distribution pipeline"),
                Label("validated", "Dogrulandi", "Validated"),
                Label("archived", "Arsivlendi", "Archived"),
                Label("manual_review", "Manuel inceleme", "Manual review"),
                Label("exceptions", "Istisna", "Exceptions"),
                Label("operations_section", "Operasyon", "Operations"),
                Label("focus_items", "Odak kalemleri", "Focus items"),
                Label("invoice_exceptions_title", "Fatura istisnalari", "Invoice exceptions"),
                Label("invoice_exceptions_copy", "Bir sonraki muhasebelestirme penceresinden once vergi kodu uyumsuzluklarini cozumleyin.", "Resolve tax-code mismatches before the next posting window."),
                Label("archive_batch_title", "Arsiv export paketi", "Archive export batch"),
                Label("archive_batch_copy", "14 numarali paket planlandi ve son kontrol icin hazir.", "Batch 14 is scheduled and ready for final verification."),
                Label("monthly_reporting_title", "Aylik raporlama", "Monthly reporting"),
                Label("monthly_reporting_copy", "Varyans paketi tamamlanmak uzere ve kontrolor incelemesine hazir.", "Variance pack is nearly complete and ready for controller review."),
                Label("shortcuts_title", "Kisa Yollarim", "My Shortcuts"),
                Label("shortcuts_empty", "Sol menuden yildizlayarak kisa yol ekleyebilirsiniz.", "You can add shortcuts by starring items in the left menu.")),
            ["admin.index"] = Form(
                "admin.index",
                "Admin Anasayfa",
                Label("title", "Admin Paneli", "Admin Panel"),
                Label("integrator_section", "Entegrator Ayarlari", "Integrator Settings"),
                Label("summary_section", "Ozet", "Summary"),
                Label("name", "Ad", "Name"),
                Label("provider", "Saglayici", "Provider"),
                Label("tax_number", "VKN", "Tax Number"),
                Label("base_url", "Base Url", "Base Url"),
                Label("polling", "Polling (sn)", "Polling (sec)"),
                Label("status", "Durum", "Status"),
                Label("active", "Aktif", "Active"),
                Label("passive", "Pasif", "Inactive"),
                Label("departments", "Departman", "Department"),
                Label("users", "Kullanici", "User"),
                Label("integrators", "Entegrator", "Integrator"),
                Label("erp_connections", "ERP Baglanti", "ERP Connection")),
            ["admin.appearance"] = Form(
                "admin.appearance",
                "Form Etiketleri",
                Label("title", "Form Etiketleri", "Form Labels"),
                Label("intro", "Form etiketlerini dil bazinda yonetin. Bos birakilan alanlar varsayilan katalog metinlerini kullanir.", "Manage form labels per language. Empty fields use the default catalog text."),
                Label("preference_section", "Kisisel Tercihler", "Personal Preferences"),
                Label("language_label", "Dil", "Language"),
                Label("theme_label", "Tema", "Theme"),
                Label("save_preferences", "Tercihleri Kaydet", "Save Preferences"),
                Label("label_section", "Form Etiketleri", "Form Labels"),
                Label("form_label", "Form", "Form"),
                Label("editor_language_label", "Duzenleme Dili", "Editing Language"),
                Label("load_form", "Formu Ac", "Open Form"),
                Label("key_column", "Anahtar", "Key"),
                Label("default_column", "Varsayilan Metin", "Default Text"),
                Label("override_column", "Ozel Metin", "Custom Text"),
                Label("editor_hint", "Alani bos birakirsaniz varsayilan katalog metni kullanilir.", "Leave a field empty to use the default catalog text."),
                Label("save_labels", "Etiketleri Kaydet", "Save Labels")),
            ["account.login"] = Form(
                "account.login",
                "Giris Formu",
                Label("title", "Giris", "Sign in"),
                Label("subtitle", "Yonetim paneline giris yapin.", "Sign in to the management panel."),
                Label("email", "Kullanici Adi", "User Name"),
                Label("company", "Sirket", "Company"),
                Label("company_placeholder", "Sirket seciniz", "Select a company"),
                Label("password", "Sifre", "Password"),
                Label("remember_me", "Beni hatirla", "Remember me"),
                Label("submit", "Giris Yap", "Sign In"),
                Label("default_admin", "Varsayilan admin", "Default admin")),
            ["account.change_password"] = Form(
                "account.change_password",
                "Sifre Degistirme Formu",
                Label("title", "Sifre Degistir", "Change Password"),
                Label("subtitle", "Guvenlik icin yeni sifreniz en az 8 karakter olmali.", "For security, your new password must be at least 8 characters."),
                Label("current_password", "Mevcut Sifre", "Current Password"),
                Label("new_password", "Yeni Sifre", "New Password"),
                Label("confirm_password", "Yeni Sifre Tekrar", "Confirm New Password"),
                Label("submit", "Kaydet", "Save"))
        };

    public static IReadOnlyCollection<UiCatalogLanguage> GetLanguages() => Languages;

    public static IReadOnlyCollection<UiCatalogTheme> GetThemes() => Themes;

    public static IReadOnlyCollection<UiCatalogForm> GetForms() =>
        Forms.Values
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static UiCatalogForm? GetForm(string formKey)
    {
        if (string.IsNullOrWhiteSpace(formKey))
        {
            return null;
        }

        Forms.TryGetValue(formKey.Trim(), out var form);
        return form;
    }

    public static bool IsSupportedLanguage(string languageCode) =>
        Languages.Any(x => string.Equals(x.Code, languageCode, StringComparison.OrdinalIgnoreCase));

    public static bool IsSupportedTheme(string themeCode) =>
        Themes.Any(x => string.Equals(x.Code, themeCode, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeLanguageCode(string? languageCode)
    {
        var normalized = languageCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultLanguageCode;
        }

        return Languages.FirstOrDefault(x => string.Equals(x.Code, normalized, StringComparison.OrdinalIgnoreCase))?.Code
               ?? DefaultLanguageCode;
    }

    public static string NormalizeThemeCode(string? themeCode)
    {
        var normalized = themeCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultThemeCode;
        }

        return Themes.FirstOrDefault(x => string.Equals(x.Code, normalized, StringComparison.OrdinalIgnoreCase))?.Code
               ?? DefaultThemeCode;
    }

    public static string GetText(string formKey, string labelKey, string? languageCode)
    {
        var form = GetForm(formKey);
        if (form is null)
        {
            return labelKey;
        }

        var label = form.Labels.FirstOrDefault(x => string.Equals(x.Key, labelKey, StringComparison.OrdinalIgnoreCase));
        if (label is null)
        {
            return labelKey;
        }

        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        if (label.Texts.TryGetValue(normalizedLanguageCode, out var exact))
        {
            return exact;
        }

        if (label.Texts.TryGetValue(DefaultLanguageCode, out var fallback))
        {
            return fallback;
        }

        return label.Texts.Values.FirstOrDefault() ?? labelKey;
    }

    private static UiCatalogForm Form(string formKey, string displayName, params UiCatalogLabel[] labels) =>
        new(formKey, displayName, labels);

    private static UiCatalogLabel Label(string key, string trText, string enText) =>
        new(
            key,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DefaultLanguageCode] = trText,
                ["en-US"] = enText
            });
}

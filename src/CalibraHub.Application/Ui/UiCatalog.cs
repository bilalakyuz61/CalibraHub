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
        new("tr-TR", "Türkçe (TR)"),
        new("en-US", "English (US)")
    ];

    // Sistem sadece iki ana temayı destekler: light (gündüz) ve dark (gece).
    // Eski renkli tema kodları (graphite, sky, vb.) NormalizeThemeCode tarafından
    // otomatik olarak default (dark) temasına dönüştürülür.
    private static readonly UiCatalogTheme[] Themes =
    [
        new("light", "Gündüz"),
        new("dark",  "Gece"),
    ];

    private static readonly IReadOnlyDictionary<string, UiCatalogForm> Forms =
        new Dictionary<string, UiCatalogForm>(StringComparer.OrdinalIgnoreCase)
        {
            ["shared.layout"] = Form(
                "shared.layout",
                "Genel Yerleşim",
                Label("login_button", "Giriş", "Sign in"),
                Label("profile_password", "Profil Bilgileri / Şifre", "Profile / Password"),
                Label("logout_button", "Çıkış Yap", "Sign out"),
                Label("system_status", "Sistem Durumu: Hazır", "System Status: Ready"),
                Label("active_company", "Aktif Şirket", "Active Company"),
                Label("user_label", "Kullanıcı", "User"),
                Label("collaboration_button", "Mesajlar", "Messages"),
                Label("collaboration_online_users", "Aktif Kullanıcılar", "Online Users"),
                Label("collaboration_conversation", "Mesajlaşma", "Conversation"),
                Label("collaboration_no_users", "Aktif kullanıcı bulunmuyor.", "No active users."),
                Label("collaboration_no_conversation", "Mesajlaşmak için bir kullanıcı seçin.", "Select a user to start messaging."),
                Label("collaboration_message_placeholder", "Mesajınızı yazın...", "Write your message..."),
                Label("collaboration_send", "Gönder", "Send"),
                Label("collaboration_status_connected", "Canlı bağlantı hazır", "Realtime connection ready"),
                Label("collaboration_status_connecting", "Bağlantı kuruluyor", "Connecting"),
                Label("collaboration_status_disconnected", "Bağlantı kesildi", "Connection lost"),
                Label("collaboration_lock_owned", "Bu kaydı şu anda siz düzenliyorsunuz.", "You are currently editing this record."),
                Label("collaboration_lock_readonly", "Bu kayıt şu anda {user} tarafından düzenlenmektedir.", "This record is currently being edited by {user}."),
                Label("collaboration_request_unlock", "Lütfen {record} kaydını kapatır mısın?", "Can you please close the {record} record?"),
                Label("collaboration_message_user", "Mesaj Gönder", "Message User"),
                Label("collaboration_select_user", "Kullanıcı Seçin", "Select User"),
                Label("collaboration_incoming_toast", "{user} size yeni bir mesaj gönderdi.", "{user} sent you a new message."),
                Label("collaboration_connection_lost_toast", "Canlı işbirliği bağlantısı kesildi.", "Realtime collaboration connection was lost."),
                Label("collaboration_connection_restored_toast", "Canlı işbirliği bağlantısı tekrar kuruldu.", "Realtime collaboration connection was restored."),
                Label("delete_confirm_title", "Kayıt silinsin mi?", "Delete this record?"),
                Label("delete_confirm_text", "Bu işlem geri alınamaz. Silmek üzere olduğunuz kayıt:", "This action cannot be undone. Record to be deleted:"),
                Label("delete_confirm_cancel", "Vazgeç", "Cancel"),
                Label("delete_confirm_submit", "Sil", "Delete")),
            ["shared.main_menu"] = Form(
                "shared.main_menu",
                "Ana Sol Menü",
                Label("collapse_menu", "Menüyü Daralt", "Collapse menu"),
                Label("search_placeholder", "Menüde ara...", "Search menu..."),
                Label("favorites_title", "Hızlı Erişim", "Quick Access"),
                Label("home", "Ana Sayfa", "Home"),
                Label("approval_processes", "Onay Süreçleri", "Approval Processes"),
                Label("electronic_documents", "Elektronik Belgeler", "Electronic Documents"),
                Label("e_invoice", "e-Fatura", "e-Invoice"),
                Label("e_archive", "e-Arşiv", "e-Archive"),
                Label("e_dispatch", "e-İrsaliye", "e-Dispatch"),
                Label("logistics", "Lojistik", "Logistics"),
                Label("purchase", "Satın Alma", "Purchase"),
                Label("sales", "Satış", "Sales"),
                Label("product_configuration", "Ürün Konfigürasyonu", "Product Configuration"),
                Label("items", "Malzeme Kartları", "Material Cards"),
                Label("fixed_definitions", "Sabit Tanımlamalar", "Fixed Definitions"),
                Label("locations", "Lokasyon Tanımlamaları", "Warehouse Locations"),
                Label("measure_units", "Ölçü Birimleri", "Measure Units"),
                Label("settings", "Ayarlar", "Settings"),
                Label("company_definition", "Şirket Tanımlama", "Company Definition"),
                Label("integrator_settings", "Entegratör Ayarları", "Integrator Settings"),
                Label("mail_settings", "Mail Ayarları", "Mail Settings"),
                Label("erp_settings", "ERP Bağlantı Ayarları", "ERP Connection Settings"),
                Label("appearance", "Dil Etiketleri", "Language Labels"),
                Label("material_card_design", "Malzeme Kartı Tasarımı", "Material Card Design"),
                Label("screen_design_settings", "Alan ve Widget Tanımları", "Field & Widget Definitions"),
                Label("system_log_report", "Sistem Log Raporu", "System Log Report")),
            ["shared.admin_menu"] = Form(
                "shared.admin_menu",
                "Admin Sol Menü",
                Label("collapse_menu", "Menüyü Daralt", "Collapse menu"),
                Label("search_placeholder", "Menüde ara...", "Search menu..."),
                Label("favorites_title", "Hızlı Erişim", "Quick Access"),
                Label("management", "Yönetim", "Management"),
                Label("panel", "Panel", "Panel"),
                Label("general_settings", "Genel Ayarlar", "General Settings"),
                Label("main_home", "Genel Ana Sayfa", "Main Home"),
                Label("settings", "Ayarlar", "Settings"),
                Label("system_settings", "Sistem Ayarları", "System Settings"),
                Label("company_definition", "Şirket Tanımlama", "Company Definition"),
                Label("integrator_settings", "Entegratör Ayarları", "Integrator Settings"),
                Label("mail_settings", "Mail Ayarları", "Mail Settings"),
                Label("erp_settings", "ERP Bağlantı Ayarları", "ERP Connection Settings"),
                Label("appearance", "Dil Etiketleri", "Language Labels"),
                Label("views", "Görünümler", "Views"),
                Label("items", "Malzeme Kartları", "Material Cards"),
                Label("screen_design_settings", "Alan ve Widget Tanımları", "Field & Widget Definitions"),
                Label("log", "Log", "Logs"),
                Label("system_log_report", "Sistem Log Raporu", "System Log Report"),
                Label("definitions", "Tanımlar", "Definitions"),
                Label("organization", "Organizasyon", "Organization"),
                Label("departments", "Departman Tanımlama", "Department Definition"),
                Label("users", "Kullanıcı Tanımlama", "User Definition"),
                Label("roles", "Rol Tanımlama", "Role Definition")),
            ["shared.dashboard_sidebar"] = Form(
                "shared.dashboard_sidebar",
                "Dashboard Kenar Çubuğu",
                Label("workspace_eyebrow", "Finans Çalışma Alanı", "Finance Workspace"),
                Label("subtitle", "Operasyon, arşiv sağlığı ve raporlama için aydınlık dashboard kabuğu.", "Light dashboard shell for operations, archive health, and reporting."),
                Label("search_placeholder", "Çalışma alanında ara", "Search workspace"),
                Label("dashboard", "Dashboard", "Dashboard"),
                Label("invoices", "Faturalar", "Invoices"),
                Label("archive", "Arşiv", "Archive"),
                Label("reports", "Raporlar", "Reports"),
                Label("today", "Bugün", "Today"),
                Label("sla_value", "%97,4 onay SLA", "97.4% approval SLA"),
                Label("sla_copy", "Fatura onayları, arşiv senkronu ve rapor üretimi hedef eşiklerde ilerliyor.", "Invoice approvals, archive sync, and report generation are all within target thresholds."),
                Label("open_live_queue", "Canlı kuyruğu aç", "Open live queue"),
                Label("appearance_link", "Form etiketlerini düzenle", "Edit form labels")),
            ["home.dashboard"] = Form(
                "home.dashboard",
                "Ana Dashboard",
                Label("overview", "Genel Bakış", "Overview"),
                Label("title", "Finansal operasyonlara tek ekranda hâkim olun", "Financial operations at a glance"),
                Label("intro", "Fatura akışını, arşiv hazırlığını ve raporlama sinyallerini tek bir temiz çalışma alanından izleyin.", "Monitor invoice throughput, archive readiness, and reporting signals from one clean workspace."),
                Label("live_sync", "Canlı senkron sağlıklı", "Live sync healthy"),
                Label("review_invoices", "Faturaları incele", "Review invoices"),
                Label("invoices_processed", "İşlenen faturalar", "Invoices processed"),
                Label("pending_approval", "Bekleyen onay", "Pending approval"),
                Label("archive_compliance", "Arşiv uyumu", "Archive compliance"),
                Label("reporting_accuracy", "Rapor doğruluğu", "Reporting accuracy"),
                Label("compared_previous", "Önceki operasyon dönemine göre.", "Compared with the previous operational cycle."),
                Label("under_sla", "4s SLA altında", "Under 4h SLA"),
                Label("purchase_exceptions", "Bekleyen kalemlerin çoğu satın alma istisnaları.", "Most waiting items are purchase-side exceptions."),
                Label("on_target", "Hedefte", "On target"),
                Label("archive_completed", "Tüm export paketleri son saat içinde tamamlandı.", "All export batches completed within the last hour."),
                Label("stable", "Stabil", "Stable"),
                Label("mapping_review", "Yalnızca birkaç eşleme finans incelemesi bekliyor.", "Only a few mappings require finance team review."),
                Label("invoices_section", "Faturalar", "Invoices"),
                Label("weekly_throughput", "Haftalık hacim", "Weekly throughput"),
                Label("this_week", "Bu hafta", "This week"),
                Label("processed_volume", "İşlenen hacim", "Processed volume"),
                Label("processed_amount", "48,2K belge", "48.2K documents"),
                Label("throughput_copy", "Satın alma ve satış belgelerinde aynı gün tamamlama oranı yükselmiş durumda.", "Clean approval flow with a higher same-day completion rate across purchase and sales documents."),
                Label("reports_section", "Raporlar", "Reports"),
                Label("recent_financial_activity", "Son finansal hareketler", "Recent financial activity"),
                Label("open_reports", "Raporları aç", "Open reports"),
                Label("document_column", "Belge", "Document"),
                Label("owner_column", "Sorumlu", "Owner"),
                Label("status_column", "Durum", "Status"),
                Label("updated_column", "Güncelleme", "Updated"),
                Label("approved", "Onaylandı", "Approved"),
                Label("needs_review", "İnceleme gerekli", "Needs review"),
                Label("queued", "Kuyrukta", "Queued"),
                Label("delivered", "Teslim edildi", "Delivered"),
                Label("archive_section", "Arşiv", "Archive"),
                Label("distribution_pipeline", "Dağıtım hattı", "Distribution pipeline"),
                Label("validated", "Doğrulandı", "Validated"),
                Label("archived", "Arşivlendi", "Archived"),
                Label("manual_review", "Manuel inceleme", "Manual review"),
                Label("exceptions", "İstisna", "Exceptions"),
                Label("operations_section", "Operasyon", "Operations"),
                Label("focus_items", "Odak kalemleri", "Focus items"),
                Label("invoice_exceptions_title", "Fatura istisnaları", "Invoice exceptions"),
                Label("invoice_exceptions_copy", "Bir sonraki muhasebeleştirme penceresinden önce vergi kodu uyumsuzluklarını çözümleyin.", "Resolve tax-code mismatches before the next posting window."),
                Label("archive_batch_title", "Arşiv export paketi", "Archive export batch"),
                Label("archive_batch_copy", "14 numaralı paket planlandı ve son kontrol için hazır.", "Batch 14 is scheduled and ready for final verification."),
                Label("monthly_reporting_title", "Aylık raporlama", "Monthly reporting"),
                Label("monthly_reporting_copy", "Varyans paketi tamamlanmak üzere ve kontrolör incelemesine hazır.", "Variance pack is nearly complete and ready for controller review."),
                Label("shortcuts_title", "Kısa Yollarım", "My Shortcuts"),
                Label("shortcuts_empty", "Sol menüden yıldızlayarak kısa yol ekleyebilirsiniz.", "You can add shortcuts by starring items in the left menu.")),
            ["admin.index"] = Form(
                "admin.index",
                "Admin Anasayfa",
                Label("title", "Admin Paneli", "Admin Panel"),
                Label("integrator_section", "Entegratör Ayarları", "Integrator Settings"),
                Label("summary_section", "Özet", "Summary"),
                Label("name", "Ad", "Name"),
                Label("provider", "Sağlayıcı", "Provider"),
                Label("tax_number", "VKN", "Tax Number"),
                Label("base_url", "Base Url", "Base Url"),
                Label("polling", "Polling (sn)", "Polling (sec)"),
                Label("status", "Durum", "Status"),
                Label("active", "Aktif", "Active"),
                Label("passive", "Pasif", "Inactive"),
                Label("departments", "Departman", "Department"),
                Label("users", "Kullanıcı", "User"),
                Label("integrators", "Entegratör", "Integrator"),
                Label("erp_connections", "ERP Bağlantı", "ERP Connection")),
            ["admin.appearance"] = Form(
                "admin.appearance",
                "Form Etiketleri",
                Label("title", "Form Etiketleri", "Form Labels"),
                Label("intro", "Form etiketlerini dil bazında yönetin. Boş bırakılan alanlar varsayılan katalog metinlerini kullanır.", "Manage form labels per language. Empty fields use the default catalog text."),
                Label("preference_section", "Kişisel Tercihler", "Personal Preferences"),
                Label("language_label", "Dil", "Language"),
                Label("theme_label", "Tema", "Theme"),
                Label("save_preferences", "Tercihleri Kaydet", "Save Preferences"),
                Label("label_section", "Form Etiketleri", "Form Labels"),
                Label("form_label", "Form", "Form"),
                Label("editor_language_label", "Düzenleme Dili", "Editing Language"),
                Label("load_form", "Formu Aç", "Open Form"),
                Label("key_column", "Anahtar", "Key"),
                Label("default_column", "Varsayılan Metin", "Default Text"),
                Label("override_column", "Özel Metin", "Custom Text"),
                Label("editor_hint", "Alanı boş bırakırsanız varsayılan katalog metni kullanılır.", "Leave a field empty to use the default catalog text."),
                Label("save_labels", "Etiketleri Kaydet", "Save Labels")),
            ["account.login"] = Form(
                "account.login",
                "Giriş Formu",
                Label("title", "Giriş", "Sign in"),
                Label("subtitle", "Yönetim paneline giriş yapın.", "Sign in to the management panel."),
                Label("email", "Kullanıcı Adı", "User Name"),
                Label("company", "Şirket", "Company"),
                Label("company_placeholder", "Şirket seçiniz", "Select a company"),
                Label("password", "Şifre", "Password"),
                Label("remember_me", "Beni hatırla", "Remember me"),
                Label("submit", "Giriş Yap", "Sign In"),
                Label("default_admin", "Varsayılan admin", "Default admin")),
            ["account.change_password"] = Form(
                "account.change_password",
                "Şifre Değiştirme Formu",
                Label("title", "Şifre Değiştir", "Change Password"),
                Label("subtitle", "Güvenlik için yeni şifreniz en az 8 karakter olmalı.", "For security, your new password must be at least 8 characters."),
                Label("current_password", "Mevcut Şifre", "Current Password"),
                Label("new_password", "Yeni Şifre", "New Password"),
                Label("confirm_password", "Yeni Şifre Tekrar", "Confirm New Password"),
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

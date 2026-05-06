namespace CalibraHub.Application.Services;

/// <summary>
/// CalibraHub.Persistence/Database/CalibraDatabaseInitializer tarafindan
/// yonetilen tablolar. DB Schema gorunumu bu listeyle filtrelenir — ayni DB'de
/// bulunabilecek external/legacy/test tablolari harita disinda kalir.
///
/// YENİ BİR TABLO EKLEDIGINDE: Bu kumeye tablo ismini ekle. (Case-insensitive
/// esleme yapilir.) Eksik tablo sadece "projede yok" gibi gozukur — hata vermez.
/// </summary>
public static class CalibraTableCatalog
{
    public static readonly IReadOnlySet<string> OwnedTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Kullanici & Yetki
        "Department", "User", "user_settings",

        // Sirket
        "company",

        // Cari
        "Contact",

        // Stok & Lojistik
        "Item", "Location", "Unit", "ItemUnits",

        // Urun Ozellik / Konfigurasyon
        "Feature", "FeatureValue", "FieldGroup", "Field",
        "material_card_field_options", "ProductConfiguration",
        "screen_layout_definitions", "stock_card_property_mappings",

        // Malzeme Gruplari & Urun Agaci
        "BOM", "BOMLine", "MaterialGroupMappings", "MaterialGroups",

        // Satis Teklifi / Belge
        "Document", "DocumentLine",
        "sales_quote_attachments", "sales_quote_line_details", "sales_representatives",

        // Fiyat Listesi & Doviz
        "PriceGroup", "PriceList", "currencies", "Exchange",

        // Belge / Rapor / Tasarim
        "design_templates", "document_types", "report_templates",

        // Not (Notes)
        "card_group_mappings", "card_groups",
        "note_attachments", "note_folders", "note_reminders", "note_shares", "notes",

        // Widget / EAV / Alan Ayarlari
        "FldSet", "Forms", "WidgetMas", "WidgetTra", "dynamic_field_values",

        // Rehber
        "GuideMas",

        // Organizasyon
        "org_chart_nodes", "org_charts",

        // Entegrasyon
        "erp_connection_settings", "incoming_documents",
        "integration_api_profiles", "integration_event_definitions", "integration_event_logs",
        "integrator_settings", "smtp_profiles",
        "CBT_EBELGEMAS", // Legacy ERP e-belge tablosu — IncomingDocumentRepository kullaniyor

        // Sistem / Log / UI
        "PLT_SISTEM_LOG", "ui_label_translations",

        // Dinamik Raporlama
        "RptDef", "RptDefRole", "RptRunLog", "RptView", "RptViewCol", "RptViewRole",

        // Sirket Parametre / Numerator / Stok Hareketi (Faz 0)
        "CompanyParameter", "Numerator", "StockMovement",

        // Uretim Is Emri (Faz 1)
        "WorkOrder", "WorkOrderSource",

        // Uretim Operasyon Sozlugu + Routing + Makine Sureleri (Faz 3)
        "Operation", "Routing", "RoutingOperation", "OperationMachineTime",

        // Is Emri Operasyonlari (Faz 3a)
        "WorkOrderOperation",

        // Uretim personneli (Faz 3a revize — User tablosundan ayri)
        "Personnel",
    };

    public static bool IsOwned(string tableName)
        => !string.IsNullOrWhiteSpace(tableName) && OwnedTableNames.Contains(tableName);
}

namespace CalibraHub.Application.Services;

/// <summary>
/// CalibraHub.Persistence/Database/CalibraDatabaseInitializer tarafindan
/// yonetilen tablolar. DB Schema gorunumu bu listeyle filtrelenir — ayni DB'de
/// bulunabilecek external/legacy/test tablolari harita disinda kalir.
///
/// YENİ BİR TABLO EKLEDIGINDE: Bu kumeye tablo ismini ekle. (Case-insensitive
/// esleme yapilir.) Eksik tablo sadece "projede yok" gibi gozukur — hata vermez.
///
/// Kaldirilanlarin nedeni:
///   material_card_field_options  → CREATE TABLE yok, terk edilmis
///   screen_layout_definitions    → CREATE TABLE yok, terk edilmis
///   ui_label_translations        → CREATE TABLE yok, terk edilmis
///   stock_card_property_mappings → ItemFeatureMappings olarak yeniden adi
///   ProductConfiguration         → ItemConfiguration olarak yeniden adi
///   org_chart_nodes / org_charts → OrgChartNode / OrgChart olarak yeniden adi
///   integrator_settings          → IntegratorSetting olarak yeniden adi
///   smtp_profiles                → SmtpProfile olarak yeniden adi
///   incoming_documents           → IncomingDocument olarak yeniden adi
/// </summary>
public static class CalibraTableCatalog
{
    public static readonly IReadOnlySet<string> OwnedTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Kullanici & Yetki
        "Department", "User", "UserSettings",

        // Sirket
        "Company",

        // Cari
        "Contact",

        // Stok & Lojistik
        "Item", "Location", "Unit", "ItemUnits", "ItemLocation",

        // Urun Ozellik / Konfigurasyon
        "Feature", "FeatureValue", "FieldGroup", "Field",
        "ItemFeatureMappings", "ItemConfiguration",

        // Malzeme Gruplari & Urun Agaci
        "BOM", "BOMLine", "MaterialGroupMappings", "MaterialGroups",

        // Satis Teklifi / Belge
        "Document", "DocumentLine",
        "sales_quote_attachments", "SalesQuoteLineDetail", "SalesRepresentative",

        // Fiyat Listesi & Doviz
        "PriceGroup", "PriceList", "Currency", "Exchange",

        // Belge / Ek / Tasarim
        "DesignTemplate", "DocumentType", "report_templates", "Attachment",

        // Not (Notes)
        "CardGroupMapping", "CardGroup",
        "NoteAttachment", "NoteFolder", "NoteReminder", "NoteReminderTarget", "NoteShare", "Note",

        // Widget / EAV / Alan Ayarlari
        "FldSet", "Forms", "WidgetMas", "WidgetTra", "dynamic_field_values",

        // Rehber
        "GuideMas",

        // Organizasyon
        "OrgChart", "OrgChartNode",

        // Entegrasyon
        "ErpConnectionSetting", "IncomingDocument",
        "IntegrationApiProfile",
        "IntegratorSetting", "SmtpProfile",
        "CBT_EBELGEMAS", // Legacy ERP e-belge tablosu — IncomingDocumentRepository kullaniyor

        // Sistem / Log
        "PLT_SISTEM_LOG",

        // Dinamik Raporlama
        "RptDef", "RptDefRole", "RptRunLog", "RptView", "RptViewCol", "RptViewRole",

        // Sirket Parametre / Numerator (Faz 0)
        "CompanyParameter", "Numerator",

        // Sayim (2026-07-02 stok hareketi konsolidasyonu — stock hareketleri artik DocumentLine'da)
        "InventoryCount", "InventoryCountLine",

        // Takvim
        "CalendarEvent",

        // Uretim Is Emri (Faz 1)
        "WorkOrder", "WorkOrderSource",

        // Uretim Operasyon Sozlugu + Routing + Makine Sureleri (Faz 3)
        "Operation", "Routing", "RoutingOperation", "OperationMachineTime",

        // Is Emri Operasyonlari (Faz 3a)
        "WorkOrderOperation",

        // Uretim personneli (Faz 3a revize — User tablosundan ayri)
        "Personnel",

        // Varlik Yonetimi (Asset Management)
        "Asset", "AssetEvent", "AssetAssignment",

        // Onay Akisi
        "ApprovalFlow", "ApprovalFlowStep", "ApprovalFlowRule", "ApprovalFlowEdge",
        "ApprovalFlowVariable", "ApprovalFlowRevision", "ApprovalFlowRunLog",
        "ApprovalInstance", "ApprovalInstanceVariable", "ApprovalStepRecord",
        "ApprovalActionToken",

        // Rapor Tasarimcisi
        "ReportSource", "ReportDesign", "ReportSnapshot",
    };

    public static bool IsOwned(string tableName)
        => !string.IsNullOrWhiteSpace(tableName) && OwnedTableNames.Contains(tableName);
}

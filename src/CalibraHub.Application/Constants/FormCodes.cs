namespace CalibraHub.Application.Constants;

/// <summary>
/// dbo.Forms tablosundaki tüm FormCode değerleri için tek kaynak.
///
/// KURAL: [PermissionScope(...)] attribute'u ve MenuDefinition.PermissionFormCode
/// değerleri SADECE bu sınıftan referans alır — string literal kullanılmaz.
/// Bu sayede:
///   - Typo anında derleme hatası üretir (runtime sessiz 403 değil)
///   - Startup assertion DB ile tutarlılığı doğrular
///   - Yeni ekran eklemek tek satır değişiklik olur
///
/// Startup assertion: <see cref="FormCodeValidator"/> — DB'deki aktif FormCode'lar
/// ile buradaki sabitler karşılaştırılır; eşleşmeyen varsa WARNING log üretilir.
/// </summary>
public static class FormCodes
{
    // ═══════════════════════════════════════════════════
    // GENEL
    // ═══════════════════════════════════════════════════
    public const string Notes    = "NOTES";
    public const string OrgChart = "ORG_CHART";
    public const string WhatsApp = "WHATSAPP";
    public const string BulkMail = "BULK_MAIL";

    // ═══════════════════════════════════════════════════
    // RAPORLAR
    // ═══════════════════════════════════════════════════
    public const string Dashboards     = "DASHBOARDS";
    public const string ReportDesigner = "REPORT_DESIGNER";

    // ═══════════════════════════════════════════════════
    // ONAY İŞLEMLERİ
    // ═══════════════════════════════════════════════════
    public const string ApprovalPending = "APPROVAL_PENDING";
    public const string EInvoice        = "EINVOICE";
    public const string EArchive        = "EARCHIVE";
    public const string EDispatch       = "EDISPATCH";

    // ═══════════════════════════════════════════════════
    // LOJİSTİK — Malzeme
    // ═══════════════════════════════════════════════════
    public const string MaterialCardEdit    = "MATERIAL_CARD_EDIT";
    public const string ProductConfig       = "PRODUCT_CONFIG";
    public const string ProductFeatureEdit  = "PRODUCT_FEATURE_EDIT";
    public const string ProductCombinations = "PRODUCT_COMBINATIONS";

    // ═══════════════════════════════════════════════════
    // LOJİSTİK — Depo
    // ═══════════════════════════════════════════════════
    public const string Transfer       = "TRANSFER";
    public const string TransferLines  = "TRANSFER_LINES";
    public const string StockIn        = "STOCK_IN";
    public const string StockInLines   = "STOCK_IN_LINES";
    public const string StockOut       = "STOCK_OUT";
    public const string StockOutLines  = "STOCK_OUT_LINES";
    public const string InventoryCount = "INVENTORY_COUNT";

    // ═══════════════════════════════════════════════════
    // SATIŞ
    // ═══════════════════════════════════════════════════
    public const string SalesQuote      = "SALES_QUOTE";
    public const string SalesQuoteNew   = "SALES_QUOTE_NEW";
    public const string SalesQuoteEdit  = "SALES_QUOTE_EDIT";
    public const string SalesQuoteLines = "SALES_QUOTE_LINES";

    public const string SalesOrder      = "SALES_ORDER";
    public const string SalesOrderNew   = "SALES_ORDER_NEW";
    public const string SalesOrderEdit  = "SALES_ORDER_EDIT";
    public const string SalesOrderLines = "SALES_ORDER_LINES";

    // ═══════════════════════════════════════════════════
    // SATIN ALMA
    // ═══════════════════════════════════════════════════
    public const string PurchaseRequest      = "PURCHASE_REQUEST";
    public const string PurchaseRequestNew   = "PURCHASE_REQUEST_NEW";
    public const string PurchaseRequestEdit  = "PURCHASE_REQUEST_EDIT";
    public const string PurchaseRequestLines = "PURCHASE_REQUEST_LINES";

    public const string PurchaseQuote      = "PURCHASE_QUOTE";
    public const string PurchaseQuoteNew   = "PURCHASE_QUOTE_NEW";
    public const string PurchaseQuoteEdit  = "PURCHASE_QUOTE_EDIT";
    public const string PurchaseQuoteLines = "PURCHASE_QUOTE_LINES";

    public const string PurchaseOrder      = "PURCHASE_ORDER";
    public const string PurchaseOrderNew   = "PURCHASE_ORDER_NEW";
    public const string PurchaseOrderEdit  = "PURCHASE_ORDER_EDIT";
    public const string PurchaseOrderLines = "PURCHASE_ORDER_LINES";

    public const string PurchaseDemand        = "PURCHASE_DEMAND";
    public const string PurchaseDemandNew    = "PURCHASE_DEMAND_NEW";
    public const string PurchaseDemandEdit   = "PURCHASE_DEMAND_EDIT";
    public const string PurchaseDemandLines  = "PURCHASE_DEMAND_LINES";

    public const string PurchaseFulfillment  = "PURCHASE_FULFILLMENT";

    // ═══════════════════════════════════════════════════
    // ÜRETİM
    // ═══════════════════════════════════════════════════
    public const string BomEdit = "BOM_EDIT";

    public const string WorkOrders     = "WORK_ORDERS";
    public const string WorkOrderEdit  = "WORK_ORDER_EDIT";
    public const string ShopFloor      = "SHOP_FLOOR";
    public const string ProductionDefs = "PRODUCTION_DEFS";

    public const string Operations   = "OPERATIONS";
    public const string OperationEdit = "OPERATION_EDIT";

    public const string Routings             = "ROUTINGS";
    public const string RoutingEdit          = "ROUTING_EDIT";
    public const string RoutingOperationEdit = "ROUTING_OPERATION_EDIT";

    public const string Personnel     = "PERSONNEL";
    public const string PersonnelEdit = "PERSONNEL_EDIT";

    public const string Shifts    = "SHIFTS";
    public const string ShiftEdit = "SHIFT_EDIT";

    public const string ActivityReasons    = "ACTIVITY_REASONS";
    public const string ActivityReasonEdit = "ACTIVITY_REASON_EDIT";

    // ═══════════════════════════════════════════════════
    // FİNANS
    // ═══════════════════════════════════════════════════
    public const string Contacts    = "CONTACTS";
    // 2026-06-13 — CONTACTS + CONTACT_EDIT birleştirildi: liste ve düzenleme tek FormCode
    // ("CONTACTS") altında. Diğer modüllerle aynı pattern. Eski "CONTACT_EDIT" form'u
    // DB migration'da IsActive=0 yapılır; yeni DB'lerde seed edilmez.
    public const string ContactEdit = "CONTACTS";

    // ═══════════════════════════════════════════════════
    // GENEL TANIMLAMALAR
    // ═══════════════════════════════════════════════════
    public const string SalesReps     = "SALES_REPS";
    public const string Currencies    = "CURRENCIES";
    public const string Locations     = "LOCATIONS";
    public const string MeasureUnits  = "MEASURE_UNITS";
    public const string MaterialGroups = "MATERIAL_GROUPS";
    public const string CardGroups    = "CARD_GROUPS";
    public const string Departments   = "DEPARTMENTS";
    public const string PriceList     = "PRICE_LIST";
    public const string PriceGroups   = "PRICE_GROUPS";
    public const string Machines      = "MACHINES";
    public const string MachineTypes  = "MACHINE_TYPES";

    // ═══════════════════════════════════════════════════
    // DÖKÜMAN YÖNETİMİ
    // ═══════════════════════════════════════════════════
    public const string DocumentManagement = "DOC_MANAGEMENT";

    // ═══════════════════════════════════════════════════
    // VARLIK YÖNETİMİ
    // ═══════════════════════════════════════════════════
    public const string Assets = "ASSETS";

    // ═══════════════════════════════════════════════════
    // TASARIM
    // ═══════════════════════════════════════════════════
    public const string DocTemplates    = "DOC_TEMPLATES";
    public const string DocLayoutRules  = "DOC_LAYOUT_RULES";
    public const string DocNumberRules  = "DOC_NUMBER_RULES";

    // ═══════════════════════════════════════════════════
    // AYARLAR
    // ═══════════════════════════════════════════════════
    public const string CompanySettings    = "COMPANY_SETTINGS";
    public const string PermissionMgmt     = "PERMISSION_MGMT";
    public const string DataVisibility     = "DATA_VISIBILITY";
    public const string IntegrationEvents  = "INTEGRATION_EVENTS";
    public const string Integrations       = "INTEGRATIONS";
    public const string ViewSettings       = "VIEW_SETTINGS";
    public const string SetupDefinitions   = "SETUP_DEFINITIONS";
    public const string Scheduler          = "SCHEDULER";
    public const string ApprovalFlows      = "APPROVAL_FLOWS";
    public const string DecimalSettings    = "DECIMAL_SETTINGS";
    public const string GeneralDefs        = "GENERAL_DEFS";      // Genel Tanımlamalar sayfası (Adres Tanımlama: Ülke/Şehir/İlçe)
    public const string AuditLog           = "AUDIT_LOG";         // İşlem Logları izleme/raporlama ekranı

    // ═══════════════════════════════════════════════════
    // GENEL (yetki kapsamına sonradan alınanlar — 2026-07-06)
    // ═══════════════════════════════════════════════════
    public const string Calendar           = "CALENDAR";
    public const string DataImport         = "DATA_IMPORT";

    // ═══════════════════════════════════════════════════
    // Tüm sabitler (startup assertion için reflection alternatifi)
    // ═══════════════════════════════════════════════════
    /// <summary>
    /// Bu sınıftaki tüm public const string değerlerini döner.
    /// Startup assertion'da DB ile karşılaştırmak için kullanılır.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = typeof(FormCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

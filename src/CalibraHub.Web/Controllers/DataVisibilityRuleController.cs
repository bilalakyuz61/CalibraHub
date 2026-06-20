using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Web.Authorization;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-06-13 — Veri Perdeleme Kuralları (satır bazlı veri kısıtlama / row-level security) yönetim
/// ekranı + JSON CRUD. Operatör bazlı kısıtlama: kurala takılan satırlar HERKESE gizlenir.
///
/// Endpoint'ler:
///   GET  /Admin/DataVisibilityRules               → SmartBoard liste view
///   GET  /Admin/DataVisibilityRules/BoardConfig   → board refresh JSON
///   GET  /Admin/DataVisibilityRuleEdit?id=        → düzenleme view (tam sayfa, tablı)
///   POST /Admin/SaveDataVisibilityRule            → upsert (CSRF)
///   POST /Admin/DeleteDataVisibilityRule?id=      → sil (CSRF)
///   POST /Admin/ToggleDataVisibilityRule?id=      → aktif/pasif (CSRF)
///   GET  /Admin/GetFormFieldKeys?formCode=&amp;fieldKind=0|1 → dinamik alan listesi
/// </summary>
[Authorize]
[PermissionScope(FormCodes.DataVisibility)]
public sealed class DataVisibilityRuleController : Controller
{
    private readonly IDataVisibilityRuleRepository _ruleRepo;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly IFormRepository _formRepo;
    private readonly IWidgetRepository _widgetRepo;
    private readonly SqlServerConnectionFactory _connFactory;

    public DataVisibilityRuleController(
        IDataVisibilityRuleRepository ruleRepo,
        IDataVisibilityFilter dvFilter,
        IFormRepository formRepo,
        IWidgetRepository widgetRepo,
        SqlServerConnectionFactory connFactory)
    {
        _ruleRepo = ruleRepo;
        _dvFilter = dvFilter;
        _formRepo = formRepo;
        _widgetRepo = widgetRepo;
        _connFactory = connFactory;
    }

    // ── Operatör meta (UI etiketleri) ──────────────────────────────────────────
    private static readonly (string Value, string Label)[] Operators =
    {
        ("eq",         "Eşittir (=)"),
        ("neq",        "Eşit değil (≠)"),
        ("gt",         "Büyüktür (>)"),
        ("gte",        "Büyük veya eşit (≥)"),
        ("lt",         "Küçüktür (<)"),
        ("lte",        "Küçük veya eşit (≤)"),
        ("between",    "Arasında (BETWEEN)"),
        ("in",         "Listede (IN)"),
        ("not_in",     "Listede değil (NOT IN)"),
        ("like",       "İçerir (LIKE)"),
        ("not_like",   "İçermez (NOT LIKE)"),
        ("startswith", "İle başlar"),
        ("endswith",   "İle biter"),
        ("isnull",     "Boş (NULL)"),
        ("isnotnull",  "Dolu (NOT NULL)"),
    };

    private static string OperatorLabel(string op) =>
        Operators.FirstOrDefault(o => o.Value == op).Label ?? op;

    /// <summary>Entegre formlar her zaman dropdown'da bulunsun (DB seed durumundan bağımsız).</summary>
    private static readonly (string Code, string Name)[] GuaranteedForms =
    {
        ("CONTACTS",              "Cari Hesaplar"),
        ("PERSONNEL",             "Personel"),
        ("ASSETS",                "Varlık Yönetimi"),
        ("WORK_ORDERS",           "İş Emirleri"),
        ("PRICE_LIST",            "Fiyat Listesi"),
        ("SALES_QUOTE",           "Satış Teklifi"),
        ("BOM_EDIT",              "Ürün Ağacı"),
        ("MATERIAL_CARD_EDIT",    "Malzeme Kartı"),
        ("OPERATIONS",            "Operasyonlar"),
        ("ROUTINGS",              "Rotalar"),
        ("MACHINES",              "Makineler"),
        ("DEPARTMENTS",           "Departmanlar"),
    };

    /// <summary>BaseTable NULL olan formlar için manuel tablo eşlemesi (kolon keşfi için).</summary>
    private static readonly Dictionary<string, string> FormTableFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CONTACTS"]              = "Contact",
        ["PERSONNEL"]             = "Personnel",
        ["PERSONNEL_EDIT"]        = "Personnel",
        ["ASSETS"]                = "Asset",
        ["WORK_ORDERS"]           = "WorkOrder",
        ["WORK_ORDER_EDIT"]       = "WorkOrder",
        ["PRICE_LIST"]            = "PriceGroup",
        ["PRICE_GROUPS"]          = "PriceGroup",
        ["SALES_QUOTE"]           = "Document",
        ["SALES_QUOTE_EDIT"]      = "Document",
        ["SALES_ORDER"]           = "Document",
        ["SALES_ORDER_EDIT"]      = "Document",
        ["PURCHASE_REQUEST"]      = "Document",
        ["PURCHASE_REQUEST_EDIT"] = "Document",
        ["PURCHASE_QUOTE"]        = "Document",
        ["PURCHASE_QUOTE_EDIT"]   = "Document",
        ["PURCHASE_ORDER"]        = "Document",
        ["PURCHASE_ORDER_EDIT"]   = "Document",
        ["PURCHASE_DEMAND"]       = "Document",
        ["PURCHASE_DEMAND_EDIT"]  = "Document",
        ["PURCHASE_FULFILLMENT"]  = "Document",
        ["MACHINES"]              = "Machine",
        ["MACHINE_TYPES"]         = "MachineType",
        ["OPERATIONS"]            = "Operation",
        ["OPERATION_EDIT"]        = "Operation",
        ["ROUTINGS"]              = "Routing",
        ["ROUTING_EDIT"]          = "Routing",
        ["DEPARTMENTS"]           = "Department",
        ["SALES_REPS"]            = "sales_representatives",
        ["LOCATIONS"]             = "Location",
        ["MEASURE_UNITS"]         = "Unit",
        ["MATERIAL_CARD_EDIT"]    = "Items",
        ["MATERIAL_GROUPS"]       = "ItemGroup",
        ["BOM_EDIT"]              = "BOM",
        ["SHOP_FLOOR"]            = "WorkOrder",
    };

    /// <summary>Dropdown'dan çıkarılan alt-form ad işaretleri.</summary>
    private static readonly string[] SubFormMarkers =
        { "Kalem Bilgisi", "Detay", "Alt Kalem", "Satır", "Üst Bilgi", "Header" };

    private static bool IsSubForm(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) return true;
        if (n.Equals("Yeni", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("New", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("Düzenleme", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var m in SubFormMarkers)
            if (n.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ── Column label helpers ───────────────────────────────────────────────────

    private static string GetColumnLabel(string col, bool isEn)
    {
        if (ColLabels.TryGetValue(col, out var p)) return isEn ? p.en : p.tr;
        return HumanizeColumn(col);
    }

    private static string HumanizeColumn(string col)
    {
        if (col.Contains('_'))
            return string.Join(" ", col.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length == 0 ? w : char.ToUpper(w[0]) + w[1..]));
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < col.Length; i++)
        {
            if (i > 0 && char.IsUpper(col[i]) && !char.IsUpper(col[i - 1]))
                sb.Append(' ');
            sb.Append(col[i]);
        }
        return sb.ToString();
    }

    private static readonly Dictionary<string, (string tr, string en)> ColLabels =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Audit / system
        ["Id"]                  = ("ID",                      "ID"),
        ["Name"]                = ("Ad",                      "Name"),
        ["Code"]                = ("Kod",                     "Code"),
        ["Description"]         = ("Açıklama",                "Description"),
        ["IsActive"]            = ("Aktif",                   "Active"),
        ["Created"]             = ("Oluşturma Tarihi",        "Created Date"),
        ["Updated"]             = ("Güncelleme Tarihi",       "Updated Date"),
        ["CreatedBy"]           = ("Oluşturan",               "Created By"),
        ["UpdatedBy"]           = ("Güncelleyen",             "Updated By"),
        ["CreatedById"]         = ("Oluşturan",               "Created By"),
        ["UpdatedById"]         = ("Güncelleyen",             "Updated By"),
        ["SortOrder"]           = ("Sıra",                    "Sort Order"),
        ["Notes"]               = ("Notlar",                  "Notes"),
        ["ParentId"]            = ("Üst Kayıt",               "Parent"),
        ["CompanyId"]           = ("Şirket",                  "Company"),
        ["Guid"]                = ("GUID",                    "GUID"),
        ["Tags"]                = ("Etiketler",               "Tags"),
        ["Version"]             = ("Versiyon",                "Version"),
        ["Type"]                = ("Tür",                     "Type"),
        ["Kind"]                = ("Tür",                     "Kind"),
        ["Status"]              = ("Durum",                   "Status"),
        ["StatusId"]            = ("Durum",                   "Status"),
        ["IsDefault"]           = ("Varsayılan",              "Default"),
        ["IsPrimary"]           = ("Birincil",                "Primary"),
        ["IsPublic"]            = ("Herkese Açık",            "Public"),
        ["ExpiryDate"]          = ("Son Kullanma Tarihi",     "Expiry Date"),
        ["ValidFrom"]           = ("Geçerlilik Başlangıcı",   "Valid From"),
        ["ValidTo"]             = ("Geçerlilik Bitişi",       "Valid To"),
        // Common FKs
        ["CurrencyId"]          = ("Para Birimi",             "Currency"),
        ["GroupId"]             = ("Grup",                    "Group"),
        ["TypeId"]              = ("Tür",                     "Type"),
        ["UnitId"]              = ("Birim",                   "Unit"),
        ["CategoryId"]          = ("Kategori",                "Category"),
        ["DepartmentId"]        = ("Departman",               "Department"),
        ["PersonnelId"]         = ("Personel",                "Personnel"),
        ["ContactId"]           = ("Cari Hesap",              "Contact"),
        ["MachineId"]           = ("Makine",                  "Machine"),
        ["OperationId"]         = ("Operasyon",               "Operation"),
        ["RoutingId"]           = ("Rota",                    "Routing"),
        ["ItemId"]              = ("Malzeme",                 "Item"),
        ["WarehouseId"]         = ("Depo",                    "Warehouse"),
        ["LocationId"]          = ("Lokasyon",                "Location"),
        ["CustomerId"]          = ("Müşteri",                 "Customer"),
        ["SupplierId"]          = ("Tedarikçi",               "Supplier"),
        ["MaterialGroupId"]     = ("Malzeme Grubu",           "Material Group"),
        ["ContactGroupId"]      = ("Cari Grup",               "Contact Group"),
        ["PaymentTermId"]       = ("Vade Koşulu",             "Payment Terms"),
        ["SalesRepId"]          = ("Satış Temsilcisi",        "Sales Rep"),
        // Items / Malzeme
        ["TaxRate"]             = ("Vergi Oranı (%)",         "Tax Rate (%)"),
        ["Combinations"]        = ("Kombinasyon",             "Combinations"),
        ["TrackCombinations"]   = ("Kombinasyon Takibi",      "Track Combinations"),
        ["ListPrice"]           = ("Liste Fiyatı",            "List Price"),
        ["SalesPrice"]          = ("Satış Fiyatı",            "Sales Price"),
        ["PurchasePrice"]       = ("Alış Fiyatı",             "Purchase Price"),
        ["Weight"]              = ("Ağırlık",                 "Weight"),
        ["Volume"]              = ("Hacim",                   "Volume"),
        ["Barcode"]             = ("Barkod",                  "Barcode"),
        ["ImageData"]           = ("Resim",                   "Image"),
        ["ImageMimeType"]       = ("Resim Türü",              "Image Type"),
        // snake_case legacy (Items ve diğer eski tablolar)
        ["description"]         = ("Açıklama",                "Description"),
        ["type_id"]             = ("Tür",                     "Type"),
        ["track_combinations"]  = ("Kombinasyon Takibi",      "Track Combinations"),
        ["created_by_user_id"]  = ("Oluşturan",               "Created By"),
        ["modified_at"]         = ("Değiştirilme Tarihi",     "Modified Date"),
        ["modified_by_user_id"] = ("Değiştiren",              "Modified By"),
        ["image_data"]          = ("Resim",                   "Image"),
        ["image_mime_type"]     = ("Resim Türü",              "Image Type"),
        ["group_code"]          = ("Grup Kodu",               "Group Code"),
        ["barcode"]             = ("Barkod",                  "Barcode"),
        ["is_active"]           = ("Aktif",                   "Active"),
        ["created_at"]          = ("Oluşturma Tarihi",        "Created Date"),
        ["updated_at"]          = ("Güncelleme Tarihi",       "Updated Date"),
        ["created_by"]          = ("Oluşturan",               "Created By"),
        ["updated_by"]          = ("Güncelleyen",             "Updated By"),
        ["sort_order"]          = ("Sıra",                    "Sort Order"),
        ["parent_id"]           = ("Üst Kayıt",               "Parent"),
        // Contact / Cari Hesap
        ["TaxNumber"]           = ("Vergi No",                "Tax Number"),
        ["TaxOffice"]           = ("Vergi Dairesi",           "Tax Office"),
        ["Phone"]               = ("Telefon",                 "Phone"),
        ["Mobile"]              = ("Cep Telefonu",            "Mobile"),
        ["Email"]               = ("E-posta",                 "Email"),
        ["Address"]             = ("Adres",                   "Address"),
        ["City"]                = ("Şehir",                   "City"),
        ["Country"]             = ("Ülke",                    "Country"),
        ["PostalCode"]          = ("Posta Kodu",              "Postal Code"),
        ["CreditLimit"]         = ("Kredi Limiti",            "Credit Limit"),
        ["Balance"]             = ("Bakiye",                  "Balance"),
        ["IBAN"]                = ("IBAN",                    "IBAN"),
        ["BankName"]            = ("Banka",                   "Bank"),
        ["IsCustomer"]          = ("Müşteri",                 "Customer"),
        ["IsSupplier"]          = ("Tedarikçi",               "Supplier"),
        ["WarehouseCode"]       = ("Depo Kodu",               "Warehouse Code"),
        // Personnel / Personel
        ["FullName"]            = ("Ad Soyad",                "Full Name"),
        ["FirstName"]           = ("Ad",                      "First Name"),
        ["LastName"]            = ("Soyad",                   "Last Name"),
        ["BirthDate"]           = ("Doğum Tarihi",            "Birth Date"),
        ["HireDate"]            = ("İşe Giriş Tarihi",        "Hire Date"),
        ["Position"]            = ("Pozisyon",                "Position"),
        ["Title"]               = ("Unvan",                   "Title"),
        ["Salary"]              = ("Maaş",                    "Salary"),
        ["WorkDays"]            = ("Çalışma Günleri",         "Work Days"),
        ["IdentityNo"]          = ("TC Kimlik No",            "Identity No"),
        // Document / Belge
        ["DocumentNumber"]      = ("Belge No",                "Document No"),
        ["DocumentDate"]        = ("Belge Tarihi",            "Document Date"),
        ["DueDate"]             = ("Vade Tarihi",             "Due Date"),
        ["DeliveryDate"]        = ("Teslimat Tarihi",         "Delivery Date"),
        ["TotalAmount"]         = ("Toplam Tutar",            "Total Amount"),
        ["NetAmount"]           = ("Net Tutar",               "Net Amount"),
        ["TaxAmount"]           = ("Vergi Tutarı",            "Tax Amount"),
        ["ApprovalStatus"]      = ("Onay Durumu",             "Approval Status"),
        ["Reference"]           = ("Referans",                "Reference"),
        // WorkOrder / İş Emri
        ["Quantity"]            = ("Miktar",                  "Quantity"),
        ["PlannedQuantity"]     = ("Planlanan Miktar",        "Planned Qty"),
        ["CompletedQty"]        = ("Tamamlanan Miktar",       "Completed Qty"),
        ["ScrapQty"]            = ("Fire Miktarı",            "Scrap Qty"),
        ["StartDate"]           = ("Başlangıç Tarihi",        "Start Date"),
        ["EndDate"]             = ("Bitiş Tarihi",            "End Date"),
        ["PlannedStart"]        = ("Planlanan Başlangıç",     "Planned Start"),
        ["PlannedEnd"]          = ("Planlanan Bitiş",         "Planned End"),
        ["Priority"]            = ("Öncelik",                 "Priority"),
        // Machine / Makine
        ["MachineName"]         = ("Makine Adı",              "Machine Name"),
        ["MachineCode"]         = ("Makine Kodu",             "Machine Code"),
        ["Capacity"]            = ("Kapasite",                "Capacity"),
        ["CycleTime"]           = ("Çevrim Süresi",           "Cycle Time"),
        ["IsMachinePark"]       = ("Makine Parkı",            "Machine Park"),
        // BOM / Ürün Ağacı
        ["ScrapRatio"]          = ("Fire Oranı (%)",          "Scrap Ratio (%)"),
        ["ConfigId"]            = ("Konfigürasyon",           "Configuration"),
        // Operation / Operasyon
        ["StandardTime"]        = ("Standart Süre",           "Standard Time"),
        ["SetupTime"]           = ("Hazırlık Süresi",         "Setup Time"),
        // Asset / Varlık
        ["SerialNumber"]        = ("Seri No",                 "Serial No"),
        ["PurchaseDate"]        = ("Alış Tarihi",             "Purchase Date"),
        ["PurchaseCost"]        = ("Alış Maliyeti",           "Purchase Cost"),
        ["DepreciationRate"]    = ("Amortisman Oranı",        "Depreciation Rate"),
        ["CurrentValue"]        = ("Güncel Değer",            "Current Value"),
        ["WarrantyExpiry"]      = ("Garanti Bitiş",           "Warranty Expiry"),
        // Generic
        ["Value"]               = ("Değer",                   "Value"),
        ["Label"]               = ("Etiket",                  "Label"),
        ["Color"]               = ("Renk",                    "Color"),
        ["Icon"]                = ("İkon",                    "Icon"),
        ["Order"]               = ("Sıra",                    "Order"),
        ["Rank"]                = ("Sıra",                    "Rank"),
        ["Level"]               = ("Seviye",                  "Level"),
        ["Path"]                = ("Yol",                     "Path"),
        ["Url"]                 = ("URL",                     "URL"),
        ["Key"]                 = ("Anahtar",                 "Key"),
        ["Size"]                = ("Boyut",                   "Size"),
        ["Format"]              = ("Format",                  "Format"),
        ["Rate"]                = ("Oran",                    "Rate"),
        ["Amount"]              = ("Tutar",                   "Amount"),
        ["Count"]               = ("Adet",                    "Count"),
        ["Total"]               = ("Toplam",                  "Total"),
        ["Max"]                 = ("Maksimum",                "Maximum"),
        ["Min"]                 = ("Minimum",                 "Minimum"),
    };

    private int? CurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    // ── Liste + board refresh ──────────────────────────────────────────────────

    [HttpGet("/Admin/DataVisibilityRules")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return View("~/Views/Admin/DataVisibilityRules.cshtml",
            new DataVisibilityRuleListViewModel { BoardConfig = config });
    }

    [HttpGet("/Admin/DataVisibilityRules/BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
        => Json(await BuildBoardConfigAsync(ct));

    // ── Düzenleme view ─────────────────────────────────────────────────────────

    [HttpGet("/Admin/DataVisibilityRuleEdit")]
    public async Task<IActionResult> Edit(int? id, CancellationToken ct)
    {
        var formOptions = await BuildFormOptionsAsync(ct);

        if (id.HasValue && id.Value > 0)
        {
            var rule = await _ruleRepo.GetByIdAsync(id.Value, ct);
            if (rule is null) return NotFound();
            return View("~/Views/Admin/DataVisibilityRuleEdit.cshtml", new DataVisibilityRuleEditViewModel
            {
                Id        = rule.Id,
                Name      = rule.Name,
                FormCode  = rule.FormCode,
                FieldKind = (int)rule.FieldKind,
                FieldKey  = rule.FieldKey,
                Operator  = rule.Operator,
                WidgetId  = rule.WidgetId,
                IsActive  = rule.IsActive,
                Values    = rule.Values.Select(v => v.ValueText ?? v.ValueId?.ToString() ?? string.Empty)
                                       .Where(s => s.Length > 0).ToList(),
                FormOptions = formOptions,
            });
        }

        return View("~/Views/Admin/DataVisibilityRuleEdit.cshtml",
            new DataVisibilityRuleEditViewModel { FormOptions = formOptions });
    }

    // ── Upsert ─────────────────────────────────────────────────────────────────

    [HttpPost("/Admin/SaveDataVisibilityRule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] DataVisibilityRuleSaveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))     return Json(new { success = false, message = "Kural adı zorunlu." });
        if (string.IsNullOrWhiteSpace(req.FormCode)) return Json(new { success = false, message = "Form seçimi zorunlu." });
        if (string.IsNullOrWhiteSpace(req.FieldKey)) return Json(new { success = false, message = "Kısıtlanacak alan zorunlu." });
        if (string.IsNullOrWhiteSpace(req.Operator)) return Json(new { success = false, message = "Operatör zorunlu." });

        var fieldKind = req.FieldKind == 1 ? DataVisibilityFieldKind.Widget : DataVisibilityFieldKind.Column;
        if (fieldKind == DataVisibilityFieldKind.Widget && (req.WidgetId is null || req.WidgetId <= 0))
            return Json(new { success = false, message = "Widget alanı için WidgetId zorunlu." });

        var op = req.Operator.Trim().ToLowerInvariant();
        if (!Operators.Any(o => o.Value == op))
            return Json(new { success = false, message = "Geçersiz operatör." });

        var noValue = op is "isnull" or "isnotnull";
        var needsTwo = op is "between";
        var isList = op is "in" or "not_in";
        var vals = (req.Values ?? new List<string>())
            .Select(v => (v ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .ToList();

        if (!noValue && vals.Count == 0)
            return Json(new { success = false, message = "Bu operatör için en az bir değer girilmeli." });
        if (needsTwo && vals.Count < 2)
            return Json(new { success = false, message = "BETWEEN için başlangıç ve bitiş değeri girilmeli." });

        var userId = CurrentUserId();
        var rule = new DataVisibilityRule
        {
            Id          = req.Id,
            Name        = req.Name.Trim(),
            FormCode    = req.FormCode.Trim().ToUpperInvariant(),
            FieldKind   = fieldKind,
            FieldKey    = req.FieldKey.Trim(),
            Operator    = op,
            WidgetId    = fieldKind == DataVisibilityFieldKind.Widget ? req.WidgetId : null,
            IsActive    = req.IsActive,
            CreatedById = req.Id <= 0 ? userId : null,
            UpdatedById = req.Id > 0 ? userId : null,
        };

        if (!noValue)
        {
            var chosen = needsTwo ? vals.Take(2) : (isList ? vals : vals.Take(1));
            var isTextOp = op is "like" or "not_like" or "startswith" or "endswith";
            foreach (var v in chosen)
            {
                var rv = new DataVisibilityRuleValue();
                // ID-bazlı eşleştirme: sayısal değer ValueId (int) olarak; metin/LIKE ValueText olarak.
                if (!isTextOp && int.TryParse(v, out var iv)) rv.ValueId = iv;
                else rv.ValueText = v;
                rule.Values.Add(rv);
            }
        }

        // Grant'lar sadece frontend sekmeyi açmış ve payload'a dahil etmişse güncellenir.
        // null gelirse sekme açılmamış demektir — mevcut grant'lar DB'den okunarak korunur.
        // Boş dizi gelirse tüm grant'lar temizlenir (bilinçli silme).
        if (req.GrantUserIds is not null)
        {
            foreach (var uid in req.GrantUserIds.Where(id => id > 0).Distinct())
                rule.Grants.Add(new DataVisibilityGrant { UserId = uid });
            foreach (var did in (req.GrantDeptIds ?? new List<int>()).Where(id => id > 0).Distinct())
                rule.Grants.Add(new DataVisibilityGrant { DepartmentId = did });
        }
        else if (rule.Id > 0)
        {
            var existingRule = await _ruleRepo.GetByIdAsync(rule.Id, ct);
            if (existingRule is not null)
                foreach (var g in existingRule.Grants)
                    rule.Grants.Add(new DataVisibilityGrant { UserId = g.UserId, DepartmentId = g.DepartmentId });
        }

        try
        {
            var id = await _ruleRepo.SaveAsync(rule, ct);
            _dvFilter.InvalidateCache(rule.FormCode);
            return Json(new { success = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Sil ────────────────────────────────────────────────────────────────────

    [HttpPost("/Admin/DeleteDataVisibilityRule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var rule = await _ruleRepo.GetByIdAsync(id, ct);
        await _ruleRepo.DeleteAsync(id, ct);
        if (rule is not null) _dvFilter.InvalidateCache(rule.FormCode);
        return Json(new { success = true });
    }

    // ── Aktif/Pasif ──────────────────────────────────────────────────────────────

    [HttpPost("/Admin/ToggleDataVisibilityRule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, CancellationToken ct)
    {
        var rule = await _ruleRepo.GetByIdAsync(id, ct);
        if (rule is null) return Json(new { success = false, message = "Kural bulunamadı." });
        var newState = !rule.IsActive;
        await _ruleRepo.SetActiveAsync(id, newState, CurrentUserId(), ct);
        _dvFilter.InvalidateCache(rule.FormCode);
        return Json(new { success = true, isActive = newState });
    }

    // ── Grant seçenekleri ─────────────────────────────────────────────────────

    [HttpGet("/Admin/GetDataVisibilityGrantOptions")]
    public async Task<IActionResult> GetGrantOptions(int ruleId, CancellationToken ct)
    {
        var companyId = int.TryParse(User.FindFirstValue("company_id"), out var cid) ? cid : 0;
        var users = await GetActiveGrantUsersAsync(companyId, ct);
        var departments = await GetActiveGrantDepartmentsAsync(ct);

        var grantUserIds = new List<int>();
        var grantDeptIds = new List<int>();
        if (ruleId > 0)
        {
            var rule = await _ruleRepo.GetByIdAsync(ruleId, ct);
            if (rule is not null)
            {
                grantUserIds = rule.Grants.Where(g => g.UserId.HasValue).Select(g => g.UserId!.Value).ToList();
                grantDeptIds = rule.Grants.Where(g => g.DepartmentId.HasValue).Select(g => g.DepartmentId!.Value).ToList();
            }
        }

        return Json(new { users, departments, grantUserIds, grantDeptIds });
    }

    private async Task<List<object>> GetActiveGrantUsersAsync(int companyId, CancellationToken ct)
    {
        var list = new List<object>();
        await using var conn = await _connFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [Id], COALESCE(NULLIF(LTRIM(RTRIM([FullName])), ''), [Email], '') AS [Name]
            FROM [dbo].[Users] WHERE [IsActive] = 1 AND [CompanyId] = @CId
            ORDER BY [Name];";
        cmd.Parameters.AddWithValue("@CId", companyId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new { id = r.GetInt32(0), name = r.GetString(1) });
        return list;
    }

    private async Task<List<object>> GetActiveGrantDepartmentsAsync(CancellationToken ct)
    {
        var list = new List<object>();
        await using var conn = await _connFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [Id], [Name] FROM [dbo].[Department] WHERE [IsActive] = 1
            ORDER BY [Name];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new { id = r.GetInt32(0), name = r.GetString(1) });
        return list;
    }

    // ── Dinamik alan listesi ──────────────────────────────────────────────────

    [HttpGet("/Admin/GetFormFieldKeys")]
    public async Task<IActionResult> GetFormFieldKeys(string formCode, int fieldKind, bool isEn = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(formCode)) return Json(Array.Empty<object>());
        formCode = formCode.Trim();

        // Widget modu — WidgetMas üzerinden widget kodları.
        if (fieldKind == 1)
        {
            var form = await _widgetRepo.GetFormByCodeAsync(formCode, ct);
            if (form is null) return Json(Array.Empty<object>());
            var widgets = await _widgetRepo.GetWidgetsByFormAsync(form.Id, ct);
            var items = widgets
                .Where(w => !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase))
                .Select(w => (object)new { value = w.WidgetCode, label = w.Label, widgetId = w.Id })
                .ToList();
            return Json(items);
        }

        // Kolon modu — INFORMATION_SCHEMA.COLUMNS + BaseTable/fallback eşlemesi.
        var tableName = await ResolveTableNameAsync(formCode, ct);
        if (string.IsNullOrWhiteSpace(tableName)) return Json(Array.Empty<object>());
        var cols = await GetTableColumnsAsync(tableName!, ct);
        return Json(cols.Select(c => (object)new { value = c, label = GetColumnLabel(c, isEn) }));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<List<DataVisibilityRuleEditViewModel.FormOption>> BuildFormOptionsAsync(CancellationToken ct)
    {
        var forms = await _formRepo.GetAllAsync(ct);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in forms.Where(f => f.IsActive && !IsSubForm(f.FormName)))
            dict[f.FormCode] = f.FormName;
        foreach (var (code, name) in GuaranteedForms)
            if (!dict.ContainsKey(code)) dict[code] = name;

        return dict.OrderBy(kv => kv.Value, StringComparer.CurrentCulture)
                   .Select(kv => new DataVisibilityRuleEditViewModel.FormOption(kv.Key, kv.Value))
                   .ToList();
    }

    private async Task<string?> ResolveTableNameAsync(string formCode, CancellationToken ct)
    {
        var form = await _formRepo.GetByCodeAsync(formCode, ct);
        var stripped = StripTable(form?.BaseTable);
        if (!string.IsNullOrWhiteSpace(stripped)) return stripped;
        return FormTableFallback.TryGetValue(formCode, out var t) ? t : null;
    }

    private static string? StripTable(string? baseTable)
    {
        if (string.IsNullOrWhiteSpace(baseTable)) return null;
        var s = baseTable.Replace("[", "").Replace("]", "").Trim();
        var dot = s.LastIndexOf('.');
        return dot >= 0 ? s[(dot + 1)..] : s;
    }

    private async Task<List<string>> GetTableColumnsAsync(string tableName, CancellationToken ct)
    {
        var cols = new List<string>();
        await using var conn = await _connFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [COLUMN_NAME]
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE [TABLE_NAME] = @t
            ORDER BY [ORDINAL_POSITION];";
        cmd.Parameters.AddWithValue("@t", tableName);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) cols.Add(r.GetString(0));
        return cols;
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var rules = await _ruleRepo.ListAllAsync(includeInactive: true, ct);
        var forms = await _formRepo.GetAllAsync(ct);
        var formNames = forms
            .GroupBy(f => f.FormCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().FormName, StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in GuaranteedForms)
            formNames.TryAdd(code, name);

        string FormName(string code) => formNames.TryGetValue(code, out var n) ? n : code;

        // Widget alanlı kurallar için WidgetId → Türkçe Label haritası. Kullanıcı, ALAN çipinde
        // alanın ham kodunu değil, widget'ın görünen (Türkçe) adını görmeli. Sadece widget
        // kuralı olan formlar için widget listesi çekilir (kuralsız form → sorgu yok).
        var widgetLabelById = new Dictionary<int, string>();
        var widgetFormCodes = rules
            .Where(r => r.FieldKind == DataVisibilityFieldKind.Widget && r.WidgetId.HasValue)
            .Select(r => r.FormCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var fc in widgetFormCodes)
        {
            var form = await _widgetRepo.GetFormByCodeAsync(fc, ct);
            if (form is null) continue;
            foreach (var w in await _widgetRepo.GetWidgetsByFormAsync(form.Id, ct))
                if (!string.IsNullOrWhiteSpace(w.Label)) widgetLabelById[w.Id] = w.Label;
        }

        // ALAN çipi/alt başlık için alanın Türkçe görünen adı: widget → WidgetMas.Label,
        // kolon → GetColumnLabel (örn. Code→Kod, ContactGroupId→Cari Grup). Bulunamazsa ham FieldKey.
        string FieldLabel(DataVisibilityRule r)
        {
            if (r.FieldKind == DataVisibilityFieldKind.Widget)
                return r.WidgetId.HasValue && widgetLabelById.TryGetValue(r.WidgetId.Value, out var wl)
                    ? wl : r.FieldKey;
            return GetColumnLabel(r.FieldKey, isEn: false);
        }

        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget("form",       "Form",     "text"),
            SmartBoardFilterHelpers.MakeStdWidget("field",      "Alan",     "text"),
            SmartBoardFilterHelpers.MakeStdWidget("operator",   "Operatör", "text"),
            SmartBoardFilterHelpers.MakeStdWidget("valueCount", "Değer",    "numeric"),
        };

        return new
        {
            boardKey          = "data-visibility-rules",
            title             = "Veri Perdeleme Kuralları",
            subtitle          = $"{rules.Count} kural",
            icon              = "EyeOff",
            iconColor         = "indigo",
            refreshUrl        = "/Admin/DataVisibilityRules/BoardConfig",
            searchPlaceholder = "Kural ara…",
            emptyText         = "Henüz veri perdeleme kuralı tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Kural", icon = "Plus", variant = "primary", url = "/Admin/DataVisibilityRuleEdit" },
            },
            masterWidgets,
            entities = rules.Select(r =>
            {
                var kindLabel    = r.FieldKind == DataVisibilityFieldKind.Widget ? "Widget" : "Kolon";
                var noValue      = r.Operator is "isnull" or "isnotnull";
                var valueSummary = noValue
                    ? string.Empty
                    : string.Join(", ", r.Values
                        .Select(v => v.ValueText ?? v.ValueId?.ToString() ?? string.Empty)
                        .Where(s => s.Length > 0));
                var toggleLabel  = r.IsActive ? "Durdur" : "Etkinleştir";
                var toggleIcon   = r.IsActive ? "ToggleRight" : "ToggleLeft";
                var toggleColor  = r.IsActive ? "orange" : "emerald";
                var descr        = $"{kindLabel} · {OperatorLabel(r.Operator)}"
                                 + (valueSummary.Length > 0 ? $" · {valueSummary}" : string.Empty);
                return (object)new
                {
                    id          = r.Id,
                    title       = r.Name,
                    subtitle    = $"{FormName(r.FormCode)} · {FieldLabel(r)}",
                    description = descr,
                    statusBadge = r.IsActive
                        ? new { label = "Aktif", color = "emerald" }
                        : (object)new { label = "Pasif", color = "gray" },
                    widgets = new object[]
                    {
                        new { id = "form",       label = "Form",     value = FormName(r.FormCode),               icon = "LayoutGrid" },
                        new { id = "field",      label = "Alan",     value = $"{FieldLabel(r)} ({kindLabel})",   icon = "Tag" },
                        new { id = "operator",   label = "Operatör", value = OperatorLabel(r.Operator),          icon = "Filter" },
                        new { id = "valueCount", label = "Değer",    value = noValue ? "0" : r.Values.Count.ToString(), icon = "Hash" },
                    },
                    primaryAction   = (object?)new { label = "Düzenle", icon = "Edit2", url = $"/Admin/DataVisibilityRuleEdit?id={r.Id}", hideButton = true },
                    secondaryAction = (object?)null,
                    extraActions = new object?[]
                    {
                        new { label = "Düzenle", icon = "Edit2", color = "amber", url = $"/Admin/DataVisibilityRuleEdit?id={r.Id}" },
                        new { label = toggleLabel, icon = toggleIcon, color = toggleColor, type = "api-post", url = $"/Admin/ToggleDataVisibilityRule?id={r.Id}" },
                        new { label = "Sil", icon = "Trash2", color = "red", type = "api-post", url = $"/Admin/DeleteDataVisibilityRule?id={r.Id}", confirm = $"\"{r.Name}\" kuralını silmek istediğinizden emin misiniz?" },
                    },
                };
            }).ToArray(),
        };
    }
}

/// <summary>
/// 2026-06-13 — Veri görünürlük kuralı upsert isteği. <see cref="Grants"/> geriye uyumlu olarak
/// korunur (eski izinli model) ama operatör bazlı modelde kullanılmaz — daima boş gelir.
/// </summary>
public sealed class DataVisibilityRuleSaveRequest
{
    public int          Id        { get; set; }
    public string       Name      { get; set; } = string.Empty;
    public string       FormCode  { get; set; } = string.Empty;
    public int          FieldKind { get; set; }
    public string       FieldKey  { get; set; } = string.Empty;
    public string       Operator  { get; set; } = "eq";
    public int?         WidgetId  { get; set; }
    public bool         IsActive  { get; set; } = true;
    public List<string> Values    { get; set; } = new();

    /// <summary>
    /// Kural kapsadığı gizlenen verileri görmeye devam edecek kullanıcı ID'leri.
    /// null = sekme açılmamış, mevcut grant'ları koru. Boş liste = tüm user grant'larını temizle.
    /// </summary>
    public List<int>?  GrantUserIds { get; set; }

    /// <summary>
    /// Kural kapsadığı gizlenen verileri görmeye devam edecek departman ID'leri.
    /// null = sekme açılmamış, mevcut grant'ları koru. Boş liste = tüm dept grant'larını temizle.
    /// </summary>
    public List<int>?  GrantDeptIds { get; set; }
}

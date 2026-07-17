using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services.Security;   // CanViewFieldAsync extension
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mobil "ek saha" (WidgetMas/EAV dinamik alan) — Android companion app icin SCHEMA
/// okuma yuzeyi. Web'in dinamik alan sisteminin (WidgetsController, /api/widgets)
/// mobile acilan karsiligi — servis katmani (IWidgetService) AYNEN reuse edilir,
/// yeniden yazilmadi.
///
/// Deger YAZMA bu controller'da DEGIL: mevcut MobileWarehouseApiController'daki belge
/// POST'larina (stock-in/stock-out/transfer/inventory-count/delivery) eklenen opsiyonel
/// `extraFields` body alaniyla, belge basariyla kaydedilip Id alindiktan SONRA ayni
/// IWidgetService.SaveRecordAsync uzerinden yazilir (bkz. o dosyadaki SaveExtraFieldsAsync).
///
/// V1 KAPSAM (lider karari, 2026-07-17): yalniz ÜST-BİLGİ (header) formlari — Depo
/// Giris/Cikis/Transfer/Sayim + Alis/Satis Irsaliyesi. Kalem-bazli (grid) widget'lar ve
/// attachment/dosya tipi KAPSAM DISI — asagida MapDataType/AllowedFormCodes ile filtrelenir.
///
/// Desteklenen 6 form kodu — web _DynamicWidgetHost.cshtml'in kullandigi FormCode'larla
/// BIREBIR AYNI (StockDocEdit.cshtml Model.DocType, InventoryEdit.cshtml FormCodes.InventoryCount,
/// Sales/DocumentEdit.cshtml Model.HeaderFormCode = DocumentTypeFormMap.Resolve(typeCode).Header):
///   Depo Giriş  → STOCK_IN            Depo Çıkış → STOCK_OUT
///   Transfer    → TRANSFER            Sayım      → INVENTORY_COUNT
///   Satış İrsaliyesi → SALES_DELIVERY_EDIT   (DİKKAT: izin kontrolünde kullanılan "SALES_DELIVERY"
///   Alış İrsaliyesi  → PURCHASE_DELIVERY_EDIT (Parent/liste form kodu) İLE KARIŞTIRILMAMALI —
///                                              widget FormCode her zaman "..._EDIT" suffix'li olan.)
///
/// Yetki — BİLİNÇLİ SAPMA (2026-07-17, rapora flag'lendi): web'in WidgetsController'i TÜM
/// /api/widgets yüzeyini class-level [PermissionScope(FormCodes.ViewSettings)] ("Alan Rehberi"
/// admin ekranı izni) ile korur — bu attribute GetSchemaByCode/GetRecordByCode/SaveRecordByCode
/// dahil HER action'a uygulanır (action-level override yok, WidgetsController.cs'de doğrulandı).
/// Bu davranışı birebir kopyalamak, ViewSettings grant'ı OLMAYAN sıradan bir mobil depo
/// operatörünün kendi belgesindeki ek sahaları hiç GÖREMEMESİ anlamına gelir — özelliği mobilde
/// fiilen devre dışı bırakırdı (web tarafında da muhtemelen aynı sorun var, ama mobilin birincil
/// kullanıcı kitlesi tam olarak bu rol). Bunun yerine bu endpoint MobileWarehouseApiController'ın
/// TÜM diğer okuma endpoint'leriyle AYNI güvenlik modelini kullanır: hedef formun KENDİ form
/// kodunda VIEW|VIEW_OWN (RequirePermissionAsync ile aynı desen). Alan-bazlı (IsPermissionControlled)
/// izin filtresi WidgetsController.FilterByFieldPermissionsAsync ile PARİTELİ uygulanır (aşağıda).
/// </summary>
[ApiController]
[Route("api/mobile/widgets")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
public sealed class MobileWidgetApiController : ControllerBase
{
    private readonly IWidgetService _widgetService;
    private readonly IPermissionService _permService;

    public MobileWidgetApiController(IWidgetService widgetService, IPermissionService permService)
    {
        _widgetService = widgetService;
        _permService = permService;
    }

    /// <summary>
    /// V1'de desteklenen 6 üst-bilgi form kodu — bu allow-list aynı zamanda endpoint'in
    /// V1 kapsamı dışına (kalem formları, admin/ayar formları vb.) taşmasını da engeller.
    /// </summary>
    private static readonly HashSet<string> AllowedFormCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        FormCodes.StockIn, FormCodes.StockOut, FormCodes.Transfer, FormCodes.InventoryCount,
        FormCodes.SalesDeliveryEdit, FormCodes.PurchaseDeliveryEdit,
    };

    /// <summary>GET için aday aksiyonlar — PermissionEnforcementFilter'in GET seti ile aynı.</summary>
    private static readonly string[] ViewActions = { "VIEW", "VIEW_OWN" };

    /// <summary>
    /// WidgetMas.DataType (web EAV kanonik değerleri — bkz. ClientApp DataTypeDropdown.jsx
    /// DATA_TYPES listesi: text/textarea/numeric/date/boolean/dropdown/multi-select/link/
    /// attachment/lookup/guide-list/grid, + WidgetService'in ayrıca tanıdığı 'group') → mobil
    /// V1 tip token'i. Haritada OLMAYAN her değer mobil tarafından render EDİLEMEZ → şemadan
    /// çıkarılır (MapDataType null dönünce filtrelenir). Kapsam dışı bırakılanlar:
    ///   multi-select, link, attachment (dosya — açıkça kapsam dışı), lookup, guide-list
    ///   (rehber/liste tipleri — web tarafı ayrı bir picker UI gerektirir),
    ///   grid (kalem/alt-tablo — kapsam dışı, "kalem alanları hariç" kuralı burada uygulanır),
    ///   group (görsel başlık, değer taşımaz).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DataTypeMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"]     = "text",
            ["textarea"] = "textarea",
            ["numeric"]  = "number",
            ["date"]     = "date",
            ["boolean"]  = "bool",
            ["dropdown"] = "select",
        };

    private static string? MapDataType(string? dataType) =>
        dataType != null && DataTypeMap.TryGetValue(dataType.Trim(), out var t) ? t : null;

    /// <summary>
    /// MobileWarehouseApiController.GetCurrentUser ile birebir aynı claim çözümü — küçük,
    /// bilinçli kod tekrarı (o dosyadaki aynı isimli metodun yorumundaki gerekçeyle aynı:
    /// iki ayrı controller, DRY için ortak taban sınıfa taşımak kapsam dışı risk).
    /// </summary>
    private (int UserId, UserRole Role, int? DepartmentId) GetCurrentUser()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = UserRole.Operator;
        int? departmentId = int.TryParse(User.FindFirstValue("department_id"), out var d) && d > 0 ? d : null;
        return (userId, role, departmentId);
    }

    /// <summary>Tek form kodu için VIEW|VIEW_OWN kontrolü — izinsizse JSON 403 (MobileWarehouseApiController.RequirePermissionAsync ile aynı gövde şekli).</summary>
    private async Task<IActionResult?> RequirePermissionAsync(
        string formCode, IReadOnlyList<string> actionCodes, CancellationToken ct)
    {
        var (userId, role, departmentId) = GetCurrentUser();
        if (await _permService.CheckAnyAsync(userId, role, departmentId, formCode, actionCodes, ct))
            return null;

        return new JsonResult(new
        {
            ok      = false,
            message = "Bu işlemi yapmak için yetkiniz yok.",
            error   = $"Yetki yok: {formCode}:{string.Join('|', actionCodes)}",
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }

    /// <summary>
    /// GET /api/mobile/widgets/schema?formCode=STOCK_IN
    ///
    /// Yanıt: [{ key, label, type, required, options, order }] — yalnız aktif (IsActive=1),
    /// sistem-alanı-olmayan (IsSystemField=false — bunlar entity'nin kendi kolonuna yazılır,
    /// WidgetTra'ya değil; mobil extraFields yolu bu alanları YAZAMAZ) ve V1-desteklenen tipte
    /// widget'lar, SortOrder'a göre sıralı. IsPermissionControlled=true alanlar
    /// CanViewFieldAsync ile filtrelenir (WidgetsController.FilterByFieldPermissionsAsync paritesi;
    /// SystemAdmin her zaman görür).
    ///
    /// 400 — formCode boş/desteklenmeyen (AllowedFormCodes dışı).
    /// 403 — hedef formda VIEW|VIEW_OWN yok.
    /// 404 — form dbo.Forms'ta kayıtlı değil (pratikte olmaz; AllowedFormCodes zaten seed'e göre sabit).
    /// 200 [] — form var ama hiç uygun (aktif + V1 tipi + görünür) widget tanımlı değil — normal/beklenen
    ///          durum (çoğu şirket henüz ek saha tanımlamamıştır); mobil bu durumda "Ek Alanlar" sekmesini gizler.
    /// </summary>
    [HttpGet("schema")]
    public async Task<IActionResult> Schema([FromQuery] string? formCode, CancellationToken ct)
    {
        var code = (formCode ?? string.Empty).Trim();
        if (code.Length == 0 || !AllowedFormCodes.Contains(code))
            return BadRequest(new { error = "Desteklenmeyen veya eksik form kodu (formCode)." });

        if (await RequirePermissionAsync(code, ViewActions, ct) is { } denied)
            return denied;

        var schema = await _widgetService.GetFormSchemaByCodeAsync(code, ct);
        if (schema == null)
            return NotFound(new { error = $"Form bulunamadı: {code}" });

        var (userId, role, deptId) = GetCurrentUser();
        var isSystemAdmin = role == UserRole.SystemAdmin;

        var fields = new List<object>();
        foreach (var w in schema.Widgets.OrderBy(w => w.SortOrder))
        {
            if (!w.IsActive || w.IsSystemField) continue;

            var type = MapDataType(w.DataType);
            if (type == null) continue; // V1-desteklenmeyen tip — bkz. DataTypeMap yorumu

            if (w.IsPermissionControlled && !isSystemAdmin)
            {
                var canView = await _permService.CanViewFieldAsync(userId, role, deptId, code, w.WidgetCode, ct);
                if (!canView) continue;
            }

            fields.Add(new
            {
                key      = w.WidgetCode,
                label    = w.Label,
                type,
                required = w.IsRequired,
                options  = type == "select"
                    ? (w.Options ?? Array.Empty<string>()).Select(o => new { value = o, label = o }).ToArray()
                    : null,
                order    = w.SortOrder,
            });
        }

        return Ok(fields);
    }
}

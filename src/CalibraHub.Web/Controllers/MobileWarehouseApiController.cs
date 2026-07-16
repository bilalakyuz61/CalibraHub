using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Mobil Depo (Android) modulu — Increment 1: lokasyon + stok bakiye lookup'lari.
///
/// Header deseni MobileApiController ile ayni (cookie auth, CSRF muaf, ayri CORS
/// policy — bkz. o dosyadaki XML yorum). Yeni is mantigi yazilmadi: lokasyon/malzeme
/// coz.umu ILogisticsConfigurationService uzerinden, stok bakiyesi ise WarehouseController
/// (GetLocationStockSnapshotJson) / PurchaseController (StockBalances) ile ayni hareket
/// cebiri + ayni SUM(BaseQuantity) ana-birim normalizasyonu (CLAUDE.md) kullanilarak
/// hesaplanir — masaustu ile birebir ayni bakiye sayisi.
///
/// Endpoint'ler:
///   GET /api/mobile/warehouse/locations   — aktif (yaprak) lokasyon listesi
///   GET /api/mobile/warehouse/stock       — malzeme kodu → lokasyon bazli stok bakiyesi
///
/// Yetki (2026-07-16): [Authorize] tek basina yeterli DEGIL — her endpoint merkezi
/// IPermissionService.CheckAnyAsync'ten gecer (bkz. RequirePermissionAsync). Okuma
/// endpoint'leri dort depo ekran kodundan (STOCK_IN/STOCK_OUT/TRANSFER/INVENTORY_COUNT)
/// herhangi birinde VIEW|VIEW_OWN ister; Increment 2 yazma endpoint'leri ayni helper'i
/// belge turunun kendi FormCode'u + CREATE/EDIT_*/DELETE_* ile cagirmali.
/// </summary>
[ApiController]
[Route("api/mobile/warehouse")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
public sealed class MobileWarehouseApiController : ControllerBase
{
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly ICompanyParameterService _companyParams;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IPermissionService _permService;
    private readonly string _schema;

    public MobileWarehouseApiController(
        ILogisticsConfigurationService logisticsService,
        ICompanyParameterService companyParams,
        IDocumentTypeRepository documentTypeRepo,
        SqlServerConnectionFactory connectionFactory,
        IPermissionService permService,
        CalibraDatabaseOptions dbOptions)
    {
        _logisticsService  = logisticsService;
        _companyParams     = companyParams;
        _documentTypeRepo  = documentTypeRepo;
        _connectionFactory = connectionFactory;
        _permService       = permService;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Yetki — merkezi IPermissionService (web ile ayni cozum)
    // ──────────────────────────────────────────────────────────────────────
    // [PermissionScope] attribute'u yerine elle kontrol, iki sebeple:
    //  1) Mobil istemci tarayici degil — red HER ZAMAN JSON 403 olmali (redirect /
    //     cookie AccessDenied challenge yok; Kotlin taraf {error} govdesini gosterir).
    //  2) Increment 2'nin belge-turu bazli dinamik FormCode cozumu (web emsali:
    //     WarehouseController.CheckStockDocPermissionAsync) statik attribute ile
    //     ifade edilemez.
    // Claim cozumu AuditLogController.GetCurrentUser / PermissionEnforcementFilter
    // ile birebir ayni — mobil ve web ayni kullanici icin ayni sonucu uretir.

    /// <summary>
    /// Increment 1 (stok sorgu) kapisi: dort depo ekranindan (Ambar Giris/Cikis/
    /// Transfer/Sayim) HERHANGI birine web'de erisebilen kullanici mobilden lokasyon +
    /// stok bakiyesi sorgulayabilir. Web'de bu bakiye verisi tek bir ekrana ait degil
    /// (sayim ekrani INVENTORY_COUNT, karsilama merkezi PURCHASE_FULFILLMENT, malzeme
    /// karti "Stok Hareketleri" sekmesi MATERIAL_CARD_EDIT) — mobil depo modulunun
    /// dogal kapisi depo ekran yetkileridir; MATERIAL_CARD_EDIT sarti depo operatorune
    /// malzeme karti duzenlemeyi de acmayi zorlardi (over-grant, reddedildi).
    /// </summary>
    private static readonly string[] StockQueryFormCodes =
        { FormCodes.StockIn, FormCodes.StockOut, FormCodes.Transfer, FormCodes.InventoryCount };

    /// <summary>GET icin aday aksiyonlar — PermissionEnforcementFilter'in GET seti ile ayni.</summary>
    private static readonly string[] ViewActions = { "VIEW", "VIEW_OWN" };

    /// <summary>
    /// AuditLogController.GetCurrentUser / PermissionEnforcementFilter ile birebir ayni
    /// claim cozumu. Rol claim'i enum adi veya Turkce label olabilir; TryParseRole ikisini
    /// de cozer, bilinmeyense Operator'a duser (ayni fail-safe fallback).
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

    /// <summary>
    /// Verilen form kodlarindan HERHANGI birinde aksiyonlardan biri izinliyse null doner;
    /// degilse JSON 403 (govde PermissionEnforcementFilter.MakeForbidResult ile ayni
    /// {ok, message, error} sekli). Kullanim — her endpoint'in ilk satiri:
    /// <code>if (await RequirePermissionAsync(StockQueryFormCodes, ViewActions, ct) is { } denied) return denied;</code>
    /// Increment 2 yazma endpoint'leri tek elemanli dizi ile cagirir, orn.
    /// <code>RequirePermissionAsync(new[] { FormCodes.StockIn }, new[] { "CREATE", "EDIT_OWN", "EDIT_ALL" }, ct)</code>.
    /// SystemAdmin ve DepartmentManager bypass'lari PermissionService icindedir.
    /// </summary>
    private async Task<IActionResult?> RequirePermissionAsync(
        IReadOnlyList<string> formCodes, IReadOnlyList<string> actionCodes, CancellationToken ct)
    {
        var (userId, role, departmentId) = GetCurrentUser();
        foreach (var formCode in formCodes)
        {
            if (await _permService.CheckAnyAsync(userId, role, departmentId, formCode, actionCodes, ct))
                return null;
        }

        return new JsonResult(new
        {
            ok      = false,
            message = "Bu işlemi yapmak için yetkiniz yok.",
            error   = $"Yetki yok: {string.Join('|', formCodes)}:{string.Join('|', actionCodes)}",
        }) { StatusCode = StatusCodes.Status403Forbidden };
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET locations
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aktif ambar lokasyonlari — yalnizca yaprak (leaf) lokasyonlar doner (alt lokasyonu
    /// olan parent'lar secilemez). Desktop emsali: WarehouseController.GetLocationsJson.
    /// </summary>
    [HttpGet("locations")]
    public async Task<IActionResult> Locations(CancellationToken ct)
    {
        if (await RequirePermissionAsync(StockQueryFormCodes, ViewActions, ct) is { } denied)
            return denied;

        var locs   = await _logisticsService.GetLocationsAsync(ct);
        var active = locs.Where(l => l.IsActive).ToList();

        // Alt lokasyonu olan (parent) lokasyonlar secilemez — yalnizca yaprak lokasyonlar doner
        // (WarehouseController.GetLocationsJson ile birebir ayni kural).
        var parentIds = active
            .Where(l => l.ParentId.HasValue)
            .Select(l => l.ParentId!.Value)
            .ToHashSet();

        var result = active
            .Where(l => !parentIds.Contains(l.Id))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationName)
            .Select(l => new
            {
                id   = l.Id,
                code = l.LocationCode,
                name = l.LocationName ?? l.LocationCode,
            });

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET stock?code=<itemCode>
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Malzeme kodundan malzeme cozer + lokasyon bazli stok bakiyesini doner. Kanonik
    /// miktar DocumentLine.BaseQuantity (ana birim) — SUM(BaseQuantity) kullanilir
    /// (CLAUDE.md "Baz-birim normalizasyonu"). Yalnizca bakiyesi &gt; 0 olan lokasyonlar
    /// listelenir (WarehouseController / PurchaseController.StockBalances emsali).
    /// </summary>
    [HttpGet("stock")]
    public async Task<IActionResult> Stock([FromQuery] string? code, CancellationToken ct)
    {
        // Yetki kontrolu validasyondan ONCE — yetkisiz istemciye parametre ipucu sizdirilmaz.
        if (await RequirePermissionAsync(StockQueryFormCodes, ViewActions, ct) is { } denied)
            return denied;

        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { error = "Malzeme kodu zorunlu." });

        var trimmedCode = code.Trim();
        var items = await _logisticsService.GetItemsForLookupAsync(ct);
        var item = items.FirstOrDefault(x =>
            string.Equals(x.Code, trimmedCode, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return NotFound(new { error = $"Malzeme bulunamadi: {trimmedCode}" });

        string? unitCode = null;
        if (item.UnitId.HasValue)
        {
            var units = await _logisticsService.GetUnitsAsync(ct);
            unitCode = units.FirstOrDefault(u => u.Id == item.UnitId.Value)?.Code;
        }

        var balances = await GetLocationBalancesAsync(item.Id, ct);

        return Ok(new
        {
            itemId   = item.Id,
            itemCode = item.Code,
            itemName = item.Name,
            unit     = unitCode,
            balances,
        });
    }

    /// <summary>
    /// Bir malzemenin TUM lokasyonlardaki bakiyesi (ana birimde, BaseQuantity). Hareket
    /// cebiri PurchaseController.StockBalances ile birebir ayni (Receipt/Issue/Transfer/
    /// Adjust, MovementType 1-4); tek fark tek ItemId'ye daraltilmis olmasi. STOCK_EFFECT_
    /// {code}=false olan belge turleri bakiye disi birakilir (StockEffectHelper — masaustuyle
    /// ayni bakiye sayisini garanti eder).
    /// </summary>
    private async Task<List<object>> GetLocationBalancesAsync(int itemId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();

        var disabledTypeIds = await StockEffectHelper.GetDisabledDocTypeIdsAsync(
            _companyParams, _documentTypeRepo, ct);
        var seFilter = disabledTypeIds.Count == 0
            ? ""
            : $" AND (d.DocumentTypeId IS NULL OR d.DocumentTypeId NOT IN ({string.Join(",", disabledTypeIds.Select((_, i) => $"@sef{i}"))}))";

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            WITH Combined AS (
                -- Giris (Receipt): hedef lokasyona +miktar (ana birim)
                SELECT dl.LocationId AS LocId, dl.BaseQuantity AS Bal
                FROM [{_schema}].[DocumentLine] dl
                JOIN [{_schema}].[Document] d ON d.Id = dl.DocumentId
                WHERE dl.ItemId = @ItemId AND dl.MovementType = 2
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Cikis (Issue): kaynak lokasyondan -miktar (ana birim)
                SELECT dl.FromLocationId, -dl.BaseQuantity
                FROM [{_schema}].[DocumentLine] dl
                JOIN [{_schema}].[Document] d ON d.Id = dl.DocumentId
                WHERE dl.ItemId = @ItemId AND dl.MovementType = 1
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Transfer: hedef lokasyona +miktar (ana birim)
                SELECT dl.LocationId, dl.BaseQuantity
                FROM [{_schema}].[DocumentLine] dl
                JOIN [{_schema}].[Document] d ON d.Id = dl.DocumentId
                WHERE dl.ItemId = @ItemId AND dl.MovementType = 3 AND dl.LocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Transfer: kaynak lokasyondan -miktar (ana birim)
                SELECT dl.FromLocationId, -dl.BaseQuantity
                FROM [{_schema}].[DocumentLine] dl
                JOIN [{_schema}].[Document] d ON d.Id = dl.DocumentId
                WHERE dl.ItemId = @ItemId AND dl.MovementType = 3 AND dl.FromLocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Sayim farki (Adjust): LocationId doluysa +miktar (fazla cikti, ana birim)
                SELECT dl.LocationId, dl.BaseQuantity
                FROM [{_schema}].[DocumentLine] dl
                JOIN [{_schema}].[Document] d ON d.Id = dl.DocumentId
                WHERE dl.ItemId = @ItemId AND dl.MovementType = 4 AND dl.LocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Sayim farki (Adjust): FromLocationId doluysa -miktar (eksik cikti, ana birim)
                SELECT dl.FromLocationId, -dl.BaseQuantity
                FROM [{_schema}].[DocumentLine] dl
                JOIN [{_schema}].[Document] d ON d.Id = dl.DocumentId
                WHERE dl.ItemId = @ItemId AND dl.MovementType = 4 AND dl.FromLocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}
            )
            SELECT c.LocId, loc.LocationName, loc.LocationCode, SUM(c.Bal) AS Balance
            FROM Combined c
            LEFT JOIN [{_schema}].[Location] loc ON loc.Id = c.LocId
            WHERE c.LocId IS NOT NULL
            GROUP BY c.LocId, loc.LocationName, loc.LocationCode
            HAVING SUM(c.Bal) > 0
            ORDER BY loc.LocationName;
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        for (var i = 0; i < disabledTypeIds.Count; i++)
            cmd.Parameters.AddWithValue($"@sef{i}", disabledTypeIds[i]);

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var locationId   = r.GetInt32(0);
            var locationName = r.IsDBNull(1) ? null : r.GetString(1);
            var locationCode = r.IsDBNull(2) ? null : r.GetString(2);
            result.Add(new
            {
                locationId,
                locationName = locationName ?? locationCode ?? $"#{locationId}",
                quantity     = r.GetDecimal(3),
            });
        }
        return result;
    }
}

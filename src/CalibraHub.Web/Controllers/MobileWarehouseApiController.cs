using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Enums;
using CalibraHub.Domain.Exceptions;
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
///   GET  /api/mobile/warehouse/locations     — aktif (yaprak) lokasyon listesi
///   GET  /api/mobile/warehouse/stock         — malzeme kodu → lokasyon bazli stok bakiyesi
///   GET  /api/mobile/warehouse/items/search  — malzeme arama (kod/ad LIKE, aranabilir rehber)
///   POST /api/mobile/warehouse/stock-in      — depo giris belgesi (Increment 2)
///   POST /api/mobile/warehouse/stock-out     — depo cikis belgesi (Increment 2)
///
/// Yetki (2026-07-16): [Authorize] tek basina yeterli DEGIL — her endpoint merkezi
/// IPermissionService.CheckAnyAsync'ten gecer (bkz. RequirePermissionAsync). Okuma
/// endpoint'leri dort depo ekran kodundan (STOCK_IN/STOCK_OUT/TRANSFER/INVENTORY_COUNT)
/// herhangi birinde VIEW|VIEW_OWN ister; yazma endpoint'leri belge turunun kendi
/// FormCode'u (StockIn/StockOut) + CREATE|EDIT_OWN|EDIT_ALL ister — web'de
/// WarehouseController.SaveDocJson'in kullandigi aksiyon setiyle birebir ayni.
///
/// Yazma is mantigi (Increment 2): yeni stok-hareket mantigi YAZILMADI — web'in
/// SaveDocJson'i hangi akisi kullaniyorsa o cagrilir (IStockDocRepository.SaveAsync:
/// belge + kalem yazimi, BaseQuantity normalizasyonu, NegativeBalanceGuard, lot/seri
/// dogrulamalari tek transaction'da). Mobil V1 kapsam karari: lot/seri (ve varyant)
/// takipli malzeme kibarca reddedilir — bu belgeler web'den girilir.
/// </summary>
[ApiController]
[Route("api/mobile/warehouse")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
[Authorize]
public sealed class MobileWarehouseApiController : ControllerBase
{
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly ILogisticsConfigurationRepository _logisticsRepo;
    private readonly ICompanyParameterService _companyParams;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly IStockDocRepository _stockDocRepo;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IPermissionService _permService;
    private readonly IAuditTrailService _audit;
    private readonly string _schema;

    public MobileWarehouseApiController(
        ILogisticsConfigurationService logisticsService,
        ILogisticsConfigurationRepository logisticsRepo,
        ICompanyParameterService companyParams,
        IDocumentTypeRepository documentTypeRepo,
        IStockDocRepository stockDocRepo,
        SqlServerConnectionFactory connectionFactory,
        IPermissionService permService,
        IAuditTrailService audit,
        CalibraDatabaseOptions dbOptions)
    {
        _logisticsService  = logisticsService;
        _logisticsRepo     = logisticsRepo;
        _companyParams     = companyParams;
        _documentTypeRepo  = documentTypeRepo;
        _stockDocRepo      = stockDocRepo;
        _connectionFactory = connectionFactory;
        _permService       = permService;
        _audit             = audit;
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

    // ──────────────────────────────────────────────────────────────────────
    // GET items/search?q=<query>&take=<n>
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Malzeme arama (rehber) — kod/ad LIKE eslesmesi. Mobil Increment 1'de malzeme
    /// yalnizca /stock?code= uzerinden TAM kod esitligiyle cozulebiliyordu (kullanici
    /// kodu harfiyen bilmek zorundaydi) — bu endpoint aranabilir rehberi ekler. Sorgu
    /// ILogisticsConfigurationService.GetItemsPagedAsync uzerinden gecer; masaustu
    /// "rehber" ile ayni semantik (SqlLogisticsConfigurationRepository.GetItemsPagedAsync:
    /// i.[Code] LIKE @Search OR i.[Name] LIKE @Search, yalnizca aktif + CompanyId filtreli
    /// kayitlar — repo icinde uygulanir). Birim kodu /stock action'iyla ayni yontemle
    /// cozulur (GetUnitsAsync + UnitId lookup).
    /// </summary>
    [HttpGet("items/search")]
    public async Task<IActionResult> SearchItems([FromQuery] string? q, [FromQuery] int? take, CancellationToken ct)
    {
        // Yetki kontrolu validasyondan ONCE — Stock/Locations ile ayni kapi (StockQueryFormCodes).
        if (await RequirePermissionAsync(StockQueryFormCodes, ViewActions, ct) is { } denied)
            return denied;

        var query = (q ?? string.Empty).Trim();
        if (query.Length == 0)
            return Ok(Array.Empty<object>());

        var pageSize = take.GetValueOrDefault(20);
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 50) pageSize = 50;

        var (items, _) = await _logisticsService.GetItemsPagedAsync(query, 0, pageSize, ct);
        if (items.Count == 0)
            return Ok(Array.Empty<object>());

        // Birim kodu — /stock action'indaki cozumle birebir ayni (GetUnitsAsync + UnitId lookup).
        var units = await _logisticsService.GetUnitsAsync(ct);
        var unitCodeById = units.ToDictionary(u => u.Id, u => u.Code);

        var result = items.Select(i => new
        {
            id   = i.Id,
            code = i.Code,
            name = i.Name,
            unit = i.UnitId.HasValue && unitCodeById.TryGetValue(i.UnitId.Value, out var unitCode)
                ? unitCode
                : "",
        });

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST stock-in / stock-out (Increment 2 — depo giris/cikis yazma)
    // ──────────────────────────────────────────────────────────────────────
    // Sozlesme (mobil istemci birebir tuketir):
    //   body: { locationId:int, lines:[{ itemId:int, quantity:number }], note?:string }
    //   200 { ok:true,  docId:int, docNumber:string }
    //   200 { ok:false, error:string }   — is kurali reddi (yetersiz stok, lot/seri, validasyon)
    //   403 { ok:false, message, error } — yetki yok (RequirePermissionAsync govdesi)
    //
    // Is mantigi web ile AYNI KAPI: WarehouseController.SaveDocJson'in cagirdigi
    // IStockDocRepository.SaveAsync kullanilir — DocNo uretimi, BaseQuantity (ana birim)
    // normalizasyonu, NegativeBalanceGuard (cikis) ve lot/seri dogrulamalari repo
    // transaction'inda calisir. Mobil yalnizca sade DTO'yu SaveStockDocRequest'e map eder.

    /// <summary>Mobil kalem — itemId, /warehouse/stock yanitindaki itemId'den gelir (kod cozumu orada yapildi).</summary>
    public sealed record MobileStockDocLineRequest(int ItemId, decimal Quantity);

    /// <summary>Mobil giris/cikis istegi — tek lokasyon + kalemler (+ opsiyonel not).</summary>
    public sealed record MobileStockDocRequest(int LocationId, IReadOnlyList<MobileStockDocLineRequest>? Lines, string? Note);

    /// <summary>Yazma aksiyon seti — web SaveDocJson ile birebir ayni (CREATE veya kendi/tum kayit duzenleme).</summary>
    private static readonly string[] WriteActions = { "CREATE", "EDIT_OWN", "EDIT_ALL" };

    /// <summary>Depo giris belgesi (STOCK_IN) — kalemler hedef lokasyona (+) yazilir.</summary>
    [HttpPost("stock-in")]
    public Task<IActionResult> StockIn([FromBody] MobileStockDocRequest? body, CancellationToken ct)
        => SaveStockDocAsync("STOCK_IN", body, ct);

    /// <summary>Depo cikis belgesi (STOCK_OUT) — kalemler kaynak lokasyondan (-) dusulur; eksi bakiye guard'i calisir.</summary>
    [HttpPost("stock-out")]
    public Task<IActionResult> StockOut([FromBody] MobileStockDocRequest? body, CancellationToken ct)
        => SaveStockDocAsync("STOCK_OUT", body, ct);

    private async Task<IActionResult> SaveStockDocAsync(string docType, MobileStockDocRequest? body, CancellationToken ct)
    {
        // Yetki: belge turunun KENDI form kodu (paylasilan okuma setinden farkli) + yazma aksiyonlari.
        var formCode = docType == "STOCK_OUT" ? FormCodes.StockOut : FormCodes.StockIn;
        if (await RequirePermissionAsync(new[] { formCode }, WriteActions, ct) is { } denied)
            return denied;

        // Validasyon — repo qty<=0 / itemId'siz satiri SESSIZCE atlar; mobilde sessiz kalem
        // kaybi (hatta bos belge yazimi) kabul edilemez, o yuzden burada acikca reddedilir.
        if (body is null || body.LocationId <= 0)
            return Ok(new { ok = false, error = "Lokasyon seçimi zorunlu." });

        var lines = (body.Lines ?? Array.Empty<MobileStockDocLineRequest>()).Where(l => l is not null).ToList();
        if (lines.Count == 0)
            return Ok(new { ok = false, error = "En az bir kalem girilmelidir." });

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].ItemId <= 0)
                return Ok(new { ok = false, error = $"Kalem {i + 1}: geçersiz malzeme." });
            if (lines[i].Quantity <= 0)
                return Ok(new { ok = false, error = $"Kalem {i + 1}: miktar 0'dan büyük olmalı." });
        }

        // Malzeme dogrulama + V1 kapsam reddi. Takip tipi kaynagi: Items.TrackingType
        // ("None" | "Lot" | "Serial", bkz. Domain.Entities.Item) — web grid'inin
        // GetMaterialsJson'da okudugu alanin aynisi. GetItemsByIdsAsync yalnizca istenen
        // id'leri ceker (50K kartli kurumda full-table okumamak icin; rapor 2026-05-17 3.10).
        // NOT: GetItemsForLookupAsync BILINCLI kullanilmadi — o projeksiyon TrackingType'i
        // doldurmaz (her kayit "None" default'una duser), takip kontrolu sessizce delinirdi.
        var items = await _logisticsRepo.GetItemsByIdsAsync(lines.Select(l => l.ItemId), ct);
        var itemMap = items.ToDictionary(x => x.Id);
        foreach (var line in lines)
        {
            if (!itemMap.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                return Ok(new { ok = false, error = $"Malzeme bulunamadı veya pasif (Id: {line.ItemId})." });

            // Mobil V1: lot/seri takipli malzeme reddi (lider karari) — sade {itemId, quantity}
            // DTO'su lot secimi/seri listesi tasiyamaz; repo dogrulamasina carpip kriptik
            // hata almak yerine kibar ve yonlendirici mesajla reddedilir.
            if (string.Equals(item.TrackingType, "Lot", StringComparison.OrdinalIgnoreCase))
                return Ok(new { ok = false, error = $"'{item.Code}' lot takipli — işlemi web'den yapın." });
            if (string.Equals(item.TrackingType, "Serial", StringComparison.OrdinalIgnoreCase))
                return Ok(new { ok = false, error = $"'{item.Code}' seri takipli — işlemi web'den yapın." });

            // Varyantli (kombinasyon) malzemede CombinationId'siz satir, web'in kombinasyon
            // kirilimli izlemesini bozar — ayni V1 kapsam reddi uygulanir (backend karari,
            // rapora not edildi; V2'de kombinasyon secici eklenince kaldirilir).
            if (item.Combinations)
                return Ok(new { ok = false, error = $"'{item.Code}' varyant (kombinasyon) takipli — işlemi web'den yapın." });
        }

        // Web StockDocEdit'in gonderdigi sekle map: STOCK_IN → ToLocationId, STOCK_OUT →
        // FromLocationId (bakiye cebirinde giris LocationId'ye +, cikis FromLocationId'den -).
        // UnitId = kartin ana birimi: mobil miktari ana birimde girer (GET /stock bakiyeleri de
        // ana birimde) → BaseQuantity carpani 1, web edit ekrani/audit birimi dogru gosterir.
        var request = new SaveStockDocRequest(
            Id: null,
            DocType: docType,
            DocNo: null,                       // repo uretir (ResolveDocNoAsync)
            DocDate: DateTime.UtcNow,          // repo .Date alir — web de UTC now gonderiyor
            FromLocationId: docType == "STOCK_OUT" ? body.LocationId : null,
            ToLocationId: docType == "STOCK_IN" ? body.LocationId : null,
            RefNo: null,
            Notes: string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(),
            Lines: lines.Select(l => new SaveStockDocLineRequest(
                Id: null,
                ItemId: l.ItemId,
                MaterialCode: null,
                MaterialName: null,
                UnitId: itemMap[l.ItemId].UnitId,
                Qty: l.Quantity,
                CombinationId: null,
                Notes: null,
                FromLocationId: null,          // null → header lokasyonuna duser (repo davranisi)
                ToLocationId: null,
                UnitCost: null)).ToList(),
            ArgeProjectId: null);

        try
        {
            var (userId, _, _) = GetCurrentUser();
            var (id, docNo) = await _stockDocRepo.SaveAsync(request, userId > 0 ? userId : null, ct);
            await LogStockDocInsertAsync(docType, request, id, docNo, ct);
            return Ok(new { ok = true, docId = id, docNumber = docNo });
        }
        catch (NegativeBalanceException nbex)
        {
            // Eksi bakiye — ProductionController.ShopFloorIssueComponent ile ayni yakalama
            // deseni: guard mesaji kullaniciya aynen gosterilir.
            return Ok(new { ok = false, error = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            // Repo dogrulama mesajlari (lot zorunlulugu vb.) — ust kontroller kacirirsa
            // son savunma hatti; mesaj kullaniciya aynen gosterilir (web SaveDocJson paritesi).
            return Ok(new { ok = false, error = ioex.Message });
        }
        catch (Exception)
        {
            return Ok(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Islem logu (CLAUDE.md audit kurali) — web LogStockDocSaveAsync'in Insert dalinin
    /// mobil karsiligi: ayni entity kodlari (depo_giris/depo_cikis), ayni "ilk deger dokumu +
    /// kalem satirlari" bicimi; /AuditLog ekrani iki kanali ayni zaman cizelgesinde gosterir.
    /// Src="Mobile" damgasi kaydin mobilden geldigini ayirt eder. Audit hatasi kaydi asla bozmaz.
    /// </summary>
    private async Task LogStockDocInsertAsync(
        string docType, SaveStockDocRequest request, int id, string docNo, CancellationToken ct)
    {
        try
        {
            var entity = docType == "STOCK_OUT" ? "depo_cikis" : "depo_giris";
            IReadOnlyList<StockDocLineDto>? insertedLines = null;
            try { insertedLines = await _stockDocRepo.GetLinesAsync(id, ct); } catch { }

            _audit.LogInsert(entity, id, docNo,
                detail: $"{request.Lines?.Count ?? 0} kalem",
                actor: new AuditActor(Source: "Mobile"),
                snapshot: new
                {
                    DocumentDate = request.DocDate.Date,
                    // Cikista kaynak, giriste hedef — kullanicinin sectigi lokasyon.
                    LocationId = docType == "STOCK_OUT" ? request.FromLocationId : request.ToLocationId,
                    request.Notes,
                },
                extraChanges: insertedLines is { Count: > 0 }
                    ? insertedLines.Select(l => new AuditFieldChange(
                        $"Line[{l.Id}]",
                        $"Kalem — {l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId)}",
                        null,
                        $"{AuditDiff.Normalize(l.Qty)} {l.UnitCode ?? "birim"}")).ToList()
                    : null);
        }
        catch { /* audit yazimi belge kaydini asla bozmaz */ }
    }
}

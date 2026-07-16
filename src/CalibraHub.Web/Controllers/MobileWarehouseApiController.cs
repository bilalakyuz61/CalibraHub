using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Entities;
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
///   POST /api/mobile/warehouse/stock-in         — depo giris belgesi (Increment 2)
///   POST /api/mobile/warehouse/stock-out        — depo cikis belgesi (Increment 2)
///   POST /api/mobile/warehouse/transfer         — depo transferi, tek kaynak+tek hedef (Increment 2)
///   POST /api/mobile/warehouse/inventory-count  — sayim belgesi, HER ZAMAN taslak (Increment 2)
///
/// Yetki (2026-07-16): [Authorize] tek basina yeterli DEGIL — her endpoint merkezi
/// IPermissionService.CheckAnyAsync'ten gecer (bkz. RequirePermissionAsync). Okuma
/// endpoint'leri dort depo ekran kodundan (STOCK_IN/STOCK_OUT/TRANSFER/INVENTORY_COUNT)
/// herhangi birinde VIEW|VIEW_OWN ister; yazma endpoint'leri belge turunun kendi
/// FormCode'u (StockIn/StockOut/Transfer/InventoryCount) + CREATE|EDIT_OWN|EDIT_ALL
/// ister — web'de WarehouseController.SaveDocJson/SaveInventoryJson'in kullandigi
/// aksiyon setiyle birebir ayni.
///
/// Yazma is mantigi (Increment 2): yeni stok-hareket mantigi YAZILMADI — web'in
/// SaveDocJson/SaveInventoryJson'inin cagirdigi IStockDocRepository.SaveAsync aynen
/// cagrilir (belge + kalem yazimi, BaseQuantity normalizasyonu, NegativeBalanceGuard,
/// lot/seri dogrulamalari tek transaction'da). Mobil V1 kapsam karari: lot/seri (ve
/// varyant) takipli malzeme kibarca reddedilir — bu belgeler web'den girilir. Sayim
/// belgesi web'deki gibi HER ZAMAN taslak (Status=0) kaydedilir — stok bakiyesini
/// degistiren "Yansit" adimi mobilden tetiklenmez (bkz. InventoryCount action yorumu).
///
/// Yanit govdesi (2026-07-16, ONEMLI FARK): Transfer/InventoryCount yazma hatalari GERCEK
/// HTTP 400/404 + bare {error} doner (StockIn/StockOut'un "200 {ok:false,error}" deseninden
/// FARKLI — lider talimati, KESIN KONTRAT). StockIn/StockOut deseni degistirilmedi.
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
    private readonly IFinanceService _financeService;
    private readonly IPriceListService _priceListService;
    private readonly IInventoryCountRepository _inventoryCountRepo;
    private readonly IDocumentSourceRepository _docSourceRepo;
    private readonly IIntegrationOnSaveDispatcher _onSaveDispatcher;
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
        IFinanceService financeService,
        IPriceListService priceListService,
        IInventoryCountRepository inventoryCountRepo,
        IDocumentSourceRepository docSourceRepo,
        IIntegrationOnSaveDispatcher onSaveDispatcher,
        CalibraDatabaseOptions dbOptions)
    {
        _logisticsService   = logisticsService;
        _logisticsRepo      = logisticsRepo;
        _companyParams      = companyParams;
        _documentTypeRepo   = documentTypeRepo;
        _stockDocRepo       = stockDocRepo;
        _connectionFactory  = connectionFactory;
        _permService        = permService;
        _audit              = audit;
        _financeService     = financeService;
        _priceListService   = priceListService;
        _inventoryCountRepo = inventoryCountRepo;
        _docSourceRepo      = docSourceRepo;
        _onSaveDispatcher   = onSaveDispatcher;
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

    /// <summary>Irsaliye (teslimat/mal kabul) ekran kodlari — cari arama + teslimat yazma kapisi.</summary>
    private static readonly string[] DeliveryFormCodes =
        { FormCodes.SalesDelivery, FormCodes.PurchaseDelivery };

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

        // Takip tipi + AutoSerial (mobil seri/lot UI gating'i) — GetItemsForLookupAsync projeksiyonu
        // TrackingType'ı doldurmadığından Items'tan doğrudan çözülür (elle string map, raw enum değil).
        var tracking = await GetTrackingMapAsync(new[] { item.Id }, ct);
        var (trackingType, autoSerial) = tracking.TryGetValue(item.Id, out var tr) ? tr : ("None", false);

        return Ok(new
        {
            itemId   = item.Id,
            itemCode = item.Code,
            itemName = item.Name,
            unit     = unitCode,
            trackingType,
            autoSerial,
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
    /// Malzeme arama (rehber) — kod/ad/barkod LIKE eslesmesi + barkod tarama girisi. Mobil
    /// Increment 1'de malzeme yalnizca /stock?code= uzerinden TAM kod esitligiyle
    /// cozulebiliyordu (kullanici kodu harfiyen bilmek zorundaydi) — bu endpoint aranabilir
    /// rehberi ekler. Sorgu ILogisticsConfigurationService.GetItemsPagedAsync uzerinden gecer;
    /// masaustu "rehber" ile ayni semantik (SqlLogisticsConfigurationRepository.GetItemsPagedAsync:
    /// i.[Code] LIKE @Search OR i.[Name] LIKE @Search OR i.[Barcode] LIKE @Search, yalnizca aktif +
    /// CompanyId filtreli kayitlar — repo icinde uygulanir, PAYLASILAN sorgu; masaustu malzeme
    /// rehberi/fiyat listesi/AI tool'lari da ayni degisiklikle barkod-farkinda oldu). Birim kodu
    /// /stock action'iyla ayni yontemle cozulur (GetUnitsAsync + UnitId lookup).
    ///
    /// BARKOD (2026-07-16, guncelleme — Items.Barcode kolonu eklendi): Onceki surumde
    /// [{Schema}].[Items] tablosunda ayrik bir Barcode kolonu YOKTU, bu yuzden asagidaki
    /// `barcode` alani gecici olarak Code'un birebir aynisiydi (BarcodeValue = m.Code
    /// yakinsamasi, ZplGeneratorService/vw_ProductBarcode ile ayni). Artik Items.Barcode
    /// NVARCHAR(50) NULL kolonu var (kullanici dogrudan girer/tarar, Code'dan bagimsiz) —
    /// bu endpoint `item.Barcode ?? item.Code` FALLBACK'i uygular: barkod girilmemis eski/
    /// yeni kayitlarda mobil taraf yine Code ile KESIN eslesme yapabilir (geriye uyumlu),
    /// barkod girilmisse gercek barkod deger olarak doner. Siralamada da hem Code hem
    /// Barcode tam-esitligi (case-insensitive) kontrol edilir.
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

        // Takip tipi + AutoSerial (mobil seri/lot UI gating'i) — GetItemsPagedAsync projeksiyonu
        // TrackingType'ı doldurmadığından Items'tan tek batch ile çözülür (elle string map).
        var tracking = await GetTrackingMapAsync(items.Select(i => i.Id), ct);

        // Barkod/kod TAM esitligi (case-insensitive) sonuclarin basina alinir — taranan deger
        // Code'a VEYA Barcode'a birebir denk geliyorsa mobil ekranda ilk sirada (idealde tek
        // sonuc) cikar. Stabil siralama: esit-olmayanlar SQL'den gelen orijinal (Code alfabetik)
        // sirasini korur.
        var ordered = items
            .OrderByDescending(i =>
                string.Equals(i.Code, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.Barcode, query, StringComparison.OrdinalIgnoreCase));

        var result = ordered.Select(i => new
        {
            id      = i.Id,
            code    = i.Code,
            name    = i.Name,
            unit    = i.UnitId.HasValue && unitCodeById.TryGetValue(i.UnitId.Value, out var unitCode)
                ? unitCode
                : "",
            barcode = i.Barcode ?? i.Code,
            trackingType = tracking.TryGetValue(i.Id, out var tr) ? tr.Tracking : "None",
            autoSerial   = tracking.TryGetValue(i.Id, out var tr2) && tr2.AutoSerial,
        });

        return Ok(result);
    }

    /// <summary>
    /// Malzeme id → (TrackingType "None"|"Lot"|"Serial", AutoSerial) haritası — Items'tan tek batch.
    /// SearchItems/Stock yanıtlarındaki seri/lot UI gating alanlarını besler. GetItemsForLookupAsync /
    /// GetItemsPagedAsync / GetItemsByIdsAsync projeksiyonları TrackingType/AutoSerial doldurmaz
    /// (Item.TrackingType her kayıtta "None" default'una düşer) — bu yüzden burada DB'den doğrudan
    /// okunur ve kanonik string'e elle map edilir (JSON enum serialize değil). Boş liste → boş harita (DB hit yok).
    /// </summary>
    private async Task<Dictionary<int, (string Tracking, bool AutoSerial)>> GetTrackingMapAsync(
        IEnumerable<int> itemIds, CancellationToken ct)
    {
        var ids = (itemIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct().ToList();
        var map = new Dictionary<int, (string, bool)>(ids.Count);
        if (ids.Count == 0) return map;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var names = ids.Select((_, i) => "@id" + i).ToArray();
        cmd.CommandText =
            $"SELECT [Id], ISNULL([TrackingType],'None'), ISNULL([AutoSerial],0) FROM [{_schema}].[Items] WHERE [Id] IN ({string.Join(",", names)});";
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(names[i], ids[i]);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var raw = r.IsDBNull(1) ? "None" : r.GetString(1);
            // Kanonik sözleşme: tam olarak "None"|"Lot"|"Serial" (bilinmeyen/boş → "None").
            var canon = string.Equals(raw, "Lot", StringComparison.OrdinalIgnoreCase) ? "Lot"
                      : string.Equals(raw, "Serial", StringComparison.OrdinalIgnoreCase) ? "Serial"
                      : "None";
            var autoSerial = !r.IsDBNull(2) && Convert.ToBoolean(r.GetValue(2));
            map[r.GetInt32(0)] = (canon, autoSerial);
        }
        return map;
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

    // ──────────────────────────────────────────────────────────────────────
    // POST transfer (Increment 2 devami — depo transferi yazma)
    // ──────────────────────────────────────────────────────────────────────
    // Sozlesme (mobil istemci birebir tuketir):
    //   body: { fromLocationId:int, toLocationId:int, lines:[{ itemId:int, quantity:number }], note?:string }
    //   200 { ok:true,  documentNumber:string }
    //   400 { error:string }             — is kurali reddi (ayni lokasyon, bos kalem, gecersiz
    //                                       miktar, lot/seri/varyant, eksi bakiye) — DIKKAT:
    //                                       stock-in/out'un aksine "ok" alani YOK, gercek HTTP
    //                                       400 (lider talimati, KESIN KONTRAT).
    //   404 { error:string }             — malzeme bulunamadi/pasif
    //   403 { ok:false, message, error } — yetki yok (RequirePermissionAsync govdesi, degismedi)
    //
    // Is mantigi web ile AYNI KAPI: WarehouseController.SaveDocJson → IStockDocRepository.SaveAsync
    // (MovementType=3/Transfer). Web'de transfer lokasyonu KALEM seviyesinde tutulur
    // (StockDocEdit.cshtml yorumu: "Transfer: lokasyon kalem seviyesinde" — her satir kendi
    // from/to'sunu tasiyabilir, coklu kaynak/hedef ciftine izin verir); mobil V1 tek kaynak+tek
    // hedef ile sadelestirir — kalemler null From/ToLocationId ile gonderilir, repo bunlari
    // HEADER'a dusurur (SqlStockDocRepository.SaveDirectDocAsync satir ~425-426:
    // line.FromLocationId ?? request.FromLocationId, ayni desen ToLocationId icin de gecerli).
    // NegativeBalanceGuard, MovementType=3 icin kaynak lokasyonda ayni transaction icinde
    // otomatik calisir (repo tarafinda, SaveDirectDocAsync) — burada ekstra kod gerekmez.

    /// <summary>Mobil transfer istegi — tek kaynak + tek hedef lokasyon (web'in kalem-bazli
    /// coklu lokasyon esnekligi mobil V1'de sadelestirildi: tum kalemler ayni yonde tasinir).</summary>
    public sealed record MobileTransferRequest(
        int FromLocationId, int ToLocationId, IReadOnlyList<MobileStockDocLineRequest>? Lines, string? Note);

    /// <summary>Depo transferi (TRANSFER) — kalemler kaynaktan (-) dusup hedefe (+) yazilir; eksi bakiye guard'i kaynakta calisir.</summary>
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] MobileTransferRequest? body, CancellationToken ct)
    {
        if (await RequirePermissionAsync(new[] { FormCodes.Transfer }, WriteActions, ct) is { } denied)
            return denied;

        if (body is null || body.FromLocationId <= 0 || body.ToLocationId <= 0)
            return BadRequest(new { error = "Kaynak ve hedef lokasyon seçimi zorunlu." });
        if (body.FromLocationId == body.ToLocationId)
            return BadRequest(new { error = "Kaynak ve hedef lokasyon aynı olamaz." });

        var lines = (body.Lines ?? Array.Empty<MobileStockDocLineRequest>()).Where(l => l is not null).ToList();
        if (lines.Count == 0)
            return BadRequest(new { error = "En az bir kalem girilmelidir." });

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].ItemId <= 0)
                return BadRequest(new { error = $"Kalem {i + 1}: geçersiz malzeme." });
            if (lines[i].Quantity <= 0)
                return BadRequest(new { error = $"Kalem {i + 1}: miktar 0'dan büyük olmalı." });
        }

        var (itemMap, itemError) = await ValidateWriteItemsAsync(lines.Select(l => l.ItemId), ct);
        if (itemError is not null) return itemError;

        // Web StockDocEdit'in gonderdigi sekle map (mobil sadelestirmesi): header hem kaynak
        // hem hedefi tasir, kalemler null birakilir → repo header'a duser (yukaridaki yorum).
        var request = new SaveStockDocRequest(
            Id: null,
            DocType: "TRANSFER",
            DocNo: null,                       // repo uretir (ResolveDocNoAsync, prefix "TRF")
            DocDate: DateTime.UtcNow,
            FromLocationId: body.FromLocationId,
            ToLocationId: body.ToLocationId,
            RefNo: null,
            Notes: string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(),
            Lines: lines.Select(l => new SaveStockDocLineRequest(
                Id: null,
                ItemId: l.ItemId,
                MaterialCode: null,
                MaterialName: null,
                UnitId: itemMap![l.ItemId].UnitId,
                Qty: l.Quantity,
                CombinationId: null,
                Notes: null,
                FromLocationId: null,          // null → header kaynagina duser (repo davranisi)
                ToLocationId: null,             // null → header hedefine duser
                UnitCost: null)).ToList(),
            ArgeProjectId: null);

        try
        {
            var (userId, _, _) = GetCurrentUser();
            var (id, docNo) = await _stockDocRepo.SaveAsync(request, userId > 0 ? userId : null, ct);
            await LogStockDocInsertAsync("TRANSFER", request, id, docNo, ct);
            return Ok(new { ok = true, documentNumber = docNo });
        }
        catch (NegativeBalanceException nbex)
        {
            // Eksi bakiye — StockIn/StockOut ile ayni yakalama deseni; guard mesaji aynen gosterilir.
            return BadRequest(new { error = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            // Repo dogrulama mesajlari (lot zorunlulugu vb.) — ust kontroller kacirirsa son
            // savunma hatti; mesaj kullaniciya aynen gosterilir (web SaveDocJson paritesi).
            return BadRequest(new { error = ioex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST inventory-count (Increment 2 devami — sayim yazma, HER ZAMAN taslak)
    // ──────────────────────────────────────────────────────────────────────
    // Sozlesme (mobil istemci birebir tuketir):
    //   body: { locationId:int, lines:[{ itemId:int, countedQuantity:number }], note?:string }
    //   200 { ok:true,  id:int, documentNumber:string, applied:bool }
    //     (id 2026-07-16'da EKLENDI — mobil "Yansit" adimi /inventory-count/{id}/apply icin kullanir)
    //   400 { error:string }             — is kurali reddi (bos kalem, negatif miktar, lot/seri/varyant)
    //   404 { error:string }             — malzeme bulunamadi/pasif
    //   403 { ok:false, message, error } — yetki yok
    //
    // Web PARITESI: WarehouseController.SaveInventoryJson AYNEN cagirilir (IStockDocRepository.
    // SaveAsync, DocType=INVENTORY_COUNT) — bu akis yalnizca TASLAK (Status=0) sayim belgesi +
    // InventoryCountLine yazar, hicbir DocumentLine/stok hareketi YARATMAZ (bkz.
    // SqlStockDocRepository.SaveInventoryCountAsync). Web'de farkin stoga yazilmasi ("Yansit",
    // WarehouseController.ApplyInventoryJson → IInventoryCountRepository.ApplyAsync) AYRI ve
    // kullanici-tetiklemeli bir SONRAKI adimdir. Mobil V1 kapsam karari: otomatik Yansitma YOK
    // — stok bakiyesini degistiren bu adim kiosk'tan gozden gecirilmeden tetiklenmeyecek, karar
    // masaustunde kalir. "applied" bu yuzden HER ZAMAN false doner; alan yine de kontratta yer
    // aliyor ki ileride (V2) mobilden Yansitma acilirsa istemci tarafi zaten hazir olsun.
    //
    // Sayim lokasyonu repo sozlesmesinde FromLocationId'de tasinir (ToLocationId DEGIL — bkz.
    // SqlStockDocRepository.SaveInventoryCountAsync yorumu: "Sayim lokasyonu UI/import handler
    // tarafindan FromLocationId'de gonderilir"). countedQuantity=0 gecerlidir ("saydim, yok" —
    // repo yorumu: "Sifir sayim gecerli giristir"), yalnizca negatif reddedilir.

    /// <summary>Mobil sayim kalemi — itemId + sayilan miktar (0 gecerli: "hic yok" sayimi).</summary>
    public sealed record MobileInventoryCountLineRequest(int ItemId, decimal CountedQuantity);

    /// <summary>Mobil sayim istegi — tek lokasyon + kalemler (+ opsiyonel not).</summary>
    public sealed record MobileInventoryCountRequest(
        int LocationId, IReadOnlyList<MobileInventoryCountLineRequest>? Lines, string? Note);

    /// <summary>Sayim belgesi (INVENTORY_COUNT) — daima TASLAK kaydedilir, stok bakiyesini etkilemez (yukaridaki yorum).</summary>
    [HttpPost("inventory-count")]
    public async Task<IActionResult> InventoryCount([FromBody] MobileInventoryCountRequest? body, CancellationToken ct)
    {
        if (await RequirePermissionAsync(new[] { FormCodes.InventoryCount }, WriteActions, ct) is { } denied)
            return denied;

        if (body is null || body.LocationId <= 0)
            return BadRequest(new { error = "Lokasyon seçimi zorunlu." });

        var lines = (body.Lines ?? Array.Empty<MobileInventoryCountLineRequest>()).Where(l => l is not null).ToList();
        if (lines.Count == 0)
            return BadRequest(new { error = "En az bir kalem girilmelidir." });

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].ItemId <= 0)
                return BadRequest(new { error = $"Kalem {i + 1}: geçersiz malzeme." });
            if (lines[i].CountedQuantity < 0)
                return BadRequest(new { error = $"Kalem {i + 1}: sayılan miktar negatif olamaz." });
        }

        var (itemMap, itemError) = await ValidateWriteItemsAsync(lines.Select(l => l.ItemId), ct);
        if (itemError is not null) return itemError;

        var request = new SaveStockDocRequest(
            Id: null,
            DocType: "INVENTORY_COUNT",
            DocNo: null,                       // repo uretir (ResolveDocNoAsync, prefix "SAY")
            DocDate: DateTime.UtcNow,
            FromLocationId: body.LocationId,   // sayim lokasyonu HER ZAMAN FromLocationId'de (repo sozlesmesi)
            ToLocationId: null,
            RefNo: null,
            Notes: string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(),
            Lines: lines.Select(l => new SaveStockDocLineRequest(
                Id: null,
                ItemId: l.ItemId,
                MaterialCode: null,
                MaterialName: null,
                UnitId: itemMap![l.ItemId].UnitId,
                Qty: l.CountedQuantity,
                CombinationId: null,
                Notes: null,
                FromLocationId: null,          // null → header sayim lokasyonuna duser
                ToLocationId: null,
                UnitCost: null)).ToList(),
            ArgeProjectId: null);

        try
        {
            var (userId, _, _) = GetCurrentUser();
            var (id, docNo) = await _stockDocRepo.SaveAsync(request, userId > 0 ? userId : null, ct);
            await LogStockDocInsertAsync("INVENTORY_COUNT", request, id, docNo, ct);
            // Web SaveInventoryJson paritesi: kayit HER ZAMAN taslak kalir (yukaridaki yorum) → applied daima false.
            // id EKLENDI (additive) — mobil "Yansit" (/inventory-count/{id}/apply) belgeyi bununla hedefler.
            return Ok(new { ok = true, id, documentNumber = docNo, applied = false });
        }
        catch (InvalidOperationException ioex)
        {
            // Lot/seri zorunlulugu gibi repo dogrulamalari — ValidateWriteItemsAsync normalde bu
            // kalemleri zaten reddeder; son savunma hatti (web SaveInventoryJson paritesi).
            return BadRequest(new { error = ioex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Malzeme dogrulama + V1 kapsam reddi (varyant + kosullu lot/seri) — Transfer, Sayim ve
    /// Delivery yazma endpoint'lerinin ORTAK kapisi. SaveStockDocAsync (Giris/Cikis) icindeki ayni
    /// kontrolun davranissal ikizi — o metod calisan/canli oldugu icin DOKUNULMADAN birakildi, kucuk
    /// bir kod tekrari (DRY ihlali) kapsam disi risk almamak icin bilerek kabul edildi.
    /// <paramref name="allowLotSerial"/> = true (yalnizca Delivery) → lot/seri takipli malzeme
    /// REDDEDILMEZ (repo delivery akisi lot/seri'yi web ambar paritesiyle isler); varyant
    /// (kombinasyon) reddi HER durumda kalir (mobil V1 kombinasyon secici yok).
    /// NOT: GetItemsByIdsAsync projeksiyonu TrackingType'i doldurmadigindan (her kayit "None") bu
    /// metodun lot/seri REDDI zaten dormant — asil takip cozumu repoda (Items'tan) yapilir; flag
    /// niyeti acik tutmak + projeksiyon ileride duzelirse dogru davranmak icindir.
    /// Basarili → itemMap dolu, Error null. Basarisiz → itemMap null, Error dolu IActionResult.
    /// </summary>
    private async Task<(Dictionary<int, Item>? ItemMap, IActionResult? Error)> ValidateWriteItemsAsync(
        IEnumerable<int> itemIds, CancellationToken ct, bool allowLotSerial = false)
    {
        var ids = itemIds.Distinct().ToList();
        var items = await _logisticsRepo.GetItemsByIdsAsync(ids, ct);
        var itemMap = items.ToDictionary(x => x.Id);

        foreach (var itemId in ids)
        {
            if (!itemMap.TryGetValue(itemId, out var item) || !item.IsActive)
                return (null, NotFound(new { error = $"Malzeme bulunamadı veya pasif (Id: {itemId})." }));

            // Lot/seri reddi yalnizca allowLotSerial=false iken (Transfer/Sayim — eski davranis KALIR).
            if (!allowLotSerial)
            {
                if (string.Equals(item.TrackingType, "Lot", StringComparison.OrdinalIgnoreCase))
                    return (null, BadRequest(new { error = $"'{item.Code}' lot takipli — işlemi web'den yapın." }));
                if (string.Equals(item.TrackingType, "Serial", StringComparison.OrdinalIgnoreCase))
                    return (null, BadRequest(new { error = $"'{item.Code}' seri takipli — işlemi web'den yapın." }));
            }
            // Varyant (kombinasyon) reddi HER durumda (delivery dahil) — mobil V1 kombinasyon secici yok.
            if (item.Combinations)
                return (null, BadRequest(new { error = $"'{item.Code}' varyant (kombinasyon) takipli — işlemi web'den yapın." }));
        }

        return (itemMap, null);
    }

    /// <summary>
    /// Islem logu (CLAUDE.md audit kurali) — web LogStockDocSaveAsync'in Insert dalinin
    /// mobil karsiligi: ayni entity kodlari (depo_giris/depo_cikis/depo_transfer/sayim), ayni
    /// "ilk deger dokumu + kalem satirlari" bicimi; /AuditLog ekrani iki kanali ayni zaman
    /// cizelgesinde gosterir. Src="Mobile" damgasi kaydin mobilden geldigini ayirt eder.
    /// Audit hatasi kaydi asla bozmaz.
    /// </summary>
    private async Task LogStockDocInsertAsync(
        string docType, SaveStockDocRequest request, int id, string docNo, CancellationToken ct)
    {
        try
        {
            var entity = AuditEntityForDocType(docType);
            IReadOnlyList<StockDocLineDto>? insertedLines = null;
            try { insertedLines = await _stockDocRepo.GetLinesAsync(id, ct); } catch { }

            _audit.LogInsert(entity, id, docNo,
                detail: $"{request.Lines?.Count ?? 0} kalem",
                actor: new AuditActor(Source: "Mobile"),
                snapshot: BuildInsertSnapshot(docType, request),
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

    /// <summary>Web AuditEntityFor (WarehouseController) ile birebir ayni harita — /AuditLog ekrani
    /// mobil ve web kaynakli kayitlari ayni entity kodu altinda birlestirir.</summary>
    private static string AuditEntityForDocType(string docType) => docType switch
    {
        "TRANSFER"        => "depo_transfer",
        "STOCK_OUT"       => "depo_cikis",
        "INVENTORY_COUNT" => "sayim",
        _                 => "depo_giris",
    };

    /// <summary>
    /// Insert audit snapshot'i — web SnapStockHeader (WarehouseController) ile ayni alan secimi:
    /// STOCK_OUT → FromLocationId, INVENTORY_COUNT → FromLocationId (sayim deposu), digerleri
    /// (STOCK_IN) → ToLocationId. TRANSFER mobil V1'de header'da HER IKI lokasyonu da tasidigi
    /// icin (web'in aksine — web'de transfer lokasyonu kalem seviyesinde, bkz. Transfer action
    /// yorumu) ikisi ayri ayri loglanir; tek LocationId alanina sikistirilirsa kaynak/hedef
    /// bilgisi kaybolurdu.
    /// </summary>
    private static object BuildInsertSnapshot(string docType, SaveStockDocRequest request)
    {
        if (docType == "TRANSFER")
        {
            return new
            {
                DocumentDate = request.DocDate.Date,
                request.FromLocationId,
                request.ToLocationId,
                request.Notes,
            };
        }
        if (docType == "INVENTORY_COUNT")
        {
            return new
            {
                DocumentDate = request.DocDate.Date,
                LocationId = request.FromLocationId,
                request.Notes,
            };
        }
        return new
        {
            DocumentDate = request.DocDate.Date,
            // Cikista kaynak, giriste hedef — kullanicinin sectigi lokasyon.
            LocationId = docType == "STOCK_OUT" ? request.FromLocationId : request.ToLocationId,
            request.Notes,
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET contacts/search?q=<query>&take=<n>&docType=<sales|purchase>
    // ──────────────────────────────────────────────────────────────────────
    // Sozlesme (mobil istemci birebir tuketir):
    //   200 [{ id:int, code:string, name:string }]
    //   403 { ok:false, message, error }  — yetki yok
    //
    // items/search'un CARI karsiligi: Contact tablosunda AccountCode/AccountTitle/TaxNumber LIKE
    // (Turkce collation) + CompanyId + IsActive + satir gorunurluk kurallari — hepsi
    // IFinanceService.GetContactsAsync icinde (paylasilan sorgu, masaustu cari rehberiyle ayni).
    // docType OPSIYONEL tip filtresi: AccountType 1=Musteri, 2=Tedarikci, 3=Her Ikisi.
    //   sales    → musteri|ikisi (1,3)   purchase → tedarikci|ikisi (2,3)   yoksa → tum cariler.

    /// <summary>Cari arama — irsaliye ekranlarinin cari rehberi (opsiyonel musteri/tedarikci filtresi).</summary>
    [HttpGet("contacts/search")]
    public async Task<IActionResult> SearchContacts(
        [FromQuery] string? q, [FromQuery] int? take, [FromQuery] string? docType, CancellationToken ct)
    {
        // Yetki: irsaliye form kodlarindan (Satis/Alis Irsaliyesi) herhangi birinde VIEW.
        if (await RequirePermissionAsync(DeliveryFormCodes, ViewActions, ct) is { } denied)
            return denied;

        var query = (q ?? string.Empty).Trim();
        if (query.Length == 0)
            return Ok(Array.Empty<object>());

        var pageSize = take.GetValueOrDefault(20);
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 50) pageSize = 50;

        var contacts = await _financeService.GetContactsAsync(null, query, ct);
        IEnumerable<ContactDto> filtered = contacts.Where(c => c.IsActive);

        if (string.Equals(docType, "sales", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(c => c.AccountType == 1 || c.AccountType == 3);
        else if (string.Equals(docType, "purchase", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(c => c.AccountType == 2 || c.AccountType == 3);

        var result = filtered.Take(pageSize).Select(c => new
        {
            id   = c.Id,
            code = c.AccountCode,
            name = c.AccountTitle,
        });
        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST delivery (irsaliye — FIFO sipariş bağlama)
    // ──────────────────────────────────────────────────────────────────────
    // Sozlesme (mobil istemci birebir tuketir — Kotlin delivery-mobile ile PAYLASILAN):
    //   body: {
    //     docType:"purchase"|"sales", contactId:int, note?:string,
    //     externalRefNumber?:string,          // Tedarikçi İrsaliye No — KABUL EDİLİR, şu an KALICI DEĞİL (kolon yok; bkz. rapor)
    //     lines:[{
    //       itemId:int, quantity:number,
    //       serials?:string[],                // satış: rezerve override/seçim · alış: girilen seriler
    //       lotCode?:string,                  // V1 tek lot/satır
    //       autoGenerateSerials?:bool         // yalnız ALIŞ + AutoSerial kartı
    //     }]
    //   }
    //   200 { ok:true, documentNumber:string,
    //         lines:[{ itemId:int, linked:[{ orderNumber:string, quantity:number }], unlinkedQuantity:number,
    //                  serials:string[], lotCode:string|null }] }   // serials = FİİLEN kullanılan (rezerve/override/FIFO)
    //   400 { error:string }              — is kurali reddi (docType, cari, kalem, varyant, seri sayısı≠adet,
    //                                        sipariş serisi değiştirilemez (param kapalı), müsait seri/lot yok,
    //                                        bağlantısız-yasak, varsayilan depo yok, eksi/lot bakiye)
    //   404 { error:string }              — cari veya malzeme bulunamadi/pasif
    //   403 { ok:false, message, error }  — yetki yok
    //
    // LOT + SERİ: lot/seri-takipli malzeme artık delivery yolunda REDDEDİLMEZ (yalnız varyant reddi kalır).
    // Seri kuralı (satış): (1) bağlanan siparişe rezerve seri varsa öncelik odur; (2) SALES_DELIVERY_SERIAL_
    // OVERRIDE açıksa istemci serileri rezervi override eder (kapalıyken farklı seri → 400); (3) rezerve yok +
    // istemci boş → FIFO otomatik (en eski müsait). Alış: seriler girilir ya da autoGenerateSerials ile üretilir.
    //
    // Miktarlar ANA BIRIMDE (stock-in/out ile ayni konvansiyon). FIFO tahsisi + stok etkisi +
    // SourceLineId + DeliveredQuantity repo transaction'inda (SaveDeliveryFifoAsync — web
    // ConvertOrderToDeliveryAsync ikizi, AYNI bag alanlari). Fiyat: bağlanan satır sipariş
    // fiyati; bağlantısız satır ResolveLinePrices (standart fiyat listesi cozucu, currency=1).

    // LOT + SERİ (2026-07-16): kalem opsiyonel seri/lot alanları — takipsiz malzemede yok sayılır.
    //   serials            → satış: rezerve override / seçim; alış: girilen/okutulan seriler.
    //   lotCode            → V1 tek lot/satır (alışta oluşturulur, satışta mevcut lot şart).
    //   autoGenerateSerials→ yalnız ALIŞ + AutoSerial kartı: sunucu seri üretir (seri listesi verilmez).
    public sealed record MobileDeliveryBodyLine(
        int ItemId, decimal Quantity,
        IReadOnlyList<string>? Serials = null, string? LotCode = null, bool AutoGenerateSerials = false);

    // ExternalRefNumber = Tedarikçi İrsaliye No (alış mal kabulünde tedarikçinin kendi irsaliye no'su).
    public sealed record MobileDeliveryBody(
        string? DocType, int ContactId, string? Note, IReadOnlyList<MobileDeliveryBodyLine>? Lines,
        string? ExternalRefNumber = null);

    /// <summary>Irsaliye (satış teslimat / alış mal kabul) — FIFO ile açık siparişlere bağlar.</summary>
    [HttpPost("delivery")]
    public async Task<IActionResult> Delivery([FromBody] MobileDeliveryBody? body, CancellationToken ct)
    {
        // (a) docType → yön + form kodu + yazma yetkisi.
        var isPurchase = string.Equals(body?.DocType, "purchase", StringComparison.OrdinalIgnoreCase);
        var isSales    = string.Equals(body?.DocType, "sales",    StringComparison.OrdinalIgnoreCase);
        if (body is null || (!isPurchase && !isSales))
            return BadRequest(new { error = "Geçersiz belge türü. docType 'sales' veya 'purchase' olmalı." });

        var formCode = isPurchase ? FormCodes.PurchaseDelivery : FormCodes.SalesDelivery;
        if (await RequirePermissionAsync(new[] { formCode }, WriteActions, ct) is { } denied)
            return denied;

        if (body.ContactId <= 0)
            return BadRequest(new { error = "Cari seçimi zorunlu." });

        var lines = (body.Lines ?? Array.Empty<MobileDeliveryBodyLine>()).Where(l => l is not null).ToList();
        if (lines.Count == 0)
            return BadRequest(new { error = "En az bir kalem girilmelidir." });

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].ItemId <= 0)
                return BadRequest(new { error = $"Kalem {i + 1}: geçersiz malzeme." });
            if (lines[i].Quantity <= 0)
                return BadRequest(new { error = $"Kalem {i + 1}: miktar 0'dan büyük olmalı." });
        }

        // Cari doğrulama — aktif olmalı (404, Stock() GET ile aynı HTTP semantiği).
        var contact = await _financeService.GetContactByIdAsync(body.ContactId, ct);
        if (contact is null || !contact.IsActive)
            return NotFound(new { error = "Cari bulunamadı veya pasif." });

        // (b) Malzeme doğrulama — LOT/SERİ artık delivery yolunda REDDEDİLMEZ (allowLotSerial:true);
        //     repo lot/seri'yi web ambar paritesiyle işler. Varyant (kombinasyon) reddi korunur.
        var (itemMap, itemError) = await ValidateWriteItemsAsync(lines.Select(l => l.ItemId), ct, allowLotSerial: true);
        if (itemError is not null) return itemError;

        // (c/d) FIFO bağlama + bağlantısız-yasak parametreleri. Okuma-zamanı varsayılanları:
        //       bağlama AÇIK (?? true), yasak KAPALI (?? false). Belge yönüne göre ayrı anahtar.
        var fifoKey   = isPurchase ? StockParameters.PurchaseDeliveryFifoBindKey     : StockParameters.SalesDeliveryFifoBindKey;
        var forbidKey = isPurchase ? StockParameters.PurchaseDeliveryRequireOrderKey : StockParameters.SalesDeliveryRequireOrderKey;
        var fifoEnabled    = await _companyParams.GetBoolAsync(StockParameters.FormCode, fifoKey, ct)   ?? true;
        var forbidUnlinked = await _companyParams.GetBoolAsync(StockParameters.FormCode, forbidKey, ct) ?? false;
        // Satış irsaliyesinde sipariş serisi değiştirilebilir mi (default AÇIK). Yalnız satış çıkışında
        // anlamlı — repo alış girişinde bu bayrağı yok sayar. Okuma-zamanı varsayılanı Parametreler'le aynı.
        var serialOverrideEnabled = await _companyParams.GetBoolAsync(
            StockParameters.FormCode, StockParameters.SalesDeliverySerialOverrideKey, ct) ?? true;

        // Tedarikçi İrsaliye No (ExternalRefNumber): Document tablosunda bunu tutacak ADANMIŞ bir kolon
        // YOK (RefNo bile gerçek kolon değil — Notes'a katlanır). Kolon EKLEME kararı/DDL db-uzman
        // sahası olduğundan alan KABUL edilir ama şu an KALICI DEĞİL (sessizce yok sayılır; 400 verilmez).
        // Sözleşme stabil kalır → Kotlin istemci alanı gönderebilir. Kolon eklenince burada persist edilecek.
        _ = body.ExternalRefNumber;

        // Bağlantısız satır fiyatı: standart fiyat çözücü (cari listesi → Genel Liste). Bağlanan
        // satırda repo sipariş fiyatını kullanır. CurrencyId mobil V1'de şirket varsayılanı (1).
        var direction = isPurchase ? PriceDirection.Purchase : PriceDirection.Sales;
        var keys = itemMap!.Keys.Select(id => new PriceEntryKey(id, null)).ToArray();
        var priceByItem = new Dictionary<int, decimal>();
        try
        {
            var resolved = await _priceListService.ResolveLinePricesAsync(
                new ResolveLinePricesRequest(body.ContactId, 1, direction, DateTime.Today, keys), ct);
            foreach (var r in resolved)
                if (r.Price.HasValue) priceByItem[r.ItemId] = r.Price.Value;
        }
        catch { /* fiyat çözümü teslimatı bloklamaz — bağlantısız satır 0 fiyatla yazılır */ }

        var repoLines = lines.Select(l => new MobileDeliveryLineInput(
            l.ItemId, l.Quantity, itemMap[l.ItemId].UnitId,
            priceByItem.TryGetValue(l.ItemId, out var pr) ? pr : 0m,
            itemMap[l.ItemId].Code,
            l.Serials, l.LotCode, l.AutoGenerateSerials)).ToList();

        try
        {
            var (userId, _, _) = GetCurrentUser();
            // (e) Kaydet — stok etkisi (MovementType) + SourceLineId + DeliveredQuantity + eksi bakiye
            //     guard'ı tek transaction'da (SaveDeliveryFifoAsync, ConvertOrderToDeliveryAsync ikizi).
            var result = await _stockDocRepo.SaveDeliveryFifoAsync(
                isPurchase, body.ContactId, body.Note, repoLines, fifoEnabled, forbidUnlinked,
                serialOverrideEnabled, userId > 0 ? userId : null, ct);

            // Belge soyağacı — irsaliye ← sipariş(ler). Web DeliverSalesOrderJson paritesi; çok
            // siparişli FIFO'da her kaynak sipariş için ayrı kenar (İlişkili Belgeler paneli).
            if (result.SourceOrderIds.Count > 0)
            {
                try
                {
                    await _docSourceRepo.EnsureSchemaAsync(ct);
                    foreach (var oid in result.SourceOrderIds)
                        await _docSourceRepo.AddAsync(result.Id, oid, ct);
                }
                catch { /* soyağacı yazımı teslimatı bozmaz */ }
            }

            // (f) OnSave entegrasyon dispatch — SalesController.SaveDocument paritesi. Repo doğrudan
            //     çağrıldığından OnSave otomatik tetiklenmez; burada BİLİNÇLİ tetiklenir.
            var entity = isPurchase ? "alis_irsaliyesi" : "satis_irsaliyesi";
            if (result.Id > 0)
            {
                var fc = CalibraHub.Web.Models.Sales.DocumentTypeFormMap.Resolve(entity);
                _onSaveDispatcher.FireOnSave(new[] { fc.HeaderNew, fc.Header }, result.Id.ToString(), User?.Identity?.Name);
            }

            // Audit — entity kodu DocumentType.Code (web DeliverSalesOrderJson ile aynı /AuditLog kanalı).
            await LogDeliveryInsertAsync(entity, result, ct);

            // (g) Bağlama özeti response.
            return Ok(new
            {
                ok = true,
                documentNumber = result.DocNo,
                lines = result.Lines.Select(l => new
                {
                    itemId           = l.ItemId,
                    linked           = l.Linked.Select(x => new { orderNumber = x.OrderNumber, quantity = x.Quantity }),
                    unlinkedQuantity = l.UnlinkedQuantity,
                    // FİİLEN kullanılan seriler (rezerve/override/FIFO çözümü) + uygulanan lot — istemci
                    // gerçek sonucu buradan okur (takipsiz malzemede boş dizi / null).
                    serials          = l.Serials ?? (IReadOnlyList<string>)Array.Empty<string>(),
                    lotCode          = l.LotCode,
                }),
            });
        }
        catch (NegativeBalanceException nbex)
        {
            return BadRequest(new { error = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            // Bağlantısız-yasak reddi / varsayılan depo yok / sipariş kalem deposu yok — mesaj aynen gösterilir.
            return BadRequest(new { error = ioex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// FIFO irsaliye insert audit'i — web DeliverSalesOrderJson/ReceivePurchaseOrderJson ile aynı
    /// entity kodu (satis_irsaliyesi/alis_irsaliyesi) + kalem dökümü; Src="Mobile" damgası. Audit
    /// hatası kaydı asla bozmaz.
    /// </summary>
    private async Task LogDeliveryInsertAsync(string entity, MobileDeliveryResult result, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<StockDocLineDto>? deliveredLines = null;
            try { deliveredLines = await _stockDocRepo.GetLinesAsync(result.Id, ct); } catch { }

            var linkedCount   = result.Lines.Sum(l => l.Linked.Count);
            var unlinkedItems = result.Lines.Count(l => l.UnlinkedQuantity > 0m);
            var serialCount   = result.Lines.Sum(l => l.Serials?.Count ?? 0);
            var lotItems      = result.Lines.Count(l => !string.IsNullOrWhiteSpace(l.LotCode));
            _audit.LogInsert(entity, result.Id, result.DocNo,
                detail: $"Mobil irsaliye — {result.Lines.Count} malzeme, {linkedCount} sipariş bağı"
                        + (unlinkedItems > 0 ? $", {unlinkedItems} bağlantısız" : "")
                        + (serialCount > 0 ? $", {serialCount} seri" : "")
                        + (lotItems > 0 ? $", {lotItems} lot" : ""),
                actor: new AuditActor(Source: "Mobile"),
                extraChanges: deliveredLines is { Count: > 0 }
                    ? deliveredLines.Select(l => new AuditFieldChange(
                        $"Line[{l.Id}]",
                        $"Kalem — {l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId)}",
                        null,
                        $"{AuditDiff.Normalize(l.Qty)} {l.UnitCode ?? "birim"}")).ToList()
                    : null);
        }
        catch { /* audit yazımı belge kaydını asla bozmaz */ }
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET items/{itemId}/serials  &  items/{itemId}/lots  (seri/lot seçici besleme)
    // ──────────────────────────────────────────────────────────────────────
    // Sozlesme (mobil istemci birebir tuketir):
    //   GET items/{itemId}/serials?locationId=&q=&take=50 → [{ serialNo:string, lotCode:string|null, entryDate:string|null }]
    //     — lokasyondaki MÜSAİT (InStock) seriler, FIFO (en eski önce), q = SerialNo LIKE.
    //   GET items/{itemId}/lots?locationId=&take=20        → [{ lotCode:string, quantity:number, expiry:string|null }]
    //     — lokasyondaki müsait (bakiye > 0) lotlar, FEFO (SKT yakın önce, SKT'siz sonda).
    //   403 { ok:false, message, error }                   — yetki yok
    // Yetki: irsaliye VIEW (DeliveryFormCodes) — seri/lot seçimi teslimat ekranının parçasıdır.
    // Sorgular web seçicileriyle (WarehouseController.GetSerialsJson / GetLotBalancesJson) aynı hareket
    // cebiri; tek fark seri tarafında lokasyon (CurrentLocationId) + q filtresi eklenmesi.

    /// <summary>Bir malzemenin lokasyondaki MÜSAİT (InStock) serileri — FIFO (en eski giriş önce), q LIKE.</summary>
    [HttpGet("items/{itemId:int}/serials")]
    public async Task<IActionResult> ItemSerials(
        int itemId, [FromQuery] int? locationId, [FromQuery] string? q, [FromQuery] int? take, CancellationToken ct)
    {
        if (await RequirePermissionAsync(DeliveryFormCodes, ViewActions, ct) is { } denied)
            return denied;
        if (itemId <= 0) return Ok(Array.Empty<object>());

        var pageSize = take.GetValueOrDefault(50);
        if (pageSize <= 0) pageSize = 50;
        if (pageSize > 200) pageSize = 200;
        int? locId = locationId is > 0 ? locationId : null;
        var query = (q ?? string.Empty).Trim();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Take) s.[SerialNo], lot.[LotNo], s.[Created]
            FROM [{_schema}].[ItemSerial] s
            LEFT JOIN [{_schema}].[Lot] lot ON lot.[Id] = s.[LotId]
            WHERE s.[ItemId] = @ItemId AND s.[IsActive] = 1 AND s.[Status] = 1
              AND (@LocId IS NULL OR s.[CurrentLocationId] = @LocId)
              AND (@Q = N'' OR s.[SerialNo] LIKE @QLike)
            ORDER BY s.[Created], s.[Id];
            """;
        cmd.Parameters.AddWithValue("@Take", pageSize);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@LocId", (object?)locId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Q", query);
        cmd.Parameters.AddWithValue("@QLike", "%" + query + "%");

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new
            {
                serialNo  = r.GetString(0),
                lotCode   = r.IsDBNull(1) ? null : r.GetString(1),
                entryDate = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2),
            });
        return Ok(result);
    }

    /// <summary>Bir malzemenin lokasyondaki müsait (bakiye > 0) lotları — FEFO (SKT yakın önce, SKT'siz sonda).</summary>
    [HttpGet("items/{itemId:int}/lots")]
    public async Task<IActionResult> ItemLots(
        int itemId, [FromQuery] int? locationId, [FromQuery] int? take, CancellationToken ct)
    {
        if (await RequirePermissionAsync(DeliveryFormCodes, ViewActions, ct) is { } denied)
            return denied;
        if (itemId <= 0) return Ok(Array.Empty<object>());

        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var pageSize = take.GetValueOrDefault(20);
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 100) pageSize = 100;
        int? locId = locationId is > 0 ? locationId : null;

        // WarehouseController.GetLotBalancesJson ile aynı FEFO net-bakiye cebiri (yalnız > 0), TOP ile sınırlı.
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Take) lot.[LotNo], lot.[ExpiryDate],
                   SUM(CASE WHEN dl.[MovementType] IN (2,3,4) AND dl.[LocationId] IS NOT NULL
                                 AND (@LocId IS NULL OR dl.[LocationId] = @LocId) THEN dl.[BaseQuantity] ELSE 0 END)
                 - SUM(CASE WHEN dl.[MovementType] IN (1,3,4) AND dl.[FromLocationId] IS NOT NULL
                                 AND (@LocId IS NULL OR dl.[FromLocationId] = @LocId) THEN dl.[BaseQuantity] ELSE 0 END) AS bal
            FROM [{_schema}].[DocumentLine] dl
            INNER JOIN [{_schema}].[Document] doc ON doc.[Id] = dl.[DocumentId]
            INNER JOIN [{_schema}].[Lot] lot ON lot.[Id] = dl.[LotId]
            WHERE dl.[ItemId] = @ItemId AND dl.[LotId] IS NOT NULL
              AND doc.[CompanyId] = @CompanyId AND doc.[IsActive] = 1
              AND dl.[MovementType] IN (1,2,3,4)
            GROUP BY lot.[Id], lot.[LotNo], lot.[ExpiryDate]
            HAVING SUM(CASE WHEN dl.[MovementType] IN (2,3,4) AND dl.[LocationId] IS NOT NULL
                                 AND (@LocId IS NULL OR dl.[LocationId] = @LocId) THEN dl.[BaseQuantity] ELSE 0 END)
                 - SUM(CASE WHEN dl.[MovementType] IN (1,3,4) AND dl.[FromLocationId] IS NOT NULL
                                 AND (@LocId IS NULL OR dl.[FromLocationId] = @LocId) THEN dl.[BaseQuantity] ELSE 0 END) > 0
            ORDER BY CASE WHEN lot.[ExpiryDate] IS NULL THEN 1 ELSE 0 END, lot.[ExpiryDate], lot.[LotNo];
            """;
        cmd.Parameters.AddWithValue("@Take", pageSize);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@LocId", (object?)locId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new
            {
                lotCode  = r.GetString(0),
                expiry   = r.IsDBNull(1) ? (DateTime?)null : r.GetDateTime(1),
                quantity = r.GetDecimal(2),
            });
        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST inventory-count/{id}/apply (Yansıt — sayım farkını stoğa yaz)
    // ──────────────────────────────────────────────────────────────────────
    // Sozlesme (mobil istemci birebir tuketir):
    //   200 { ok:true, writtenCount:int }
    //   400 { error:string }              — idempotent red (belge Draft değil) / geçersiz belge
    //   403 { ok:false, message, error }  — yetki yok
    //
    // Web ApplyInventoryJson PARITESI: IInventoryCountRepository.ApplyAsync taslak sayım farklarını
    // DocumentLine'a (MovementType=4/Adjust) atomik yazar. IDEMPOTENT — ikinci çağrı (Status Draft
    // değil) InvalidOperationException fırlatır, mesaj AYNEN {error} olarak döner (çift tıklama koruması).

    /// <summary>Sayım "Yansıt" — taslak sayım farklarını stok bakiyesine işler (idempotent).</summary>
    [HttpPost("inventory-count/{id:int}/apply")]
    public async Task<IActionResult> ApplyInventoryCount(int id, CancellationToken ct)
    {
        if (await RequirePermissionAsync(new[] { FormCodes.InventoryCount }, WriteActions, ct) is { } denied)
            return denied;
        if (id <= 0)
            return BadRequest(new { error = "Geçersiz belge." });
        try
        {
            var writtenCount = await _inventoryCountRepo.ApplyAsync(id, ct);
            return Ok(new { ok = true, writtenCount });
        }
        catch (InvalidOperationException ex)
        {
            // Optimistic-lock idempotency (belge zaten Yansıtılmış/Draft değil) — mesaj aynen döner.
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }
}

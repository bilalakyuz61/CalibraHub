using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Warehouse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class WarehouseController : Controller
{
    private readonly IStockDocRepository _stockDocRepo;
    private readonly IInventoryCountRepository _inventoryCountRepo;
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly IArgeProjectService _argeService;
    private readonly IFieldSettingRepository _fieldSettings;
    private readonly ICompanyParameterService _companyParams;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly IDocumentSourceRepository _docSourceRepo;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IAuditTrailService _audit;
    private readonly IPermissionService _permService;
    private readonly string _schema;

    private static readonly JsonSerializerOptions BoardJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public WarehouseController(
        IStockDocRepository stockDocRepo,
        IInventoryCountRepository inventoryCountRepo,
        ILogisticsConfigurationService logisticsService,
        IArgeProjectService argeService,
        IFieldSettingRepository fieldSettings,
        ICompanyParameterService companyParams,
        IDocumentTypeRepository documentTypeRepo,
        IDocumentSourceRepository docSourceRepo,
        SqlServerConnectionFactory connectionFactory,
        IAuditTrailService audit,
        IPermissionService permService,
        CalibraDatabaseOptions dbOptions)
    {
        _stockDocRepo = stockDocRepo;
        _inventoryCountRepo = inventoryCountRepo;
        _logisticsService = logisticsService;
        _argeService = argeService;
        _fieldSettings = fieldSettings;
        _companyParams = companyParams;
        _documentTypeRepo = documentTypeRepo;
        _docSourceRepo = docSourceRepo;
        _connectionFactory = connectionFactory;
        _audit = audit;
        _permService = permService;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    private string CurrentUser() => User.FindFirstValue(ClaimTypes.Name) ?? "system";
    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // ── İşlem logu (audit trail) yardımcıları ──────────────────────────────
    // Depo belgelerinin audit entity kodu = DocumentType.Code (SqlStockDocRepository.TypeCodeFor
    // ile birebir). Edit ekranındaki "Değişiklik Geçmişi" aynı kodla sorgular.

    private static string AuditEntityFor(string? docType) => docType switch
    {
        "TRANSFER"        => "depo_transfer",
        "STOCK_OUT"       => "depo_cikis",
        "INVENTORY_COUNT" => "sayim",
        _                 => "depo_giris",
    };

    // ── Yetki: paylaşılan Giriş/Çıkış/Transfer endpoint'leri ───────────────
    // StockEntryEdit / GetDocJson / SaveDocJson / DeleteDocJson tek action üzerinden
    // STOCK_IN, STOCK_OUT ve TRANSFER belgelerini birlikte yönetir (bkz. StockDocEdit.cshtml).
    // [PermissionScope] statik attribute tek FormCode taşıyabildiği için bu üçünü
    // ayırt edemez — SalesController.SaveDocument/DeleteDocument ile aynı desen:
    // attribute yerine burada request/DB'den okunan DocType'a göre dinamik çözülür.
    // INVENTORY_COUNT: sayım ekranı (InventoryEdit) GetDocJson'ı okuma için paylaşır;
    // sayım belgesi StockIn'e değil kendi form koduna (INVENTORY_COUNT) gate edilir —
    // sayfa gate'i [PermissionScope(FormCodes.InventoryCount)] ile aynı kullanıcı kümesi.

    private static string FormCodeForDocType(string? docType) => docType switch
    {
        "TRANSFER"        => FormCodes.Transfer,
        "STOCK_OUT"       => FormCodes.StockOut,
        "INVENTORY_COUNT" => FormCodes.InventoryCount,
        _                 => FormCodes.StockIn,
    };

    private async Task<bool> CheckStockDocPermissionAsync(
        string? docType, IReadOnlyList<string> actionCodes, CancellationToken ct)
    {
        var formCode = FormCodeForDocType(docType);
        UserAuthorizationCatalog.TryParseRole(User.FindFirstValue(ClaimTypes.Role) ?? "", out var role);
        int? deptId = int.TryParse(User.FindFirstValue("department_id"), out var d) && d > 0 ? d : null;
        return await _permService.CheckAnyAsync(CurrentUserId() ?? 0, role, deptId, formCode, actionCodes, ct);
    }

    /// <summary>RefNo repo tarafında Notes'a "[Ref: x]" olarak gömülür (CombineNotesWithRef) —
    /// diff'te yanlış pozitif olmaması için request tarafında aynı birleşim uygulanır.</summary>
    private static string? CombineNotesWithRefForAudit(string? notes, string? refNo)
    {
        if (string.IsNullOrWhiteSpace(refNo)) return notes;
        var prefix = $"[Ref: {refNo.Trim()}]";
        return string.IsNullOrWhiteSpace(notes) ? prefix : $"{prefix} {notes.Trim()}";
    }

    /// <summary>Header diff snapshot'ı (eski kayıt) — yalnızca ekranda düzenlenebilen header alanları.
    /// Sayımda lokasyon DTO'da FromLocationId'de, diğer tiplerde ToLocationId'de taşınır.</summary>
    private static object SnapStockHeader(StockDocDto d) => new
    {
        DocumentDate = d.DocDate.Date,
        LocationId   = d.DocType == "INVENTORY_COUNT" ? d.FromLocationId : d.ToLocationId,
        d.Notes,
        d.ArgeProjectId,
    };

    /// <summary>Header diff snapshot'ı (yeni istek) — eski DTO snapshot'ı ile aynı alan adları.</summary>
    private static object SnapStockHeader(SaveStockDocRequest r) => new
    {
        DocumentDate = r.DocDate.Date,
        LocationId   = r.DocType == "INVENTORY_COUNT" ? r.FromLocationId : r.ToLocationId,
        Notes        = CombineNotesWithRefForAudit(r.Notes, r.RefNo),
        r.ArgeProjectId,
    };

    /// <summary>
    /// Kalem diff'i — kaydetme DELETE+INSERT çalıştığı için satır Id'leri stabil değildir;
    /// eşleştirme içerik anahtarıyla yapılır (ItemId + Kombinasyon + Lot) ve aynı anahtarda
    /// sıra korunur. Yalnızca eklenen/silinen/değişen satırlar loglanır.
    /// </summary>
    private static List<AuditFieldChange> BuildStockLineChanges(
        IReadOnlyList<StockDocLineDto> oldLines, IReadOnlyList<StockDocLineDto> newLines)
    {
        var changes = new List<AuditFieldChange>();

        static string Key(StockDocLineDto l) => $"{l.ItemId}|{l.CombinationId}|{l.LotNo}";
        static string LineName(StockDocLineDto l) => l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId);
        static string LineSummary(StockDocLineDto l) =>
            $"{AuditDiff.Normalize(l.Qty)} {l.UnitCode ?? "birim"}";

        var oldByKey = oldLines.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var newByKey = newLines.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var allKeys = oldByKey.Keys.Union(newByKey.Keys);

        foreach (var key in allKeys)
        {
            var olds = oldByKey.TryGetValue(key, out var ol) ? ol : new List<StockDocLineDto>();
            var news = newByKey.TryGetValue(key, out var nl) ? nl : new List<StockDocLineDto>();
            var paired = Math.Min(olds.Count, news.Count);

            // Aynı anahtar altında eşleşen satırlar — alan bazlı diff
            for (var i = 0; i < paired; i++)
            {
                var o = olds[i];
                var n = news[i];
                var name = LineName(n);
                void AddIfChanged(string field, string label, object? oldVal, object? newVal)
                {
                    var os = AuditDiff.Normalize(oldVal);
                    var ns = AuditDiff.Normalize(newVal);
                    if (!string.Equals(os ?? "", ns ?? "", StringComparison.Ordinal))
                        changes.Add(new AuditFieldChange($"Line[{key}].{field}", $"{name} · {label}", os, ns));
                }
                AddIfChanged("Quantity", "Miktar", o.Qty, n.Qty);
                AddIfChanged("FromLocation", "Çıkış Lokasyonu",
                    o.FromLocationName ?? o.FromLocationId?.ToString(),
                    n.FromLocationName ?? n.FromLocationId?.ToString());
                AddIfChanged("ToLocation", "Giriş Lokasyonu",
                    o.ToLocationName ?? o.ToLocationId?.ToString(),
                    n.ToLocationName ?? n.ToLocationId?.ToString());
                AddIfChanged("UnitCost", "Birim Maliyet", o.UnitCost, n.UnitCost);
                AddIfChanged("Notes", "Not", o.Notes, n.Notes);
            }

            // Fazla eski satırlar → silindi, fazla yeni satırlar → eklendi
            for (var i = paired; i < olds.Count; i++)
                changes.Add(new AuditFieldChange($"Line[{key}]",
                    $"Kalem Silindi — {LineName(olds[i])}", LineSummary(olds[i]), null));
            for (var i = paired; i < news.Count; i++)
                changes.Add(new AuditFieldChange($"Line[{key}]",
                    $"Kalem Eklendi — {LineName(news[i])}", null, LineSummary(news[i])));
        }

        return changes;
    }

    /// <summary>Kaydetmeden ÖNCE mevcut belge + satır snapshot'ını okur (yalnızca audit için;
    /// okunamazsa null döner, kayıt akışı etkilenmez).</summary>
    private async Task<(StockDocDto? Doc, IReadOnlyList<StockDocLineDto>? Lines)> TryGetStockDocForAuditAsync(
        int id, CancellationToken ct)
    {
        try
        {
            var doc = await _stockDocRepo.GetByIdAsync(id, ct);
            var lines = doc is null ? null : await _stockDocRepo.GetLinesAsync(id, ct);
            return (doc, lines);
        }
        catch { return (null, null); }
    }

    /// <summary>Silinen belgenin kalem dökümü — silme log satırına snapshot olarak eklenir
    /// ("ne kayboldu" izlenebilsin). Old dolu, New null.</summary>
    private static List<AuditFieldChange>? BuildDeletedLineSnapshot(IReadOnlyList<StockDocLineDto>? lines)
    {
        if (lines is not { Count: > 0 }) return null;
        return lines.Select(l => new AuditFieldChange(
            $"Line[{l.Id}]",
            $"Kalem — {l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId)}",
            LineValueSummary(l),
            null)).ToList();
    }

    /// <summary>Yeni belgenin kalem dökümü — ekleme log satırına ilk değerler olarak eklenir. New dolu, Old null.</summary>
    private static List<AuditFieldChange>? BuildInsertedLineSnapshot(IReadOnlyList<StockDocLineDto>? lines)
    {
        if (lines is not { Count: > 0 }) return null;
        return lines.Select(l => new AuditFieldChange(
            $"Line[{l.Id}]",
            $"Kalem — {l.MaterialName ?? l.MaterialCode ?? ("#" + l.ItemId)}",
            null,
            LineValueSummary(l))).ToList();
    }

    private static string LineValueSummary(StockDocLineDto l) =>
        $"{AuditDiff.Normalize(l.Qty)} {l.UnitCode ?? "birim"}"
        + (string.IsNullOrWhiteSpace(l.LotNo) ? "" : $" · Lot {l.LotNo}");

    /// <summary>Başarılı depo belgesi kaydı sonrası işlem logu — yeni kayıt Insert,
    /// güncelleme header + kalem diff'i. Audit hatası kayıt akışını asla bozmaz.</summary>
    private async Task LogStockDocSaveAsync(
        SaveStockDocRequest request, int id, string docNo,
        StockDocDto? oldDoc, IReadOnlyList<StockDocLineDto>? oldLines, CancellationToken ct)
    {
        try
        {
            var entity = AuditEntityFor(request.DocType);
            var lineCount = request.Lines?.Count ?? 0;
            if (oldDoc is null)
            {
                // request.Id > 0 ama eski kayıt okunamadıysa da Insert yerine detay ile Update yazmak
                // yanıltıcı olurdu — eski durum bilinmiyorsa yeni kayıtta Insert, güncellemede detay log.
                if (request.Id is > 0)
                {
                    _audit.LogChanges(entity, id, docNo, Array.Empty<AuditFieldChange>(),
                        detail: $"Güncellendi · {lineCount} kalem (önceki durum okunamadı)");
                }
                else
                {
                    // İlk değer dökümü: başlık alanları + kaydedilen kalemler ("boş → değer")
                    IReadOnlyList<StockDocLineDto>? insertedLines = null;
                    try { insertedLines = await _stockDocRepo.GetLinesAsync(id, ct); } catch { }
                    _audit.LogInsert(entity, id, docNo, detail: $"{lineCount} kalem",
                        snapshot: SnapStockHeader(request),
                        extraChanges: BuildInsertedLineSnapshot(insertedLines));
                }
                return;
            }

            var changes = new List<AuditFieldChange>();
            changes.AddRange(AuditDiff.Compute(SnapStockHeader(oldDoc), SnapStockHeader(request), entity));
            if (oldLines is not null)
            {
                var newLines = await _stockDocRepo.GetLinesAsync(id, ct);
                changes.AddRange(BuildStockLineChanges(oldLines, newLines));
            }
            _audit.LogChanges(entity, id, docNo, changes);
        }
        catch { /* audit yazımı belge kaydını asla bozmaz */ }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TRANSFER
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Transfer)]
    public async Task<IActionResult> Transfer(CancellationToken ct)
    {
        var config = await BuildTransferBoardConfigAsync(ct);
        return View(new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/TransferBoardConfig")]
    public async Task<IActionResult> TransferBoardConfig(CancellationToken ct)
        => Json(await BuildTransferBoardConfigAsync(ct));

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Transfer)]
    public async Task<IActionResult> TransferEdit(int? id, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        var bindings = await GetLineGuideBindingsAsync(FormCodes.TransferLines, ct);
        var gridConfig = BuildLineGridConfig("TRANSFER", bindings);
        var vm = new StockDocEditViewModel
        {
            DocId   = id,
            DocType = "TRANSFER",
            LineGridConfigJson = JsonSerializer.Serialize(gridConfig, BoardJsonOpts),
        };
        return View("StockDocEdit", vm);
    }

    /// <summary>
    /// Kalem grid'i rehber (tip-1) bağlantıları: önce formun kendi kodu, kayıt yoksa
    /// SALES_QUOTE_LINES varsayılanından devral — kalem yapıları özdeş (Sales ile aynı fallback).
    /// </summary>
    private async Task<IReadOnlyCollection<FieldGuideBindingDto>> GetLineGuideBindingsAsync(string lineFormCode, CancellationToken ct)
    {
        var bindings = await _fieldSettings.GetGuideBindingsForFormAsync(lineFormCode, ct);
        if (bindings.Count == 0)
            bindings = await _fieldSettings.GetGuideBindingsForFormAsync("SALES_QUOTE_LINES", ct);
        return bindings;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SAYIM
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> Inventory(CancellationToken ct)
    {
        var config = await BuildInventoryBoardConfigAsync(ct);
        return View(new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/InventoryBoardConfig")]
    public async Task<IActionResult> InventoryBoardConfig(CancellationToken ct)
        => Json(await BuildInventoryBoardConfigAsync(ct));

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> InventoryEdit(int? id, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        var bindings = await GetLineGuideBindingsAsync("INVENTORY_COUNT_LINES", ct);
        var gridConfig = BuildInventoryLineGridConfig(bindings);
        var vm = new InventoryEditViewModel
        {
            DocId              = id,
            LineGridConfigJson = JsonSerializer.Serialize(gridConfig, BoardJsonOpts),
        };
        return View(vm);
    }

    // Sayım deposundaki beklenen stok miktarlarını döndürür (stok hareketleri üzerinden)
    [HttpGet]
    public async Task<IActionResult> GetLocationStockSnapshotJson(int locationId, string? date, CancellationToken ct)
    {
        if (locationId <= 0) return Json(Array.Empty<object>());
        var upToDate   = DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.None, out var d)
                         ? d.Date : DateTime.Today;
        var companyId  = _connectionFactory.ResolveCurrentCompanyId();

        // Stok etkisi kapalı (STOCK_EFFECT_{code}=false) belge türlerini bakiye dışı bırak
        var disabledTypeIds = await CalibraHub.Application.Services.StockEffectHelper
            .GetDisabledDocTypeIdsAsync(_companyParams, _documentTypeRepo, ct);
        var seFilter = disabledTypeIds.Count == 0
            ? ""
            : $" AND (d.DocumentTypeId IS NULL OR d.DocumentTypeId NOT IN ({string.Join(",", disabledTypeIds.Select((_, i) => $"@sef{i}"))}))";

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                l.ItemId,
                i.Code             AS material_code,
                i.Name             AS material_name,
                l.UnitId,
                u.Code             AS unit_code,
                l.CombinationId,
                cfg.RecordCode     AS combination_code,
                SUM(
                    CASE
                        WHEN l.MovementType = 2 AND l.LocationId     = @LocId THEN  l.Quantity
                        WHEN l.MovementType = 1 AND l.FromLocationId = @LocId THEN -l.Quantity
                        WHEN l.MovementType = 3 AND l.LocationId     = @LocId THEN  l.Quantity
                        WHEN l.MovementType = 3 AND l.FromLocationId = @LocId THEN -l.Quantity
                        WHEN l.MovementType = 4 AND l.LocationId     = @LocId THEN  l.Quantity
                        WHEN l.MovementType = 4 AND l.FromLocationId = @LocId THEN -l.Quantity
                        ELSE 0
                    END
                ) AS expected_qty
            FROM [{_schema}].[Document] d
            JOIN [{_schema}].[DocumentLine] l ON l.DocumentId = d.id
            LEFT JOIN [{_schema}].[Items]             i   ON i.Id   = l.ItemId
            LEFT JOIN [{_schema}].[Unit]              u   ON u.Id   = l.UnitId
            LEFT JOIN [{_schema}].[ItemConfiguration] cfg ON cfg.Id = l.CombinationId
            WHERE d.CompanyId = @CompanyId
              AND d.IsActive  = 1
              AND CONVERT(DATE, d.DocumentDate) <= @Date
              AND l.MovementType IN (1,2,3,4)
              AND (l.FromLocationId = @LocId OR l.LocationId = @LocId){seFilter}
            GROUP BY l.ItemId, i.Code, i.Name, l.UnitId, u.Code, l.CombinationId, cfg.RecordCode
            HAVING SUM(
                CASE
                    WHEN l.MovementType = 2 AND l.LocationId     = @LocId THEN  l.Quantity
                    WHEN l.MovementType = 1 AND l.FromLocationId = @LocId THEN -l.Quantity
                    WHEN l.MovementType = 3 AND l.LocationId     = @LocId THEN  l.Quantity
                    WHEN l.MovementType = 3 AND l.FromLocationId = @LocId THEN -l.Quantity
                    WHEN l.MovementType = 4 AND l.LocationId     = @LocId THEN  l.Quantity
                    WHEN l.MovementType = 4 AND l.FromLocationId = @LocId THEN -l.Quantity
                    ELSE 0
                END
            ) > 0
            ORDER BY i.Code;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@LocId",     locationId);
        cmd.Parameters.AddWithValue("@Date",      upToDate);
        for (var i = 0; i < disabledTypeIds.Count; i++)
            cmd.Parameters.AddWithValue($"@sef{i}", disabledTypeIds[i]);

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new
            {
                itemId          = r.GetInt32(0),
                materialCode    = r.IsDBNull(1) ? null : r.GetString(1),
                materialName    = r.IsDBNull(2) ? null : r.GetString(2),
                unitId          = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                unitCode        = r.IsDBNull(4) ? null : r.GetString(4),
                combinationId   = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                combinationCode = r.IsDBNull(6) ? null : r.GetString(6),
                expectedQty     = r.GetDecimal(7),
            });
        }
        return Json(result);
    }

    // Lot bakiyeleri — hareket satırındaki Lot/Parti seçici dropdown'ını besler (Lot takibi).
    // locationId verilirse o depodaki, verilmezse tüm depolardaki net lot bakiyeleri (yalnız > 0).
    // FEFO sırası: SKT'si yakın olan önce, SKT'siz lotlar sonda.
    [HttpGet]
    public async Task<IActionResult> GetLotBalancesJson(int itemId, int? locationId, CancellationToken ct)
    {
        if (itemId <= 0) return Json(Array.Empty<object>());
        var companyId = _connectionFactory.ResolveCurrentCompanyId();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT lot.[LotNo], lot.[ExpiryDate],
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
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@LocId", (object?)locationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);

        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var lotNo = r.GetString(0);
            DateTime? expiry = r.IsDBNull(1) ? null : r.GetDateTime(1);
            var bal = r.GetDecimal(2);
            var label = "Bakiye: " + bal.ToString("N2", tr)
                + (expiry.HasValue ? " · SKT: " + expiry.Value.ToString("dd.MM.yyyy", tr) : "");
            result.Add(new { lotNo, balance = bal, expiryDate = expiry, label });
        }
        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> SaveInventoryJson([FromBody] SaveStockDocRequest? request, CancellationToken ct)
    {
        if (request is null)
            return Json(new { success = false, message = "Geçersiz istek." });
        try
        {
            // İşlem logu: güncellemede eski header + kalem snapshot'ı kaydetmeden ÖNCE alınır
            var (oldDoc, oldLines) = request.Id is > 0
                ? await TryGetStockDocForAuditAsync(request.Id.Value, ct)
                : ((StockDocDto?)null, (IReadOnlyList<StockDocLineDto>?)null);

            var (id, docNo) = await _stockDocRepo.SaveAsync(request, CurrentUserId(), ct);

            await LogStockDocSaveAsync(request, id, docNo, oldDoc, oldLines, ct);
            return Json(new { success = true, id, docNo });
        }
        catch (InvalidOperationException ioex)
        {
            // İzlenebilirlik/doğrulama hatası — kullanıcıya doğrudan gösterilir (lot/seri zorunlu vb.)
            return Json(new { success = false, message = ioex.Message });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveInventoryJson] HATA: {ex}");
            return Json(new { success = false, message = "İşlem sırasında bir hata oluştu: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> DeleteInventoryJson(int id, CancellationToken ct)
    {
        try
        {
            // Yansıtılmış (Applied) sayım immutable — fark satırları belgeye bağlı; silinirse
            // stok bakiyesi sessizce geri döner. Bu yüzden yalnızca Draft sayım silinebilir.
            var status = await _inventoryCountRepo.GetStatusAsync(id, ct);
            if (status == 1)
                return Json(new { ok = false, error = "Yansıtılmış sayım fişi silinemez. Yansıtılan stok farkları bu belgeye bağlıdır; silinmesi bakiyeyi bozar." });

            // İşlem logu: silinen belgenin kimliği + kalem dökümü silmeden ÖNCE okunur
            var (docForAudit, linesForAudit) = await TryGetStockDocForAuditAsync(id, ct);

            await _stockDocRepo.DeleteAsync(id, ct);

            if (docForAudit is not null)
                _audit.LogDelete(AuditEntityFor(docForAudit.DocType), id, docForAudit.DocNo,
                    detail: linesForAudit is { Count: > 0 } ? $"{linesForAudit.Count} kalem" : null,
                    snapshot: BuildDeletedLineSnapshot(linesForAudit));
            return Json(new { ok = true });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Yansıt (2026-07-02) — taslak sayım satırlarını güncel bakiyeyle karşılaştırır, farkları
    /// DocumentLine'a (Adjust) atomik yazar. Idempotent: aynı sayım ikinci kez yansıtılamaz.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> ApplyInventoryJson(int id, CancellationToken ct)
    {
        try
        {
            var writtenCount = await _inventoryCountRepo.ApplyAsync(id, ct);
            return Json(new { ok = true, writtenCount });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>Yansıtma iptali (unpost) — yansıtılmış sayımı taslağa döndürür, stok hareketlerini geri alır.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> RevertInventoryJson(int id, CancellationToken ct)
    {
        try
        {
            var removed = await _inventoryCountRepo.RevertAsync(id, ct);
            return Json(new { ok = true, removed });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException ex)
        {
            // İptal, bir depoda bakiyeyi eksiye düşürüyor (yansıtılan stok sonradan tüketilmiş).
            return Json(new { ok = false, error = "Yansıtma iptal edilemez — " + ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>Sayım bağlantısız bakiye sıfırlama — depodaki TÜM canlı bakiyeleri sıfırlayan Adjust satırları yazar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> ZeroLocationBalancesJson(int id, CancellationToken ct)
    {
        try
        {
            var writtenCount = await _inventoryCountRepo.ZeroLocationBalancesAsync(id, ct);
            return Json(new { ok = true, writtenCount });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>Sayılmayan stokların sıfırlanması — sayım kalemlerinde yer almayan bakiyeleri sıfırlar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> ZeroUncountedJson(int id, CancellationToken ct)
    {
        try
        {
            var writtenCount = await _inventoryCountRepo.ZeroUncountedAsync(id, ct);
            return Json(new { ok = true, writtenCount });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AMBAR GİRİ�? / ÇIKI�?
    // ═══════════════════════════════════════════════════════════════════════

    // Legacy birleşik "Ambar Giriş / Çıkış" ekranı KALDIRILDI (2026-07-09): her ekran kendi
    // tek "+" butonunu taşır (Giriş → Giriş Belgesi, Çıkış → Çıkış Belgesi). Menü zaten ayrık
    // (/Warehouse/StockIn + /Warehouse/StockOut). Bu endpoint yalnız eski tab/bookmark/dashboard
    // linkleri için korunur ve ayrık Giriş ekranına yönlendirir → iki-buton ekranı hiçbir yerden çıkmaz.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public IActionResult StockEntry() => Redirect("/Warehouse/StockIn");

    // Eski birleşik board'un refreshUrl'i buraya gelir; hâlâ açık kalmış eski bir tab refresh
    // olunca da tek-butonlu (Giriş) config alsın diye "in" döner (birleşik/null değil).
    [HttpGet("/Warehouse/StockEntryBoardConfig")]
    public async Task<IActionResult> StockEntryBoardConfig(CancellationToken ct)
        => Json(await BuildStockEntryBoardConfigAsync("in", ct));

    // 2026-07-06: Ambar Giriş / Çıkış menüde ayrıldı — her tip kendi listesini açar.
    // StockEntry (birleşik) eski link/dashboard'lar için backward-compat korunur.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public async Task<IActionResult> StockIn(CancellationToken ct)
    {
        ViewData["Title"] = "Ambar Giriş";
        var config = await BuildStockEntryBoardConfigAsync("in", ct);
        return View("StockEntry", new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/StockInBoardConfig")]
    public async Task<IActionResult> StockInBoardConfig(CancellationToken ct)
        => Json(await BuildStockEntryBoardConfigAsync("in", ct));

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockOut)]
    public async Task<IActionResult> StockOut(CancellationToken ct)
    {
        ViewData["Title"] = "Ambar Çıkış";
        var config = await BuildStockEntryBoardConfigAsync("out", ct);
        return View("StockEntry", new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/StockOutBoardConfig")]
    public async Task<IActionResult> StockOutBoardConfig(CancellationToken ct)
        => Json(await BuildStockEntryBoardConfigAsync("out", ct));

    [HttpGet]
    public async Task<IActionResult> StockEntryEdit(int? id, string? type, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        var docType = string.Equals(type, "out", StringComparison.OrdinalIgnoreCase)
            ? "STOCK_OUT" : "STOCK_IN";

        // Mevcut belge varsa gerçek tipini oku
        if (id.HasValue && id.Value > 0)
        {
            var existing = await _stockDocRepo.GetByIdAsync(id.Value, ct);
            if (existing != null) docType = existing.DocType;
        }

        // Statik [PermissionScope(StockIn)] bu ekranı STOCK_OUT'ta da yanlışlıkla StockIn
        // yetkisine bağlıyordu — belge tipine göre dinamik kontrol (bkz. CheckStockDocPermissionAsync).
        if (!await CheckStockDocPermissionAsync(docType, new[] { "VIEW", "VIEW_OWN" }, ct))
            return Forbid();

        var lineFormCode = docType == "STOCK_OUT" ? FormCodes.StockOutLines : FormCodes.StockInLines;
        var bindings = await GetLineGuideBindingsAsync(lineFormCode, ct);
        var gridConfig = BuildLineGridConfig(docType, bindings);
        var vm = new StockDocEditViewModel
        {
            DocId   = id,
            DocType = docType,
            LineGridConfigJson = JsonSerializer.Serialize(gridConfig, BoardJsonOpts),
        };
        return View("StockDocEdit", vm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SHARED API
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetDocJson(int id, CancellationToken ct)
    {
        var doc = await _stockDocRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();
        var lines = await _stockDocRepo.GetLinesAsync(id, ct);
        // Sayım ise durum (0=Draft,1=Applied,2=Cancelled) — edit ekranı Applied'da
        // Yansıt yerine "Yansıtma İptali" gösterip Kaydet/Sil'i kilitler.
        byte? inventoryStatus = doc.DocType == "INVENTORY_COUNT"
            ? await _inventoryCountRepo.GetStatusAsync(id, ct)
            : null;
        return Json(new { doc, lines, inventoryStatus });
    }

    [HttpPost]
    public async Task<IActionResult> SaveDocJson([FromBody] SaveStockDocRequest? request, CancellationToken ct)
    {
        if (request is null)
            return Json(new { success = false, message = "Geçersiz istek." });

        // Statik [PermissionScope(StockIn)] bu paylaşılan endpoint'te STOCK_OUT/TRANSFER
        // kayıtlarını da yanlışlıkla StockIn yetkisine bağlıyordu (bkz. CheckStockDocPermissionAsync).
        if (!await CheckStockDocPermissionAsync(request.DocType, new[] { "CREATE", "EDIT_OWN", "EDIT_ALL" }, ct))
            return Json(new { success = false, message = "Bu belge için yetkiniz bulunmuyor." });
        try
        {
            // İşlem logu: güncellemede eski header + kalem snapshot'ı kaydetmeden ÖNCE alınır
            var (oldDoc, oldLines) = request.Id is > 0
                ? await TryGetStockDocForAuditAsync(request.Id.Value, ct)
                : ((StockDocDto?)null, (IReadOnlyList<StockDocLineDto>?)null);

            var (id, docNo) = await _stockDocRepo.SaveAsync(request, CurrentUserId(), ct);

            await LogStockDocSaveAsync(request, id, docNo, oldDoc, oldLines, ct);
            return Json(new { success = true, id, docNo });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            return Json(new { success = false, message = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            // Lot zorunluluğu / lot bakiyesi gibi doğrulama mesajları kullanıcıya aynen gösterilir.
            return Json(new { success = false, message = ioex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>Satış siparişi teslimatı — açık kalemler için fiziksel çıkış yazar + rezervasyonu serbest bırakır (Faz 2).
    /// body.Lines dolu ise kısmi teslimat (kalem başı miktar); boş/null ise tüm açık miktar teslim edilir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeliverSalesOrderJson([FromBody] DeliverOrderRequest body, CancellationToken ct)
    {
        var orderId = body?.OrderId ?? 0;
        if (orderId <= 0) return Json(new { success = false, message = "Sipariş bulunamadı." });
        try
        {
            var (id, docNo) = await _stockDocRepo.DeliverSalesOrderAsync(orderId, CurrentUserId(), BuildDeliverMap(body!.Lines), ct);
            // Belge soyağacı: satış irsaliyesi ← satış siparişi.
            // Repo ParentDocumentId + kalem SourceLineId set ediyor; İlişkili Belgeler paneli DocumentSource okur.
            await _docSourceRepo.EnsureSchemaAsync(ct);
            await _docSourceRepo.AddAsync(id, orderId, ct);
            // İşlem logu: yeni bir satis_irsaliyesi belgesidir (kalem dökümüyle)
            IReadOnlyList<StockDocLineDto>? deliveredLines = null;
            try { deliveredLines = await _stockDocRepo.GetLinesAsync(id, ct); } catch { }
            _audit.LogInsert("satis_irsaliyesi", id, docNo,
                detail: $"Satış siparişi teslimatı → irsaliye (Sipariş #{orderId})",
                extraChanges: BuildInsertedLineSnapshot(deliveredLines));
            return Json(new { success = true, id, docNo });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            return Json(new { success = false, message = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            return Json(new { success = false, message = ioex.Message });
        }
        catch (Exception)
        {
            return Json(new { success = false, message = "Teslimat sırasında bir hata oluştu." });
        }
    }

    /// <summary>Satın alma siparişi mal kabulü — açık kalemler için Alış İrsaliyesi (stok girişi) yazar.
    /// body.Lines dolu ise kısmi mal kabul (kalem başı miktar); boş/null ise tüm açık miktar kabul edilir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReceivePurchaseOrderJson([FromBody] DeliverOrderRequest body, CancellationToken ct)
    {
        var orderId = body?.OrderId ?? 0;
        if (orderId <= 0) return Json(new { success = false, message = "Sipariş bulunamadı." });
        try
        {
            var (id, docNo) = await _stockDocRepo.ReceivePurchaseOrderAsync(orderId, CurrentUserId(), BuildDeliverMap(body!.Lines), ct);
            // Belge soyağacı: alış irsaliyesi ← satın alma siparişi.
            await _docSourceRepo.EnsureSchemaAsync(ct);
            await _docSourceRepo.AddAsync(id, orderId, ct);
            IReadOnlyList<StockDocLineDto>? receivedLines = null;
            try { receivedLines = await _stockDocRepo.GetLinesAsync(id, ct); } catch { }
            _audit.LogInsert("alis_irsaliyesi", id, docNo,
                detail: $"Satın alma siparişi mal kabulü → irsaliye (Sipariş #{orderId})",
                extraChanges: BuildInsertedLineSnapshot(receivedLines));
            return Json(new { success = true, id, docNo });
        }
        catch (InvalidOperationException ioex)
        {
            return Json(new { success = false, message = ioex.Message });
        }
        catch (Exception)
        {
            return Json(new { success = false, message = "Mal kabul sırasında bir hata oluştu." });
        }
    }

    /// <summary>Kısmi teslimat/mal kabul modalı — siparişin açık (teslim edilmemiş) kalemlerini döner.</summary>
    [HttpGet]
    public async Task<IActionResult> OrderOpenLinesJson(int orderId, CancellationToken ct)
    {
        if (orderId <= 0) return Json(new { success = false, message = "Sipariş bulunamadı." });
        try
        {
            var lines = await _stockDocRepo.GetOrderOpenLinesAsync(orderId, ct);
            return Json(new { success = true, lines });
        }
        catch (Exception)
        {
            return Json(new { success = false, message = "Açık kalemler yüklenemedi." });
        }
    }

    // UI payload'ındaki kalem listesini repo haritasına çevirir (LineId → teslim miktarı).
    // Boş/null → null döner = tüm açık miktar (tam teslimat). ≤0 miktarlar elenir; tekrar eden
    // LineId'de son değer geçerli.
    private static IReadOnlyDictionary<int, decimal>? BuildDeliverMap(IReadOnlyList<DeliverLineQtyDto>? lines)
        => (lines is { Count: > 0 })
            ? lines.Where(l => l.LineId > 0 && l.Quantity > 0m)
                   .GroupBy(l => l.LineId)
                   .ToDictionary(g => g.Key, g => g.Last().Quantity)
            : null;

    /// <summary>Kısmi teslimat isteği — OrderId + opsiyonel kalem miktarları (gösterim birimi).</summary>
    public sealed record DeliverOrderRequest(int OrderId, IReadOnlyList<DeliverLineQtyDto>? Lines);
    public sealed record DeliverLineQtyDto(int LineId, decimal Quantity);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocJson(int id, CancellationToken ct)
    {
        try
        {
            // İşlem logu: silinen belgenin kimliği + kalem dökümü silmeden ÖNCE okunur.
            // Aynı okuma DocType'a göre doğru form koduyla dinamik izin kontrolü için de
            // kullanılır — statik [PermissionScope(StockIn)] STOCK_OUT/TRANSFER silme işlemlerini
            // de yanlışlıkla StockIn yetkisine bağlıyordu (bkz. CheckStockDocPermissionAsync).
            var (docForAudit, linesForAudit) = await TryGetStockDocForAuditAsync(id, ct);

            if (!await CheckStockDocPermissionAsync(docForAudit?.DocType, new[] { "DELETE_OWN", "DELETE_ALL" }, ct))
                return Json(new { ok = false, error = "Bu belgeyi silmek için yetkiniz bulunmuyor." });

            await _stockDocRepo.DeleteAsync(id, ct);

            if (docForAudit is not null)
                _audit.LogDelete(AuditEntityFor(docForAudit.DocType), id, docForAudit.DocNo,
                    detail: linesForAudit is { Count: > 0 } ? $"{linesForAudit.Count} kalem" : null,
                    snapshot: BuildDeletedLineSnapshot(linesForAudit));
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLocationsJson(CancellationToken ct)
    {
        var locs    = await _logisticsService.GetLocationsAsync(ct);
        var active  = locs.Where(l => l.IsActive).ToList();
        // Alt lokasyonu olan (parent) lokasyonlar seçilemez — yalnızca yaprak (leaf) lokasyonlar döner
        var parentIds = active.Where(l => l.ParentId.HasValue)
                              .Select(l => l.ParentId!.Value)
                              .ToHashSet();
        return Json(active
            .Where(l => !parentIds.Contains(l.Id))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationName)
            .Select(l => new { l.Id, l.LocationCode, l.LocationName }));
    }

    // AR-GE/ÜR-GE proje listesi — ambar çıkış fişinde "Proje" seçici için (sarf malzeme takibi).
    [HttpGet]
    public async Task<IActionResult> GetArgeProjectsJson(CancellationToken ct)
    {
        var projects = await _argeService.ListAsync(null, null, ct);
        return Json(projects.Select(p => new
        {
            id = p.DocumentId,
            name = p.Name,
            projectType = p.ProjectType,
            label = (p.ProjectType == 1 ? "ÜR-GE" : "AR-GE") + " · " + p.DocumentNumber + " · " + p.Name,
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialsJson(string? docType, CancellationToken ct)
    {
        var snapshot = await _logisticsService.GetSnapshotAsync(ct);

        // Planlama: bu belge tipinde kilitli malzemeler lookup'ta gösterilmez ("o belgede seçilemesin")
        var lockedIds = string.IsNullOrWhiteSpace(docType)
            ? new HashSet<int>()
            : (await _logisticsService.GetLockedItemIdsByDocTypeAsync(docType, ct)).ToHashSet();
        return Json(snapshot.Items
            .Where(x => x.IsActive && !lockedIds.Contains(x.Id))
            .Select(x => new
            {
                Id = x.Id,
                MaterialCode = x.Code,
                MaterialName = x.Name,
                x.UnitId,
                TrackCombinations = x.Combinations,
                // İzlenebilirlik: grid'in Lot/Seri hücresi (buton aktifliği + modal tipi) için
                TrackSerial = string.Equals(x.TrackingType, "Serial", StringComparison.OrdinalIgnoreCase),
                TrackLot    = string.Equals(x.TrackingType, "Lot", StringComparison.OrdinalIgnoreCase),
                AutoSerial = x.AutoSerial,
            })
            .OrderBy(x => x.MaterialCode));
    }

    // Stoktaki (InStock) seriler — çıkış/transfer seri seçim modalını besler (Seri takibi Faz 2).
    [HttpGet]
    public async Task<IActionResult> GetSerialsJson(int itemId, CancellationToken ct)
    {
        if (itemId <= 0) return Json(Array.Empty<object>());

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.[SerialNo], lot.[LotNo], s.[Created], lot.[ExpiryDate]
            FROM [{_schema}].[ItemSerial] s
            LEFT JOIN [{_schema}].[Lot] lot ON lot.[Id] = s.[LotId]
            WHERE s.[ItemId] = @ItemId AND s.[IsActive] = 1 AND s.[Status] = 1
            ORDER BY s.[SerialNo];
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new
            {
                serialNo = r.GetString(0),
                lotNo = r.IsDBNull(1) ? null : r.GetString(1),
                // FIFO/FEFO otomatik doldurma anahtarları (seri seçim modalı butonları)
                created = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2),      // FIFO: en eski giriş önce
                expiryDate = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3),   // FEFO: en yakın SKT önce
            });
        return Json(result);
    }

    // Sayım satırlarının izlenebilirlik eşlemesi (InventoryCountLine.Id → {serials[], lotBreakdown[]}) —
    // taslak yeniden yüklemede grid satırına seri/lot kırılımını doldurmak için.
    [HttpGet]
    public async Task<IActionResult> GetInventoryLineSerials(int documentId, CancellationToken ct)
    {
        if (documentId <= 0) return Json(new Dictionary<string, object>());
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT l.[Id], l.[Serials], l.[LotBreakdown], l.[SerialBreakdown]
            FROM [{_schema}].[InventoryCountLine] l
            INNER JOIN [{_schema}].[InventoryCount] ic ON ic.[Id] = l.[InventoryCountId]
            WHERE ic.[DocumentId] = @Doc
              AND ((l.[Serials] IS NOT NULL AND LEN(l.[Serials]) > 0)
                   OR (l.[LotBreakdown] IS NOT NULL AND LEN(l.[LotBreakdown]) > 0)
                   OR (l.[SerialBreakdown] IS NOT NULL AND LEN(l.[SerialBreakdown]) > 0));
            """;
        cmd.Parameters.AddWithValue("@Doc", documentId);
        var map = new Dictionary<string, object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var serials = (r.IsDBNull(1) ? "" : r.GetString(1))
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            object[] breakdown = System.Array.Empty<object>();
            if (!r.IsDBNull(2))
            {
                try
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<List<StockLotBreakdownItem>>(r.GetString(2));
                    breakdown = (arr ?? new()).Select(b => (object)new { lotNo = b.LotNo, expiryDate = b.ExpiryDate, description = b.Description, qty = b.Qty }).ToArray();
                }
                catch { }
            }
            // Zengin seri kırılımı (seri=parti): [{serialNo, expiryDate, description, qty}]
            object[] serialBd = System.Array.Empty<object>();
            if (!r.IsDBNull(3))
            {
                try
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<List<CountSerialBreakdownItem>>(r.GetString(3));
                    serialBd = (arr ?? new()).Select(b => (object)new { serialNo = b.SerialNo, expiryDate = b.ExpiryDate, description = b.Description, qty = b.Qty }).ToArray();
                }
                catch { }
            }
            map[r.GetInt32(0).ToString()] = new { serials, lotBreakdown = breakdown, serialBreakdown = serialBd };
        }
        return Json(map);
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialUnitsJson(string materialCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return Json(Array.Empty<object>());

        var allUnits = await _logisticsService.GetUnitsAsync(ct);
        var unitById = allUnits.Where(u => u.IsActive).ToDictionary(u => u.Id);

        var snapshot = await _logisticsService.GetSnapshotAsync(ct);
        var item = snapshot.Items.FirstOrDefault(x =>
            string.Equals(x.Code, materialCode, StringComparison.OrdinalIgnoreCase));
        if (item == null) return Json(Array.Empty<object>());

        var seen = new HashSet<int>();
        var result = new List<object>();

        void AddUnit(int? id)
        {
            if (!id.HasValue || !seen.Add(id.Value)) return;
            if (!unitById.TryGetValue(id.Value, out var u)) return;
            result.Add(new { id = u.Id, code = u.Code, name = u.Name });
        }

        AddUnit(item.UnitId);
        var conversions = await _logisticsService.GetItemUnitsAsync(item.Id, ct);
        foreach (var cv in conversions) AddUnit(cv.UnitId);

        return Json(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Board config builders
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<object> BuildTransferBoardConfigAsync(CancellationToken ct)
    {
        var docs = await _stockDocRepo.GetByTypeAsync("TRANSFER", ct);
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget("w_depo",  "Depo",  "text"),
            SmartBoardFilterHelpers.MakeStdWidget("w_kalem", "Kalem", "numeric"),
        };

        var entities = docs.Select(d => new
        {
            id       = d.Id,
            title    = d.DocNo,
            subtitle = d.DocDate.ToString("dd.MM.yyyy", tr),
            description = BuildTransferDesc(d),
            imageUrl    = (string?)null,
            statusBadge = (object?)null,
            widgets = new object[]
            {
                new { id = "w_depo",  type = "data", dataType = "text", label = "Depo",
                      value = "Çoklu Depo", detail = (string?)null, color = "indigo" },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem",
                      value = d.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem", color = "slate" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/Warehouse/TransferEdit?id={d.Id}", hideButton = true,
            },
            secondaryAction = new
            {
                label = "Sil", icon = "Trash2",
                apiUrl = $"/Warehouse/DeleteDocJson?id={d.Id}",
                apiMethod = "POST",
                confirm = $"Bu transfer belgesini silmek istediğinizden emin misiniz? ({d.DocNo})",
            },
        }).ToList();

        return new
        {
            boardKey          = "warehouse-transfer",
            title             = "Transfer Belgesi",
            subtitle          = $"{entities.Count} belge",
            icon              = "ArrowLeftRight",
            iconColor         = "indigo",
            refreshUrl        = "/Warehouse/TransferBoardConfig",
            searchPlaceholder = "Belge no, depo ara…",
            emptyText         = "Henüz transfer belgesi oluşturulmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Transfer", icon = "Plus", variant = "primary",
                      url = "/Warehouse/TransferEdit" },
            },
            masterWidgets,
            entities,
        };
    }

    /// <summary>
    /// type: "in" → yalnız Ambar Giriş, "out" → yalnız Ambar Çıkış, null → birleşik liste
    /// (eski /Warehouse/StockEntry linki için backward-compat). Her mod kendi boardKey'ini
    /// taşır — widget/filtre tercihleri localStorage'da ayrı saklanır.
    /// </summary>
    private async Task<object> BuildStockEntryBoardConfigAsync(string? type, CancellationToken ct)
    {
        var isIn  = string.Equals(type, "in",  StringComparison.OrdinalIgnoreCase);
        var isOut = string.Equals(type, "out", StringComparison.OrdinalIgnoreCase);
        string[] docTypes = isIn ? ["STOCK_IN"] : isOut ? ["STOCK_OUT"] : ["STOCK_IN", "STOCK_OUT"];
        var docs = await _stockDocRepo.GetByTypesAsync(docTypes, ct);
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var typeOptions = SmartBoardFilterHelpers.ToOptionsList(new[] { "Giriş", "Çıkış" });
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeOptionsWidget("w_type",  "Hareket", typeOptions),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_loc",   "Depo",    "text"),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_kalem", "Kalem",   "numeric"),
        };

        var entities = docs.Select(d => new
        {
            id       = d.Id,
            title    = d.DocNo,
            subtitle = d.DocDate.ToString("dd.MM.yyyy", tr),
            description = BuildStockEntryDesc(d),
            imageUrl    = (string?)null,
            statusBadge = (object?)null,
            widgets = new object[]
            {
                new { id = "w_type", type = "data", dataType = "options", label = "Hareket",
                      value = d.DocType == "STOCK_IN" ? "Giriş" : "Çıkış",
                      detail = (string?)null,
                      color = d.DocType == "STOCK_IN" ? "emerald" : "rose" },
                new { id = "w_loc", type = "data", dataType = "text", label = "Depo",
                      value = (d.DocType == "STOCK_IN" ? d.ToLocationName : d.FromLocationName) ?? "-",
                      detail = (string?)null, color = "indigo" },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem",
                      value = d.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem", color = "slate" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/Warehouse/StockEntryEdit?id={d.Id}", hideButton = true,
            },
            secondaryAction = new
            {
                label = "Sil", icon = "Trash2",
                apiUrl = $"/Warehouse/DeleteDocJson?id={d.Id}",
                apiMethod = "POST",
                confirm = $"Bu belgeyi silmek istediğinizden emin misiniz? ({d.DocNo})",
            },
        }).ToList();

        var actions = isIn
            ? new object[] { new { id = "new-in",  label = "Giriş Belgesi", icon = "Plus", variant = "primary",
                                   url = "/Warehouse/StockEntryEdit?type=in" } }
            : isOut
            ? new object[] { new { id = "new-out", label = "Çıkış Belgesi", icon = "Plus", variant = "primary",
                                   url = "/Warehouse/StockEntryEdit?type=out" } }
            : new object[]
            {
                new { id = "new-in",  label = "Giriş Belgesi",  icon = "Plus", variant = "primary",
                      url = "/Warehouse/StockEntryEdit?type=in" },
                new { id = "new-out", label = "Çıkış Belgesi",  icon = "Plus", variant = "secondary",
                      url = "/Warehouse/StockEntryEdit?type=out" },
            };

        return new
        {
            boardKey          = isIn ? "warehouse-stock-in" : isOut ? "warehouse-stock-out" : "warehouse-stock-entry",
            title             = isIn ? "Ambar Giriş" : isOut ? "Ambar Çıkış" : "Ambar Giriş / Çıkış",
            subtitle          = $"{entities.Count} belge",
            icon              = isIn ? "PackagePlus" : isOut ? "PackageMinus" : "Warehouse",
            iconColor         = isOut ? "rose" : "emerald",
            refreshUrl        = isIn ? "/Warehouse/StockInBoardConfig"
                              : isOut ? "/Warehouse/StockOutBoardConfig"
                              : "/Warehouse/StockEntryBoardConfig",
            searchPlaceholder = "Belge no ara…",
            emptyText         = isIn ? "Henüz giriş belgesi oluşturulmamış"
                              : isOut ? "Henüz çıkış belgesi oluşturulmamış"
                              : "Henüz ambar belgesi oluşturulmamış",
            actions,
            masterWidgets,
            entities,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Line grid config — CalibraLineItemsGrid için server-side JSON
    // ═══════════════════════════════════════════════════════════════════════

    // internal: ProductionController üretim sarfı modalı aynı STOCK_OUT kolon setini
    // (lookup + kombinasyon + seri-pick + lot + miktar) yeniden kullanır (2026-07-10).
    internal static object BuildLineGridConfig(string docType, IReadOnlyCollection<FieldGuideBindingDto>? bindings = null)
    {
        var isTransfer = docType == "TRANSFER";
        var bindingMap = (bindings ?? [])
            .ToDictionary(b => b.FieldKey, b => b, StringComparer.OrdinalIgnoreCase);
        bindingMap.TryGetValue("materialCode", out var matBinding);
        var lineFormCode = docType switch
        {
            "TRANSFER"  => FormCodes.TransferLines,
            "STOCK_OUT" => FormCodes.StockOutLines,
            _           => FormCodes.StockInLines,
        };
        // Planlama: kilit sorgusu için sistem belge tipi kodu (depo_giris/depo_cikis/depo_transfer)
        var lockDocType = docType switch
        {
            "TRANSFER"  => "depo_transfer",
            "STOCK_OUT" => "depo_cikis",
            _           => "depo_giris",
        };

        var locationCols = isTransfer ? new object[]
        {
            new
            {
                key             = "fromLocationId",
                label           = "Kaynak Depo",
                type            = "select",
                optionsUrl      = "/Warehouse/GetLocationsJson",
                optionsValueKey = "id",
                optionsLabelKey = "locationName",
                width           = 160,
                align           = "left",
                icon            = "Warehouse",
            },
            new
            {
                key             = "toLocationId",
                label           = "Hedef Depo",
                type            = "select",
                optionsUrl      = "/Warehouse/GetLocationsJson",
                optionsValueKey = "id",
                optionsLabelKey = "locationName",
                width           = 160,
                align           = "left",
                icon            = "ArrowRight",
            },
        } : Array.Empty<object>();

        var baseCols = new object[]
        {
            new
            {
                key            = "materialCode",
                label          = "Malzeme Kodu",
                type           = "text-lookup",
                guideCode      = matBinding?.GuideCode,
                filterJson     = matBinding?.FilterJson,
                formCode       = lineFormCode,
                formatJson     = matBinding?.FormatJson,
                lookupUrl      = matBinding == null ? $"/Warehouse/GetMaterialsJson?docType={lockDocType}" : (string?)null,
                lookupValueKey = "materialCode",
                lookupLabelKey = "materialName",
                lookupFillMap  = new Dictionary<string, string>
                {
                    ["materialName"]      = "materialName",
                    ["stockCardId"]       = "id",
                    ["trackCombinations"] = "trackCombinations",
                    ["unitId"]            = "unitId",
                    ["trackSerial"]       = "trackSerial",
                    ["autoSerial"]        = "autoSerial",
                },
                width    = 200,
                required = true,
                align    = "left",
                icon     = "Hash",
            },
            new
            {
                key      = "materialName",
                label    = "Malzeme Adı",
                type     = "text",
                width    = "flex",
                @readonly = true,
                align    = "left",
                icon     = "FileText",
            },
            new
            {
                key            = "combinationCode",
                label          = "Kombinasyon",
                type           = "combination-lookup",
                width          = 130,
                align          = "center",
                icon           = "CircleDot",
                visibleWhenKey = "trackCombinations",
            },
            new
            {
                // Seri (Seri takibi Faz 2): seri-takipli stokta buton "n/adet" sayacı gösterir,
                // modal açar. Giriş: serbest seri girişi (AutoSerial stokta boş bırakılabilir —
                // sunucu üretir). Çıkış/transfer: stoktaki serilerden seçim (GetSerialsJson).
                // Zorunluluk/tekillik/durum kontrolleri server-side (ResolveSerialsForLineAsync).
                key        = "serials",
                label      = "Seri",
                type       = "serial-entry",
                serialMode = docType == "STOCK_IN" ? "entry" : "pick",
                serialsUrl = "/Warehouse/GetSerialsJson?itemId={stockCardId}",
                width      = 90,
                align      = "center",
                icon       = "Barcode",
            },
            new
            {
                key             = "unitId",
                label           = "Birim",
                type            = "select",
                optionsUrl      = "/Warehouse/GetMaterialUnitsJson?materialCode={materialCode}",
                optionsValueKey = "id",
                optionsLabelKey = "name",
                autoSelectFirst = true,
                width           = 80,
                align           = "center",
                icon            = "Ruler",
            },
            new
            {
                // Lot / Parti (Lot takibi Faz 1): lot-takipli stokta (TrackingType='Lot')
                // zorunluluk + mevcut-lot kontrolü server-side yapılır (SqlStockDocRepository.
                // ResolveLotForLineAsync); takipsiz stokta serbest metin olarak korunur.
                // text-lookup: mevcut lot bakiyelerini FEFO sıralı önerir (transfer satırında
                // kaynak depoya göre; satırda depo yoksa tüm depolar) — serbest yazım da geçerli,
                // eşleşmeyen değer ham kaydedilir (girişte yeni lot yaratır).
                key            = "lotNo",
                label          = "Lot / Parti",
                type           = "text-lookup",
                lookupUrl      = "/Warehouse/GetLotBalancesJson?itemId={stockCardId}&locationId={fromLocationId}",
                lookupValueKey = "lotNo",
                lookupLabelKey = "label",
                placeholder    = "Lot no",
                width          = 130,
                align          = "left",
                icon           = "Tag",
            },
            new
            {
                key       = "quantity",
                label     = "Miktar",
                type      = "number",
                width     = 100,
                precision = 2,
                min       = 0,
                align     = "right",
                icon      = "Sigma",
            },
            // Satır bazlı "Notlar" sütunu kaldırıldı (2026-07-07): belge notu üst
            // bilgideki Notlar alanında tutulur. Kalem bazlı not gerekirse Admin →
            // Widget Tanımları ile bu form code'a ("depo_giris/cikis/transfer_lines")
            // alan tanımlanır — hardcoded kolon eklenmez.
        };

        return new
        {
            schemaVersion = "v1",
            columns = locationCols.Concat(baseCols).ToArray(),
        };
    }

    private async Task<object> BuildInventoryBoardConfigAsync(CancellationToken ct)
    {
        var docs = await _stockDocRepo.GetByTypeAsync("INVENTORY_COUNT", ct);
        var appliedIds = await _inventoryCountRepo.GetAppliedDocumentIdsAsync(ct);
        var tr   = CultureInfo.GetCultureInfo("tr-TR");
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget("w_depo",  "Depo",  "text"),
            SmartBoardFilterHelpers.MakeStdWidget("w_kalem", "Kalem", "numeric"),
        };

        var entities = docs.Select(d =>
        {
            // Yansıtılmış (Applied) sayım immutable: "Yansıtıldı" rozeti göster, Sil'i gizle.
            var isApplied = appliedIds.Contains(d.Id);
            return new
            {
                id          = d.Id,
                title       = d.DocNo,
                subtitle    = d.DocDate.ToString("dd.MM.yyyy", tr),
                description = d.FromLocationName ?? "",
                imageUrl    = (string?)null,
                statusBadge = isApplied ? (object?)new { label = "Yansıtıldı", color = "emerald" } : null,
                widgets = new object[]
                {
                    new { id = "w_depo",  type = "data", dataType = "text",    label = "Depo",
                          value = d.FromLocationName ?? "-", detail = (string?)null, color = "violet" },
                    new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem",
                          value = d.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem", color = "slate" },
                },
                primaryAction = new
                {
                    label = isApplied ? "Görüntüle" : "Düzenle", icon = "Edit", color = "amber",
                    url   = $"/Warehouse/InventoryEdit?id={d.Id}", hideButton = true,
                },
                // Applied → "Yansıtma İptali" (unpost, taslağa döner). Draft → "Sil".
                secondaryAction = isApplied
                    ? (object?)new
                    {
                        label     = "Yansıtma İptali", icon = "RotateCcw",
                        apiUrl    = $"/Warehouse/RevertInventoryJson?id={d.Id}",
                        apiMethod = "POST",
                        confirm   = $"Yansıtma iptal edilsin mi? Bu sayımın stok hareketleri geri alınacak ve belge taslağa dönecek. ({d.DocNo})",
                    }
                    : new
                    {
                        label     = "Sil", icon = "Trash2",
                        apiUrl    = $"/Warehouse/DeleteInventoryJson?id={d.Id}",
                        apiMethod = "POST",
                        confirm   = $"Bu sayım belgesini silmek istediğinizden emin misiniz? ({d.DocNo})",
                    },
            };
        }).ToList();

        return new
        {
            boardKey          = "warehouse-inventory",
            title             = "Stok Sayımı",
            subtitle          = $"{entities.Count} belge",
            icon              = "ClipboardCheck",
            iconColor         = "violet",
            refreshUrl        = "/Warehouse/InventoryBoardConfig",
            searchPlaceholder = "Belge no, depo ara…",
            emptyText         = "Henüz sayım belgesi oluşturulmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Sayım", icon = "Plus", variant = "primary",
                      url = "/Warehouse/InventoryEdit" },
            },
            masterWidgets,
            entities,
        };
    }

    private static object BuildInventoryLineGridConfig(IReadOnlyCollection<FieldGuideBindingDto>? bindings = null)
    {
        var bindingMap = (bindings ?? [])
            .ToDictionary(b => b.FieldKey, b => b, StringComparer.OrdinalIgnoreCase);
        bindingMap.TryGetValue("materialCode", out var matBinding);

        return new
        {
            schemaVersion = "v1",
            // Ondalık: lineFormCode yok — sayım ailesinin kök kodu açıkça verilmezse
            // grid SALES_QUOTE_LINES varsayılanına düşer (yanlış aile).
            decimalFormCode = FormCodes.InventoryCount,
            columns = new object[]
            {
            new
            {
                key            = "materialCode",
                label          = "Malzeme Kodu",
                type           = "text-lookup",
                guideCode      = matBinding?.GuideCode,
                filterJson     = matBinding?.FilterJson,
                formCode       = "INVENTORY_COUNT_LINES",
                formatJson     = matBinding?.FormatJson,
                lookupUrl      = matBinding == null ? "/Warehouse/GetMaterialsJson?docType=sayim" : (string?)null,
                lookupValueKey = "materialCode",
                lookupLabelKey = "materialName",
                lookupFillMap  = new Dictionary<string, string>
                {
                    ["materialName"]      = "materialName",
                    ["stockCardId"]       = "id",
                    ["trackCombinations"] = "trackCombinations",
                    ["trackSerial"]       = "trackSerial",
                    ["trackLot"]          = "trackLot",
                    ["autoSerial"]        = "autoSerial",
                    ["unitId"]            = "unitId",
                },
                width    = 200,
                required = matBinding?.IsRequired ?? false,
                align    = "left",
                icon     = "Hash",
            },
            new
            {
                key      = "materialName",
                label    = "Malzeme Adı",
                type     = "text",
                width    = "flex",
                @readonly = true,
                align    = "left",
                icon     = "FileText",
            },
            new
            {
                key            = "combinationCode",
                label          = "Kombinasyon",
                type           = "combination-lookup",
                width          = 130,
                align          = "center",
                icon           = "CircleDot",
                visibleWhenKey = "trackCombinations",
            },
            new
            {
                key             = "unitId",
                label           = "Birim",
                type            = "select",
                optionsUrl      = "/Warehouse/GetMaterialUnitsJson?materialCode={materialCode}",
                optionsValueKey = "id",
                optionsLabelKey = "name",
                autoSelectFirst = true,
                width           = 80,
                align           = "center",
                icon            = "Ruler",
            },
            new
            {
                // İzlenebilirlik (Sayım) — tek "Lot / Seri" butonu → amaca özel modal.
                // Seri-takipli: seri tara/gir (adet = Sayılan Miktar). Lot-takipli: çoklu lot kırılımı
                // (Lot No + miktar), toplam = Sayılan Miktar. Zorunluluk server-side (SaveInventoryCountAsync).
                key        = "trace",
                label      = "Lot / Seri",
                type       = "trace-entry",
                serialsUrl = "/Warehouse/GetSerialsJson?itemId={stockCardId}",
                lotUrl     = "/Warehouse/GetLotBalancesJson?itemId={stockCardId}&locationId={fromLocationId}",
                width      = 110,
                align      = "center",
                icon       = "Barcode",
            },
            new
            {
                key       = "quantity",
                label     = "Sayılan Miktar",
                type      = "number",
                width     = 130,
                precision = 2,
                min       = 0,
                align     = "right",
                icon      = "Sigma",
            },
            new
            {
                key             = "fromLocationId",
                label           = "Lokasyon",
                type            = "select",
                optionsUrl      = "/Warehouse/GetLocationsJson",
                optionsValueKey = "id",
                optionsLabelKey = "locationName",
                width           = 160,
                align           = "left",
                icon            = "Warehouse",
            },
            // Satır bazlı "Notlar" sütunu kaldırıldı (2026-07-09): belge notu üst bilgideki
            // Notlar alanında tutulur. Kalem bazlı not gerekirse Admin → Widget Tanımları ile
            // "INVENTORY_COUNT_LINES" form code'una Ek Alan tanımlanır — hardcoded kolon eklenmez.
        },
        };
    }

    private static string BuildTransferDesc(StockDocDto d)
        => d.RefNo is { Length: > 0 } r ? $"Ref: {r}" : "";


    private static string BuildStockEntryDesc(StockDocDto d)
    {
        var loc = d.DocType == "STOCK_IN" ? d.ToLocationName : d.FromLocationName;
        return loc ?? "";
    }
}

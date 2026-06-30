using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// SQL View tabanli jenerik Rehber (Lookup) HTTP endpoint'leri.
///
/// PR 3'te admin endpoint'leri (GET /, POST, DELETE) kaldirildi — GuideMas
/// tablosu yonetimi artik gereksiz; UI direkt fiziksel view'lari kullaniyor.
///
///   GET    /api/guides/views                    → cbv_Guide_% view listesi (form modalı için)
///   GET    /api/guides/views/{viewName}/columns → View'in kolon listesi (dinamik dropdown)
///   GET    /api/guides/{guideCode}/schema       → Rehber metadatası (guideCode VEYA viewName)
///   GET    /api/guides/{guideCode}              → Arama + sayfalama (guideCode VEYA viewName)
///   GET    /api/guides/{guideCode}/resolve      → Tek value → display
///   GET    /api/guides/{guideCode}/distinct/{column} → Distinct degerler (filtre cipleri)
/// </summary>
[Authorize]
[ApiController]
[Route("api/guides")]
[PermissionScope(FormCodes.ViewSettings)]
public sealed class GuidesController : ControllerBase
{
    private readonly IGuideService _guideService;
    private readonly IGuideRepository _guideRepository;

    public GuidesController(IGuideService guideService, IGuideRepository guideRepository)
    {
        _guideService = guideService;
        _guideRepository = guideRepository;
    }

    /// <summary>
    /// DB'deki cbv_Guide_% view'larını listele (form modalı dropdown'u için).
    /// PR 2'den itibaren UI'nin tek rehber kaynagi — duplikat YOK.
    /// GET /api/guides/views
    /// </summary>
    [HttpGet("views")]
    public async Task<IActionResult> ListViews(CancellationToken ct)
    {
        try
        {
            var views = await _guideRepository.ListGuideViewsAsync(ct);
            return Ok(views);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// Bir SQL view'ın kolon adlarını döner (dinamik dropdown için)
    /// GET /api/guides/views/{viewName}/columns
    /// </summary>
    [HttpGet("views/{viewName}/columns")]
    public async Task<IActionResult> GetViewColumns(string viewName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            return BadRequest(new { success = false, message = "viewName gerekli" });

        try
        {
            var columns = await _guideRepository.GetViewColumnsAsync(viewName, ct);
            return Ok(columns);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    // ──────────────────────────────────────────────────────
    // Runtime Lookup API (widget renderer tarafından kullanılır)
    // {guideCode} parametresi GuideMas.GuideCode VEYA GuideMas.ViewName kabul eder —
    // SqlGuideRepository.GetByCodeAsync iki kolonda da eslestirme yapar.
    // ──────────────────────────────────────────────────────

    [HttpGet("{guideCode}/schema")]
    public async Task<IActionResult> GetSchema(string guideCode, CancellationToken ct)
    {
        var schema = await _guideService.GetSchemaAsync(guideCode, ct);
        if (schema == null)
            return NotFound(new { success = false, message = $"Rehber bulunamadi: '{guideCode}'" });
        return Ok(schema);
    }

    public sealed record SetGuideDefaultFilterRequest(string? Filter);

    /// <summary>
    /// Rehber bazli varsayilan WHERE filter — bu rehberin kullanildigi tum form
    /// alanlarinda runtime'da otomatik AND ile uygulanir. Filter NULL/bos ise filtre
    /// kaldirilir.
    /// PUT /api/guides/{guideCode}/default-filter   body: { "filter": "TYPID IN (2,3)" }
    /// </summary>
    [HttpPut("{guideCode}/default-filter")]
    public async Task<IActionResult> SetDefaultFilter(
        string guideCode,
        [FromBody] SetGuideDefaultFilterRequest body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(guideCode))
            return BadRequest(new { success = false, message = "Rehber kodu bos olamaz." });
        try
        {
            var affected = await _guideService.SetDefaultFilterAsync(guideCode, body?.Filter, ct);
            if (affected == 0)
                return NotFound(new { success = false, message = $"Rehber bulunamadi: '{guideCode}'" });
            return Ok(new { success = true, affected });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{guideCode}/resolve")]
    public async Task<IActionResult> Resolve(
        string guideCode,
        [FromQuery] string value,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BadRequest(new { success = false, message = "value parametresi bos olamaz." });
        try
        {
            var dto = await _guideService.ResolveAsync(guideCode, value, ct);
            if (dto == null)
                return NotFound(new { success = false, message = "Kayit bulunamadi." });
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Bir kolonun DISTINCT degerlerini doner (max 200) — runtime'da
    /// rehber popup'inda distinct filtre cipleri icin.
    /// GET /api/guides/{guideCode}/distinct/{column}?q=...
    /// q dolu ise sunucu-tarafi LIKE filtresi (Turkish_CI_AI) uygulanir.
    /// </summary>
    [HttpGet("{guideCode}/distinct/{column}")]
    public async Task<IActionResult> GetDistinct(
        string guideCode,
        string column,
        [FromQuery] string? q,
        [FromQuery] string? constraints = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(column))
            return BadRequest(new { success = false, message = "column parametresi bos olamaz." });
        try
        {
            // Constraints — Search ile ayni JSON formati. Distinct popover'i listede
            // gosterilen satirlarla tutarli kalsin diye SearchAsync'e gonderilen ayni
            // WHERE fragment burada da uygulanir (rapor: 2026-05-18 kullanici geri bildirimi).
            IReadOnlyCollection<GuideConstraintDto>? parsedConstraints = null;
            if (!string.IsNullOrWhiteSpace(constraints))
            {
                try
                {
                    parsedConstraints = JsonSerializer.Deserialize<List<GuideConstraintDto>>(
                        constraints,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception parseEx)
                {
                    Console.Error.WriteLine($"[GuideDistinct] Constraint JSON parse hatasi (guide='{guideCode}'): {parseEx.Message}; raw='{constraints}'");
                }
            }

            var values = await _guideService.GetDistinctValuesAsync(guideCode, column, q, ct, parsedConstraints);
            if (values == null)
                return NotFound(new { success = false, message = $"Rehber bulunamadi: '{guideCode}'" });
            return Ok(values);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            Console.Error.WriteLine($"[GuideDistinct] SQL hatasi (guide='{guideCode}', col='{column}'): {sqlEx.Number} {sqlEx.Message}");
            return StatusCode(500, new { success = false, message = sqlEx.Message });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GuideDistinct] Hata: {ex}");
            return StatusCode(500, new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet("{guideCode}")]
    public async Task<IActionResult> Search(
        string guideCode,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sortColumn = null,
        [FromQuery] string? sortDirection = null,
        [FromQuery] string? constraints = null,
        CancellationToken ct = default)
    {
        try
        {
            // Constraints JSON deserialize — hata olursa logla ve kisitsiz devam et.
            // Eski silent-catch frontend'in raw SQL constraint'i ({rawSql,logic} —
            // structural alanlar yok) ile uyumsuzluk durumunu gizliyordu; artik
            // GuideConstraintDto tum alanlari nullable, deserialize toleransli.
            IReadOnlyCollection<GuideConstraintDto>? parsedConstraints = null;
            if (!string.IsNullOrWhiteSpace(constraints))
            {
                try
                {
                    parsedConstraints = JsonSerializer.Deserialize<List<GuideConstraintDto>>(
                        constraints,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    // DEBUG: incoming constraints'i logla — sorun teshisi icin (gecici)
                    Console.WriteLine($"[GuideSearch:DBG] guide='{guideCode}' raw='{constraints}' parsedCount={(parsedConstraints?.Count ?? -1)}");
                    if (parsedConstraints != null)
                    {
                        foreach (var c in parsedConstraints)
                            Console.WriteLine($"  → Field='{c.Field}' Op='{c.Operator}' Val='{c.Value}' Logic='{c.Logic}' RawSql='{c.RawSql}'");
                    }
                }
                catch (Exception parseEx)
                {
                    Console.Error.WriteLine($"[GuideSearch] Constraint JSON parse hatasi (guide='{guideCode}'): {parseEx.Message}; raw='{constraints}'");
                }
            }

            var result = await _guideService.SearchAsync(
                guideCode, search, page, pageSize, sortColumn, sortDirection, ct, parsedConstraints);
            if (result == null)
                return NotFound(new { success = false, message = $"Rehber bulunamadi: '{guideCode}'" });
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            Console.Error.WriteLine($"[GuideSearch] SQL hatasi (guide='{guideCode}'): {sqlEx.Number} {sqlEx.Message}");
            return StatusCode(500, new
            {
                success = false,
                message = $"SQL hatasi ({sqlEx.Number}): {sqlEx.Message}",
                guide = guideCode
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GuideSearch] Hata (guide='{guideCode}'): {ex}");
            return StatusCode(500, new
            {
                success = false,
                message = "Islem sirasinda bir hata olustu.",
                guide = guideCode,
                type = ex.GetType().Name
            });
        }
    }
}

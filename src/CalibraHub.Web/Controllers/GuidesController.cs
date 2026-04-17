using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// SQL View tabanli jenerik Rehber (Lookup) HTTP endpoint'leri.
///
///   GET    /api/guides                          → Rehber katalogu (liste)
///   GET    /api/guides/views                    → cbv_Guide_% view listesi (form modalı için)
///   GET    /api/guides/views/{viewName}/columns → View'in kolon listesi (dinamik dropdown)
///   POST   /api/guides                          → Yeni/güncelle rehber tanımı
///   DELETE /api/guides/{id}                     → Soft-delete
///   GET    /api/guides/{guideCode}/schema       → Rehber metadatası
///   GET    /api/guides/{guideCode}              → Arama + sayfalama
///   GET    /api/guides/{guideCode}/resolve      → Tek value → display
/// </summary>
[ApiController]
[Route("api/guides")]
public sealed class GuidesController : ControllerBase
{
    private readonly IGuideService _guideService;
    private readonly IGuideRepository _guideRepository;

    public GuidesController(IGuideService guideService, IGuideRepository guideRepository)
    {
        _guideService = guideService;
        _guideRepository = guideRepository;
    }

    // ──────────────────────────────────────────────────────
    // Rehber Merkezi Admin API
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Tüm aktif rehberlerin kataloğu (liste sayfası için)
    /// GET /api/guides
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var catalog = await _guideService.GetCatalogAsync(ct);
        return Ok(catalog);
    }

    /// <summary>
    /// DB'deki cbv_Guide_% view'larını listele (form modalı dropdown'u için)
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
            return StatusCode(500, new { success = false, message = ex.Message });
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
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Rehber ekle (id=0) veya güncelle (id>0)
    /// POST /api/guides
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertGuideRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "Geçersiz istek." });

        if (string.IsNullOrWhiteSpace(request.GuideLabel))
            return BadRequest(new { success = false, message = "Rehber adı zorunlu." });
        if (string.IsNullOrWhiteSpace(request.ViewName))
            return BadRequest(new { success = false, message = "SQL View adı zorunlu." });
        if (string.IsNullOrWhiteSpace(request.ValueColumn))
            return BadRequest(new { success = false, message = "Değer kolonu zorunlu." });
        if (string.IsNullOrWhiteSpace(request.DisplayColumn))
            return BadRequest(new { success = false, message = "Gösterim kolonu zorunlu." });

        try
        {
            var id = await _guideRepository.UpsertAsync(request, ct);
            return Ok(new { success = true, id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Rehberi devre dışı bırak (soft-delete)
    /// DELETE /api/guides/{id}
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Geçersiz id." });

        try
        {
            await _guideRepository.DeleteAsync(id, ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ──────────────────────────────────────────────────────
    // Runtime Lookup API (widget renderer tarafından kullanılır)
    // ──────────────────────────────────────────────────────

    [HttpGet("{guideCode}/schema")]
    public async Task<IActionResult> GetSchema(string guideCode, CancellationToken ct)
    {
        var schema = await _guideService.GetSchemaAsync(guideCode, ct);
        if (schema == null)
            return NotFound(new { success = false, message = $"Rehber bulunamadi: '{guideCode}'" });
        return Ok(schema);
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
            // Constraints JSON deserialize — hata olursa sessizce null
            IReadOnlyCollection<GuideConstraintDto>? parsedConstraints = null;
            if (!string.IsNullOrWhiteSpace(constraints))
            {
                try
                {
                    parsedConstraints = JsonSerializer.Deserialize<List<GuideConstraintDto>>(
                        constraints,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { /* Geçersiz JSON → kısıtsız devam */ }
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
                message = ex.Message,
                guide = guideCode,
                type = ex.GetType().Name
            });
        }
    }
}

using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// SQL View Yönetimi — CLAUDE.md "SQL View Yönetimi — Kontrollü İstisna" (2026-07-17, Faz 1).
/// Mevcut program view'larını (join/hesaplanan alan ile) genişletme + sıfırdan özel view kurma.
///
/// SystemAdmin-only: DatabaseMetadataController/LegacyMigrationController ile BİREBİR aynı
/// erişim deseni ([Authorize] + [PermissionScope(FormCodes.SetupDefinitions)]). Bu araç
/// CREATE OR ALTER VIEW çalıştırabildiği ve serbest SQL kabul ettiği için tehlikeli dev/sistem
/// araçları kovasındadır — admin/DepartmentManager'a AÇILMAZ (bkz. PermissionService.CheckAsync).
///
/// Endpoint'ler:
///   GET  /api/view-builder/views                                 → sys.views + override durumu
///   GET  /api/view-builder/views/{viewName}                       → mevcut SQL (OBJECT_DEFINITION) + override metadata
///   POST /api/view-builder/preview                                → örnek satır + kolonlar + kartezyen sinyali (kalıcı değişiklik yok)
///   POST /api/view-builder/apply                                  → kaydet + CREATE OR ALTER VIEW uygula
///   POST /api/view-builder/revert/{viewDefinitionId}              → en son revizyona geri al
///   GET  /api/view-builder/schema/tables                          → tablo/view listesi (görsel kurucu dropdown'ı)
///   GET  /api/view-builder/schema/tables/{schema}/{name}/columns  → bir tablonun kolonları
/// </summary>
[Authorize]
[PermissionScope(FormCodes.SetupDefinitions)]
[ApiController]
[Route("api/view-builder")]
[IgnoreAntiforgeryToken]
public sealed class ViewBuilderController : ControllerBase
{
    private readonly IViewBuilderService _service;
    private readonly ILogger<ViewBuilderController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public ViewBuilderController(IViewBuilderService service, ILogger<ViewBuilderController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // GET /api/view-builder/views
    [HttpGet("views")]
    public async Task<IActionResult> GetViews(CancellationToken ct)
    {
        try
        {
            var views = await _service.ListViewsAsync(ct);
            return new JsonResult(views, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "View listesi alınırken hata");
            return StatusCode(500, new { success = false, message = "View listesi alınamadı." });
        }
    }

    // GET /api/view-builder/views/{viewName}
    [HttpGet("views/{viewName}")]
    public async Task<IActionResult> GetViewDetail(string viewName, CancellationToken ct)
    {
        try
        {
            var detail = await _service.GetViewDetailAsync(viewName, ct);
            if (detail is null) return NotFound(new { success = false, message = "View bulunamadı." });
            return new JsonResult(detail, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "View tanımı alınırken hata: {ViewName}", viewName);
            return StatusCode(500, new { success = false, message = "View tanımı alınamadı." });
        }
    }

    // POST /api/view-builder/preview
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewViewRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { success = false, message = "İstek gövdesi boş." });
        try
        {
            var result = await _service.PreviewAsync(request, ct);
            return new JsonResult(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "View önizleme sırasında hata");
            return StatusCode(500, new { success = false, message = "Önizleme sırasında beklenmeyen bir hata oluştu." });
        }
    }

    // POST /api/view-builder/apply
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] ApplyViewRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { success = false, message = "İstek gövdesi boş." });
        try
        {
            var username = User.Identity?.Name ?? "system";
            var result = await _service.ApplyAsync(request, username, ct);
            return new JsonResult(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "View uygulama sırasında hata: {ViewName}", request.ViewName);
            return StatusCode(500, new { success = false, message = "Uygulama sırasında beklenmeyen bir hata oluştu." });
        }
    }

    // POST /api/view-builder/revert/{viewDefinitionId}
    [HttpPost("revert/{viewDefinitionId:int}")]
    public async Task<IActionResult> Revert(int viewDefinitionId, CancellationToken ct)
    {
        try
        {
            var username = User.Identity?.Name ?? "system";
            var result = await _service.RevertAsync(viewDefinitionId, username, ct);
            return new JsonResult(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "View geri alma sırasında hata: {Id}", viewDefinitionId);
            return StatusCode(500, new { success = false, message = "Geri alma sırasında beklenmeyen bir hata oluştu." });
        }
    }

    // GET /api/view-builder/schema/tables
    [HttpGet("schema/tables")]
    public async Task<IActionResult> GetSchemaTables(CancellationToken ct)
    {
        try
        {
            var tables = await _service.GetSchemaObjectsAsync(ct);
            return new JsonResult(tables, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şema tablo listesi alınırken hata");
            return StatusCode(500, new { success = false, message = "Tablo listesi alınamadı." });
        }
    }

    // GET /api/view-builder/schema/tables/{schema}/{name}/columns
    [HttpGet("schema/tables/{schema}/{name}/columns")]
    public async Task<IActionResult> GetSchemaColumns(string schema, string name, CancellationToken ct)
    {
        try
        {
            var columns = await _service.GetSchemaColumnsAsync(schema, name, ct);
            return new JsonResult(columns, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şema kolon listesi alınırken hata: {Schema}.{Name}", schema, name);
            return StatusCode(500, new { success = false, message = "Kolon listesi alınamadı." });
        }
    }
}

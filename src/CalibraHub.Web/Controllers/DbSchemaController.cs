using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Aktif sirket DB'sinin fiziksel sema haritasini gosterir.
/// Sadece metadata (sys.* introspection); ornek veri/PII OKUNMAZ.
/// </summary>
[Authorize]
[Route("admin/db-schema")]
public sealed class DbSchemaController : Controller
{
    private readonly IDbSchemaService _service;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DbSchemaController(IDbSchemaService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public IActionResult Index() => View("~/Views/Admin/DbSchema.cshtml");

    [HttpGet("api/tables")]
    public async Task<IActionResult> GetTables(CancellationToken ct)
    {
        var tables = await _service.GetTablesAsync(ct);
        return Json(tables, JsonOptions);
    }

    [HttpGet("api/tables/{schema}/{name}")]
    public async Task<IActionResult> GetTableDetail(string schema, string name, CancellationToken ct)
    {
        var detail = await _service.GetTableDetailAsync(schema, name, ct);
        if (detail is null) return NotFound();
        return Json(detail, JsonOptions);
    }

    [HttpGet("api/views")]
    public async Task<IActionResult> GetViews(CancellationToken ct)
    {
        var views = await _service.GetViewsAsync(ct);
        return Json(views, JsonOptions);
    }

    [HttpPut("api/views/{name}/description")]
    public async Task<IActionResult> SaveViewDescription(string name, [FromBody] SaveViewDescriptionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "View adı boş olamaz." });
        var userName = User.Identity?.Name ?? "system";
        await _service.SaveViewDescriptionAsync(name, request.Description?.Trim(), userName, ct);
        return Ok(new { ok = true });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string format, CancellationToken ct)
    {
        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        switch (normalized)
        {
            case "csv":
            {
                var body = await _service.BuildCsvAsync(ct);
                return File(System.Text.Encoding.UTF8.GetBytes(body), "text/csv", $"db-schema-{stamp}.csv");
            }
            case "json":
            {
                var tables = await _service.GetTablesAsync(ct);
                var full = new List<object>();
                foreach (var t in tables)
                {
                    var detail = await _service.GetTableDetailAsync(t.Schema, t.Name, ct);
                    if (detail is not null) full.Add(detail);
                }
                var json = JsonSerializer.Serialize(full, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
                return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"db-schema-{stamp}.json");
            }
            case "mermaid":
            {
                var body = await _service.BuildMermaidErAsync(ct);
                return File(System.Text.Encoding.UTF8.GetBytes(body), "text/plain", $"db-schema-{stamp}.mmd");
            }
            case "markdown":
            case "md":
            {
                var body = await _service.BuildMarkdownAsync(ct);
                return File(System.Text.Encoding.UTF8.GetBytes(body), "text/markdown", $"db-schema-{stamp}.md");
            }
            default:
                return BadRequest(new { error = "Desteklenmeyen format. Gecerli: csv, json, mermaid, markdown." });
        }
    }
}

public sealed record SaveViewDescriptionRequest(string? Description);

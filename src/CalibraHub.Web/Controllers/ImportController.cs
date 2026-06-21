using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-06-20 — Şablon-tabanlı içe aktarım (AI'sız). Cari pilotu.
///
/// Razor:
///   GET  /Import                          → React ekranı (şablon yönetimi + içe aktarım sihirbazı)
///
/// JSON API (React tüketir):
///   GET  /Import/api/target-fields        → hedef alan kataloğu (entity=CONTACT)
///   GET  /Import/api/templates            → şablon listesi
///   GET  /Import/api/templates/{id}       → şablon detay
///   POST /Import/api/templates/save       → kaydet (SaveImportTemplateRequest)
///   POST /Import/api/templates/delete/{id}
///   POST /Import/api/templates/toggle/{id}
///   POST /Import/api/read-headers         → dosya yükle → sayfa+başlık+örnek satır
///   POST /Import/api/preview              → dosya + şablon → satır doğrulama (yazmaz)
///   POST /Import/api/commit               → dosya + şablon → kayıt + rapor
/// </summary>
[Authorize]
public sealed class ImportController : Controller
{
    private const long MaxUploadBytes = 12L * 1024 * 1024;   // 12 MB
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IImportService _service;

    public ImportController(IImportService service) => _service = service;

    // ── Razor ──────────────────────────────────────────────────────────
    [HttpGet("/Import")]
    public IActionResult Index() => View();

    // ── Hedef alan kataloğu ────────────────────────────────────────────
    [HttpGet("/Import/api/target-fields")]
    public IActionResult TargetFields([FromQuery] string entity = "CONTACT")
    {
        var fields = _service.GetTargetFields(string.IsNullOrWhiteSpace(entity) ? "CONTACT" : entity);
        return Json(new { success = true, fields });
    }

    // ── Boş şablon indir ───────────────────────────────────────────────
    [HttpGet("/Import/api/blank-template")]
    public async Task<IActionResult> BlankTemplate(
        [FromQuery] string entity = "CONTACT",
        [FromQuery] int? templateId = null,
        CancellationToken ct = default)
    {
        var (bytes, fileName) = await _service.BuildBlankTemplateAsync(entity, templateId, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // ── Şablon CRUD ────────────────────────────────────────────────────
    [HttpGet("/Import/api/templates")]
    public async Task<IActionResult> ListTemplates([FromQuery] bool includeInactive = true, CancellationToken ct = default)
    {
        try
        {
            var items = await _service.ListTemplatesAsync(includeInactive, ct);
            return Json(new { success = true, items });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpGet("/Import/api/templates/{id:int}")]
    public async Task<IActionResult> GetTemplate(int id, CancellationToken ct)
    {
        try
        {
            var dto = await _service.GetTemplateAsync(id, ct);
            return dto is null
                ? Json(new { success = false, error = "Şablon bulunamadı." })
                : Json(new { success = true, template = dto });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Import/api/templates/save")]
    public async Task<IActionResult> SaveTemplate([FromBody] SaveImportTemplateRequest request, CancellationToken ct)
    {
        if (request is null) return Json(new { success = false, error = "İstek boş." });
        try
        {
            var (ok, error, id) = await _service.SaveTemplateAsync(request, CurrentUserId(), ct);
            return ok
                ? Json(new { success = true, id })
                : Json(new { success = false, error });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Import/api/templates/delete/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id, CancellationToken ct)
    {
        try { await _service.DeleteTemplateAsync(id, ct); return Json(new { success = true }); }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Import/api/templates/toggle/{id:int}")]
    public async Task<IActionResult> ToggleTemplate(int id, CancellationToken ct)
    {
        try { var active = await _service.ToggleTemplateAsync(id, ct); return Json(new { success = true, isActive = active }); }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    // ── Dosya işlemleri ────────────────────────────────────────────────
    [HttpPost("/Import/api/read-headers")]
    public async Task<IActionResult> ReadHeaders(
        IFormFile? file,
        [FromForm] string? sheetName,
        [FromForm] int headerRowIndex = 1,
        CancellationToken ct = default)
    {
        var (bytes, err) = await ReadUploadAsync(file, ct);
        if (err is not null) return Json(new { success = false, error = err });

        var result = _service.ReadHeaders(bytes!, file!.FileName, sheetName, headerRowIndex);
        return Json(result);
    }

    [HttpPost("/Import/api/preview")]
    public async Task<IActionResult> Preview(IFormFile? file, [FromForm] string? spec, CancellationToken ct)
    {
        var (bytes, err) = await ReadUploadAsync(file, ct);
        if (err is not null) return Json(new { success = false, error = err });

        if (!TryParseSpec(spec, out var template, out var specErr))
            return Json(new { success = false, error = specErr });

        var result = await _service.PreviewAsync(template!, bytes!, file!.FileName, ct);
        return Json(result);
    }

    [HttpPost("/Import/api/commit")]
    public async Task<IActionResult> Commit(IFormFile? file, [FromForm] string? spec, CancellationToken ct)
    {
        var (bytes, err) = await ReadUploadAsync(file, ct);
        if (err is not null) return Json(new { success = false, error = err });

        if (!TryParseSpec(spec, out var template, out var specErr))
            return Json(new { success = false, error = specErr });

        var result = await _service.CommitAsync(template!, bytes!, file!.FileName, CurrentUserId(), ct);
        return Json(result);
    }

    // ── Yardımcılar ────────────────────────────────────────────────────
    private static async Task<(byte[]? Bytes, string? Error)> ReadUploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return (null, "Dosya seçilmedi.");
        if (file.Length > MaxUploadBytes) return (null, $"Dosya çok büyük (max {MaxUploadBytes / (1024 * 1024)} MB).");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return (ms.ToArray(), null);
    }

    private static bool TryParseSpec(string? spec, out SaveImportTemplateRequest? template, out string? error)
    {
        template = null; error = null;
        if (string.IsNullOrWhiteSpace(spec)) { error = "Şablon tanımı boş."; return false; }
        try
        {
            template = JsonSerializer.Deserialize<SaveImportTemplateRequest>(spec, Json);
            if (template is null) { error = "Şablon tanımı çözümlenemedi."; return false; }
            return true;
        }
        catch (Exception ex) { error = "Şablon tanımı geçersiz: " + ex.Message; return false; }
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}

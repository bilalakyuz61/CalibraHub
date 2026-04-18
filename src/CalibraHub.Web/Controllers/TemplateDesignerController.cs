using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public class TemplateDesignerController : Controller
{
    private readonly IDesignTemplateRepository _repo;
    private readonly IReportTemplateRepository _reportTemplateRepo;
    private readonly IWebHostEnvironment _env;

    public TemplateDesignerController(
        IDesignTemplateRepository repo,
        IReportTemplateRepository reportTemplateRepo,
        IWebHostEnvironment env)
    {
        _repo = repo;
        _reportTemplateRepo = reportTemplateRepo;
        _env = env;
    }

    // GET /TemplateDesigner
    // GET /TemplateDesigner?type=document
    // GET /TemplateDesigner?type=document&subType=sales_order
    [HttpGet]
    public async Task<IActionResult> Index(string? type, string? subType, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<DesignTemplate> templates;

        if (!string.IsNullOrWhiteSpace(subType))
            templates = await _repo.GetBySubTypeAsync(subType, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(type))
            templates = await _repo.GetByTypeAsync(type, cancellationToken);
        else
            templates = await _repo.GetAllAsync(cancellationToken);

        ViewBag.FilterType    = type    ?? string.Empty;
        ViewBag.FilterSubType = subType ?? string.Empty;
        return View(templates);
    }

    // GET /TemplateDesigner/Edit/{id}
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var template = await _repo.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();
        return View(template);
    }

    // GET /TemplateDesigner/New?type=document&subType=sales_order
    [HttpGet]
    public IActionResult New(string type = "document", string? subType = null)
    {
        var template = new DesignTemplate
        {
            Id        = Guid.NewGuid(),
            Name      = string.Empty,
            Type      = type,
            SubType   = subType,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        return View("Edit", template);
    }

    // POST /TemplateDesigner/Save  (AJAX JSON)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Şablon adı boş olamaz." });

        var existing = await _repo.GetByIdAsync(request.Id, cancellationToken);
        var template = existing ?? new DesignTemplate
        {
            Id        = request.Id,
            Name      = request.Name,
            Type      = request.Type,
            CreatedAt = DateTime.Now
        };

        template.Name        = request.Name;
        template.Type        = request.Type;
        template.SubType     = string.IsNullOrWhiteSpace(request.SubType) ? null : request.SubType;
        template.Description = request.Description;
        template.HtmlContent = request.HtmlContent;
        template.CssContent  = request.CssContent;
        template.GjsData     = request.GjsData;
        template.JsrContent  = string.IsNullOrWhiteSpace(request.JsrContent) ? null : request.JsrContent;
        template.IsActive    = true;

        await _repo.SaveAsync(template, cancellationToken);
        return Ok(new { id = template.Id, updatedAt = template.UpdatedAt });
    }

    // POST /TemplateDesigner/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _repo.DeleteAsync(id, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    // POST /TemplateDesigner/DeleteJson  (AJAX)
    [HttpPost]
    public async Task<IActionResult> DeleteJson(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _repo.DeleteAsync(id, cancellationToken);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // GET /TemplateDesigner/Preview/{id}
    [HttpGet]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var template = await _repo.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();
        return View(template);
    }

    // ── CalibraHub.Designer companion app API (FastReport .frx) ────────────

    // GET /api/designer/template/{id} — report_templates tablosundan .frx icerigini oku (DB binary)
    [AllowAnonymous]
    [HttpGet("/api/designer/template/{id:int}")]
    public async Task<IActionResult> GetDesignerTemplate(int id, CancellationToken cancellationToken)
    {
        var t = await _reportTemplateRepo.GetByIdAsync(id, cancellationToken);
        if (t is null) return NotFound();

        string? frxContent = null;

        // Oncelik: DB'de saklanan binary icerik
        if (t.FrxContent is { Length: > 0 })
        {
            frxContent = System.Text.Encoding.UTF8.GetString(t.FrxContent);
        }
        // Eski kayitlar icin file-system fallback
        else if (!string.IsNullOrWhiteSpace(t.FrxFilePath))
        {
            var fullPath = Path.Combine(_env.WebRootPath, "Document", t.FrxFilePath);
            if (System.IO.File.Exists(fullPath))
                frxContent = await System.IO.File.ReadAllTextAsync(fullPath, cancellationToken);
        }

        return Ok(new { id = t.Id, name = t.Name, type = "report", FrxContent = frxContent });
    }

    // POST /api/designer/template/{id} — .frx icerigini DB'ye yaz
    [AllowAnonymous]
    [HttpPost("/api/designer/template/{id:int}")]
    public async Task<IActionResult> SaveDesignerTemplate(
        int id, [FromBody] SaveFrxRequest request, CancellationToken cancellationToken)
    {
        var t = await _reportTemplateRepo.GetByIdAsync(id, cancellationToken);
        if (t is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.FrxContent))
            return Ok(new { ok = true });

        // DB'ye binary olarak yaz
        var updated = new CalibraHub.Domain.Entities.ReportTemplate
        {
            Id             = t.Id,
            Name           = t.Name,
            DocumentTypeId = t.DocumentTypeId,
            FrxFilePath    = t.FrxFilePath,  // legacy alan korunur
            FrxContent     = System.Text.Encoding.UTF8.GetBytes(request.FrxContent),
            Description    = t.Description,
            IsDefault      = t.IsDefault,
            IsActive       = t.IsActive,
            CreatedAt      = t.CreatedAt,
        };
        await _reportTemplateRepo.SaveAsync(updated, cancellationToken);

        return Ok(new { ok = true });
    }
}

public sealed record SaveFrxRequest(string FrxContent);

public sealed record SaveTemplateRequest(
    Guid    Id,
    string  Name,
    string  Type,
    string? SubType,
    string? Description,
    string? HtmlContent,
    string? CssContent,
    string? GjsData,
    string? JsrContent
);

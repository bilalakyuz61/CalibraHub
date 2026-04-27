using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Web.Infrastructure.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public class TemplateDesignerController : Controller
{
    private readonly IDesignTemplateRepository _repo;
    private readonly IReportTemplateRepository _reportTemplateRepo;
    private readonly IReportTemplateSourceRepository _sourceRepo;
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IWebHostEnvironment _env;
    private readonly ReportSchemaProvider _schemaProvider;

    public TemplateDesignerController(
        IDesignTemplateRepository repo,
        IReportTemplateRepository reportTemplateRepo,
        IReportTemplateSourceRepository sourceRepo,
        IDocumentTypeRepository docTypeRepo,
        IWebHostEnvironment env,
        ReportSchemaProvider schemaProvider)
    {
        _repo = repo;
        _reportTemplateRepo = reportTemplateRepo;
        _sourceRepo = sourceRepo;
        _docTypeRepo = docTypeRepo;
        _env = env;
        _schemaProvider = schemaProvider;
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

    // GET /TemplateDesigner/FastDesigner
    [HttpGet]
    public IActionResult FastDesigner() => View();


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
    // FrxSanitizer.Strip ile tum connection/sifre node'lari temizlenir ve yerine
    // vw_ReportDocument kolonlarindan uretilen BusinessObjectDataSource enjekte edilir.
    // Boylece Designer'da kullanici sifre/connection string gormez, sadece alan adlarini gorur.
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

        // Coklu source destegi: DB'de kayitli source'lar varsa HEPSINI enjekte et.
        // Yoksa geriye uyumlu tek-source (Belge = template.SqlViewName veya docType default).
        var storedSources = await _sourceRepo.GetByTemplateIdAsync(t.Id, cancellationToken);

        var injectList = new List<(string SourceName, string ViewName)>();
        if (storedSources.Count > 0)
        {
            foreach (var s in storedSources)
            {
                injectList.Add((s.SourceName, s.ViewName));
            }
        }
        else
        {
            // Geriye uyumlu: virtual "Belge" single-source
            var effectiveView = !string.IsNullOrWhiteSpace(t.SqlViewName) ? t.SqlViewName!.Trim() : null;
            if (string.IsNullOrWhiteSpace(effectiveView))
            {
                var docType = await _docTypeRepo.GetByIdAsync(t.DocumentTypeId, cancellationToken);
                effectiveView = docType?.SqlViewName;
            }
            if (string.IsNullOrWhiteSpace(effectiveView)) effectiveView = "vw_ReportDocument";
            injectList.Add(("Belge", effectiveView));
        }

        // Sanitize + inject sema (her source icin ayri)
        if (!string.IsNullOrWhiteSpace(frxContent))
        {
            var working = FrxSanitizer.Strip(frxContent);
            foreach (var (srcName, viewName) in injectList)
            {
                var columns = await _schemaProvider.GetSchemaAsync(viewName, cancellationToken);
                working = FrxSanitizer.InjectBusinessObject(working, srcName, columns);
            }
            // Eski sablonlardaki DataBand DataSource="Belge" gibi kirik
            // referanslari mevcut alias listesine yonlendir → her kalem icin
            // DataBand tekrar etsin.
            var aliases = injectList.Select(s => s.SourceName).ToArray();
            if (aliases.Length > 0)
                working = FrxSanitizer.NormalizeDataSourceReferences(working, aliases, aliases[0]);
            frxContent = working;
        }

        return Ok(new
        {
            id = t.Id,
            name = t.Name,
            type = "report",
            sources = injectList.Select(s => new { name = s.SourceName, view = s.ViewName }).ToArray(),
            FrxContent = frxContent
        });
    }

    // POST /api/designer/template/{id} — .frx icerigini DB'ye yaz
    // FrxSanitizer.Strip ile upload edilen frx'teki baglanti/sifre bilgileri
    // yeniden temizlenir — kullanici manuel olarak eklese bile DB'ye yazilmaz.
    [AllowAnonymous]
    [HttpPost("/api/designer/template/{id:int}")]
    public async Task<IActionResult> SaveDesignerTemplate(
        int id, [FromBody] SaveFrxRequest request, CancellationToken cancellationToken)
    {
        var t = await _reportTemplateRepo.GetByIdAsync(id, cancellationToken);
        if (t is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.FrxContent))
            return Ok(new { ok = true });

        // Kullanici manuel olarak connection eklemeyi denese bile guvenli — strip edilir.
        var cleanFrx = FrxSanitizer.Strip(request.FrxContent);

        // DataBand DataSource referanslarini guncel alias listesine yonlendir
        // (eski "Belge" referansi yeni alias'a remap olur)
        var storedSources = await _sourceRepo.GetByTemplateIdAsync(t.Id, cancellationToken);
        if (storedSources.Count > 0)
        {
            var aliases = storedSources.Select(s => s.SourceName).ToArray();
            cleanFrx = FrxSanitizer.NormalizeDataSourceReferences(cleanFrx, aliases, aliases[0]);
        }

        // DB'ye binary olarak yaz
        var updated = new CalibraHub.Domain.Entities.ReportTemplate
        {
            Id                = t.Id,
            Name              = t.Name,
            DocumentTypeId    = t.DocumentTypeId,
            FrxFilePath       = t.FrxFilePath,  // legacy alan korunur
            FrxContent        = System.Text.Encoding.UTF8.GetBytes(cleanFrx),
            Description       = t.Description,
            IsDefault         = t.IsDefault,
            IsActive          = t.IsActive,
            SqlViewName       = t.SqlViewName,
            KeyColumn         = t.KeyColumn,
            OutputOptionsJson = t.OutputOptionsJson,
            OrderColumn       = t.OrderColumn,
            OrderDirection    = t.OrderDirection,
            CreatedAt         = t.CreatedAt,
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

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class DocumentController : Controller
{
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IReportTemplateRepository _templateRepo;
    private readonly IDocumentGenerationService _generationService;
    private readonly IWebHostEnvironment _env;

    public DocumentController(
        IDocumentTypeRepository docTypeRepo,
        IReportTemplateRepository templateRepo,
        IDocumentGenerationService generationService,
        IWebHostEnvironment env)
    {
        _docTypeRepo       = docTypeRepo;
        _templateRepo      = templateRepo;
        _generationService = generationService;
        _env               = env;
    }

    // ── Liste ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(string? type, CancellationToken ct)
    {
        var docTypes = await _docTypeRepo.GetAllAsync(ct);
        var selectedType = !string.IsNullOrEmpty(type)
            ? docTypes.FirstOrDefault(d => d.Code == type)
            : docTypes.FirstOrDefault();

        ViewBag.DocumentTypes = docTypes;
        ViewBag.SelectedType = selectedType;

        var templates = selectedType is not null
            ? await _templateRepo.GetByDocumentTypeIdAsync(selectedType.Id, ct)
            : Array.Empty<Domain.Entities.ReportTemplate>();

        ViewBag.Templates = templates;
        return View();
    }

    // ── Sablonlar (AJAX) ──────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Templates(Guid documentTypeId, CancellationToken ct)
    {
        var templates = await _templateRepo.GetByDocumentTypeIdAsync(documentTypeId, ct);
        return Json(templates.Select(t => new
        {
            t.Id,
            t.Name,
            t.FrxFilePath,
            t.Description,
            t.IsDefault,
            t.IsActive,
            CreatedAt = t.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
        }));
    }

    // ── FRX Yukle ─────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file, Guid documentTypeId, string name, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Dosya secilmedi.");

        if (!file.FileName.EndsWith(".frx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Sadece .frx dosyalari yuklenebilir.");

        if (file.Length > 1_048_576)
            return BadRequest("Dosya boyutu 1 MB'yi asamaz.");

        var docType = await _docTypeRepo.GetByIdAsync(documentTypeId, ct);
        if (docType is null)
            return NotFound("Belge tipi bulunamadi.");

        var dir = Path.Combine(_env.WebRootPath, "Document", "Templates", docType.Code);
        Directory.CreateDirectory(dir);

        var shortId = Guid.NewGuid().ToString("N")[..8];
        var safeName = SanitizeFileName(name);
        var fileName = $"{safeName}_{shortId}.frx";
        var filePath = Path.Combine(dir, fileName);

        await using (var fs = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(fs, ct);
        }

        var relativePath = $"Templates/{docType.Code}/{fileName}";

        var template = new Domain.Entities.ReportTemplate
        {
            Name           = name,
            DocumentTypeId = documentTypeId,
            FrxFilePath    = relativePath,
        };

        await _templateRepo.SaveAsync(template, ct);

        return Json(new { success = true, template.Id, template.FrxFilePath });
    }

    // ── Yeni Bos Sablon Olustur ───────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> CreateBlank(Guid documentTypeId, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Sablon adi bos olamaz.");

        var docType = await _docTypeRepo.GetByIdAsync(documentTypeId, ct);
        if (docType is null)
            return NotFound("Belge tipi bulunamadi.");

        var dir = Path.Combine(_env.WebRootPath, "Document", "Templates", docType.Code);
        Directory.CreateDirectory(dir);

        var shortId = Guid.NewGuid().ToString("N")[..8];
        var safeName = SanitizeFileName(name);
        var fileName = $"{safeName}_{shortId}.frx";
        var filePath = Path.Combine(dir, fileName);

        var frxContent = MinimalFrx(name);
        await System.IO.File.WriteAllTextAsync(filePath, frxContent, ct);

        var relativePath = $"Templates/{docType.Code}/{fileName}";
        var template = new Domain.Entities.ReportTemplate
        {
            Name           = name,
            DocumentTypeId = documentTypeId,
            FrxFilePath    = relativePath,
        };
        await _templateRepo.SaveAsync(template, ct);

        return Json(new { success = true, template.Id, template.FrxFilePath });
    }

    private static string MinimalFrx(string reportName) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Report ScriptLanguage="CSharp" ReportInfo.Name="{reportName}" ReportInfo.Created="{DateTime.Now:MM/dd/yyyy HH:mm:ss}" ReportInfo.Modified="{DateTime.Now:MM/dd/yyyy HH:mm:ss}" ReportInfo.CreatorVersion="2026.1.8">
          <Dictionary/>
          <ReportPage Name="Page1" Landscape="false" PaperWidth="210" PaperHeight="297" MarginLeft="10" MarginRight="10" MarginTop="10" MarginBottom="10">
            <ReportTitleBand Name="ReportTitle1" Top="0" Width="718.2" Height="37.8">
              <TextObject Name="Text1" Left="0" Top="0" Width="718.2" Height="37.8" Text="{reportName}" Font="Arial, 18pt, style=Bold"/>
            </ReportTitleBand>
            <DataBand Name="Data1" Top="41.8" Width="718.2" Height="30">
              <TextObject Name="Text2" Left="0" Top="0" Width="718.2" Height="30" Text="[Data.ProductCode]" Font="Arial, 10pt"/>
            </DataBand>
            <PageFooterBand Name="PageFooter1" Top="75.8" Width="718.2" Height="18.9">
              <TextObject Name="Text3" Left="0" Top="0" Width="718.2" Height="18.9" Text="Sayfa [PageN] / [TotalPages]" HorzAlign="Right" Font="Arial, 8pt"/>
            </PageFooterBand>
          </ReportPage>
        </Report>
        """;

    // ── PDF Uret ──────────────────────────────────────────────────────────────

    [HttpGet("/Document/Generate/{templateId:guid}/{recordId:guid}")]
    public async Task<IActionResult> Generate(Guid templateId, Guid recordId, CancellationToken ct)
    {
        try
        {
            var pdf = await _generationService.GeneratePdfAsync(templateId, recordId, ct);
            var template = await _templateRepo.GetByIdAsync(templateId, ct);
            var fileName = $"{SanitizeFileName(template?.Name ?? "belge")}.pdf";
            return File(pdf, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── PDF Onizleme ──────────────────────────────────────────────────────────

    [HttpGet("/Document/Preview/{templateId:guid}/{recordId:guid}")]
    public async Task<IActionResult> Preview(Guid templateId, Guid recordId, CancellationToken ct)
    {
        try
        {
            var pdf = await _generationService.GeneratePdfAsync(templateId, recordId, ct);
            Response.Headers["Content-Disposition"] = "inline";
            return File(pdf, "application/pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── ZPL Cikti ─────────────────────────────────────────────────────────────

    [HttpGet("/Document/Zpl/{recordId:guid}")]
    public async Task<IActionResult> Zpl(Guid recordId, string type, CancellationToken ct)
    {
        try
        {
            var zpl = await _generationService.GenerateZplAsync(recordId, type, ct);
            return Content(zpl, "text/plain");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Sablon Sil ────────────────────────────────────────────────────────────

    [HttpPost("/Document/Delete/{templateId:guid}")]
    public async Task<IActionResult> Delete(Guid templateId, CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct);
        if (template is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(template.FrxFilePath))
        {
            var fullPath = Path.Combine(_env.WebRootPath, "Document", template.FrxFilePath);
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        await _templateRepo.DeleteAsync(templateId, ct);
        return Json(new { success = true });
    }

    // ── Varsayilan Yap ────────────────────────────────────────────────────────

    [HttpPost("/Document/SetDefault/{templateId:guid}")]
    public async Task<IActionResult> SetDefault(Guid templateId, CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct);
        if (template is null)
            return NotFound();

        // Mevcut default'u kaldir
        var siblings = await _templateRepo.GetByDocumentTypeIdAsync(template.DocumentTypeId, ct);
        foreach (var s in siblings.Where(s => s.IsDefault && s.Id != templateId))
        {
            s.IsDefault = false;
            await _templateRepo.SaveAsync(s, ct);
        }

        template.IsDefault = true;
        await _templateRepo.SaveAsync(template, ct);

        return Json(new { success = true });
    }

    // ── Yeniden Adlandir ────────────────────────────────────────────────────

    [HttpPost("/Document/Rename/{templateId:guid}")]
    public async Task<IActionResult> Rename(Guid templateId, [FromForm] string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Sablon adi bos olamaz.");

        var template = await _templateRepo.GetByIdAsync(templateId, ct);
        if (template is null)
            return NotFound();

        var updated = new Domain.Entities.ReportTemplate
        {
            Id             = template.Id,
            Name           = name.Trim(),
            DocumentTypeId = template.DocumentTypeId,
            FrxFilePath    = template.FrxFilePath,
            Description    = template.Description,
            IsDefault      = template.IsDefault,
            IsActive       = template.IsActive,
            CreatedAt      = template.CreatedAt,
        };
        await _templateRepo.SaveAsync(updated, ct);

        return Json(new { success = true });
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

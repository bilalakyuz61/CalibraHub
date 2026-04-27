using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// FastReport Open Source ile .frx sablonlarindan PDF / HTML ciktisi uretir.
/// </summary>
[Authorize]
public sealed class ReportController : Controller
{
    private readonly IReportTemplateRepository _templateRepo;
    private readonly IDocumentGenerationService _generationService;

    public ReportController(
        IReportTemplateRepository templateRepo,
        IDocumentGenerationService generationService)
    {
        _templateRepo      = templateRepo;
        _generationService = generationService;
    }

    [HttpGet("/Report/Pdf/{templateId:int}/{recordId:int}")]
    public async Task<IActionResult> Pdf(int templateId, int recordId, CancellationToken ct)
    {
        try
        {
            var pdf = await _generationService.GeneratePdfAsync(templateId, recordId, ct);
            var template = await _templateRepo.GetByIdAsync(templateId, ct);
            var fileName = $"{SanitizeFileName(template?.Name ?? "rapor")}.pdf";
            return File(pdf, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("/Report/Preview/{templateId:int}/{recordId:int}")]
    public async Task<IActionResult> Preview(int templateId, int recordId, CancellationToken ct)
    {
        try
        {
            var pdf = await _generationService.GeneratePdfAsync(templateId, recordId, ct);
            Response.Headers["Content-Disposition"] = $"inline; filename=\"rapor.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("/Report/HtmlPreview/{templateId:int}/{recordId:int}")]
    public async Task<IActionResult> HtmlPreview(int templateId, int recordId, CancellationToken ct)
    {
        try
        {
            var html = await _generationService.GenerateHtmlPreviewAsync(templateId, recordId, ct);
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            return Content($"<p style='font-family:sans-serif;color:#c00;padding:2rem'>{ex.Message}</p>", "text/html");
        }
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

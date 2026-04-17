using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
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
    private readonly ISmtpProfileRepository _smtpProfileRepo;

    public DocumentController(
        IDocumentTypeRepository docTypeRepo,
        IReportTemplateRepository templateRepo,
        IDocumentGenerationService generationService,
        ISmtpProfileRepository smtpProfileRepo)
    {
        _docTypeRepo       = docTypeRepo;
        _templateRepo      = templateRepo;
        _generationService = generationService;
        _smtpProfileRepo   = smtpProfileRepo;
    }

    // ── Liste ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Belge şablonları ekranında gösterilmeyecek belge tipi kodları.
    /// Şimdilik fatura ve irsaliye hariç tutulur.
    /// </summary>
    private static readonly HashSet<string> HiddenDocumentTypeCodes =
        new(StringComparer.OrdinalIgnoreCase) { "fatura", "irsaliye" };

    [HttpGet]
    public async Task<IActionResult> Index(string? type, CancellationToken ct)
    {
        var allDocTypes = await _docTypeRepo.GetAllAsync(ct);
        var docTypes = allDocTypes.Where(d => !HiddenDocumentTypeCodes.Contains(d.Code)).ToArray();

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
            t.Description,
            t.IsDefault,
            t.IsActive,
            HasContent = (t.FrxContent?.Length ?? 0) > 0 || !string.IsNullOrWhiteSpace(t.FrxFilePath),
            ContentSize = t.FrxContent?.Length ?? 0,
            CreatedAt = t.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
        }));
    }

    // ── FRX Yukle (DB'ye) ────────────────────────────────────────────────────

    [HttpPost]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, Guid documentTypeId, string name, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Json(new { success = false, message = "Dosya secilmedi." });

        if (!file.FileName.EndsWith(".frx", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, message = "Sadece .frx dosyalari yuklenebilir." });

        if (file.Length > 1_048_576)
            return Json(new { success = false, message = "Dosya boyutu 1 MB'yi asamaz." });

        var docType = await _docTypeRepo.GetByIdAsync(documentTypeId, ct);
        if (docType is null)
            return Json(new { success = false, message = "Belge tipi bulunamadi." });

        byte[] content;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            content = ms.ToArray();
        }

        var template = new Domain.Entities.ReportTemplate
        {
            Name           = (name ?? Path.GetFileNameWithoutExtension(file.FileName)).Trim(),
            DocumentTypeId = documentTypeId,
            FrxContent     = content,
        };

        await _templateRepo.SaveAsync(template, ct);
        return Json(new { success = true, id = template.Id, name = template.Name });
    }

    // ── Yeni Bos Sablon Olustur (DB'ye) ──────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> CreateBlank(Guid documentTypeId, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, message = "Sablon adi bos olamaz." });

        var docType = await _docTypeRepo.GetByIdAsync(documentTypeId, ct);
        if (docType is null)
            return Json(new { success = false, message = "Belge tipi bulunamadi." });

        var frxXml = MinimalFrx(name);
        var content = Encoding.UTF8.GetBytes(frxXml);

        var template = new Domain.Entities.ReportTemplate
        {
            Name           = name.Trim(),
            DocumentTypeId = documentTypeId,
            FrxContent     = content,
        };
        await _templateRepo.SaveAsync(template, ct);

        return Json(new { success = true, id = template.Id, name = template.Name });
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

    // ── FRX Indir (DB'den binary) ────────────────────────────────────────────

    [HttpGet("/Document/Download/{templateId:guid}")]
    public async Task<IActionResult> Download(Guid templateId, CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct);
        if (template is null) return NotFound();
        if (template.FrxContent is not { Length: > 0 })
            return NotFound("Sablon icerigi bos.");

        var fileName = SanitizeFileName(template.Name) + ".frx";
        return File(template.FrxContent, "application/xml", fileName);
    }

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

        // Not: Eski kayitlardaki file system dosyalari yok sayiliyor.
        // Yeni akiste zaten DB'de tutuluyor.
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
            FrxContent     = template.FrxContent,
            Description    = template.Description,
            IsDefault      = template.IsDefault,
            IsActive       = template.IsActive,
            CreatedAt      = template.CreatedAt,
        };
        await _templateRepo.SaveAsync(updated, ct);

        return Json(new { success = true });
    }

    // ── Mail Gonder (PDF ek olarak) ─────────────────────────────────────────

    [HttpPost("/Document/SendEmail")]
    public async Task<IActionResult> SendEmail([FromBody] SendTemplateEmailRequest request, CancellationToken ct)
    {
        if (request is null)
            return Json(new { success = false, message = "Gecersiz istek." });
        if (string.IsNullOrWhiteSpace(request.ToEmail))
            return Json(new { success = false, message = "Alici adresi zorunludur." });
        if (string.IsNullOrWhiteSpace(request.Subject))
            return Json(new { success = false, message = "Konu zorunludur." });

        var template = await _templateRepo.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
            return Json(new { success = false, message = "Sablon bulunamadi." });

        // SMTP profili (ilk aktif profil; Admin tarafinda tanimlanan)
        var profiles = await _smtpProfileRepo.GetAllAsync(ct);
        var smtp = profiles.FirstOrDefault(p => p.IsActive);
        if (smtp is null)
            return Json(new { success = false, message = "Aktif bir SMTP profili tanimli degil. Mail ayarlarindan profil olusturun." });

        try
        {
            byte[]? attachmentBytes = null;
            var attachmentName = SanitizeFileName(template.Name) + ".pdf";

            if (request.RecordId.HasValue && request.RecordId.Value != Guid.Empty)
            {
                try
                {
                    attachmentBytes = await _generationService.GeneratePdfAsync(template.Id, request.RecordId.Value, ct);
                }
                catch (Exception genEx)
                {
                    return Json(new { success = false, message = "PDF uretilemedi: " + genEx.Message });
                }
            }

            using var mail = new MailMessage
            {
                From = new MailAddress(smtp.FromEmail, string.IsNullOrWhiteSpace(smtp.FromDisplayName) ? smtp.FromEmail : smtp.FromDisplayName),
                Subject = request.Subject,
                Body = request.BodyHtml ?? request.BodyText ?? string.Empty,
                IsBodyHtml = !string.IsNullOrWhiteSpace(request.BodyHtml),
            };
            foreach (var addr in (request.ToEmail ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                mail.To.Add(addr);

            if (attachmentBytes is { Length: > 0 })
            {
                var attStream = new MemoryStream(attachmentBytes);
                mail.Attachments.Add(new Attachment(attStream, attachmentName, "application/pdf"));
            }

            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = smtp.UseSsl,
                Credentials = new NetworkCredential(smtp.Username, smtp.Password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20_000,
            };

            await client.SendMailAsync(mail);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Mail gonderim hatasi: " + ex.Message });
        }
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public sealed record SendTemplateEmailRequest(
    Guid TemplateId,
    Guid? RecordId,
    string ToEmail,
    string Subject,
    string? BodyHtml,
    string? BodyText);

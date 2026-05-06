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
    private readonly IReportTemplateSourceRepository _sourceRepo;
    private readonly IDocumentGenerationService _generationService;
    private readonly ISmtpProfileRepository _smtpProfileRepo;
    private readonly IReportDataRepository _reportDataRepo;
    private readonly IDocumentRepository _documentRepo;
    private readonly IFinanceRepository _financeRepo;

    public DocumentController(
        IDocumentTypeRepository docTypeRepo,
        IReportTemplateRepository templateRepo,
        IReportTemplateSourceRepository sourceRepo,
        IDocumentGenerationService generationService,
        ISmtpProfileRepository smtpProfileRepo,
        IReportDataRepository reportDataRepo,
        IDocumentRepository documentRepo,
        IFinanceRepository financeRepo)
    {
        _docTypeRepo       = docTypeRepo;
        _templateRepo      = templateRepo;
        _sourceRepo        = sourceRepo;
        _generationService = generationService;
        _smtpProfileRepo   = smtpProfileRepo;
        _reportDataRepo    = reportDataRepo;
        _documentRepo      = documentRepo;
        _financeRepo       = financeRepo;
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
    public async Task<IActionResult> Templates(int documentTypeId, CancellationToken ct)
    {
        var templates = await _templateRepo.GetByDocumentTypeIdAsync(documentTypeId, ct);
        var docType   = await _docTypeRepo.GetByIdAsync(documentTypeId, ct);
        // Sablonun kendi view'i yoksa docType'in default view'ini listede goster
        var defaultView = docType?.SqlViewName ?? string.Empty;
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
            ViewName = string.IsNullOrWhiteSpace(t.SqlViewName) ? defaultView : t.SqlViewName,
            IsViewOverride = !string.IsNullOrWhiteSpace(t.SqlViewName),
            KeyColumn = t.KeyColumn ?? string.Empty,
        }));
    }

    // ── Bir view'in kolon listesi (dialog'da key column dropdown icin) ──
    [HttpGet("/Document/ViewColumns/{viewName}")]
    public async Task<IActionResult> ViewColumns(string viewName, CancellationToken ct)
    {
        try
        {
            var cols = await _reportDataRepo.GetViewColumnsAsync(viewName, ct);
            return Json(new { success = true, columns = cols });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message, columns = Array.Empty<string>() });
        }
    }

    // ── Sablon source'larini listele ──
    [HttpGet("/Document/Sources/{templateId:int}")]
    public async Task<IActionResult> Sources(int templateId, CancellationToken ct)
    {
        var sources = await _sourceRepo.GetByTemplateIdAsync(templateId, ct);
        return Json(sources.Select(s => new
        {
            s.Id,
            s.SourceName,
            s.ViewName,
            s.KeyColumn,
            s.ParentSourceName,
            s.ParentKeyColumn,
            s.IsPrimary,
            s.DisplayOrder,
            s.SortColumn,
            s.SortDirection,
        }));
    }

    // ── Sablon source'larini topluca kaydet (atomic replace) ──
    [HttpPost("/Document/Sources/{templateId:int}")]
    public async Task<IActionResult> SaveSources(int templateId, [FromBody] SaveSourcesRequest request, CancellationToken ct)
    {
        if (request?.Sources is null || request.Sources.Count == 0)
            return Json(new { success = false, message = "En az bir data source gerekli." });

        // Validation
        var nameRegex = new System.Text.RegularExpressions.Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryCount = 0;
        foreach (var s in request.Sources)
        {
            if (string.IsNullOrWhiteSpace(s.SourceName) || !nameRegex.IsMatch(s.SourceName))
                return Json(new { success = false, message = $"Gecersiz source adi: '{s.SourceName}'" });
            if (string.IsNullOrWhiteSpace(s.ViewName) || !nameRegex.IsMatch(s.ViewName))
                return Json(new { success = false, message = $"Gecersiz view adi: '{s.ViewName}'" });
            if (string.IsNullOrWhiteSpace(s.KeyColumn) || !nameRegex.IsMatch(s.KeyColumn))
                return Json(new { success = false, message = $"Gecersiz key kolon: '{s.KeyColumn}'" });
            if (!seen.Add(s.SourceName))
                return Json(new { success = false, message = $"Tekrarlanan source adi: '{s.SourceName}'" });
            if (s.IsPrimary) primaryCount++;
        }
        if (primaryCount != 1)
            return Json(new { success = false, message = "Tam olarak BIR tane primary source olmali." });

        // Validation: her view gercekten DB'de var mi + key column o view'da mi?
        foreach (var s in request.Sources)
        {
            try
            {
                var cols = await _reportDataRepo.GetViewColumnsAsync(s.ViewName, ct);
                if (cols.Count == 0)
                    return Json(new { success = false, message = $"'{s.ViewName}' view'i bulunamadi." });
                if (!cols.Contains(s.KeyColumn, StringComparer.OrdinalIgnoreCase))
                    return Json(new { success = false, message = $"'{s.ViewName}' view'inda '{s.KeyColumn}' kolonu yok." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"'{s.ViewName}' dogrulanamadi: {ex.Message}" });
            }
        }

        var entities = request.Sources.Select((s, idx) =>
        {
            var sortCol = string.IsNullOrWhiteSpace(s.SortColumn) ? null : s.SortColumn.Trim();
            if (sortCol != null && !nameRegex.IsMatch(sortCol)) sortCol = null;
            string? sortDir = null;
            if (sortCol != null)
            {
                var d = (s.SortDirection ?? "ASC").Trim().ToUpperInvariant();
                sortDir = (d == "DESC") ? "DESC" : "ASC";
            }
            return new Domain.Entities.ReportTemplateSource
            {
                TemplateId       = templateId,
                SourceName       = s.SourceName.Trim(),
                ViewName         = s.ViewName.Trim(),
                KeyColumn        = s.KeyColumn.Trim(),
                ParentSourceName = string.IsNullOrWhiteSpace(s.ParentSourceName) ? null : s.ParentSourceName.Trim(),
                ParentKeyColumn  = string.IsNullOrWhiteSpace(s.ParentKeyColumn)  ? null : s.ParentKeyColumn.Trim(),
                IsPrimary        = s.IsPrimary,
                DisplayOrder     = idx,
                SortColumn       = sortCol,
                SortDirection    = sortDir,
            };
        }).ToList();

        await _sourceRepo.ReplaceAllAsync(templateId, entities, ct);
        return Json(new { success = true });
    }

    // ── DB'deki kayitli view'lari listele (CreateBlank dialog combobox'i icin) ──
    [HttpGet("/Document/AvailableViews")]
    public async Task<IActionResult> AvailableViews(CancellationToken ct)
    {
        try
        {
            var views = await _reportDataRepo.GetAvailableViewsAsync(ct);
            return Json(new { success = true, views });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message, views = Array.Empty<string>() });
        }
    }

    // ── FRX Yukle (DB'ye) ────────────────────────────────────────────────────

    [HttpPost]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, int documentTypeId, string name, CancellationToken ct)
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

        // SaveAsync INSERT ile atanan IDENTITY id'yi doner — entity.Id init-only oldugu
        // icin burada elle set edemiyoruz, ancak client'a donen "id" alani gercek Id olmalidir.
        var newId = await _templateRepo.SaveAsync(template, ct);
        return Json(new { success = true, id = newId, name = template.Name });
    }

    // ── Yeni Bos Sablon Olustur (DB'ye) ──────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> CreateBlank(int documentTypeId, string name, string? sqlViewName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, message = "Sablon adi bos olamaz." });

        var docType = await _docTypeRepo.GetByIdAsync(documentTypeId, ct);
        if (docType is null)
            return Json(new { success = false, message = "Belge tipi bulunamadi." });

        // View adi opsiyonel: girildiyse SANITIZE et ve regex ile dogrula
        string? cleanView = null;
        if (!string.IsNullOrWhiteSpace(sqlViewName))
        {
            var trimmed = sqlViewName.Trim();
            if (trimmed.Length > 150 || !System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                return Json(new { success = false, message = "Gecersiz view adi. Sadece harf, rakam ve alt cizgi kullanin." });
            cleanView = trimmed;
        }

        // Effective view — sablonun view'i yoksa belge tipinin default view'ini kullan
        var effectiveView = cleanView ?? docType.SqlViewName;
        var requiredKey = docType.RequiredKeyColumn;

        // Belge tipi icin zorunlu key kolonu tanimli ise view'in BU kolonu icermesi ZORUNLU.
        // Olmadan basim sirasinda recordId filtresi calisamaz → kullaniciya sablon kaydetmesine izin verme.
        if (!string.IsNullOrWhiteSpace(effectiveView) && !string.IsNullOrWhiteSpace(requiredKey))
        {
            try
            {
                var cols = await _reportDataRepo.GetViewColumnsAsync(effectiveView, ct);
                if (!cols.Contains(requiredKey, StringComparer.OrdinalIgnoreCase))
                {
                    return Json(new
                    {
                        success = false,
                        message = $"'{effectiveView}' view'inda beklenen '{requiredKey}' kolonu bulunamadi. Bu belge tipi '{requiredKey}' gerektirir."
                    });
                }
            }
            catch
            {
                // View kolonlari okunamazsa (ornek view yoksa) sessizce gec — diger kontrollerde yakalanir
            }
        }

        var frxXml = MinimalFrx(name);
        var content = Encoding.UTF8.GetBytes(frxXml);

        var template = new Domain.Entities.ReportTemplate
        {
            Name           = name.Trim(),
            DocumentTypeId = documentTypeId,
            FrxContent     = content,
            SqlViewName    = cleanView,
            KeyColumn      = requiredKey, // docType'tan otomatik atanir, kullanici secimi yok
        };
        var newId = await _templateRepo.SaveAsync(template, ct);

        return Json(new { success = true, id = newId, name = template.Name });
    }

    // ── Belge tipinin default view ve required key column bilgisi ──
    [HttpGet("/Document/DocumentTypeView/{documentTypeId:int}")]
    public async Task<IActionResult> DocumentTypeView(int documentTypeId, CancellationToken ct)
    {
        var dt = await _docTypeRepo.GetByIdAsync(documentTypeId, ct);
        if (dt is null) return Json(new { success = false, message = "Belge tipi bulunamadi." });
        return Json(new
        {
            success     = true,
            view        = dt.SqlViewName ?? string.Empty,
            name        = dt.Name,
            requiredKey = dt.RequiredKeyColumn ?? string.Empty,
        });
    }

    // Yeni sablon olusturuldugunda baslangic icerigi. DataBand varsayilan olarak
    // "Belge" BusinessObject'ine bagli — tasarim sirasinda drag-drop ile alan
    // eklenebilir ve hemen veri iteration'i calisir. Placeholder alan referansi
    // yok (CS0234 compile hatasi riski kaldirildi).
    private static string MinimalFrx(string reportName) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Report ScriptLanguage="CSharp" ReportInfo.Name="{reportName}" ReportInfo.Created="{DateTime.Now:MM/dd/yyyy HH:mm:ss}" ReportInfo.Modified="{DateTime.Now:MM/dd/yyyy HH:mm:ss}" ReportInfo.CreatorVersion="2026.1.8">
          <Dictionary/>
          <ReportPage Name="Page1" Landscape="false" PaperWidth="210" PaperHeight="297" MarginLeft="10" MarginRight="10" MarginTop="10" MarginBottom="10">
            <ReportTitleBand Name="ReportTitle1" Top="0" Width="718.2" Height="37.8">
              <TextObject Name="Text1" Left="0" Top="0" Width="718.2" Height="37.8" Text="{reportName}" Font="Arial, 18pt, style=Bold" HorzAlign="Center"/>
            </ReportTitleBand>
            <DataBand Name="Data1" Top="41.8" Width="718.2" Height="30" DataSource="Belge"/>
            <PageFooterBand Name="PageFooter1" Top="75.8" Width="718.2" Height="18.9">
              <TextObject Name="Text3" Left="0" Top="0" Width="718.2" Height="18.9" Text="Sayfa [PageN] / [TotalPages]" HorzAlign="Right" Font="Arial, 8pt"/>
            </PageFooterBand>
          </ReportPage>
        </Report>
        """;

    // ── FRX Indir (DB'den binary) ────────────────────────────────────────────

    [HttpGet("/Document/Download/{templateId:int}")]
    public async Task<IActionResult> Download(int templateId, CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct);
        if (template is null) return NotFound();
        if (template.FrxContent is not { Length: > 0 })
            return NotFound("Sablon icerigi bos.");

        var fileName = SanitizeFileName(template.Name) + ".frx";
        return File(template.FrxContent, "application/xml", fileName);
    }

    // ── PDF Uret ──────────────────────────────────────────────────────────────

    [HttpGet("/Document/Generate/{templateId:int}/{recordId:int}")]
    public async Task<IActionResult> Generate(int templateId, int recordId, CancellationToken ct)
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

    [HttpGet("/Document/Preview/{templateId:int}/{recordId:int}")]
    public async Task<IActionResult> Preview(int templateId, int recordId, [FromQuery] int? download, CancellationToken ct)
    {
        try
        {
            var pdf = await _generationService.GeneratePdfAsync(templateId, recordId, ct);
            if (download == 1)
            {
                var fileName = $"belge-{templateId}-{recordId}.pdf";
                Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
                return File(pdf, "application/pdf", fileName);
            }
            Response.Headers["Content-Disposition"] = "inline";
            return File(pdf, "application/pdf");
        }
        catch (Exception ex)
        {
            // Compile hatasi (eski sablonda [Data.ProductCode] gibi gecersiz alan referansi)
            // kullaniciya duz text yerine HTML kart olarak acik sekilde gostersin.
            Response.StatusCode = 400;
            Response.Headers["Content-Disposition"] = "inline";
            var safeMsg  = System.Net.WebUtility.HtmlEncode(ex.Message ?? "Bilinmeyen hata");
            var safeType = System.Net.WebUtility.HtmlEncode(ex.GetType().FullName ?? "Exception");
            // Gercek root cause'u surface et — InnerException varsa onu da goster
            var inner = ex.InnerException;
            var innerText = inner != null
                ? System.Net.WebUtility.HtmlEncode($"{inner.GetType().FullName}: {inner.Message}")
                : string.Empty;
            var safeStack = System.Net.WebUtility.HtmlEncode((ex.StackTrace ?? string.Empty).Length > 2000
                ? ex.StackTrace!.Substring(0, 2000) + "..."
                : (ex.StackTrace ?? string.Empty));
            var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Onizleme Hatasi</title>"
                + "<style>body{font-family:Segoe UI,sans-serif;background:#0a0d17;color:#e2e8f0;padding:32px;line-height:1.6}"
                + ".card{max-width:820px;margin:40px auto;background:#15182b;border:1px solid #3a3f5a;border-radius:14px;padding:28px;box-shadow:0 12px 40px rgba(0,0,0,.4)}"
                + "h1{margin:0 0 10px;font-size:1.3rem;color:#fca5a5}"
                + "p{margin:8px 0;color:#cbd5e1;font-size:.92rem}"
                + "pre{background:#0a0d17;border:1px solid #3a3f5a;padding:14px;border-radius:10px;font-size:.78rem;color:#fca5a5;white-space:pre-wrap;word-break:break-word;max-height:280px;overflow:auto}"
                + "details{margin-top:12px}summary{cursor:pointer;color:#fbbf24;font-size:.85rem}"
                + ".hint{background:rgba(245,158,11,.08);border:1px solid rgba(245,158,11,.25);border-radius:8px;padding:12px 14px;color:#fcd34d;font-size:.82rem;margin-top:16px}"
                + ".meta{font-size:.75rem;color:#94a3b8;margin-top:4px}</style>"
                + "</head><body><div class='card'>"
                + "<h1>&#9888; Onizleme uretilemedi</h1>"
                + "<p>Secili FastReport sablonunda bir alan referansi veya ifade hatali.</p>"
                + "<pre>" + safeMsg + "</pre>"
                + "<div class='meta'>Exception: <code>" + safeType + "</code></div>"
                + (innerText.Length > 0 ? "<div class='meta'>Inner: <code>" + innerText + "</code></div>" : "")
                + "<details><summary>Stack trace (gelistirici)</summary><pre>" + safeStack + "</pre></details>"
                + "<div class='hint'><strong>Cozum:</strong> Tasarim &rarr; Belge Sablonlari ekranindan sablonu acin (&#9998; butonu) ve hatali TextObject'i duzeltin. "
                + "Ornegin <code>[Data.ProductCode]</code> yerine <code>[Belge.MalzemeKodu]</code> yazin.</div>"
                + "</div></body></html>";
            return Content(html, "text/html; charset=utf-8");
        }
    }

    // ── ZPL Cikti ─────────────────────────────────────────────────────────────

    [HttpGet("/Document/Zpl/{recordId:int}")]
    public async Task<IActionResult> Zpl(int recordId, string type, CancellationToken ct)
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

    [HttpPost("/Document/Delete/{templateId:int}")]
    public async Task<IActionResult> Delete(int templateId, CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct);
        if (template is null)
            return NotFound();

        // Not: Eski kayitlardaki file system dosyalari yok sayiliyor.
        // Yeni akiste zaten DB'de tutuluyor.
        await _templateRepo.DeleteAsync(templateId, ct);
        return Json(new { success = true });
    }

    // ── Varsayilan Sablon Sorgula ─────────────────────────────────────────────
    // Belge turu koduna gore o tipin varsayilan (IsDefault=true) sablonunu getirir.
    // Bulunamazsa tipe ait ilk aktif sablonu doner. Hic sablon yoksa null.
    // Yazdir/Preview butonlari icin kullanilir — client bu endpoint'ten templateId
    // alip /Document/Preview/{templateId}/{recordId} ile onizleme acar.
    [HttpGet("/Document/DefaultTemplate")]
    public async Task<IActionResult> DefaultTemplate(string typeCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            return Json(new { success = false, message = "typeCode gerekli." });

        var docType = await _docTypeRepo.GetByCodeAsync(typeCode, ct);
        if (docType is null)
            return Json(new { success = false, message = $"Belge tipi bulunamadi: {typeCode}" });

        var defaultTemplate = await _templateRepo.GetDefaultByDocumentTypeIdAsync(docType.Id, ct);
        if (defaultTemplate != null)
            return Json(new { success = true, id = defaultTemplate.Id, name = defaultTemplate.Name, isDefault = true, outputOptions = defaultTemplate.OutputOptionsJson });

        // Varsayilan yoksa — ilk aktif sablonu doner (olmasi halinde yazdir yine calissin)
        var all = await _templateRepo.GetByDocumentTypeIdAsync(docType.Id, ct);
        var first = all.FirstOrDefault(t => t.IsActive);
        if (first != null)
            return Json(new { success = true, id = first.Id, name = first.Name, isDefault = false, outputOptions = first.OutputOptionsJson });

        return Json(new { success = false, message = "Bu belge tipi icin tanimlanmis sablon yok." });
    }

    // ── Cikti Ayarlari (Per-Template) ─────────────────────────────────────────
    // Belge Sablonlari ekranindan kullanici sablon icin Baski/PDF/Mail
    // varsayilanlarini kaydeder; Sales/DocumentEdit > Yazdir bu ayarlari
    // okuyup direkt uygular (modal acmadan).
    [HttpGet("/Document/OutputOptions/{templateId:int}")]
    public async Task<IActionResult> GetOutputOptions(int templateId, CancellationToken ct)
    {
        var t = await _templateRepo.GetByIdAsync(templateId, ct);
        if (t is null) return NotFound();
        return Json(new { success = true, options = t.OutputOptionsJson });
    }

    [HttpPost("/Document/OutputOptions/{templateId:int}")]
    public async Task<IActionResult> SaveOutputOptions(int templateId, [FromBody] SaveOutputOptionsRequest request, CancellationToken ct)
    {
        var t = await _templateRepo.GetByIdAsync(templateId, ct);
        if (t is null) return NotFound();

        // Bos JSON veya null → ayari temizle
        var json = string.IsNullOrWhiteSpace(request?.OptionsJson) ? null : request!.OptionsJson;

        var updated = new Domain.Entities.ReportTemplate
        {
            Id                = t.Id,
            Name              = t.Name,
            DocumentTypeId    = t.DocumentTypeId,
            FrxFilePath       = t.FrxFilePath,
            FrxContent        = t.FrxContent,
            Description       = t.Description,
            IsDefault         = t.IsDefault,
            IsActive          = t.IsActive,
            SqlViewName       = t.SqlViewName,
            KeyColumn         = t.KeyColumn,
            OutputOptionsJson = json,
            OrderColumn       = t.OrderColumn,
            OrderDirection    = t.OrderDirection,
            CreatedAt         = t.CreatedAt,
        };
        await _templateRepo.SaveAsync(updated, ct);
        return Json(new { success = true });
    }

    // ── Hazir Tasarim Uygula — Satis Teklifi Master-Detail ────────────────────
    // Template icin:
    //   - Stored sources: vw_ReportDocument (primary) + vw_DocumentCombination
    //   - Frx XML: ReportTitle (header) + MasterData (kalem) + nested DataBand
    //     (kombinasyon, MasterData + Filter ile kalem'e bagli) + PageFooter
    // Kullanim: tarayicida GET /Document/ApplySalesQuoteDesign/{templateId}
    [AllowAnonymous]
    [HttpGet("/Document/ApplySalesQuoteDesign/{templateId:int}")]
    public async Task<IActionResult> ApplySalesQuoteDesign(int templateId, CancellationToken ct)
    {
        var t = await _templateRepo.GetByIdAsync(templateId, ct);
        if (t is null) return NotFound();

        // Tek source: vw_ReportDocument (kalem + belge + STUFF'li KombinasyonDetay).
        // FastReport OpenSource group/master-detail desteklemedigi icin her kalemin
        // kombinasyon detayi multiline TextObject ile basilir.
        var sources = new List<Domain.Entities.ReportTemplateSource>
        {
            new()
            {
                TemplateId = templateId,
                SourceName = "vw_ReportDocument",
                ViewName   = "vw_ReportDocument",
                KeyColumn  = "BelgeId",
                IsPrimary  = true,
                DisplayOrder = 0,
                SortColumn = "SiraNo",
                SortDirection = "ASC",
            },
        };
        await _sourceRepo.ReplaceAllAsync(templateId, sources, ct);

        // 2) Frx XML (master-detail)
        var frx = BuildSalesQuoteMasterDetailFrx(t.Name);

        var updated = new Domain.Entities.ReportTemplate
        {
            Id                = t.Id,
            Name              = t.Name,
            DocumentTypeId    = t.DocumentTypeId,
            FrxFilePath       = t.FrxFilePath,
            FrxContent        = Encoding.UTF8.GetBytes(frx),
            Description       = t.Description,
            IsDefault         = t.IsDefault,
            IsActive          = t.IsActive,
            SqlViewName       = "vw_ReportDocument",
            KeyColumn         = "BelgeId",
            OutputOptionsJson = t.OutputOptionsJson,
            OrderColumn       = null,
            OrderDirection    = null,
            CreatedAt         = t.CreatedAt,
        };
        await _templateRepo.SaveAsync(updated, ct);

        return Json(new
        {
            success = true,
            templateId = templateId,
            message = "Tasarim uygulandi. Designer'da acabilir veya teklifi yazdirabilirsiniz.",
            sources = sources.Select(s => new { s.SourceName, s.ViewName, s.KeyColumn, s.IsPrimary }).ToArray(),
        });
    }

    // Frx'i URL'den build edilen her seferinde yeni bir "build stamp" ile
    // isaretliyoruz (ReportTitle'in altinda kucuk yazi) — boylece PDF onizlemede
    // hangi versiyonun aciladigi anlaşilir.
    private static string BuildSalesQuoteMasterDetailFrx(string name)
    {
        var safeName = System.Security.SecurityElement.Escape(name) ?? "Satis Teklifi";
        var now = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss");
        var buildStamp = $"v2-multiline · {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        // Yaklasim: tek DataBand (vw_DocumentCombination — her kombinasyon icin 1 row,
        // kombinasyonu olmayan kalem icin de 1 row null kolonla). GroupHeader KalemId
        // bazli kırılım saglar; GroupFooter genel toplam yerine kalem sonunu ayirir.
        // ReportTitle'in veri kaynagi vw_ReportDocument (bir belge icin birden fazla
        // satir donebilir; TextObject ilk satirin degerini gosterir — header sadece 1 kez
        // basildigi icin sorun olmaz).
        return $"""
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Name="{safeName}" ReportInfo.Created="{now}" ReportInfo.Modified="{now}" ReportInfo.CreatorVersion="2026.1.8">
  <Dictionary>
    <TableDataSource Name="vw_ReportDocument" Enabled="true">
      <Column Name="BelgeId" DataType="System.Int32"/>
      <Column Name="BelgeNo" DataType="System.String"/>
      <Column Name="BelgeTarihi" DataType="System.DateTime"/>
      <Column Name="GecerlilikTarihi" DataType="System.DateTime"/>
      <Column Name="CariUnvani" DataType="System.String"/>
      <Column Name="CariVergiNo" DataType="System.String"/>
      <Column Name="TemsilciAdi" DataType="System.String"/>
      <Column Name="GenelToplam" DataType="System.Decimal"/>
      <Column Name="KalemId" DataType="System.Int32"/>
      <Column Name="SiraNo" DataType="System.Int32"/>
      <Column Name="MalzemeAdi" DataType="System.String"/>
      <Column Name="MalzemeKodu" DataType="System.String"/>
      <Column Name="Miktar" DataType="System.Decimal"/>
      <Column Name="BirimKodu" DataType="System.String"/>
      <Column Name="BirimFiyat" DataType="System.Decimal"/>
      <Column Name="SatirToplami" DataType="System.Decimal"/>
      <Column Name="KombinasyonDetay" DataType="System.String"/>
    </TableDataSource>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="false" PaperWidth="210" PaperHeight="297" MarginLeft="10" MarginRight="10" MarginTop="10" MarginBottom="10">
    <ReportTitleBand Name="ReportTitle1" Top="0" Width="718.2" Height="125">
      <TextObject Name="TitleText" Left="0" Top="0" Width="718.2" Height="30" Text="SATIS TEKLIFI" Font="Arial, 16pt, style=Bold" HorzAlign="Center"/>
      <TextObject Name="BuildStamp" Left="0" Top="28" Width="718.2" Height="12" Text="Tasarim {buildStamp}" Font="Arial, 7pt, style=Italic" HorzAlign="Center" TextColor="140,140,140"/>
      <TextObject Name="LblBelgeNo" Left="0" Top="40" Width="90" Height="18" Text="Teklif No:" Font="Arial, 9pt"/>
      <TextObject Name="ValBelgeNo" Left="90" Top="40" Width="250" Height="18" Text="[vw_ReportDocument.BelgeNo]" Font="Arial, 9pt, style=Bold"/>
      <TextObject Name="LblTarih" Left="0" Top="60" Width="90" Height="18" Text="Tarih:" Font="Arial, 9pt"/>
      <TextObject Name="ValTarih" Left="90" Top="60" Width="250" Height="18" Text="[vw_ReportDocument.BelgeTarihi]" Font="Arial, 9pt" Format="Date" Format.Format="d"/>
      <TextObject Name="LblGecerlilik" Left="0" Top="80" Width="90" Height="18" Text="Gecerlilik:" Font="Arial, 9pt"/>
      <TextObject Name="ValGecerlilik" Left="90" Top="80" Width="250" Height="18" Text="[vw_ReportDocument.GecerlilikTarihi]" Font="Arial, 9pt" Format="Date" Format.Format="d"/>
      <TextObject Name="LblCari" Left="370" Top="40" Width="90" Height="18" Text="Musteri:" Font="Arial, 9pt"/>
      <TextObject Name="ValCari" Left="460" Top="40" Width="258" Height="18" Text="[vw_ReportDocument.CariUnvani]" Font="Arial, 9pt, style=Bold"/>
      <TextObject Name="LblVergi" Left="370" Top="60" Width="90" Height="18" Text="Vergi No:" Font="Arial, 9pt"/>
      <TextObject Name="ValVergi" Left="460" Top="60" Width="258" Height="18" Text="[vw_ReportDocument.CariVergiNo]" Font="Arial, 9pt"/>
      <TextObject Name="LblTemsilci" Left="370" Top="80" Width="90" Height="18" Text="Temsilci:" Font="Arial, 9pt"/>
      <TextObject Name="ValTemsilci" Left="460" Top="80" Width="258" Height="18" Text="[vw_ReportDocument.TemsilciAdi]" Font="Arial, 9pt"/>
      <LineObject Name="TitleLine" Left="0" Top="112" Width="718" Height="0" Border.Lines="Top" Border.Width="1"/>
    </ReportTitleBand>
    <DataHeaderBand Name="DataHeader1" Top="129" Width="718.2" Height="22" FillColor="240,240,240">
      <TextObject Name="HdrSiraNo" Left="0" Top="0" Width="40" Height="22" Text="S.No" Font="Arial, 9pt, style=Bold" HorzAlign="Center" VertAlign="Center"/>
      <TextObject Name="HdrMalzeme" Left="40" Top="0" Width="260" Height="22" Text="Malzeme / Kombinasyon" Font="Arial, 9pt, style=Bold" VertAlign="Center"/>
      <TextObject Name="HdrMiktar" Left="300" Top="0" Width="70" Height="22" Text="Miktar" Font="Arial, 9pt, style=Bold" HorzAlign="Right" VertAlign="Center"/>
      <TextObject Name="HdrBirim" Left="370" Top="0" Width="60" Height="22" Text="Birim" Font="Arial, 9pt, style=Bold" HorzAlign="Center" VertAlign="Center"/>
      <TextObject Name="HdrFiyat" Left="430" Top="0" Width="100" Height="22" Text="Birim Fiyat" Font="Arial, 9pt, style=Bold" HorzAlign="Right" VertAlign="Center"/>
      <TextObject Name="HdrToplam" Left="530" Top="0" Width="188" Height="22" Text="Satir Toplami" Font="Arial, 9pt, style=Bold" HorzAlign="Right" VertAlign="Center"/>
    </DataHeaderBand>
    <DataBand Name="Data1" Top="155" Width="718.2" Height="22" DataSource="vw_ReportDocument" CanGrow="true">
      <TextObject Name="RowSiraNo" Left="0" Top="0" Width="40" Height="22" Text="[vw_ReportDocument.SiraNo]" Font="Arial, 9pt" HorzAlign="Center" VertAlign="Top"/>
      <TextObject Name="RowMalzeme" Left="40" Top="0" Width="260" Height="22" Text="[vw_ReportDocument.MalzemeAdi]" Font="Arial, 9pt, style=Bold" VertAlign="Top"/>
      <TextObject Name="RowKombinasyon" Left="50" Top="20" Width="250" Height="14" Text="[vw_ReportDocument.KombinasyonDetay]" Font="Arial, 8pt, style=Italic" TextColor="96,96,96" WordWrap="true" CanGrow="true" VertAlign="Top"/>
      <TextObject Name="RowMiktar" Left="300" Top="0" Width="70" Height="22" Text="[vw_ReportDocument.Miktar]" Font="Arial, 9pt" HorzAlign="Right" VertAlign="Top" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2"/>
      <TextObject Name="RowBirim" Left="370" Top="0" Width="60" Height="22" Text="[vw_ReportDocument.BirimKodu]" Font="Arial, 9pt" HorzAlign="Center" VertAlign="Top"/>
      <TextObject Name="RowFiyat" Left="430" Top="0" Width="100" Height="22" Text="[vw_ReportDocument.BirimFiyat]" Font="Arial, 9pt" HorzAlign="Right" VertAlign="Top" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2"/>
      <TextObject Name="RowToplam" Left="530" Top="0" Width="188" Height="22" Text="[vw_ReportDocument.SatirToplami]" Font="Arial, 9pt, style=Bold" HorzAlign="Right" VertAlign="Top" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2"/>
    </DataBand>
    <ReportSummaryBand Name="ReportSummary1" Top="181" Width="718.2" Height="30">
      <LineObject Name="SumLine" Left="0" Top="2" Width="718" Height="0" Border.Lines="Top" Border.Width="1"/>
      <TextObject Name="LblGenelTop" Left="430" Top="8" Width="100" Height="20" Text="GENEL TOPLAM:" Font="Arial, 10pt, style=Bold" HorzAlign="Right" VertAlign="Center"/>
      <TextObject Name="ValGenelTop" Left="530" Top="8" Width="188" Height="20" Text="[vw_ReportDocument.GenelToplam]" Font="Arial, 10pt, style=Bold" HorzAlign="Right" VertAlign="Center" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="215" Width="718.2" Height="20">
      <TextObject Name="FooterText" Left="0" Top="0" Width="718.2" Height="18" Text="Sayfa [PageN] / [TotalPages]" Font="Arial, 8pt" HorzAlign="Right" TextColor="128,128,128"/>
    </PageFooterBand>
  </ReportPage>
</Report>
""";
    }

    // ── Sifirdan Satis Teklifi Sablonu Olustur + Tasarim Uygula ────────────
    // ── Ocaksan stili teklif tasarimi ────────────────────────────────────────
    // OTS26-04xxx PDF'inden esinlenilen yapi: ust banner (logo + sirket bilgileri
    // + TEKLIF kutusu), musteri blogu, border'li kalem tablosu (SiraNo/Aciklama/
    // Adet/BirimFiyat/Tutar), TOPLAM + GENEL TOPLAM, alt notlar + banka + kase.
    // Kullanim: GET /Document/CreateAndApplyOcaksanDesign?name=Ocaksan-Teklif
    [AllowAnonymous]
    [HttpGet("/Document/CreateAndApplyOcaksanDesign")]
    public async Task<IActionResult> CreateAndApplyOcaksanDesign(string? name, CancellationToken ct)
    {
        var docType = await _docTypeRepo.GetByCodeAsync("satis_teklifi", ct);
        if (docType is null)
            return Json(new { success = false, message = "'satis_teklifi' belge tipi bulunamadi." });

        var finalName = string.IsNullOrWhiteSpace(name) ? "Ocaksan-Teklif" : name.Trim();

        var existing = await _templateRepo.GetByDocumentTypeIdAsync(docType.Id, ct);
        var sameName = existing
            .Where(x => string.Equals(x.Name, finalName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Id).ToList();

        int newId;
        if (sameName.Count > 0)
        {
            newId = sameName[0].Id;
            foreach (var dup in sameName.Skip(1))
                await _templateRepo.DeleteAsync(dup.Id, ct);
        }
        else
        {
            var blank = new Domain.Entities.ReportTemplate
            {
                Name           = finalName,
                DocumentTypeId = docType.Id,
                FrxContent     = Array.Empty<byte>(),
                SqlViewName    = "vw_ReportDocument",
                KeyColumn      = "BelgeId",
                IsDefault      = true,
                IsActive       = true,
            };
            newId = await _templateRepo.SaveAsync(blank, ct);
        }
        foreach (var e in existing.Where(x => x.Id != newId && x.IsDefault))
        {
            e.IsDefault = false;
            await _templateRepo.SaveAsync(e, ct);
        }

        var sources = new List<Domain.Entities.ReportTemplateSource>
        {
            new()
            {
                TemplateId = newId,
                SourceName = "vw_ReportDocument",
                ViewName   = "vw_ReportDocument",
                KeyColumn  = "BelgeId",
                IsPrimary  = true,
                DisplayOrder = 0,
                SortColumn = "SiraNo",
                SortDirection = "ASC",
            },
        };
        await _sourceRepo.ReplaceAllAsync(newId, sources, ct);

        var t = await _templateRepo.GetByIdAsync(newId, ct)!;
        var frx = BuildOcaksanQuoteFrx(t!.Name);

        var updated = new Domain.Entities.ReportTemplate
        {
            Id                = t.Id,
            Name              = t.Name,
            DocumentTypeId    = t.DocumentTypeId,
            FrxFilePath       = t.FrxFilePath,
            FrxContent        = Encoding.UTF8.GetBytes(frx),
            Description       = t.Description,
            IsDefault         = t.IsDefault,
            IsActive          = t.IsActive,
            SqlViewName       = "vw_ReportDocument",
            KeyColumn         = "BelgeId",
            OutputOptionsJson = t.OutputOptionsJson,
            CreatedAt         = t.CreatedAt,
        };
        await _templateRepo.SaveAsync(updated, ct);

        return Json(new
        {
            success = true,
            templateId = newId,
            message = "Ocaksan teklif tasarimi uygulandi.",
        });
    }

    private static string BuildOcaksanQuoteFrx(string name)
    {
        var safeName = System.Security.SecurityElement.Escape(name) ?? "Ocaksan Teklif";
        var now = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss");
        var stamp = $"v1-ocaksan · {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        return $$"""
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Name="{{safeName}}" ReportInfo.Created="{{now}}" ReportInfo.Modified="{{now}}" ReportInfo.CreatorVersion="2026.1.8">
  <Dictionary>
    <TableDataSource Name="vw_ReportDocument" Enabled="true">
      <Column Name="BelgeId" DataType="System.Int32"/>
      <Column Name="BelgeNo" DataType="System.String"/>
      <Column Name="BelgeTarihi" DataType="System.DateTime"/>
      <Column Name="GecerlilikTarihi" DataType="System.DateTime"/>
      <Column Name="ParaBirimi" DataType="System.String"/>
      <Column Name="AraToplam" DataType="System.Decimal"/>
      <Column Name="KdvTutari" DataType="System.Decimal"/>
      <Column Name="GenelToplam" DataType="System.Decimal"/>
      <Column Name="CariUnvani" DataType="System.String"/>
      <Column Name="CariAdres" DataType="System.String"/>
      <Column Name="CariSehir" DataType="System.String"/>
      <Column Name="CariIlce" DataType="System.String"/>
      <Column Name="CariVergiNo" DataType="System.String"/>
      <Column Name="TemsilciAdi" DataType="System.String"/>
      <Column Name="BelgeNotu" DataType="System.String"/>
      <Column Name="SirketAdi" DataType="System.String"/>
      <Column Name="SirketTamAdres" DataType="System.String"/>
      <Column Name="SirketVergiSatiri" DataType="System.String"/>
      <Column Name="KalemId" DataType="System.Int32"/>
      <Column Name="SiraNo" DataType="System.Int32"/>
      <Column Name="MalzemeAdi" DataType="System.String"/>
      <Column Name="MalzemeKodu" DataType="System.String"/>
      <Column Name="MalzemeAciklamasi" DataType="System.String"/>
      <Column Name="Miktar" DataType="System.Decimal"/>
      <Column Name="BirimKodu" DataType="System.String"/>
      <Column Name="BirimFiyat" DataType="System.Decimal"/>
      <Column Name="SatirToplami" DataType="System.Decimal"/>
      <Column Name="KombinasyonDetay" DataType="System.String"/>
    </TableDataSource>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="false" PaperWidth="210" PaperHeight="297" MarginLeft="10" MarginRight="10" MarginTop="10" MarginBottom="10">

    <!-- ============ ÜST BANNER (logo + sirket + TEKLIF kutusu) ============ -->
    <ReportTitleBand Name="ReportTitle1" Top="0" Width="718.2" Height="200">
      <!-- SOL: logo placeholder + sirket -->
      <PictureObject Name="LogoPic" Left="0" Top="0" Width="120" Height="80" Border.Lines="All" Border.Width="1" Border.Color="220,220,220" SizeMode="Center"/>
      <TextObject Name="LblLogoHint" Left="0" Top="60" Width="120" Height="20" Text="LOGO" Font="Arial, 8pt, style=Italic" HorzAlign="Center" VertAlign="Bottom" TextColor="180,180,180"/>
      <TextObject Name="SirketAdi" Left="0" Top="84" Width="380" Height="22" Text="[vw_ReportDocument.SirketAdi]" Font="Arial, 14pt, style=Bold"/>
      <TextObject Name="SirketUnvan" Left="0" Top="106" Width="380" Height="14" Text="" Font="Arial, 7pt, style=Bold"/>
      <TextObject Name="SirketAdres" Left="0" Top="120" Width="380" Height="14" Text="[vw_ReportDocument.SirketTamAdres]" Font="Arial, 8pt"/>
      <TextObject Name="SirketTel" Left="0" Top="138" Width="380" Height="14" Text="TEL :" Font="Arial, 8pt, style=Bold"/>
      <TextObject Name="SirketFax" Left="0" Top="152" Width="380" Height="14" Text="FAX:" Font="Arial, 8pt, style=Bold"/>

      <!-- SAĞ: TEKLIF kutusu -->
      <ShapeObject Name="TklfBox" Left="540" Top="0" Width="178" Height="32" Shape="Rectangle" Border.Lines="All" Border.Width="2" FillColor="245,245,245"/>
      <TextObject Name="TklfBoxLbl" Left="540" Top="0" Width="178" Height="32" Text="TEKLİF" Font="Arial, 18pt, style=Bold" HorzAlign="Center" VertAlign="Center"/>
      <TextObject Name="LblTarih" Left="540" Top="40" Width="80" Height="16" Text="TARİH :" Font="Arial, 9pt, style=Bold"/>
      <TextObject Name="ValTarih" Left="620" Top="40" Width="98" Height="16" Text="[vw_ReportDocument.BelgeTarihi]" Font="Arial, 9pt, style=Bold" HorzAlign="Right" Format="Date" Format.Format="d"/>
      <TextObject Name="LblTeklifNo" Left="540" Top="58" Width="80" Height="16" Text="TEKLİF NO:" Font="Arial, 9pt, style=Bold"/>
      <TextObject Name="ValTeklifNo" Left="620" Top="58" Width="98" Height="16" Text="[vw_ReportDocument.BelgeNo]" Font="Arial, 9pt, style=Bold" HorzAlign="Right"/>

      <!-- MUSTERI BLOGU (border'li 3 satir) -->
      <ShapeObject Name="MusBoxF" Left="0" Top="172" Width="80" Height="20" Shape="Rectangle" Border.Lines="All" Border.Width="1" FillColor="245,245,245"/>
      <TextObject Name="LblFirma" Left="0" Top="172" Width="80" Height="20" Text="FİRMA ADI :" Font="Arial, 8pt, style=Bold" VertAlign="Center" HorzAlign="Center"/>
      <ShapeObject Name="MusBoxFV" Left="80" Top="172" Width="638" Height="20" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="ValFirma" Left="84" Top="172" Width="630" Height="20" Text="[vw_ReportDocument.CariUnvani]" Font="Arial, 9pt, style=Bold" VertAlign="Center"/>
    </ReportTitleBand>

    <!-- Musteri devami: Proje + Adres -->
    <PageHeaderBand Name="PageHeader1" Top="204" Width="718.2" Height="44">
      <ShapeObject Name="ProjBoxL" Left="0" Top="0" Width="80" Height="20" Shape="Rectangle" Border.Lines="All" Border.Width="1" FillColor="245,245,245"/>
      <TextObject Name="LblProje" Left="0" Top="0" Width="80" Height="20" Text="PROJE ADI :" Font="Arial, 8pt, style=Bold" VertAlign="Center" HorzAlign="Center"/>
      <ShapeObject Name="ProjBoxV" Left="80" Top="0" Width="638" Height="20" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="ValProje" Left="84" Top="0" Width="630" Height="20" Text="[vw_ReportDocument.BelgeNotu]" Font="Arial, 9pt, style=Bold" VertAlign="Center"/>

      <ShapeObject Name="AdrBoxL" Left="0" Top="20" Width="80" Height="20" Shape="Rectangle" Border.Lines="All" Border.Width="1" FillColor="245,245,245"/>
      <TextObject Name="LblAdres" Left="0" Top="20" Width="80" Height="20" Text="ADRES :" Font="Arial, 8pt, style=Bold" VertAlign="Center" HorzAlign="Center"/>
      <ShapeObject Name="AdrBoxV" Left="80" Top="20" Width="638" Height="20" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="ValAdres" Left="84" Top="20" Width="630" Height="20" Text="[vw_ReportDocument.CariAdres]" Font="Arial, 9pt" VertAlign="Center"/>
    </PageHeaderBand>

    <!-- KOLON BASLIK -->
    <ColumnHeaderBand Name="ColumnHeader1" Top="252" Width="718.2" Height="26" FillColor="245,245,245">
      <ShapeObject Name="HdrBg" Left="0" Top="0" Width="718" Height="26" Shape="Rectangle" Border.Lines="All" Border.Width="1" FillColor="245,245,245"/>
      <TextObject Name="HdrSira" Left="0" Top="0" Width="50" Height="26" Text="SIRA NO" Font="Arial, 8pt, style=Bold" HorzAlign="Center" VertAlign="Center" Border.Lines="Right" Border.Width="1"/>
      <TextObject Name="HdrAck"  Left="50" Top="0" Width="380" Height="26" Text="AÇIKLAMA" Font="Arial, 8pt, style=Bold" HorzAlign="Center" VertAlign="Center" Border.Lines="Right" Border.Width="1"/>
      <TextObject Name="HdrAdet" Left="430" Top="0" Width="60" Height="26" Text="ADET" Font="Arial, 8pt, style=Bold" HorzAlign="Center" VertAlign="Center" Border.Lines="Right" Border.Width="1"/>
      <TextObject Name="HdrFyt"  Left="490" Top="0" Width="100" Height="26" Text="BİRİM FİYAT" Font="Arial, 8pt, style=Bold" HorzAlign="Center" VertAlign="Center" Border.Lines="Right" Border.Width="1"/>
      <TextObject Name="HdrTtr"  Left="590" Top="0" Width="128" Height="26" Text="TUTAR" Font="Arial, 8pt, style=Bold" HorzAlign="Center" VertAlign="Center"/>
    </ColumnHeaderBand>

    <!-- KALEM SATIRI (border'li, alt italik aciklama) -->
    <DataBand Name="Data1" Top="282" Width="718.2" Height="48" DataSource="vw_ReportDocument" CanGrow="true">
      <ShapeObject Name="RowBg" Left="0" Top="0" Width="718" Height="48" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="RowSira" Left="0" Top="2" Width="50" Height="20" Text="[vw_ReportDocument.SiraNo]" Font="Arial, 9pt, style=Bold" HorzAlign="Center" VertAlign="Center" Border.Lines="Right" Border.Width="1"/>

      <TextObject Name="RowAdName" Left="54" Top="4" Width="372" Height="18" Text="[vw_ReportDocument.MalzemeAdi]" Font="Arial, 9pt, style=Bold" VertAlign="Top"/>
      <TextObject Name="RowAdSize" Left="54" Top="22" Width="372" Height="14" Text="[vw_ReportDocument.MalzemeAciklamasi]" Font="Arial, 8pt, style=Italic" TextColor="80,80,80" VertAlign="Top" CanGrow="true" WordWrap="true"/>
      <TextObject Name="RowAdDesc" Left="54" Top="32" Width="372" Height="14" Text="[vw_ReportDocument.KombinasyonDetay]" Font="Arial, 7pt, style=Italic" TextColor="120,120,120" VertAlign="Top" CanGrow="true" WordWrap="true"/>

      <TextObject Name="RowAdSep" Left="430" Top="0" Width="0" Height="48" Border.Lines="Left" Border.Width="1"/>
      <TextObject Name="RowAdet"  Left="430" Top="2" Width="60" Height="20" Text="[vw_ReportDocument.Miktar]" Font="Arial, 9pt" HorzAlign="Center" VertAlign="Center" Format="Number" Format.UseLocale="true" Format.DecimalDigits="0" Border.Lines="Right" Border.Width="1"/>
      <TextObject Name="RowFyt"   Left="490" Top="2" Width="100" Height="20" Text="[String.Format(&quot;€ {0:#,##0.00}&quot;, [vw_ReportDocument.BirimFiyat])]" Font="Arial, 9pt" HorzAlign="Right" VertAlign="Center" Border.Lines="Right" Border.Width="1"/>
      <TextObject Name="RowTtr"   Left="590" Top="2" Width="128" Height="20" Text="[String.Format(&quot;€ {0:#,##0.00}&quot;, [vw_ReportDocument.SatirToplami])]" Font="Arial, 9pt, style=Bold" HorzAlign="Right" VertAlign="Center"/>
    </DataBand>

    <!-- TOPLAM + GENEL TOPLAM -->
    <ReportSummaryBand Name="ReportSummary1" Top="334" Width="718.2" Height="80">
      <ShapeObject Name="SumBox1" Left="430" Top="6" Width="288" Height="26" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="SumLbl1" Left="430" Top="6" Width="160" Height="26" Text="TOPLAM" Font="Arial, 10pt, style=Bold" HorzAlign="Center" VertAlign="Center" Border.Lines="Right" Border.Width="1"/>
      <TextObject Name="SumVal1" Left="592" Top="6" Width="124" Height="26" Text="[String.Format(&quot;€ {0:#,##0.00}&quot;, [vw_ReportDocument.AraToplam])]" Font="Arial, 10pt, style=Bold" HorzAlign="Right" VertAlign="Center"/>

      <ShapeObject Name="SumBox2" Left="430" Top="36" Width="288" Height="28" Shape="Rectangle" Border.Lines="All" Border.Width="2" FillColor="240,240,240"/>
      <TextObject Name="SumLbl2" Left="430" Top="36" Width="160" Height="28" Text="GENEL TOPLAM" Font="Arial, 11pt, style=Bold" HorzAlign="Center" VertAlign="Center" Border.Lines="Right" Border.Width="2"/>
      <TextObject Name="SumVal2" Left="592" Top="36" Width="124" Height="28" Text="[String.Format(&quot;€ {0:#,##0.00}&quot;, [vw_ReportDocument.GenelToplam])]" Font="Arial, 11pt, style=Bold" HorzAlign="Right" VertAlign="Center"/>
    </ReportSummaryBand>

    <!-- ALT NOTLAR + BANKA + KASE -->
    <PageFooterBand Name="PageFooter1" Top="418" Width="718.2" Height="240">
      <TextObject Name="LblPaket" Left="0" Top="0" Width="100" Height="14" Text="PAKETLEME" Font="Arial, 8pt, style=Bold"/>
      <TextObject Name="ValPaket" Left="100" Top="0" Width="618" Height="14" Text=":" Font="Arial, 8pt"/>
      <TextObject Name="LblNak"   Left="0" Top="14" Width="100" Height="14" Text="NAKLİYE" Font="Arial, 8pt, style=Bold"/>
      <TextObject Name="ValNak"   Left="100" Top="14" Width="618" Height="14" Text=":" Font="Arial, 8pt"/>
      <TextObject Name="LblOd"    Left="0" Top="28" Width="100" Height="14" Text="ÖDEME" Font="Arial, 8pt, style=Bold"/>
      <TextObject Name="ValOd"    Left="100" Top="28" Width="618" Height="14" Text=":" Font="Arial, 8pt"/>

      <ShapeObject Name="NoteBox" Left="0" Top="48" Width="718" Height="84" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="Note1" Left="6" Top="50" Width="710" Height="14" Text="" Font="Arial, 8pt"/>
      <TextObject Name="Note2" Left="6" Top="64" Width="710" Height="14" Text="" Font="Arial, 8pt"/>
      <TextObject Name="Note3" Left="6" Top="78" Width="710" Height="14" Text="" Font="Arial, 8pt"/>
      <TextObject Name="Note4" Left="6" Top="92" Width="710" Height="14" Text="" Font="Arial, 8pt, style=Bold"/>
      <TextObject Name="Note5" Left="6" Top="106" Width="710" Height="14" Text="" Font="Arial, 8pt"/>

      <ShapeObject Name="BankBox" Left="0" Top="138" Width="200" Height="56" Shape="Rectangle" Border.Lines="All" Border.Width="1" FillColor="245,245,245"/>
      <TextObject Name="BankLbl" Left="0" Top="138" Width="200" Height="56" Text="BANKA BİLGİSİ" Font="Arial, 8pt, style=Bold" HorzAlign="Center" VertAlign="Center"/>
      <ShapeObject Name="BankBoxR" Left="200" Top="138" Width="518" Height="56" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="Bank1" Left="206" Top="140" Width="510" Height="14" Text="" Font="Arial, 8pt"/>
      <TextObject Name="Bank2" Left="206" Top="156" Width="510" Height="14" Text="" Font="Arial, 8pt"/>
      <TextObject Name="Bank3" Left="206" Top="172" Width="510" Height="14" Text="" Font="Arial, 8pt"/>

      <ShapeObject Name="KaseBox1" Left="0" Top="200" Width="359" Height="36" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="KaseLbl1" Left="0" Top="200" Width="359" Height="14" Text="TEKLİFİ VEREN :" Font="Arial, 8pt, style=Bold" VertAlign="Top" HorzAlign="Center" FillColor="245,245,245"/>
      <TextObject Name="KaseVal1" Left="0" Top="216" Width="359" Height="20" Text="[vw_ReportDocument.SirketAdi]" Font="Arial, 9pt, style=Bold" HorzAlign="Center" VertAlign="Center"/>

      <ShapeObject Name="KaseBox2" Left="359" Top="200" Width="359" Height="36" Shape="Rectangle" Border.Lines="All" Border.Width="1"/>
      <TextObject Name="KaseLbl2" Left="359" Top="200" Width="359" Height="14" Text="SİPARİŞİ ONAYLAYAN FİRMA" Font="Arial, 8pt, style=Bold" VertAlign="Top" HorzAlign="Center" FillColor="245,245,245"/>
      <TextObject Name="KaseVal2" Left="359" Top="216" Width="359" Height="20" Text="KAŞE" Font="Arial, 9pt, style=Italic" HorzAlign="Center" VertAlign="Center" TextColor="180,180,180"/>

      <TextObject Name="StampText" Left="0" Top="220" Width="718" Height="10" Text="Tasarım {{stamp}}" Font="Arial, 6pt, style=Italic" HorzAlign="Right" TextColor="180,180,180"/>
    </PageFooterBand>

  </ReportPage>
</Report>
""";
    }

    // Kullanim: GET /Document/CreateAndApplySalesQuoteDesign?name=Deneme
    // Sonuc: "satis_teklifi" belge tipine bagli yeni bir ReportTemplate olusturur,
    // vw_ReportDocument + vw_DocumentCombination source'larini ekler, master-detail
    // frx'i yukler, IsDefault=true IsActive=true yapar.
    [AllowAnonymous]
    [HttpGet("/Document/CreateAndApplySalesQuoteDesign")]
    public async Task<IActionResult> CreateAndApplySalesQuoteDesign(string? name, CancellationToken ct)
    {
        var docType = await _docTypeRepo.GetByCodeAsync("satis_teklifi", ct);
        if (docType is null)
            return Json(new { success = false, message = "'satis_teklifi' belge tipi bulunamadi." });

        var finalName = string.IsNullOrWhiteSpace(name) ? "Deneme" : name.Trim();

        // 1) Idempotent davranis: ayni belge tipinde ayni isimli template'leri bul.
        //    Birincisi (en eski Id) GUNCELLENIR, fazla duplicate'ler SILINIR.
        //    Bu sayede her cagrida yeni satir olusmaz, Belge Sablonlari sayfasinda
        //    tek bir "Deneme" gorunur. Diger aktif default'lar IsDefault=false yapilir.
        var existing = await _templateRepo.GetByDocumentTypeIdAsync(docType.Id, ct);
        var sameName = existing
            .Where(x => string.Equals(x.Name, finalName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Id)
            .ToList();

        int newId;
        if (sameName.Count > 0)
        {
            newId = sameName[0].Id;
            // Fazla duplicate'leri sil (sources CASCADE ile silinir)
            foreach (var dup in sameName.Skip(1))
            {
                await _templateRepo.DeleteAsync(dup.Id, ct);
            }
        }
        else
        {
            var blank = new Domain.Entities.ReportTemplate
            {
                Name           = finalName,
                DocumentTypeId = docType.Id,
                FrxContent     = Array.Empty<byte>(),
                SqlViewName    = "vw_ReportDocument",
                KeyColumn      = "BelgeId",
                IsDefault      = true,
                IsActive       = true,
            };
            newId = await _templateRepo.SaveAsync(blank, ct);
        }

        // Diger default'lari kaldir (tek default kalsin)
        foreach (var e in existing.Where(x => x.Id != newId && x.IsDefault))
        {
            e.IsDefault = false;
            await _templateRepo.SaveAsync(e, ct);
        }

        // 2) Frx iceriglerini uygula (ayni mantigi ApplySalesQuoteDesign'dan cagir)
        return await ApplySalesQuoteDesign(newId, ct);
    }

    // ── Bir view'in kolon listesini ve ilk N satirini dok (debug) ──
    [AllowAnonymous]
    [HttpGet("/api/designer/debug/view/{viewName}/{recordId:int?}")]
    public async Task<IActionResult> DebugView(string viewName, int? recordId, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(viewName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return BadRequest(new { error = "Gecersiz view adi" });

        IReadOnlyList<string> cols = Array.Empty<string>();
        try { cols = await _reportDataRepo.GetViewColumnsAsync(viewName, ct); }
        catch (Exception ex) { return Json(new { viewName, error = ex.Message }); }

        System.Data.DataTable? sample = null;
        if (recordId.HasValue && recordId.Value > 0)
        {
            try { sample = await _reportDataRepo.GetReportDataAsync(viewName, recordId.Value, null, null, null, ct); }
            catch (Exception ex) { return Json(new { viewName, columns = cols, sampleError = ex.Message }); }
        }

        var rows = new List<Dictionary<string, object?>>();
        if (sample != null)
        {
            for (int i = 0; i < Math.Min(sample.Rows.Count, 5); i++)
            {
                var d = new Dictionary<string, object?>();
                foreach (System.Data.DataColumn c in sample.Columns)
                    d[c.ColumnName] = sample.Rows[i][c] == DBNull.Value ? null : sample.Rows[i][c];
                rows.Add(d);
            }
        }

        return Json(new
        {
            viewName,
            recordId,
            columns = cols,
            rowCount = sample?.Rows.Count ?? -1,
            sample = rows,
        });
    }

    // ── Tum template'leri listele (debug) ──
    [AllowAnonymous]
    [HttpGet("/api/designer/debug/list")]
    public async Task<IActionResult> DebugList(CancellationToken ct)
    {
        var all = await _templateRepo.GetAllAsync(ct);
        var docTypes = await _docTypeRepo.GetAllAsync(ct);
        var dtById = docTypes.ToDictionary(d => d.Id, d => d);
        return Json(all.Select(t => new
        {
            t.Id,
            t.Name,
            DocumentTypeId   = t.DocumentTypeId,
            DocumentTypeCode = dtById.TryGetValue(t.DocumentTypeId, out var dt) ? dt.Code : null,
            DocumentTypeName = dtById.TryGetValue(t.DocumentTypeId, out var dt2) ? dt2.Name : null,
            t.IsActive,
            t.IsDefault,
            t.SqlViewName,
            FrxSize = t.FrxContent?.Length ?? 0,
            t.CreatedAt,
        }));
    }

    // ── Tani (Debug) — DataBand DataSource analizi ───────────────────────────
    // Kullanici "2 kalem var ama sadece 1 basildi" dediginde, frx'in DataBand
    // DataSource'u ile stored sources alias'larinin eslesip eslesmedigini ve
    // her source'un dondurdugu satir sayisini gormek icin. AllowAnonymous —
    // hassas bilgi icermez (sadece kolon + alias + sayi).
    [AllowAnonymous]
    [HttpGet("/api/designer/debug/{templateId:int}/{recordId:int}")]
    public async Task<IActionResult> DebugTemplate(int templateId, int recordId, CancellationToken ct)
    {
        var t = await _templateRepo.GetByIdAsync(templateId, ct);
        if (t is null) return NotFound();

        var frxStr = t.FrxContent is { Length: > 0 }
            ? System.Text.Encoding.UTF8.GetString(t.FrxContent)
            : null;

        var dataBandRefs = new List<object>();
        var dictSourceAliases = new List<string>();
        if (!string.IsNullOrWhiteSpace(frxStr))
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(frxStr);
                foreach (var el in doc.Descendants())
                {
                    if (el.Name.LocalName.EndsWith("Band") || el.Name.LocalName == "DataBand")
                    {
                        var ds = el.Attribute("DataSource")?.Value;
                        if (!string.IsNullOrWhiteSpace(ds))
                        {
                            dataBandRefs.Add(new { element = el.Name.LocalName, name = el.Attribute("Name")?.Value, dataSource = ds });
                        }
                    }
                    if (el.Name.LocalName == "TableDataSource" || el.Name.LocalName == "BusinessObjectDataSource")
                    {
                        var dictName = el.Attribute("Name")?.Value;
                        if (!string.IsNullOrWhiteSpace(dictName))
                            dictSourceAliases.Add($"{el.Name.LocalName}:{dictName}");
                    }
                }
            }
            catch { /* parse hatasini yoksay */ }
        }

        var stored = await _sourceRepo.GetByTemplateIdAsync(templateId, ct);
        var srcInfo = new List<object>();
        foreach (var s in stored)
        {
            int rowCount = -1;
            try
            {
                var dt = await _reportDataRepo.GetReportDataAsync(s.ViewName, recordId, s.KeyColumn, s.SortColumn, s.SortDirection, ct);
                rowCount = dt.Rows.Count;
            }
            catch (Exception ex)
            {
                srcInfo.Add(new { s.SourceName, s.ViewName, s.KeyColumn, s.SortColumn, s.SortDirection, error = ex.Message });
                continue;
            }
            srcInfo.Add(new { s.SourceName, s.ViewName, s.KeyColumn, s.SortColumn, s.SortDirection, rowCount });
        }

        return Json(new
        {
            templateId,
            recordId,
            templateName = t.Name,
            storedSources = srcInfo,
            dictionarySourceAliases = dictSourceAliases,
            dataBandReferences = dataBandRefs,
            frxPreview = frxStr?.Length > 3000 ? frxStr.Substring(0, 3000) + "... [truncated]" : frxStr
        });
    }

    // ── Siralama Ayarlari (Per-Template) ─────────────────────────────────────
    // Generation sirasinda view sorgusuna ORDER BY [OrderColumn] OrderDirection
    // olarak eklenir. View'in INFORMATION_SCHEMA'sindan kullanici secer.
    [HttpGet("/Document/SortOptions/{templateId:int}")]
    public async Task<IActionResult> GetSortOptions(int templateId, CancellationToken ct)
    {
        var t = await _templateRepo.GetByIdAsync(templateId, ct);
        if (t is null) return NotFound();

        var docType = await _docTypeRepo.GetByIdAsync(t.DocumentTypeId, ct);
        var viewName = !string.IsNullOrWhiteSpace(t.SqlViewName) ? t.SqlViewName : docType?.SqlViewName;

        IReadOnlyList<string> columns = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(viewName))
        {
            try
            {
                var raw = await _reportDataRepo.GetViewColumnsAsync(viewName, ct);
                columns = raw.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch { /* sessiz */ }
        }

        return Json(new
        {
            success   = true,
            view      = viewName,
            columns,
            column    = t.OrderColumn,
            direction = t.OrderDirection ?? "ASC"
        });
    }

    [HttpPost("/Document/SortOptions/{templateId:int}")]
    public async Task<IActionResult> SaveSortOptions(int templateId, [FromBody] SaveSortOptionsRequest request, CancellationToken ct)
    {
        var t = await _templateRepo.GetByIdAsync(templateId, ct);
        if (t is null) return NotFound();

        var col = string.IsNullOrWhiteSpace(request?.Column) ? null : request!.Column!.Trim();
        if (col != null && !System.Text.RegularExpressions.Regex.IsMatch(col, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return Json(new { success = false, message = "Gecersiz kolon adi." });

        var dir = (request?.Direction ?? "ASC").Trim().ToUpperInvariant();
        if (dir != "ASC" && dir != "DESC") dir = "ASC";

        var updated = new Domain.Entities.ReportTemplate
        {
            Id                = t.Id,
            Name              = t.Name,
            DocumentTypeId    = t.DocumentTypeId,
            FrxFilePath       = t.FrxFilePath,
            FrxContent        = t.FrxContent,
            Description       = t.Description,
            IsDefault         = t.IsDefault,
            IsActive          = t.IsActive,
            SqlViewName       = t.SqlViewName,
            KeyColumn         = t.KeyColumn,
            OutputOptionsJson = t.OutputOptionsJson,
            OrderColumn       = col,
            OrderDirection    = col == null ? null : dir,
            CreatedAt         = t.CreatedAt,
        };
        await _templateRepo.SaveAsync(updated, ct);
        return Json(new { success = true });
    }

    // ── Varsayilan Yap ────────────────────────────────────────────────────────

    [HttpPost("/Document/SetDefault/{templateId:int}")]
    public async Task<IActionResult> SetDefault(int templateId, CancellationToken ct)
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

    [HttpPost("/Document/Rename/{templateId:int}")]
    public async Task<IActionResult> Rename(int templateId, [FromForm] string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Sablon adi bos olamaz.");

        var template = await _templateRepo.GetByIdAsync(templateId, ct);
        if (template is null)
            return NotFound();

        var updated = new Domain.Entities.ReportTemplate
        {
            Id                = template.Id,
            Name              = name.Trim(),
            DocumentTypeId    = template.DocumentTypeId,
            FrxFilePath       = template.FrxFilePath,
            FrxContent        = template.FrxContent,
            Description       = template.Description,
            IsDefault         = template.IsDefault,
            IsActive          = template.IsActive,
            SqlViewName       = template.SqlViewName,
            KeyColumn         = template.KeyColumn,
            OutputOptionsJson = template.OutputOptionsJson,
            OrderColumn       = template.OrderColumn,
            OrderDirection    = template.OrderDirection,
            CreatedAt         = template.CreatedAt,
        };
        await _templateRepo.SaveAsync(updated, ct);

        return Json(new { success = true });
    }

    // ── Belge Mail Alici ────────────────────────────────────────────────────
    // Cikti Secenekleri / Mail gonderim modali acildiginda cari karttan email
    // adresini cekip kullaniciya gosterir; kullanici degistirebilir veya virgul/
    // noktali virgulle ek alici(lar) ekleyebilir. SendEmail bu degeri kabul eder.
    [HttpGet("/Document/GetEmailRecipient")]
    public async Task<IActionResult> GetEmailRecipient([FromQuery] int recordId, CancellationToken ct)
    {
        if (recordId <= 0)
            return Json(new { success = false, email = "", message = "Belge id zorunlu." });
        var doc = await _documentRepo.GetByIdAsync(recordId, ct);
        if (doc?.ContactId is not int cid || cid <= 0)
            return Json(new { success = true, email = "", message = "Belgede cari atanmamis." });
        var contact = await _financeRepo.GetContactByIdAsync(cid, ct);
        var email = contact?.Email ?? "";
        return Json(new
        {
            success     = true,
            email,
            contactId   = cid,
            contactCode = contact?.AccountCode,
            contactName = contact?.AccountTitle,
        });
    }

    // ── Mail Gonder (PDF ek olarak) ─────────────────────────────────────────

    [HttpPost("/Document/SendEmail")]
    public async Task<IActionResult> SendEmail([FromBody] SendTemplateEmailRequest request, CancellationToken ct)
    {
        if (request is null)
            return Json(new { success = false, message = "Gecersiz istek." });
        if (string.IsNullOrWhiteSpace(request.Subject))
            return Json(new { success = false, message = "Konu zorunludur." });

        var template = await _templateRepo.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
            return Json(new { success = false, message = "Sablon bulunamadi." });

        // Alici adresi: bos ise belge → cari kart → email zincirinden cek
        var toEmail = request.ToEmail;
        if (string.IsNullOrWhiteSpace(toEmail) && request.RecordId.HasValue && request.RecordId.Value > 0)
        {
            var doc = await _documentRepo.GetByIdAsync(request.RecordId.Value, ct);
            if (doc?.ContactId is int cid && cid > 0)
            {
                var contact = await _financeRepo.GetContactByIdAsync(cid, ct);
                if (!string.IsNullOrWhiteSpace(contact?.Email))
                    toEmail = contact!.Email;
            }
        }
        if (string.IsNullOrWhiteSpace(toEmail))
            return Json(new { success = false, message = "Alici adresi bulunamadi. Cari kartta e-posta tanimli degil." });

        // SMTP profili (ilk aktif profil; Admin tarafinda tanimlanan)
        var profiles = await _smtpProfileRepo.GetAllAsync(ct);
        var smtp = profiles.FirstOrDefault(p => p.IsActive);
        if (smtp is null)
            return Json(new { success = false, message = "Aktif bir SMTP profili tanimli degil. Mail ayarlarindan profil olusturun." });

        try
        {
            byte[]? attachmentBytes = null;
            var attachmentName = SanitizeFileName(template.Name) + ".pdf";

            if (request.RecordId.HasValue && request.RecordId.Value > 0)
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
            foreach (var addr in (toEmail ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
    int TemplateId,
    int? RecordId,
    string? ToEmail,
    string Subject,
    string? BodyHtml,
    string? BodyText);

public sealed record SaveOutputOptionsRequest(string? OptionsJson);

public sealed record SaveSortOptionsRequest(string? Column, string? Direction);

public sealed record SaveSourcesRequest(List<SaveSourceItem> Sources);

public sealed record SaveSourceItem(
    string SourceName,
    string ViewName,
    string KeyColumn,
    string? ParentSourceName,
    string? ParentKeyColumn,
    bool IsPrimary,
    string? SortColumn = null,
    string? SortDirection = null);

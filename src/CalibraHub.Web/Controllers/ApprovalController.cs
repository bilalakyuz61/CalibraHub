using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Approval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Xml.Linq;
using System.Xml.Xsl;
using System.Xml;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class ApprovalController : Controller
{
    private readonly IApprovalQueueService _approvalQueueService;
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly IDocumentImportService _documentImportService;
    private readonly IIncomingDocumentRepository _incomingDocumentRepository;
    private readonly IApprovalFlowService _approvalFlowService;
    private readonly IDocumentService _documentService;

    public ApprovalController(
        IApprovalQueueService approvalQueueService,
        IUiConfigurationService uiConfigurationService,
        IDocumentImportService documentImportService,
        IIncomingDocumentRepository incomingDocumentRepository,
        IApprovalFlowService approvalFlowService,
        IDocumentService documentService)
    {
        _approvalQueueService = approvalQueueService;
        _uiConfigurationService = uiConfigurationService;
        _documentImportService = documentImportService;
        _incomingDocumentRepository = incomingDocumentRepository;
        _approvalFlowService = approvalFlowService;
        _documentService = documentService;
    }

    public Task<IActionResult> Index(
        string? kind,
        int? page,
        int? pageSize,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? isProcessed,
        CancellationToken cancellationToken)
    {
        var normalizedKind = NormalizeKind(kind);
        if (!string.IsNullOrWhiteSpace(normalizedKind))
        {
            return Task.FromResult(RedirectToKindPage(normalizedKind, page, pageSize, dateFrom, dateTo, isProcessed));
        }

        return RenderQueuePageAsync(kind: null, page, pageSize, dateFrom, dateTo, isProcessed, cancellationToken);
    }

    public Task<IActionResult> EInvoice(
        int? page,
        int? pageSize,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? isProcessed,
        CancellationToken cancellationToken) =>
        RenderQueuePageAsync("EInvoice", page, pageSize, dateFrom, dateTo, isProcessed, cancellationToken);

    public Task<IActionResult> EArchive(
        int? page,
        int? pageSize,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? isProcessed,
        CancellationToken cancellationToken) =>
        RenderQueuePageAsync("EArchive", page, pageSize, dateFrom, dateTo, isProcessed, cancellationToken);

    public Task<IActionResult> EDispatch(
        int? page,
        int? pageSize,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? isProcessed,
        CancellationToken cancellationToken) =>
        RenderQueuePageAsync("EDispatch", page, pageSize, dateFrom, dateTo, isProcessed, cancellationToken);

    [HttpGet]
    public async Task<IActionResult> ViewPayload(Guid id, CancellationToken cancellationToken)
    {
        var document = await _incomingDocumentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var raw = document.PayloadRaw ?? string.Empty;
        string xmlContent;
        try
        {
            xmlContent = XDocument.Parse(raw).ToString();
        }
        catch
        {
            xmlContent = raw;
        }

        var vm = new ApprovalDocumentViewerViewModel
        {
            Id = document.Id,
            DocumentNumber = document.DocumentNumber,
            Kind = document.Kind.ToString(),
            IssueDate = document.IssueDate,
            SenderTaxNumber = document.SenderTaxNumber ?? string.Empty,
            SenderName = document.SenderName,
            EnvelopeId = document.EnvelopeId ?? string.Empty,
            XmlContent = xmlContent,
            RenderData = ParseInvoiceRenderData(xmlContent)
        };

        ViewData["Title"] = $"Belge: {document.DocumentNumber}";
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> DocumentLines(Guid id, CancellationToken cancellationToken)
    {
        // 1. Sağ tıklanan belgenin XML/Veritabanı dökümanı bulunur
        var document = await _incomingDocumentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null) 
            return Content("<div class='p-3 text-center text-danger fw-medium'>Belge veritabanında bulunamadı.</div>", "text/html");

        // 2. Anlık olarak (Payload Parser ile) XML parçalanır ve satır dizileri bulunur
        var xmlContent = document.PayloadRaw ?? string.Empty;
        var rd = ParseInvoiceRenderData(xmlContent);
        
        if (rd is null || rd.Lines.Count == 0) 
            return Content("<div class='p-4 text-center text-muted'><svg viewBox='0 0 24 24' width='24' height='24' stroke='currentColor' stroke-width='2' fill='none' class='mb-2 opacity-50'><circle cx='12' cy='12' r='10'></circle><line x1='12' y1='8' x2='12' y2='12'></line><line x1='12' y1='16' x2='12.01' y2='16'></line></svg><br>Bu belgede okunabilir bir kalem (satır) verisine ulaşılamadı.</div>", "text/html");

        // 3. Bulunan satırlar o yaratılan kompakt görünüme (_DocumentLines) çizilerek yollanır
        return PartialView("_DocumentLines", rd.Lines);
    }

    private static Web.Models.Approval.InvoiceRenderData? ParseInvoiceRenderData(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent) || !xmlContent.TrimStart().StartsWith('<'))
            return null;
        try
        {
            var root = XDocument.Parse(xmlContent).Root;
            if (root is null) return null;

            string? Val(XElement el, string name) =>
                el.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value?.Trim();
            XElement? Elem(XElement el, string name) =>
                el.Elements().FirstOrDefault(e => e.Name.LocalName == name);

            Web.Models.Approval.InvoiceParty? ParseParty(XElement? partyEl)
            {
                if (partyEl is null) return null;
                var partyNameEl = Elem(partyEl, "PartyName");
                var name = Val(partyEl, "Name")
                    ?? (partyNameEl is not null ? Val(partyNameEl, "Name") : null);
                var taxScheme = Elem(partyEl, "PartyTaxScheme");
                var taxNumber = taxScheme is not null ? Val(taxScheme, "CompanyID") : null;
                var taxOfficeName = taxScheme is not null
                    ? (Elem(taxScheme, "TaxScheme") is { } ts ? Val(ts, "Name") : null)
                    : null;
                var postalAddr = Elem(partyEl, "PostalAddress");
                string? addrLine = null, city = null, country = null;
                if (postalAddr is not null)
                {
                    addrLine = Val(postalAddr, "StreetName");
                    city = Val(postalAddr, "CityName") ?? Val(postalAddr, "CitySubdivisionName");
                    country = Elem(postalAddr, "Country") is { } c ? Val(c, "Name") : null;
                }
                return new Web.Models.Approval.InvoiceParty
                {
                    Name = name,
                    TaxNumber = taxNumber,
                    TaxOfficeName = taxOfficeName,
                    AddressLine = addrLine,
                    City = city,
                    Country = country
                };
            }

            var supplierPartyEl = Elem(Elem(root, "AccountingSupplierParty") ?? Elem(root, "DespatchSupplierParty") ?? root, "Party");
            var customerPartyEl = Elem(Elem(root, "AccountingCustomerParty") ?? Elem(root, "DeliveryCustomerParty") ?? root, "Party");

            var lines = root.Elements()
                .Where(e => e.Name.LocalName is "InvoiceLine" or "CreditNoteLine" or "DespatchLine")
                .Select(line =>
                {
                    var qtyEl = line.Elements().FirstOrDefault(e => e.Name.LocalName == "InvoicedQuantity"
                                    || e.Name.LocalName == "CreditedQuantity"
                                    || e.Name.LocalName == "DeliveredQuantity");
                    var itemEl = Elem(line, "Item");
                    var priceEl = Elem(line, "Price");
                    var lineTaxEl = Elem(line, "TaxTotal");
                    var lineTaxSubEl = lineTaxEl is not null ? Elem(lineTaxEl, "TaxSubtotal") : null;
                    var lineTaxCatEl = lineTaxSubEl is not null ? Elem(lineTaxSubEl, "TaxCategory") : null;
                    return new Web.Models.Approval.InvoiceLineItem
                    {
                        LineNo = Val(line, "ID"),
                        ItemName = itemEl is not null ? Val(itemEl, "Name") : null,
                        Quantity = qtyEl?.Value?.Trim(),
                        UnitCode = qtyEl?.Attribute("unitCode")?.Value,
                        UnitPrice = priceEl is not null ? Val(priceEl, "PriceAmount") : null,
                        LineAmount = Val(line, "LineExtensionAmount"),
                        TaxRate = lineTaxCatEl is not null ? Val(lineTaxCatEl, "Percent") : null,
                        TaxAmount = lineTaxEl is not null ? Val(lineTaxEl, "TaxAmount") : null
                    };
                })
                .ToArray();

            var taxSummaries = root.Elements()
                .Where(e => e.Name.LocalName == "TaxTotal")
                .SelectMany(tt => tt.Elements().Where(e => e.Name.LocalName == "TaxSubtotal"))
                .Select(sub =>
                {
                    var cat = Elem(sub, "TaxCategory");
                    var scheme = cat is not null ? Elem(cat, "TaxScheme") : null;
                    return new Web.Models.Approval.InvoiceTaxSummary
                    {
                        TaxName = scheme is not null ? Val(scheme, "Name") : null,
                        Rate = cat is not null ? Val(cat, "Percent") : null,
                        TaxableAmount = Val(sub, "TaxableAmount"),
                        TaxAmount = Val(sub, "TaxAmount")
                    };
                })
                .ToArray();

            var totalTaxEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "TaxTotal");
            var legalTotal = Elem(root, "LegalMonetaryTotal");
            var notes = root.Elements()
                .Where(e => e.Name.LocalName == "Note")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v));

            return new Web.Models.Approval.InvoiceRenderData
            {
                ProfileId = Val(root, "ProfileID"),
                TypeCode = Val(root, "InvoiceTypeCode") ?? Val(root, "CreditNoteTypeCode"),
                Currency = Val(root, "DocumentCurrencyCode"),
                Note = string.Join(" / ", notes),
                Supplier = ParseParty(supplierPartyEl),
                Customer = ParseParty(customerPartyEl),
                Lines = lines,
                TaxSummaries = taxSummaries,
                LineExtensionAmount = legalTotal is not null ? Val(legalTotal, "LineExtensionAmount") : null,
                TaxAmount = totalTaxEl is not null ? Val(totalTaxEl, "TaxAmount") : null,
                PayableAmount = legalTotal is not null ? Val(legalTotal, "PayableAmount") : null
            };
        }
        catch
        {
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadPayload(Guid id, CancellationToken cancellationToken)
    {
        var document = await _incomingDocumentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var raw = document.PayloadRaw ?? string.Empty;
        string xmlContent;
        try
        {
            xmlContent = XDocument.Parse(raw).ToString();
        }
        catch
        {
            xmlContent = raw;
        }

        var safeDocNumber = string.Concat(document.DocumentNumber
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        var fileName = $"{safeDocNumber}.xml";

        return File(Encoding.UTF8.GetBytes(xmlContent), "application/xml", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> ViewHtml(Guid id, CancellationToken cancellationToken)
    {
        var document = await _incomingDocumentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null || string.IsNullOrWhiteSpace(document.PayloadRaw))
        {
            return NotFound();
        }

        try
        {
            var html = ExtractHtmlFromUbl(document.PayloadRaw);
            if (html == null)
            {
                return Content("Bu evrak için XSLT (Tasarım) şablonu bulunamadı.", "text/plain", Encoding.UTF8);
            }
            return Content(html, "text/html", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return Content($"Fatura görseli oluşturulamadı: {ex.Message}", "text/plain", Encoding.UTF8);
        }
    }

    private static string? ExtractHtmlFromUbl(string xmlContent)
    {
        // Temiz bir XML elde et
        var cleanXml = xmlContent.Trim().TrimStart('\uFEFF', '\u200B');
        int firstXmlIndex = cleanXml.IndexOf('<');
        if (firstXmlIndex > 0)
        {
            cleanXml = cleanXml.Substring(firstXmlIndex);
        }

        var xDoc = XDocument.Parse(cleanXml);
        XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        XNamespace cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

        var attachmentNodes = xDoc.Descendants(cac + "Attachment")
            .Select(x => x.Element(cbc + "EmbeddedDocumentBinaryObject"))
            .Where(x => x != null);

        var xsltBase64 = attachmentNodes
            .FirstOrDefault(x => x!.Attribute("filename")?.Value?.EndsWith(".xslt", StringComparison.OrdinalIgnoreCase) == true)?.Value;

        if (string.IsNullOrWhiteSpace(xsltBase64))
            return null;

        var xsltBytes = Convert.FromBase64String(xsltBase64);
        var xsltText = System.Text.Encoding.UTF8.GetString(xsltBytes);
        
        // Temiz bir XSLT elde et (olasi Turkce, UTF-8 Byte Order Mark temizlikleri)
        var cleanXslt = xsltText.Trim().TrimStart('\uFEFF', '\u200B');
        int firstXsltIndex = cleanXslt.IndexOf('<');
        if (firstXsltIndex > 0)
        {
            cleanXslt = cleanXslt.Substring(firstXsltIndex);
        }

        using var xsltReader = XmlReader.Create(new StringReader(cleanXslt));
        var transform = new XslCompiledTransform();
        transform.Load(xsltReader);

        using var xmlReader = new StringReader(cleanXml);
        using var sourceReader = XmlReader.Create(xmlReader);
        
        using var outputContent = new StringWriter();
        using var writer = XmlWriter.Create(outputContent, transform.OutputSettings ?? new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true });
        
        transform.Transform(sourceReader, writer);
        
        return outputContent.ToString();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PullFromPortal(
        string? kind,
        int? page,
        int? pageSize,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        try
        {
            // HTTP istek token'i uzun SOAP cagrilarini kesebildigi icin CancellationToken.None kullanilir.
            // Entegrator client zaten settings.TimeoutSeconds ile kendi CTS'ini olusturuyor.
            _ = cancellationToken; // parametre ileride gerekirse kullanilabilir
            var result = await _documentImportService.ImportFromActiveIntegratorsAsync(dateFrom, dateTo, CancellationToken.None);
            TempData["AdminSuccess"] = $"Logo portaldan {result.ImportedCount} yeni belge eklendi, {result.SkippedCount} belge atlandı.";
        }
        catch (OperationCanceledException)
        {
            TempData["AdminError"] = "Portal guncellemesi zaman asimina ugradi. Entegrator ayarlarindaki zaman asimi degerini artirabilirsiniz.";
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = $"Portal guncellemesi sirasinda hata: {ex.Message}";
        }

        var normalizedKind = NormalizeKind(kind);
        return RedirectToKindPage(normalizedKind, page, pageSize, dateFrom, dateTo, null);
    }

    private async Task<IActionResult> RenderQueuePageAsync(
        string? kind,
        int? page,
        int? pageSize,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? isProcessed,
        CancellationToken cancellationToken)
    {
        var normalizedKind = NormalizeKind(kind);
        var pendingQueue = await _approvalQueueService.GetPendingAsync(isProcessed, cancellationToken);

        if (!string.IsNullOrWhiteSpace(normalizedKind))
        {
            pendingQueue = pendingQueue
                .Where(x => string.Equals(x.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var resolvedPageSize = await ResolveGridPageSizeAsync(GetGridKey(normalizedKind), pageSize, cancellationToken);
        var totalCount = pendingQueue.Count;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0
            ? 1
            : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);
        var documents = pendingQueue
            .Skip((currentPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToArray();
        var kindTitle = GetKindTitle(normalizedKind);
        var pageTitle = GetPageTitle(normalizedKind);
        var resolvedDateFrom = dateFrom ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var resolvedDateTo = dateTo ?? DateOnly.FromDateTime(DateTime.Today);

        ViewData["Kind"] = normalizedKind;
        ViewData["KindTitle"] = kindTitle;
        ViewData["Title"] = pageTitle;
        ViewData["IsProcessed"] = isProcessed;

        return View(nameof(Index), new ApprovalQueueViewModel
        {
            Documents = documents,
            Kind = normalizedKind ?? string.Empty,
            KindTitle = kindTitle,
            PageTitle = pageTitle,
            DateFrom = resolvedDateFrom,
            DateTo = resolvedDateTo,
            BoardConfig = BuildApprovalBoardConfig(documents, normalizedKind),
            ListState = BuildGridListState(
                gridKey: GetGridKey(normalizedKind),
                page: currentPage,
                pageSize: resolvedPageSize,
                totalCount: totalCount,
                totalPages: totalPages,
                itemLabel: "belge")
        });
    }

    private IActionResult RedirectToKindPage(string? kind, int? page = null, int? pageSize = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, bool? isProcessed = null) =>
        NormalizeKind(kind) switch
        {
            "EInvoice" => RedirectToAction(nameof(EInvoice), new { page, pageSize, dateFrom, dateTo, isProcessed }),
            "EArchive" => RedirectToAction(nameof(EArchive), new { page, pageSize, dateFrom, dateTo, isProcessed }),
            "EDispatch" => RedirectToAction(nameof(EDispatch), new { page, pageSize, dateFrom, dateTo, isProcessed }),
            _ => RedirectToAction(nameof(Index), new { page, pageSize, dateFrom, dateTo, isProcessed })
        };

    private static string? NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        var normalized = kind.Trim().ToLowerInvariant()
            .Replace('ş', 's')
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

        return normalized switch
        {
            "einvoice" or "e-fatura" or "fatura" => "EInvoice",
            "earchive" or "e-arsiv" or "arsiv" => "EArchive",
            "edispatch" or "e-irsaliye" or "irsaliye" => "EDispatch",
            _ => null
        };
    }

    private static string GetKindTitle(string? kind) =>
        kind switch
        {
            "EInvoice" => "e-Fatura",
            "EArchive" => "e-Arsiv",
            "EDispatch" => "e-Irsaliye",
            _ => "Tum Belgeler"
        };

    private static string GetPageTitle(string? kind) =>
        kind switch
        {
            "EInvoice" => "e-Fatura",
            "EArchive" => "e-Arsiv",
            "EDispatch" => "e-Irsaliye",
            _ => "Elektronik Belgeler"
        };

    private async Task<int> ResolveGridPageSizeAsync(
        string gridKey,
        int? requestedPageSize,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var storedPageSize = await _uiConfigurationService.GetGridPageSizePreferenceAsync(
            userId,
            gridKey,
            20,
            cancellationToken);

        var resolvedPageSize = requestedPageSize.GetValueOrDefault() > 0
            ? requestedPageSize!.Value
            : storedPageSize;

        if (userId.HasValue && userId.Value > 0 && resolvedPageSize != storedPageSize)
        {
            await _uiConfigurationService.SaveGridPageSizePreferenceAsync(
                userId.Value,
                gridKey,
                resolvedPageSize,
                cancellationToken);
        }

        return resolvedPageSize;
    }

    private static CalibraHub.Web.Models.Shared.GridListStateViewModel BuildGridListState(
        string gridKey,
        int page,
        int pageSize,
        int totalCount,
        int totalPages,
        string itemLabel) =>
        new()
        {
            GridKey = gridKey,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            ItemLabel = itemLabel,
            PageSizeOptions =
            [
                new("10", "10", pageSize == 10),
                new("20", "20", pageSize == 20),
                new("30", "30", pageSize == 30),
                new("50", "50", pageSize == 50),
                new("100", "100", pageSize == 100)
            ]
        };

    private int? GetCurrentUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(rawUserId, out var userId) ? userId : null;
    }

    private static string GetGridKey(string? kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? "approval-all"
            : $"approval-{kind.Trim().ToLowerInvariant()}";

    private static object BuildApprovalBoardConfig(
        IReadOnlyCollection<PendingApprovalDocumentDto> documents,
        string? kind)
    {
        var entities = documents.Select(doc => (object)new
        {
            id       = doc.Id.ToString(),
            title    = doc.DocumentNumber,
            subtitle = doc.Kind switch {
                "EInvoice"  => "e-Fatura",
                "EArchive"  => "e-Arsiv",
                "EDispatch" => "e-Irsaliye",
                _           => doc.Kind
            },
            description = (string?)null,
            imageUrl    = (string?)null,
            statusBadge = doc.IsProcessed
                ? new { label = "İslendi", color = "emerald" }
                : (object?)null,
            widgets = new object[] {
                new { id="w_sender",    type="data", dataType="text",     label="Gonderici",     value=(doc.SenderName ?? doc.SenderTaxNumber ?? ""), detail=(doc.SenderTaxNumber ?? ""), color="slate"  },
                new { id="w_scenario",  type="data", dataType="text",     label="Senaryo",       value=(doc.Scenario ?? "-"),                         color="indigo" },
                new { id="w_issuedate", type="data", dataType="date",     label="Belge Tarihi",  value=doc.IssueDate.ToString("dd.MM.yyyy"),          color="cyan"   },
                new { id="w_imported",  type="data", dataType="datetime", label="Sisteme Giris", value=doc.ImportedAt.ToString("dd.MM.yyyy HH:mm"),   color="slate"  },
            },
            primaryAction   = (object?)null,
            secondaryAction = (object?)null,
            extraActions = new object[] {
                new { label="Goruntule",  icon="Eye",          color="blue",
                      type="navigate",   url=$"/Approval/ViewPayload/{doc.Id}" },
                new { label="Kalemler",   icon="List",         color="slate",
                      type="fetch-modal", fetchUrl=$"/Approval/DocumentLines/{doc.Id}",
                      modalTitle=$"Kalemler - {doc.DocumentNumber}" },
                new { label="Indir",      icon="Download",     color="green",
                      type="download",   url=$"/Approval/DownloadPayload/{doc.Id}" },
                new { label="Onay Akisi", icon="GitBranch",    color="indigo",
                      type="fetch-modal", fetchUrl=$"/Approval/ApprovalPanel/{doc.Id}",
                      modalTitle=$"Onay Akisi - {doc.DocumentNumber}" },
                new { label=doc.IsProcessed ? "Islenmedi Yap" : "Islendi Isaretl",
                      icon =doc.IsProcessed ? "XCircle"       : "CheckCircle2",
                      color=doc.IsProcessed ? "red"           : "emerald",
                      type ="api-post",
                      url  ="/Approval/ToggleProcessed",
                      body =new Dictionary<string,object> { ["id"] = doc.Id, ["isProcessed"] = !doc.IsProcessed } },
            },
        }).ToArray();

        return new {
            boardKey          = string.IsNullOrEmpty(kind) ? "approval-all" : $"approval-{kind.ToLowerInvariant()}",
            title             = kind switch {
                "EInvoice"  => "e-Fatura",
                "EArchive"  => "e-Arsiv",
                "EDispatch" => "e-Irsaliye",
                _           => "Elektronik Belgeler"
            },
            subtitle          = $"{entities.Length} belge",
            icon              = "FileText",
            iconColor         = "indigo",
            searchable        = true,
            searchPlaceholder = "Belge no, gonderici, VKN...",
            emptyText         = "Bekleyen belge bulunmuyor",
            actions           = Array.Empty<object>(),
            masterWidgets     = new List<object> {
                SmartBoardFilterHelpers.MakeStdWidget("w_sender",    "Gonderici",     "text"),
                SmartBoardFilterHelpers.MakeStdWidget("w_scenario",  "Senaryo",       "text"),
                SmartBoardFilterHelpers.MakeStdWidget("w_issuedate", "Belge Tarihi",  "date"),
                SmartBoardFilterHelpers.MakeStdWidget("w_imported",  "Sisteme Giris", "datetime"),
            },
            entities,
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleProcessed(Guid id, bool isProcessed, CancellationToken cancellationToken)
    {
        try
        {
            await _approvalQueueService.ToggleProcessingStatusAsync(id, isProcessed, cancellationToken);
            return Json(new { success = true, isProcessed = isProcessed });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Onay Paneli — modal içeriği (HTML partial) ────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ApprovalPanel(Guid id, CancellationToken cancellationToken)
    {
        var document = await _incomingDocumentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null) return Content("<div class='p-3 text-danger'>Belge bulunamadı.</div>", "text/html");

        // IncomingDocument (e-fatura/e-arşiv) artık ApprovalInstance.DocumentId (INT) ile doğrudan
        // ilişkilendirilemiyor — Guid PK uyumsuzluğu. IncomingDocument tabanlı onay akışları ileride
        // IncomingDocument'a özel bir FK sütunu (IncomingDocumentId UNIQUEIDENTIFIER) eklenerek desteklenecek.
        // Şimdilik instance bulunamadı (null) olarak devam edilir.
        ApprovalInstanceDto? instance = null;
        var allFlows = await _approvalFlowService.GetAllAsync(cancellationToken);
        // 'Document' = "Tüm Belgeler" wildcard (yeni standart), 'All' = legacy. Spesifik tip
        // (EInvoice/EArchive/SalesQuote/...) ile birebir eşleşme + wildcard'lar dahil edilir.
        var kindFlows = allFlows.Where(f => f.IsActive && (
            f.DocumentKind == document.Kind.ToString() ||
            f.DocumentKind == "Document" ||
            f.DocumentKind == "All")).ToList();

        var currentUserId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserName = User.FindFirstValue(ClaimTypes.Name) ?? "system";

        return PartialView("_ApprovalPanel", new Web.Models.Approval.ApprovalPanelViewModel
        {
            DocumentId      = id,
            DocumentNumber  = document.DocumentNumber,
            DocumentKind    = document.Kind.ToString(),
            Instance        = instance,
            AvailableFlows  = kindFlows,
            CurrentUserId   = currentUserId,
            CurrentUserName = currentUserName,
        });
    }

    // ── Onay Akışı — belgenin mevcut onay örneğini getir ──────────────────────
    [HttpGet]
    public async Task<IActionResult> ApprovalInstance(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            // IncomingDocument (Guid PK) üzerinden ApprovalInstance araması artık desteklenmiyor —
            // DocumentId kolonu INT FK'ya dönüştürüldü. Bu endpoint geçici olarak boş döner.
            ApprovalInstanceDto? instance = null;
            if (instance is null) return Json(new { found = false });
            return Json(new
            {
                found = true,
                instanceId = instance.Id,
                status = instance.Status,
                flowName = instance.FlowName,
                currentStep = instance.CurrentStep,
                totalSteps = instance.TotalSteps,
                startedBy = instance.StartedBy,
                startedAt = instance.StartedAt.ToString("dd.MM.yyyy HH:mm"),
                rejectNote = instance.RejectNote,
                steps = instance.StepRecords.Select(s => new
                {
                    stepOrder = s.StepOrder,
                    stepName = s.StepName,
                    status = s.Status,
                    approverName = s.ApproverName,
                    note = s.Note,
                    actionDate = s.ActionDate?.ToString("dd.MM.yyyy HH:mm"),
                }),
            });
        }
        catch (Exception ex)
        {
            return Json(new { found = false, error = ex.Message });
        }
    }

    // ── Uygun akışları getir (belge türü + tutar + VKN + departman'a göre) ──
    [HttpGet]
    public async Task<IActionResult> MatchFlow(string kind, decimal? amount, string? taxNo, int? departmentId, CancellationToken cancellationToken)
    {
        try
        {
            var flow = await _approvalFlowService.MatchFlowAsync(kind, amount, taxNo, departmentId, cancellationToken);
            if (flow is null) return Json(new { matched = false });
            return Json(new
            {
                matched = true,
                flowId = flow.Id,
                flowName = flow.Name,
                stepCount = flow.Steps.Count(s => s.IsActive),
            });
        }
        catch (Exception ex)
        {
            return Json(new { matched = false, error = ex.Message });
        }
    }

    // ── Tüm akış listesi (İşleme Al modalı için) ──────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ListFlows(string? kind, CancellationToken cancellationToken)
    {
        try
        {
            var all = await _approvalFlowService.GetAllAsync(cancellationToken);
            var filtered = string.IsNullOrWhiteSpace(kind)
                ? all
                : all.Where(f => f.IsActive && (
                    f.DocumentKind == kind ||
                    f.DocumentKind == "Document" ||
                    f.DocumentKind == "All")).ToList();
            return Json(filtered.Select(f => new
            {
                id = f.Id, name = f.Name,
                documentKind = f.DocumentKind,
                stepCount = f.StepCount,
            }));
        }
        catch (Exception ex)
        {
            return Json(new object[] { });
        }
    }

    // ── İşleme Al — onay sürecini başlat ──────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartApproval(int documentId, int flowId, CancellationToken cancellationToken)
    {
        try
        {
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var instance = await _approvalFlowService.StartAsync(
                new StartApprovalRequest(documentId, flowId, userName), cancellationToken);
            return Json(new { ok = true, instanceId = instance.Id, status = instance.Status,
                currentStep = instance.CurrentStep, totalSteps = instance.TotalSteps });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Mevcut adımı onayla ───────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveStep(int instanceId, string? note, CancellationToken cancellationToken)
    {
        try
        {
            var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var instance = await _approvalFlowService.ApproveStepAsync(
                new ApproveStepRequest(instanceId, userId, userName, note), cancellationToken);

            // Onay tamamlandıysa Document.Status = Approved yap (alis_talebi için)
            if (string.Equals(instance.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                if (instance.DocumentId.HasValue)
                    await _documentService.ChangeStatusAsync(instance.DocumentId.Value, "Approved", cancellationToken);
            }

            return Json(new { ok = true, status = instance.Status,
                currentStep = instance.CurrentStep, totalSteps = instance.TotalSteps });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }



    // ── Reddet ────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectApproval(int instanceId, string note, CancellationToken cancellationToken)
    {
        try
        {
            var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var instance = await _approvalFlowService.RejectAsync(
                new RejectStepRequest(instanceId, userId, userName, note), cancellationToken);
            return Json(new { ok = true, status = instance.Status });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── İptal et ──────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelApproval(int instanceId, CancellationToken cancellationToken)
    {
        try
        {
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            await _approvalFlowService.CancelAsync(instanceId, userName, cancellationToken);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }
}

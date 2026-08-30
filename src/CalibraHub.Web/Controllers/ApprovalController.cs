using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Approval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Globalization;
using System.Xml.Linq;
using System.Xml.Xsl;
using System.Xml;

namespace CalibraHub.Web.Controllers;

[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.ApprovalFlows)]
public sealed class ApprovalController : Controller
{
    private readonly IApprovalQueueService _approvalQueueService;
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly IDocumentImportService _documentImportService;
    private readonly IIncomingDocumentRepository _incomingDocumentRepository;
    private readonly IApprovalFlowService _approvalFlowService;
    private readonly IDocumentService _documentService;
    private readonly ICapaService _capaService;
    private readonly ILogger<ApprovalController> _logger;

    public ApprovalController(
        IApprovalQueueService approvalQueueService,
        IUiConfigurationService uiConfigurationService,
        IDocumentImportService documentImportService,
        IIncomingDocumentRepository incomingDocumentRepository,
        IApprovalFlowService approvalFlowService,
        IDocumentService documentService,
        ICapaService capaService,
        ILogger<ApprovalController> logger)
    {
        _approvalQueueService = approvalQueueService;
        _uiConfigurationService = uiConfigurationService;
        _documentImportService = documentImportService;
        _incomingDocumentRepository = incomingDocumentRepository;
        _approvalFlowService = approvalFlowService;
        _documentService = documentService;
        _capaService = capaService;
        _logger = logger;
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
    public async Task<IActionResult> ViewPayload(int id, CancellationToken cancellationToken)
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

        // XML yalniz ONLINE (entegrator) kayitlarda vardir. OFFLINE (ERP/Netsis) kayitta
        // PayloadRaw bir JSON izleme kaydidir; UBL YOKTUR. Bu yuzden ekranin uc sekmesi de
        // (GIB gorunumu, ozet, XML) bos kaliyordu — veri aslinda yerli/legacy kalem
        // tablolarinda duruyor. XML ayristirilamazsa ozet o tablolardan uretilir.
        var hasXml = xmlContent.TrimStart().StartsWith('<');
        var renderData = ParseInvoiceRenderData(xmlContent);
        if (renderData is null || renderData.Lines.Count == 0)
        {
            var lines = await _incomingDocumentRepository.GetLinesAsync(id, cancellationToken);
            if (lines.Count > 0)
            {
                renderData = BuildRenderDataFromLines(document, lines);
            }
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
            HasXmlPayload = hasXml,
            RenderData = renderData
        };

        ViewData["Title"] = $"Belge: {document.DocumentNumber}";
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> DocumentLines(int id, CancellationToken cancellationToken)
    {
        // 1. Sağ tıklanan belgenin XML/Veritabanı dökümanı bulunur
        var document = await _incomingDocumentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null) 
            return Content("<div class='p-3 text-center text-danger fw-medium'>Belge veritabanında bulunamadı.</div>", "text/html");

        // 2. Kalemler ONCE YERLI TABLODAN okunur (IncomingDocumentLine).
        //
        // Eskiden her istekte PayloadRaw icindeki UBL XML'i bastan ayristiriliyordu. Bu,
        // OFFLINE (ERP/Netsis) kayitlarda HIC calismaz: o kayitlarda ayristirilacak UBL
        // YOKTUR (ERP zarf tablosundaki XMLVERI olculdu — 14.382 satirin tamaminda bos),
        // veri iliskisel tablolardan gelir ve ice aktarimda yerli tablolara yazilir.
        //
        // XML ayristirma GERI DUSUS olarak korunur: detay tablolari eklenmeden ONCE
        // aktarilmis online kayitlarin kalemleri hala gorunsun (yoksa gecmis belgeler
        // bir anda bos gorunurdu).
        var dbLines = await _incomingDocumentRepository.GetLinesAsync(id, cancellationToken);
        if (dbLines.Count > 0)
        {
            var mapped = dbLines.Select(l => new Web.Models.Approval.InvoiceLineItem
            {
                LineNo     = l.LineNumber.ToString(CultureInfo.InvariantCulture),
                ItemName   = l.ItemName ?? l.ItemCode,
                Quantity   = l.Quantity.ToString(CultureInfo.InvariantCulture),
                UnitCode   = l.UnitCode,
                UnitPrice  = l.UnitPrice.ToString(CultureInfo.InvariantCulture),
                LineAmount = l.LineAmount?.ToString(CultureInfo.InvariantCulture),
                TaxRate    = l.VatRate?.ToString(CultureInfo.InvariantCulture),
                TaxAmount  = l.Taxes.FirstOrDefault(t => t.TaxAmount.HasValue)?.TaxAmount?
                                .ToString(CultureInfo.InvariantCulture),
            }).ToList();
            return PartialView("_DocumentLines", mapped);
        }

        var xmlContent = document.PayloadRaw ?? string.Empty;
        var rd = ParseInvoiceRenderData(xmlContent);

        if (rd is null || rd.Lines.Count == 0) 
            return Content("<div class='p-4 text-center text-muted'><svg viewBox='0 0 24 24' width='24' height='24' stroke='currentColor' stroke-width='2' fill='none' class='mb-2 opacity-50'><circle cx='12' cy='12' r='10'></circle><line x1='12' y1='8' x2='12' y2='12'></line><line x1='12' y1='16' x2='12.01' y2='16'></line></svg><br>Bu belgede okunabilir bir kalem (satır) verisine ulaşılamadı.</div>", "text/html");

        // 3. Bulunan satırlar o yaratılan kompakt görünüme (_DocumentLines) çizilerek yollanır
        return PartialView("_DocumentLines", rd.Lines);
    }

    /// <summary>
    /// UBL XML olmayan belgeler icin ozet gorunumu YERLI (IncomingDocumentLine) ya da
    /// LEGACY (CBT_EBELGEKALEM) kalem verisinden uretir. Toplamlar kalemlerden toplanir;
    /// belge basligi zaten ana kayitta vardir.
    /// </summary>
    private static Web.Models.Approval.InvoiceRenderData BuildRenderDataFromLines(
        Domain.Entities.IncomingDocument document,
        IReadOnlyList<CalibraHub.Application.Services.EDocument.EDocumentLineData> lines)
    {
        static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        var mappedLines = lines.Select(l => new Web.Models.Approval.InvoiceLineItem
        {
            LineNo     = l.LineNumber.ToString(CultureInfo.InvariantCulture),
            ItemName   = l.ItemName ?? l.ItemCode,
            Quantity   = Num(l.Quantity),
            UnitCode   = l.UnitCode,
            UnitPrice  = Num(l.UnitPrice),
            LineAmount = l.LineAmount.HasValue ? Num(l.LineAmount.Value) : null,
            TaxRate    = l.VatRate.HasValue ? Num(l.VatRate.Value) : null,
            TaxAmount  = l.Taxes.FirstOrDefault(t => t.TaxAmount.HasValue)?.TaxAmount is { } ta
                            ? Num(ta)
                            : null,
        }).ToArray();

        // Kalem tutari yoksa miktar x birim fiyat ile TURETME yapilmaz: ikisi de kaynaktan
        // gelmedigi durumda uydurma toplam gostermek, eksik veriyi gizlerdi.
        var lineTotal = lines.Where(l => l.LineAmount.HasValue).Sum(l => l.LineAmount!.Value);
        var taxTotal = lines.SelectMany(l => l.Taxes)
                            .Where(t => t.TaxAmount.HasValue)
                            .Sum(t => t.TaxAmount!.Value);

        var taxSummaries = lines
            .SelectMany(l => l.Taxes)
            .Where(t => t.TaxAmount.HasValue || t.TaxableAmount.HasValue)
            .GroupBy(t => new { Name = t.Name ?? "KDV", t.TaxPercent })
            .Select(g => new Web.Models.Approval.InvoiceTaxSummary
            {
                TaxName       = g.Key.Name,
                Rate          = g.Key.TaxPercent.HasValue ? Num(g.Key.TaxPercent.Value) : null,
                TaxableAmount = g.Any(t => t.TaxableAmount.HasValue)
                                    ? Num(g.Where(t => t.TaxableAmount.HasValue).Sum(t => t.TaxableAmount!.Value))
                                    : null,
                TaxAmount     = g.Any(t => t.TaxAmount.HasValue)
                                    ? Num(g.Where(t => t.TaxAmount.HasValue).Sum(t => t.TaxAmount!.Value))
                                    : null,
            })
            .ToArray();

        return new Web.Models.Approval.InvoiceRenderData
        {
            TypeCode = document.Kind.ToString(),
            Currency = lines.Select(l => l.CurrencyCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
            Supplier = new Web.Models.Approval.InvoiceParty
            {
                Name = document.SenderName,
                TaxNumber = string.IsNullOrWhiteSpace(document.SenderTaxNumber) ? null : document.SenderTaxNumber,
            },
            Customer = string.IsNullOrWhiteSpace(document.RecipientTaxNumber)
                ? null
                : new Web.Models.Approval.InvoiceParty { TaxNumber = document.RecipientTaxNumber },
            Lines = mappedLines,
            TaxSummaries = taxSummaries,
            LineExtensionAmount = lineTotal == 0m ? null : Num(lineTotal),
            TaxAmount = taxTotal == 0m ? null : Num(taxTotal),
            PayableAmount = lineTotal == 0m && taxTotal == 0m ? null : Num(lineTotal + taxTotal),
        };
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
    public async Task<IActionResult> DownloadPayload(int id, CancellationToken cancellationToken)
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
    public async Task<IActionResult> ViewHtml(int id, CancellationToken cancellationToken)
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
            _logger.LogError(ex, "[Onay] Fatura görseli oluşturulamadı (id={Id}).", id);
            return Content("Fatura görseli oluşturulamadı.", "text/plain", Encoding.UTF8);
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
            _logger.LogError(ex, "[Onay] Portal güncellemesi başarısız.");
            TempData["AdminError"] = "Portal güncellemesi sırasında bir hata oluştu.";
        }

        var normalizedKind = NormalizeKind(kind);
        return RedirectToKindPage(normalizedKind, page, pageSize, dateFrom, dateTo, null);
    }


    /// <summary>
    /// E-belge kuyrugunun SmartBoard (C-Grid) yapilandirmasi — proje standardi.
    ///
    /// <para>Uc ekran (e-Fatura / e-Arsiv / e-Irsaliye) AYNI yapiyi kullanir; fark yalnizca
    /// <paramref name="kind"/> suzgeci, baslik ve ikondur. Ayri ayri uc kart kurgusu yazmak
    /// ayni bakim yukunu uce katlardi.</para>
    ///
    /// <para>Filtreleme SmartBoard'un kendi filtre panelinden yapilir: panel
    /// <c>masterWidgets</c> listesini okur ve karttaki ayni <c>id</c>'li veri parcalariyla
    /// eslestirir. Bu yuzden her kart widget'inin id/label/dataType ucusu master listesiyle
    /// BIREBIR ayni olmalidir — ayrisirsa filtre sessizce hicbir sey eslemez.</para>
    /// </summary>
    private async Task<object> BuildEDocumentBoardConfigAsync(
        string? kind, bool? isProcessed, CancellationToken cancellationToken)
    {
        var normalizedKind = NormalizeKind(kind);
        var queue = await _approvalQueueService.GetPendingAsync(isProcessed, cancellationToken);

        if (!string.IsNullOrWhiteSpace(normalizedKind))
        {
            queue = queue
                .Where(x => string.Equals(x.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var (icon, color) = normalizedKind switch
        {
            "EInvoice"  => ("FileText", "indigo"),
            "EArchive"  => ("Archive", "violet"),
            "EDispatch" => ("Truck", "amber"),
            _           => ("Files", "slate"),
        };

        var kindTitle = GetKindTitle(normalizedKind);

        var entities = queue
            .OrderByDescending(d => d.ImportedAt)
            .Select(d => new
            {
                id = d.Id,
                title = d.DocumentNumber,
                subtitle = string.IsNullOrWhiteSpace(d.SenderName) ? d.SenderTaxNumber : d.SenderName,
                description = (string?)null,
                imageUrl = (string?)null,
                statusBadge = d.IsProcessed
                    ? new { label = "İşlendi", color = "emerald" }
                    : new { label = "Bekliyor", color = "amber" },
                widgets = new object[]
                {
                    new { id = "w_issue_date", type = "data", dataType = "date",
                          label = "Belge Tarihi", value = d.IssueDate.ToString("dd.MM.yyyy"),
                          detail = (string?)null, color = "indigo" },
                    new { id = "w_sender", type = "data", dataType = "text",
                          label = "Gönderen", value = d.SenderName ?? "-",
                          detail = (string?)null, color = "blue" },
                    new { id = "w_sender_vkn", type = "data", dataType = "text",
                          label = "Gönderen VKN", value = d.SenderTaxNumber,
                          detail = (string?)null, color = "slate" },
                    new { id = "w_scenario", type = "data", dataType = "text",
                          label = "Senaryo", value = d.Scenario ?? "-",
                          detail = (string?)null, color = "violet" },
                    new { id = "w_status", type = "data", dataType = "text",
                          label = "Durum", value = d.IsProcessed ? "İşlendi" : "Bekliyor",
                          detail = (string?)null, color = d.IsProcessed ? "emerald" : "amber" },
                    new { id = "w_imported", type = "data", dataType = "date",
                          label = "Alınma", value = d.ImportedAt.ToString("dd.MM.yyyy HH:mm"),
                          detail = (string?)null, color = "slate" },
                    // Kaynak: karma kurulumda "bu belge entegratorden mi ERP'den mi geldi"
                    // sorusu teshiste kritik; eskiden yalniz PayloadRaw'a bakarak yanitlanabiliyordu.
                    new { id = "w_source", type = "data", dataType = "text",
                          label = "Kaynak",
                          value = string.Equals(d.IngestSource, "Offline", StringComparison.OrdinalIgnoreCase)
                              ? "Çevrimdışı (ERP)" : "Çevrimiçi (Entegratör)",
                          detail = (string?)null,
                          color = string.Equals(d.IngestSource, "Offline", StringComparison.OrdinalIgnoreCase)
                              ? "amber" : "emerald" },
                },
                primaryAction = new
                {
                    label = "Görüntüle",
                    icon = "Eye",
                    color = "indigo",
                    url = Url.Action(nameof(ViewPayload), "Approval", new { id = d.Id }),
                    hideButton = true,   // karta tiklayinca acilir
                },
                secondaryAction = (object?)null,
            })
            .ToArray();

        // Filtre paneli bu listeden beslenir; kart widget'lariyla ayni id/label/dataType.
        var masterWidgets = new object[]
        {
            new { id = "w_issue_date",  label = "Belge Tarihi", dataType = "date" },
            new { id = "w_sender",      label = "Gönderen",     dataType = "text" },
            new { id = "w_sender_vkn",  label = "Gönderen VKN", dataType = "text" },
            new { id = "w_scenario",    label = "Senaryo",      dataType = "text" },
            new { id = "w_status",      label = "Durum",        dataType = "text" },
            new { id = "w_imported",    label = "Alınma",       dataType = "date" },
            new { id = "w_source",      label = "Kaynak",       dataType = "text" },
        };

        return new
        {
            boardKey = "edocument-" + (normalizedKind ?? "all").ToLowerInvariant(),
            title = kindTitle,
            subtitle = $"{entities.Length} belge",
            icon,
            iconColor = color,
            refreshUrl = Url.Action(nameof(EDocumentBoardConfig), "Approval",
                                    new { kind = normalizedKind, isProcessed }),
            searchPlaceholder = "Belge no / gönderen ara…",
            emptyText = "Bu kuyrukta belge yok",
            actions = Array.Empty<object>(),
            masterWidgets,
            entities,
        };
    }

    /// <summary>
    /// SmartBoard yerinde yenileme ucu (GET, tam config JSON). C-Grid standardi:
    /// kart uzerindeki degisiklikten sonra sayfa yeniden YUKLENMEZ, board bu uctan tazelenir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> EDocumentBoardConfig(
        string? kind, bool? isProcessed, CancellationToken cancellationToken)
        => Json(await BuildEDocumentBoardConfigAsync(kind, isProcessed, cancellationToken));

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
        // SmartBoard yapilandirmasi ayni veriden uretilir; gorunum yalnizca mount eder.
        ViewData["BoardConfig"] = await BuildEDocumentBoardConfigAsync(
            normalizedKind, isProcessed, cancellationToken);

        return View("Board", new ApprovalQueueViewModel
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
    public async Task<IActionResult> ToggleProcessed(int id, bool isProcessed, CancellationToken cancellationToken)
    {
        try
        {
            await _approvalQueueService.ToggleProcessingStatusAsync(id, isProcessed, cancellationToken);
            return Json(new { success = true, isProcessed = isProcessed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Onay] İşlendi işareti değiştirilemedi (id={Id}).", id);
            return Json(new { success = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ── Onay Paneli — modal içeriği (HTML partial) ────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ApprovalPanel(int id, CancellationToken cancellationToken)
    {
        var document = await _incomingDocumentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null) return Content("<div class='p-3 text-danger'>Belge bulunamadı.</div>", "text/html");

        // 2026-08-28 — ENGEL KALKTI, DAVRANIŞ BİLİNÇLİ OLARAK DEĞİŞTİRİLMEDİ:
        // IncomingDocument.Id artık INT (tablo PK'si ile aynı tip), yani ApprovalInstance.DocumentId
        // (INT) ile doğrudan ilişkilendirilebilir; eski "Guid PK uyumsuzluğu" gerekçesi geçersiz.
        // Bu ucu yeniden etkinleştirmek KULLANICI KARARI (görünür davranış değişikliği) — tip
        // düzeltmesinin yan etkisi olarak sessizce açılmadı. Açılacaksa: DocumentId üzerinden
        // ApprovalInstance sorgusu geri eklenir.
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
    public async Task<IActionResult> ApprovalInstance(int id, CancellationToken cancellationToken)
    {
        try
        {
            // 2026-08-28 — ENGEL KALKTI (IncomingDocument.Id artık INT), ama uç bilinçli olarak
            // boş dönmeye devam ediyor: yeniden etkinleştirmek görünür davranış değişikliğidir ve
            // kullanıcı kararına bırakıldı. Bkz. ApprovalPanel'deki not.
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
            _logger.LogError(ex, "[Onay] Onay örneği okunamadı (id={Id}).", id);
            return Json(new { found = false, error = "İşlem sırasında bir hata oluştu." });
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
            _logger.LogError(ex, "[Onay] Akış eşleştirme başarısız (kind={Kind}).", kind);
            return Json(new { matched = false, error = "İşlem sırasında bir hata oluştu." });
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
            _logger.LogError(ex, "[Onay] Akış listesi okunamadı (kind={Kind}) — boş liste dönülüyor.", kind);
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
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Onay] Onay başlatılamadı (documentId={DocumentId}, flowId={FlowId}).", documentId, flowId);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ── Mevcut adımı onayla ───────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveStep(int instanceId, string? note, string? choiceArmId, CancellationToken cancellationToken)
    {
        try
        {
            var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            var instance = await _approvalFlowService.ApproveStepAsync(
                new ApproveStepRequest(instanceId, userId, userName, note, choiceArmId), cancellationToken);

            // Onay tamamlandıysa entity'yi ilerlet. EntityKind="Capa" (DÖF kapanış onayı) AYRI
            // bir dala gider — CapaService kapanış guard'ını tekrar doğrulayıp kapatır. Diğer
            // TÜM entity kind'ları (varsayılan "Document" dahil) ESKİ davranışı aynen korur.
            if (string.Equals(instance.Status, "Approved", StringComparison.OrdinalIgnoreCase) && instance.DocumentId.HasValue)
            {
                if (string.Equals(instance.EntityKind, "Capa", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await _capaService.OnClosureApprovalCompletedAsync(instance.DocumentId.Value, approved: true, GetCurrentUserId(), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "DÖF kapanış onayı tamamlama hatası (documentId={Id}).", instance.DocumentId.Value);
                    }
                }
                else
                {
                    await _documentService.ChangeStatusAsync(instance.DocumentId.Value, "Approved", cancellationToken);
                }
            }

            return Json(new { ok = true, status = instance.Status,
                currentStep = instance.CurrentStep, totalSteps = instance.TotalSteps });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Onay adımı işlenirken hata (instanceId={InstanceId}).", instanceId);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
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

            // Onay reddedildiyse entity'yi ilerlet. EntityKind="Capa" AYRI dala gider (CapaService
            // Aksiyonda'ya geri döner); diğer TÜM entity kind'ları eski davranışı korur.
            if (string.Equals(instance.Status, "Rejected", StringComparison.OrdinalIgnoreCase) && instance.DocumentId.HasValue)
            {
                if (string.Equals(instance.EntityKind, "Capa", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await _capaService.OnClosureApprovalCompletedAsync(instance.DocumentId.Value, approved: false, GetCurrentUserId(), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "DÖF kapanış onayı reddi işlenirken hata (documentId={Id}).", instance.DocumentId.Value);
                    }
                }
                else
                {
                    await _documentService.ChangeStatusAsync(instance.DocumentId.Value, "Rejected", cancellationToken);
                }
            }

            return Json(new { ok = true, status = instance.Status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Onay reddi işlenirken hata (instanceId={InstanceId}).", instanceId);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
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
            _logger.LogError(ex, "[Onay] Onay iptali başarısız (instanceId={InstanceId}).", instanceId);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }
}

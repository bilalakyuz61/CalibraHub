using System.Data;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

public sealed class DocumentGenerationService : IDocumentGenerationService
{
    private readonly IReportTemplateRepository _templateRepo;
    private readonly IReportTemplateSourceRepository _sourceRepo;
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IReportDataRepository _dataRepo;
    private readonly IReportService _reportService;
    private readonly ZplGeneratorService _zplGenerator;
    private readonly ILogger<DocumentGenerationService> _logger;

    public DocumentGenerationService(
        IReportTemplateRepository templateRepo,
        IReportTemplateSourceRepository sourceRepo,
        IDocumentTypeRepository docTypeRepo,
        IReportDataRepository dataRepo,
        IReportService reportService,
        ZplGeneratorService zplGenerator,
        ILogger<DocumentGenerationService> logger)
    {
        _templateRepo  = templateRepo;
        _sourceRepo    = sourceRepo;
        _docTypeRepo   = docTypeRepo;
        _dataRepo      = dataRepo;
        _reportService = reportService;
        _zplGenerator  = zplGenerator;
        _logger        = logger;
    }

    public async Task<byte[]> GeneratePdfAsync(int templateId, int recordId, CancellationToken ct = default)
    {
        var (template, sources) = await LoadTemplateAndSourcesAsync(templateId, recordId, ct);
        var hasContent = template.FrxContent is { Length: > 0 };

        _logger.LogInformation("[RPT] PDF uretiliyor: template={Template}, record={Record}, sourceCount={N}, sources=[{Sources}]",
            template.Name, recordId, sources.Count,
            string.Join(", ", sources.Select(kv => $"{kv.Key}:{kv.Value.Rows.Count}r")));

        // Legacy: "Belge" ile ayni tabloya "Data" alias'i ekle (eski [Data.X] sablonlari icin)
        if (sources.TryGetValue("Belge", out var belge) && !sources.ContainsKey("Data"))
        {
            sources["Data"] = belge;
        }

        return hasContent
            ? await _reportService.ExportPdfFromBytesAsync(template.FrxContent!, (IReadOnlyDictionary<string, DataTable>)sources, ct)
            : await _reportService.ExportPdfAsync(template.FrxFilePath!, sources["Belge"], ct);
    }

    public async Task<string> GenerateHtmlPreviewAsync(int templateId, int recordId, CancellationToken ct = default)
    {
        var (template, sources) = await LoadTemplateAndSourcesAsync(templateId, recordId, ct);
        var hasContent = template.FrxContent is { Length: > 0 };

        if (sources.TryGetValue("Belge", out var belge) && !sources.ContainsKey("Data"))
        {
            sources["Data"] = belge;
        }

        return hasContent
            ? await _reportService.ExportHtmlFromBytesAsync(template.FrxContent!, (IReadOnlyDictionary<string, DataTable>)sources, ct)
            : await _reportService.ExportHtmlAsync(template.FrxFilePath!, sources["Belge"], ct);
    }

    /// <summary>
    /// Template ve data source'larini cozumleyip her birinin DataTable'ini yukler.
    ///
    /// Source cozumleme onceligi:
    ///   1) DB'de kayitli <c>report_template_sources</c> satirlari varsa onlar kullanilir (multi-source)
    ///   2) Yoksa geriye uyumlu virtual primary olusturulur — template.SqlViewName + KeyColumn
    ///      (yoksa docType.SqlViewName + RequiredKeyColumn) → "Belge" adinda tek source
    ///
    /// Detail source'lar (ParentSourceName != null): parent'in ilgili kolonundaki
    /// degerler IN ile filtrelemek yerine SIMPLIFIED yaklasim — her source'un kendi
    /// KeyColumn'u = @RecordId ile filtrelenir. Bu, detail view'in join'li olmasini
    /// gerektirir (ornek: vw_ReportDocumentDetails'da BelgeId kolonu var). Daha karmasik
    /// parent-derived filtering ileride eklenebilir.
    /// </summary>
    private async Task<(ReportTemplate Template, Dictionary<string, DataTable> Sources)> LoadTemplateAndSourcesAsync(
        int templateId, int recordId, CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct)
            ?? throw new InvalidOperationException($"Sablon bulunamadi: {templateId}");

        var docType = await _docTypeRepo.GetByIdAsync(template.DocumentTypeId, ct)
            ?? throw new InvalidOperationException($"Belge tipi bulunamadi: {template.DocumentTypeId}");

        var hasContent = template.FrxContent is { Length: > 0 };
        var hasFilePath = !string.IsNullOrWhiteSpace(template.FrxFilePath);
        if (!hasContent && !hasFilePath)
            throw new InvalidOperationException("Sablon icin FRX icerigi tanimlanmamis.");

        // 1) DB'den source kayitlari
        var storedSources = await _sourceRepo.GetByTemplateIdAsync(templateId, ct);

        var result = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);

        if (storedSources.Count > 0)
        {
            // Her source icin SELECT * FROM view WHERE [keyColumn] = @recordId [+ ORDER BY]
            // Source-level siralama: her source'un kendi SortColumn/SortDirection'i uygulanir.
            foreach (var src in storedSources)
            {
                var dt = await _dataRepo.GetReportDataAsync(
                    src.ViewName, recordId, src.KeyColumn,
                    src.SortColumn, src.SortDirection, ct);
                dt.TableName = src.SourceName;
                result[src.SourceName] = dt;
            }
        }
        else
        {
            // Geriye uyumlu: virtual primary "Belge" source'u
            var viewName = !string.IsNullOrWhiteSpace(template.SqlViewName)
                ? template.SqlViewName!.Trim()
                : docType.SqlViewName;
            if (string.IsNullOrWhiteSpace(viewName))
                throw new InvalidOperationException($"Belge tipi '{docType.Name}' icin SQL View tanimlanmamis.");

            var keyCol = !string.IsNullOrWhiteSpace(template.KeyColumn)
                ? template.KeyColumn
                : docType.RequiredKeyColumn;

            var dt = await _dataRepo.GetReportDataAsync(
                viewName, recordId, keyCol, null, null, ct);
            result["Belge"] = dt;
        }

        return (template, result);
    }

    public async Task<string> GenerateZplAsync(int recordId, string documentTypeCode, CancellationToken ct = default)
    {
        var docType = await _docTypeRepo.GetByCodeAsync(documentTypeCode, ct)
            ?? throw new InvalidOperationException($"Belge tipi bulunamadi: {documentTypeCode}");

        if (string.IsNullOrWhiteSpace(docType.SqlViewName))
            throw new InvalidOperationException($"Belge tipi '{docType.Name}' icin SQL View tanimlanmamis.");

        var data = await _dataRepo.GetReportDataAsync(docType.SqlViewName, recordId, ct);
        return _zplGenerator.Generate(data);
    }
}

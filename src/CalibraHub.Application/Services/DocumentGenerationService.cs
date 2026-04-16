using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

public sealed class DocumentGenerationService : IDocumentGenerationService
{
    private readonly IReportTemplateRepository _templateRepo;
    private readonly IDocumentTypeRepository _docTypeRepo;
    private readonly IReportDataRepository _dataRepo;
    private readonly IReportService _reportService;
    private readonly ZplGeneratorService _zplGenerator;
    private readonly ILogger<DocumentGenerationService> _logger;

    public DocumentGenerationService(
        IReportTemplateRepository templateRepo,
        IDocumentTypeRepository docTypeRepo,
        IReportDataRepository dataRepo,
        IReportService reportService,
        ZplGeneratorService zplGenerator,
        ILogger<DocumentGenerationService> logger)
    {
        _templateRepo  = templateRepo;
        _docTypeRepo   = docTypeRepo;
        _dataRepo      = dataRepo;
        _reportService = reportService;
        _zplGenerator  = zplGenerator;
        _logger        = logger;
    }

    public async Task<byte[]> GeneratePdfAsync(Guid templateId, Guid recordId, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct)
            ?? throw new InvalidOperationException($"Sablon bulunamadi: {templateId}");

        var docType = await _docTypeRepo.GetByIdAsync(template.DocumentTypeId, ct)
            ?? throw new InvalidOperationException($"Belge tipi bulunamadi: {template.DocumentTypeId}");

        if (string.IsNullOrWhiteSpace(template.FrxFilePath))
            throw new InvalidOperationException("Sablon icin FRX dosya yolu tanimlanmamis.");

        if (string.IsNullOrWhiteSpace(docType.SqlViewName))
            throw new InvalidOperationException($"Belge tipi '{docType.Name}' icin SQL View tanimlanmamis.");

        var data = await _dataRepo.GetReportDataAsync(docType.SqlViewName, recordId, ct);
        _logger.LogDebug("PDF uretiliyor: template={Template}, record={Record}, rows={Rows}",
            template.Name, recordId, data.Rows.Count);

        return await _reportService.ExportPdfAsync(template.FrxFilePath, data, ct);
    }

    public async Task<string> GenerateHtmlPreviewAsync(Guid templateId, Guid recordId, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetByIdAsync(templateId, ct)
            ?? throw new InvalidOperationException($"Sablon bulunamadi: {templateId}");

        var docType = await _docTypeRepo.GetByIdAsync(template.DocumentTypeId, ct)
            ?? throw new InvalidOperationException($"Belge tipi bulunamadi: {template.DocumentTypeId}");

        if (string.IsNullOrWhiteSpace(template.FrxFilePath))
            throw new InvalidOperationException("Sablon icin FRX dosya yolu tanimlanmamis.");

        if (string.IsNullOrWhiteSpace(docType.SqlViewName))
            throw new InvalidOperationException($"Belge tipi '{docType.Name}' icin SQL View tanimlanmamis.");

        var data = await _dataRepo.GetReportDataAsync(docType.SqlViewName, recordId, ct);
        return await _reportService.ExportHtmlAsync(template.FrxFilePath, data, ct);
    }

    public async Task<string> GenerateZplAsync(Guid recordId, string documentTypeCode, CancellationToken ct = default)
    {
        var docType = await _docTypeRepo.GetByCodeAsync(documentTypeCode, ct)
            ?? throw new InvalidOperationException($"Belge tipi bulunamadi: {documentTypeCode}");

        if (string.IsNullOrWhiteSpace(docType.SqlViewName))
            throw new InvalidOperationException($"Belge tipi '{docType.Name}' icin SQL View tanimlanmamis.");

        var data = await _dataRepo.GetReportDataAsync(docType.SqlViewName, recordId, ct);
        return _zplGenerator.Generate(data);
    }
}

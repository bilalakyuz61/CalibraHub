namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentGenerationService
{
    Task<byte[]> GeneratePdfAsync(Guid templateId, Guid recordId, CancellationToken ct = default);
    Task<string> GenerateHtmlPreviewAsync(Guid templateId, Guid recordId, CancellationToken ct = default);
    Task<string> GenerateZplAsync(Guid recordId, string documentTypeCode, CancellationToken ct = default);
}

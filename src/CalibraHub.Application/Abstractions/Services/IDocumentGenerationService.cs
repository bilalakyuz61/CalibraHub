namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentGenerationService
{
    Task<byte[]> GeneratePdfAsync(int templateId, int recordId, CancellationToken ct = default);
    Task<string> GenerateHtmlPreviewAsync(int templateId, int recordId, CancellationToken ct = default);
    Task<string> GenerateZplAsync(int recordId, string documentTypeCode, CancellationToken ct = default);
}

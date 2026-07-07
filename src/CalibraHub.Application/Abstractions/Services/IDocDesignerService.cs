using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocDesignerService
{
    Task<IReadOnlyCollection<DocLayoutSummaryDto>> ListAsync(string? docType, CancellationToken ct);
    Task<DocLayoutDetailDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveDocLayoutRequest req, ReportCallerContext caller, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task SetDefaultAsync(int id, CancellationToken ct);
    /// <summary>Mevcut tasarımın kopyasını oluşturur; yeni layout id'sini döner.</summary>
    Task<int> CloneAsync(int sourceId, CancellationToken ct);
    Task<byte[]> RenderPdfAsync(DocLayoutRunRequest req, CancellationToken ct);
    Task<string> RenderHtmlPreviewAsync(DocLayoutRunRequest req, CancellationToken ct);
}

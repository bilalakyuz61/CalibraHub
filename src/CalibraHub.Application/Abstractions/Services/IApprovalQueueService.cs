using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IApprovalQueueService
{
    Task<IReadOnlyCollection<PendingApprovalDocumentDto>> GetPendingAsync(bool? isProcessed, CancellationToken cancellationToken);

    /// <summary>Kuyrugun tek sayfasi (SQL tarafinda suzulur ve dilimlenir).</summary>
    Task<(IReadOnlyList<PendingApprovalDocumentDto> Items, int TotalCount)> GetPendingPageAsync(
        string? kind, bool? isProcessed, string? search, int page, int pageSize,
        CancellationToken cancellationToken);
    Task ToggleProcessingStatusAsync(int documentId, bool isProcessed, CancellationToken cancellationToken);
}

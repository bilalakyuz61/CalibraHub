using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IApprovalQueueService
{
    Task<IReadOnlyCollection<PendingApprovalDocumentDto>> GetPendingAsync(bool? isProcessed, CancellationToken cancellationToken);
    Task ToggleProcessingStatusAsync(Guid documentId, bool isProcessed, CancellationToken cancellationToken);
}

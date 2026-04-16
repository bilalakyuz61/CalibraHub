using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IIncomingDocumentRepository
{
    Task<bool> ExistsByEnvelopeIdAsync(string envelopeId, CancellationToken cancellationToken);
    Task<bool> ExistsByDocumentNumberAndRecipientAsync(
        string documentNumber,
        string recipientTaxNumber,
        DocumentKind kind,
        CancellationToken cancellationToken);
    Task AddAsync(IncomingDocument document, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<IncomingDocument>> GetPendingApprovalsAsync(bool? isProcessed, CancellationToken cancellationToken);
    Task<IncomingDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateIsProcessedAsync(Guid id, bool isProcessed, CancellationToken cancellationToken);
}

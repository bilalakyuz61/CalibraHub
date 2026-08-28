using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryIncomingDocumentRepository : IIncomingDocumentRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryIncomingDocumentRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<bool> ExistsByEnvelopeIdAsync(string envelopeId, CancellationToken cancellationToken)
    {
        var exists = _dataStore.IncomingDocuments.Values
            .Any(x => string.Equals(x.EnvelopeId, envelopeId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(exists);
    }

    public Task<bool> ExistsByDocumentNumberAndRecipientAsync(
        string documentNumber,
        string recipientTaxNumber,
        DocumentKind kind,
        CancellationToken cancellationToken)
    {
        var exists = _dataStore.IncomingDocuments.Values.Any(x =>
            string.Equals(x.DocumentNumber, documentNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.RecipientTaxNumber, recipientTaxNumber, StringComparison.OrdinalIgnoreCase) &&
            x.Kind == kind);

        return Task.FromResult(exists);
    }

    public Task AddAsync(IncomingDocument document, CancellationToken cancellationToken,
                         CalibraHub.Application.Services.EDocument.EDocumentDetails? details = null)
    {
        _dataStore.IncomingDocuments.TryAdd(document.Id, document);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<IncomingDocument>> GetPendingApprovalsAsync(bool? isProcessed, CancellationToken cancellationToken)
    {
        var query = _dataStore.IncomingDocuments.Values
            .Where(x => x.ApprovalStatus == ApprovalStatus.Pending);

        if (isProcessed.HasValue)
        {
            query = query.Where(x => x.IsProcessed == isProcessed.Value);
        }

        IReadOnlyCollection<IncomingDocument> result = query.ToArray();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<CalibraHub.Application.Services.EDocument.EDocumentLineData>> GetLinesAsync(
        int documentId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CalibraHub.Application.Services.EDocument.EDocumentLineData>>(
            Array.Empty<CalibraHub.Application.Services.EDocument.EDocumentLineData>());

    public Task<int> GetMaxOfflineSourceKeyAsync(DocumentKind kind, CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<IncomingDocument?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.IncomingDocuments.TryGetValue(id, out var document);
        return Task.FromResult(document);
    }

    public Task UpdateIsProcessedAsync(int id, bool isProcessed, CancellationToken cancellationToken)
    {
        if (_dataStore.IncomingDocuments.TryGetValue(id, out var document))
        {
            document.SetProcessed(isProcessed);
        }
        return Task.CompletedTask;
    }
}

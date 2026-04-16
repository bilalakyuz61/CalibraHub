using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Integrations;

public interface IIntegratorDocumentClient
{
    Task<IReadOnlyCollection<IncomingDocumentPayload>> PullDocumentsAsync(
        IntegratorSettings settings,
        int maxRecordsPerPull,
        IntegratorDocumentPullOptions pullOptions,
        CancellationToken cancellationToken);

    Task MarkDocumentsAsReceivedAsync(
        IntegratorSettings settings,
        IReadOnlyCollection<IncomingDocumentPayload> documents,
        CancellationToken cancellationToken);
}

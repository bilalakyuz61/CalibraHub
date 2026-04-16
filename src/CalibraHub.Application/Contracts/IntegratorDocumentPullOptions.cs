using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record IntegratorDocumentPullOptions(
    bool IncludeReceivedDocumentsInPull,
    bool IncludeIssuedEInvoicesInPull,
    bool IncludeIssuedEArchivesInPull,
    bool IncludeIssuedEDispatchesInPull,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null)
{
    public bool IncludesIssuedKind(DocumentKind kind) =>
        kind switch
        {
            DocumentKind.EArchive => IncludeIssuedEArchivesInPull,
            DocumentKind.EDispatch => IncludeIssuedEDispatchesInPull,
            _ => IncludeIssuedEInvoicesInPull
        };
}

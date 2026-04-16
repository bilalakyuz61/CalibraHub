using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using System.Xml.Linq;

namespace CalibraHub.Application.Services;

public sealed class ApprovalQueueService : IApprovalQueueService
{
    private readonly IIncomingDocumentRepository _incomingDocumentRepository;

    public ApprovalQueueService(IIncomingDocumentRepository incomingDocumentRepository)
    {
        _incomingDocumentRepository = incomingDocumentRepository;
    }

    public async Task<IReadOnlyCollection<PendingApprovalDocumentDto>> GetPendingAsync(bool? isProcessed, CancellationToken cancellationToken)
    {
        var pendingDocuments = await _incomingDocumentRepository.GetPendingApprovalsAsync(isProcessed, cancellationToken);

        return pendingDocuments
            .OrderByDescending(x => x.ImportedAt)
            .Select(x => new PendingApprovalDocumentDto(
                x.Id,
                x.EnvelopeId,
                x.DocumentNumber,
                x.Kind.ToString(),
                ExtractScenario(x.PayloadRaw),
                x.SenderTaxNumber,
                x.SenderName,
                x.IssueDate,
                x.ImportedAt,
                x.IsProcessed))
            .ToArray();
    }

    public async Task ToggleProcessingStatusAsync(Guid documentId, bool isProcessed, CancellationToken cancellationToken)
    {
        await _incomingDocumentRepository.UpdateIsProcessedAsync(documentId, isProcessed, cancellationToken);
    }

    private static string? ExtractScenario(string? payloadRaw)
    {
        if (string.IsNullOrWhiteSpace(payloadRaw))
            return null;
            
        try
        {
            // Yüksek boyutlu XML parse işlemleri inanılmaz ağır olduğu için sadece metin araştırması yapıyoruz
            var pIndex = payloadRaw.IndexOf("<cbc:ProfileID");
            if (pIndex < 0) pIndex = payloadRaw.IndexOf("<ProfileID");
            
            if (pIndex > -1)
            {
                var start = payloadRaw.IndexOf('>', pIndex) + 1;
                var end = payloadRaw.IndexOf("</", start);
                if (start > 0 && end > start && end - start < 100)
                {
                    var profileId = payloadRaw.Substring(start, end - start).Trim();
                    return ParseScenarioLabel(profileId);
                }
            }
            return null;
        }
        catch { return null; }
    }

    private static string? ParseScenarioLabel(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return null;
        // ProfileID can be "TICARIFATURA", "TR:TICARIFATURA:1.0", etc.
        // Extract the meaningful all-uppercase segment.
        var parts = profileId.Split(':');
        var label = parts.FirstOrDefault(p =>
            p.Length > 4 &&
            p.ToUpperInvariant() == p &&
            p.All(c => char.IsLetter(c) || char.IsDigit(c)));
        return label ?? profileId.Trim();
    }
}

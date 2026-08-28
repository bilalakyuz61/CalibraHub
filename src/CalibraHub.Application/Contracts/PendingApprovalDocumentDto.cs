namespace CalibraHub.Application.Contracts;

public sealed record PendingApprovalDocumentDto(
    int Id,
    string EnvelopeId,
    string DocumentNumber,
    string Kind,
    string? Scenario,
    string SenderTaxNumber,
    string? SenderName,
    DateOnly IssueDate,
    DateTime ImportedAt,
    bool IsProcessed,
    /// <summary>Belgenin sisteme HANGI YOLDAN girdigi: Online (entegrator) | Offline (ERP).
    /// Positional record — alan SONA eklendi ki mevcut cagri yerleri kaymasin.</summary>
    string IngestSource);

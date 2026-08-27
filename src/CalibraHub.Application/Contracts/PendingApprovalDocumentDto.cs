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
    bool IsProcessed);

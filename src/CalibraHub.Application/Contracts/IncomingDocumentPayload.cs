using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record IncomingDocumentPayload(
    string EnvelopeId,
    string DocumentNumber,
    DocumentKind Kind,
    DocumentDirection Direction,
    DateOnly IssueDate,
    string SenderTaxNumber,
    string? SenderName,
    string RecipientTaxNumber,
    string PayloadRaw);

namespace CalibraHub.Web.Models.Approval;

public sealed class ApprovalDocumentViewerViewModel
{
    public required Guid Id { get; init; }
    public required string DocumentNumber { get; init; }
    public required string Kind { get; init; }
    public required DateOnly IssueDate { get; init; }
    public required string SenderTaxNumber { get; init; }
    public string? SenderName { get; init; }
    public required string EnvelopeId { get; init; }
    public required string XmlContent { get; init; }
    public InvoiceRenderData? RenderData { get; init; }
}

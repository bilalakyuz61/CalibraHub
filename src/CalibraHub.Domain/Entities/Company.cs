namespace CalibraHub.Domain.Entities;

public sealed class Company
{
    public int Id { get; set; }
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Address { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? PostalCode { get; init; }
    public required string TaxOffice { get; init; }
    public required string TaxNumber { get; init; }
    public bool IsEDocumentApprovalEnabled { get; init; }
    public string? DatabaseConnectionString { get; init; }
    public bool IsActive { get; private set; } = true;

    public void Deactivate() => IsActive = false;
}

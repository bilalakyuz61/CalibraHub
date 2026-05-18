namespace CalibraHub.Application.Contracts;

public sealed record SalesRepresentativeDto(int Id, string RepName, bool IsActive);
public sealed record CreateSalesRepresentativeRequest(string RepName, bool IsActive = true);
public sealed record UpdateSalesRepresentativeRequest(int Id, string RepName, bool IsActive);

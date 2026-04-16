namespace CalibraHub.Application.Contracts;

public sealed record SalesRepresentativeDto(int Id, string RepCode, string RepName, bool IsActive);
public sealed record CreateSalesRepresentativeRequest(string RepCode, string RepName, bool IsActive = true);
public sealed record UpdateSalesRepresentativeRequest(int Id, string RepCode, string RepName, bool IsActive);

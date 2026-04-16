using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ISalesRepresentativeService
{
    Task<IReadOnlyCollection<SalesRepresentativeDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<SalesRepresentativeDto?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateSalesRepresentativeRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> UpdateAsync(UpdateSalesRepresentativeRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken cancellationToken);
}

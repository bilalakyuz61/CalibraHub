using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ICariGroupService
{
    Task<IReadOnlyCollection<CariGroupDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<CariGroupDto?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateCariGroupRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> UpdateAsync(UpdateCariGroupRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken cancellationToken);
}

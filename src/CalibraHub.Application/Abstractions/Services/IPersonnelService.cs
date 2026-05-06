using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IPersonnelService
{
    Task<IReadOnlyCollection<PersonnelDto>> ListAsync(bool includeInactive, bool onlyOperators, CancellationToken ct);
    Task<PersonnelDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SavePersonnelRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<PersonnelDto?> GetByPinOrCardAsync(string? pinCode, string? cardNo, CancellationToken ct);
}

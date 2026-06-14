using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICompanyRepository
{
    Task<IReadOnlyCollection<Company>> GetAllAsync(CancellationToken cancellationToken);
    Task<Company?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAsync(Company company, CancellationToken cancellationToken);
    Task UpdateAsync(Company company, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}

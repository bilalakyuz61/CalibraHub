using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICompanyDefinitionRepository
{
    Task<IReadOnlyCollection<CompanyDefinition>> GetAllAsync(CancellationToken cancellationToken);
    Task<CompanyDefinition?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAsync(CompanyDefinition company, CancellationToken cancellationToken);
    Task UpdateAsync(CompanyDefinition company, CancellationToken cancellationToken);
}

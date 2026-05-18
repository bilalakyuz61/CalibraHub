using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocLayoutRuleService
{
    Task<IReadOnlyCollection<DocLayoutRuleDto>> ListAllAsync(CancellationToken ct);
    Task<DocLayoutRuleDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveDocLayoutRuleRequest req, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

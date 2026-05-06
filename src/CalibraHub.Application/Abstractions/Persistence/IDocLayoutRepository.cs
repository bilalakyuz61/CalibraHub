using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDocLayoutRepository
{
    Task<IReadOnlyCollection<DocLayoutSummaryDto>> ListAsync(string? docType, CancellationToken ct);
    Task<DocLayout?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocLayoutDs>> GetDataSourcesAsync(int layoutId, CancellationToken ct);
    Task<int> UpsertAsync(SaveDocLayoutRequest req, Guid ownerUserId, CancellationToken ct);
    Task ReplaceDataSourcesAsync(int layoutId, IReadOnlyCollection<DocLayoutDsDto> sources, CancellationToken ct);
    Task SoftDeleteAsync(int id, CancellationToken ct);
}

using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IScreenLayoutRepository
{
    Task<ScreenLayoutDefinition?> GetByScreenCodeAsync(string screenCode, CancellationToken cancellationToken);
    Task UpsertAsync(ScreenLayoutDefinition definition, CancellationToken cancellationToken);
}

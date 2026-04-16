using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IIntegratorImportLogRepository
{
    Task WriteAsync(IntegratorImportLogWriteRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<IntegratorImportLogEntryDto>> GetRecentAsync(int take, CancellationToken cancellationToken);
    Task CleanupExpiredAsync(int integratorSettingsId, int retentionDays, CancellationToken cancellationToken);
}

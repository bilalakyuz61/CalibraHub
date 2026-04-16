using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IAdminReadService
{
    Task<AdminSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<IntegratorImportLogEntryDto>> GetRecentIntegratorImportLogsAsync(
        int take,
        CancellationToken cancellationToken);
}

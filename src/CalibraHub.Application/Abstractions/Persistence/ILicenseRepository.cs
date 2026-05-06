using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ILicenseRepository
{
    Task<LicenseRecord?> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(LicenseRecord record, CancellationToken cancellationToken);
}

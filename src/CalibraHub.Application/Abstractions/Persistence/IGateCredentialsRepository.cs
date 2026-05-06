using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>Gate sifresi persistence — tek row (id=1).</summary>
public interface IGateCredentialsRepository
{
    Task<GateCredentials?> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(GateCredentials credentials, CancellationToken cancellationToken);
}

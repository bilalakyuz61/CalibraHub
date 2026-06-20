using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ISmtpProfileRepository
{
    Task<IReadOnlyCollection<SmtpProfile>> GetAllAsync(CancellationToken cancellationToken);
    Task<SmtpProfile?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task AddAsync(SmtpProfile profile, CancellationToken cancellationToken);
    Task UpdateAsync(SmtpProfile profile, CancellationToken cancellationToken);
}

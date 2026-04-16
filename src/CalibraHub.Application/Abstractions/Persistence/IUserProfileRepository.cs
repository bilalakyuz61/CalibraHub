using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IUserProfileRepository
{
    Task<IReadOnlyCollection<UserProfile>> GetAllAsync(CancellationToken cancellationToken);
    Task<UserProfile?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<UserProfile?> GetByEmailAndCompanyIdAsync(string email, int companyId, CancellationToken cancellationToken);
    Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(UserProfile userProfile, CancellationToken cancellationToken);
    Task UpdateAsync(UserProfile userProfile, CancellationToken cancellationToken);
}

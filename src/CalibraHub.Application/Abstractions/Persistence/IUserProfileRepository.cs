using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IUserProfileRepository
{
    Task<IReadOnlyCollection<UserProfile>> GetAllAsync(CancellationToken cancellationToken);
    Task<UserProfile?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<UserProfile?> GetByEmailAndCompanyIdAsync(string email, int companyId, CancellationToken cancellationToken);
    Task<UserProfile?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task AddAsync(UserProfile userProfile, CancellationToken cancellationToken);
    Task UpdateAsync(UserProfile userProfile, CancellationToken cancellationToken);

    // Şifre sıfırlama token işlemleri
    Task SetResetTokenAsync(int userId, string token, DateTime expiry, CancellationToken ct);
    Task<UserProfile?> GetByResetTokenAsync(string token, CancellationToken ct);
    Task ClearResetTokenAsync(int userId, CancellationToken ct);
}

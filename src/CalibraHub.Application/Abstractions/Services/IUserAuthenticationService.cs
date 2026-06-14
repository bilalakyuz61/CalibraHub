using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IUserAuthenticationService
{
    Task<AuthenticatedUserDto?> AuthenticateAsync(string email, string password, int companyId, CancellationToken cancellationToken);
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken);
}

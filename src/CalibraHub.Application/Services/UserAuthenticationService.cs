using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;

namespace CalibraHub.Application.Services;

public sealed class UserAuthenticationService : IUserAuthenticationService
{
    private readonly ICompanyDefinitionRepository _companyDefinitionRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IPasswordHashService _passwordHashService;

    public UserAuthenticationService(
        ICompanyDefinitionRepository companyDefinitionRepository,
        IUserProfileRepository userProfileRepository,
        IPasswordHashService passwordHashService)
    {
        _companyDefinitionRepository = companyDefinitionRepository;
        _userProfileRepository = userProfileRepository;
        _passwordHashService = passwordHashService;
    }

    public async Task<AuthenticatedUserDto?> AuthenticateAsync(
        string email,
        string password,
        int companyId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim();

        if (string.IsNullOrWhiteSpace(normalizedEmail) ||
            string.IsNullOrWhiteSpace(password) ||
            companyId == 0)
        {
            return null;
        }

        var company = await _companyDefinitionRepository.GetByIdAsync(companyId, cancellationToken);
        if (company is null || !company.IsActive)
        {
            return null;
        }

        var user = await _userProfileRepository.GetByEmailAndCompanyIdAsync(
            normalizedEmail,
            companyId,
            cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (!_passwordHashService.VerifyPassword(password, user.PasswordHash))
        {
            return null;
        }

        return new AuthenticatedUserDto(
            user.Id,
            user.FullName,
            user.Email,
            UserAuthorizationCatalog.GetRoleLabel(user.Role),
            company.Id,
            company.Name);
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await _userProfileRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new ArgumentException("Kullanici bulunamadi.");
        }

        if (!_passwordHashService.VerifyPassword(currentPassword, user.PasswordHash))
        {
            throw new ArgumentException("Mevcut sifre hatali.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            throw new ArgumentException("Yeni sifre en az 8 karakter olmalidir.");
        }

        user.SetPasswordHash(_passwordHashService.HashPassword(newPassword));
        await _userProfileRepository.UpdateAsync(user, cancellationToken);
    }
}

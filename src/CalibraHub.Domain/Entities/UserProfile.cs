using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class UserProfile : Entity
{
    public const string DefaultLanguageCode = "tr-TR";
    public const string DefaultThemeCode = "light";

    public int CompanyId { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string EmployeeCode { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid? SupervisorUserId { get; init; }
    public UserRole Role { get; init; } = UserRole.Operator;
    public IReadOnlyCollection<UserPermission> Permissions { get; init; } = Array.Empty<UserPermission>();
    public string PasswordHash { get; private set; } = string.Empty;
    public string LanguageCode { get; private set; } = DefaultLanguageCode;
    public string ThemeCode { get; private set; } = DefaultThemeCode;
    public string GridPreferencesJson { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash cannot be empty.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }

    public void SetInterfacePreferences(string languageCode, string themeCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            throw new ArgumentException("Language code cannot be empty.", nameof(languageCode));
        }

        if (string.IsNullOrWhiteSpace(themeCode))
        {
            throw new ArgumentException("Theme code cannot be empty.", nameof(themeCode));
        }

        LanguageCode = languageCode.Trim();
        ThemeCode = themeCode.Trim();
    }

    public void SetGridPreferencesJson(string? gridPreferencesJson)
    {
        GridPreferencesJson = string.IsNullOrWhiteSpace(gridPreferencesJson)
            ? string.Empty
            : gridPreferencesJson.Trim();
    }

    public void Deactivate() => IsActive = false;
}

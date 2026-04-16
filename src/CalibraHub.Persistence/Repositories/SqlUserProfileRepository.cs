using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlUserProfileRepository : IUserProfileRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlUserProfileRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[users]";
    }

    public async Task<IReadOnlyCollection<UserProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var users = new List<UserProfile>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [full_name], [email], [employee_code], [department_id], [supervisor_user_id], [role], [permissions], [password_hash], [language_code], [theme_code], [grid_preferences_json], [is_active]
            FROM {_tableName}
            ORDER BY [full_name];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public async Task<UserProfile?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [full_name], [email], [employee_code], [department_id], [supervisor_user_id], [role], [permissions], [password_hash], [language_code], [theme_code], [grid_preferences_json], [is_active]
            FROM {_tableName}
            WHERE [email] = @Email;
            """;
        command.Parameters.Add(new SqlParameter("@Email", email));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapUser(reader);
    }

    public async Task<UserProfile?> GetByEmailAndCompanyIdAsync(
        string email,
        int companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [full_name], [email], [employee_code], [department_id], [supervisor_user_id], [role], [permissions], [password_hash], [language_code], [theme_code], [grid_preferences_json], [is_active]
            FROM {_tableName}
            WHERE [email] = @Email
              AND [company_id] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@Email", email));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapUser(reader);
    }

    public async Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [full_name], [email], [employee_code], [department_id], [supervisor_user_id], [role], [permissions], [password_hash], [language_code], [theme_code], [grid_preferences_json], [is_active]
            FROM {_tableName}
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapUser(reader);
    }

    public async Task AddAsync(UserProfile userProfile, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([id], [company_id], [full_name], [email], [employee_code], [department_id], [supervisor_user_id], [role], [permissions], [password_hash], [language_code], [theme_code], [grid_preferences_json], [is_active])
            VALUES
                (@Id, @CompanyId, @FullName, @Email, @EmployeeCode, @DepartmentId, @SupervisorUserId, @Role, @Permissions, @PasswordHash, @LanguageCode, @ThemeCode, @GridPreferencesJson, @IsActive);
            """;

        AddUserParameters(command, userProfile);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(UserProfile userProfile, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET [company_id] = @CompanyId,
                [full_name] = @FullName,
                [email] = @Email,
                [employee_code] = @EmployeeCode,
                [department_id] = @DepartmentId,
                [supervisor_user_id] = @SupervisorUserId,
                [role] = @Role,
                [permissions] = @Permissions,
                [password_hash] = @PasswordHash,
                [language_code] = @LanguageCode,
                [theme_code] = @ThemeCode,
                [grid_preferences_json] = @GridPreferencesJson,
                [is_active] = @IsActive
            WHERE [id] = @Id;
            """;

        AddUserParameters(command, userProfile);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddUserParameters(SqlCommand command, UserProfile userProfile)
    {
        command.Parameters.Add(new SqlParameter("@Id", userProfile.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", userProfile.CompanyId));
        command.Parameters.Add(new SqlParameter("@FullName", userProfile.FullName));
        command.Parameters.Add(new SqlParameter("@Email", userProfile.Email));
        command.Parameters.Add(new SqlParameter("@EmployeeCode", userProfile.EmployeeCode));
        command.Parameters.Add(new SqlParameter("@DepartmentId", userProfile.DepartmentId));
        command.Parameters.Add(new SqlParameter("@SupervisorUserId", (object?)userProfile.SupervisorUserId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Role", userProfile.Role.ToString()));
        command.Parameters.Add(new SqlParameter("@Permissions", SerializePermissions(userProfile.Permissions)));
        command.Parameters.Add(new SqlParameter("@PasswordHash", userProfile.PasswordHash));
        command.Parameters.Add(new SqlParameter("@LanguageCode", userProfile.LanguageCode));
        command.Parameters.Add(new SqlParameter("@ThemeCode", userProfile.ThemeCode));
        command.Parameters.Add(new SqlParameter("@GridPreferencesJson", (object?)userProfile.GridPreferencesJson ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", userProfile.IsActive));
    }

    private static UserProfile MapUser(SqlDataReader reader)
    {
        var roleRaw = reader.GetString(7);
        var permissionsRaw = reader.GetString(8);
        var passwordHash = reader.GetString(9);
        var languageCode = reader.IsDBNull(10) ? UserProfile.DefaultLanguageCode : reader.GetString(10);
        var themeCode = reader.IsDBNull(11) ? UserProfile.DefaultThemeCode : reader.GetString(11);
        var gridPreferencesJson = reader.IsDBNull(12) ? string.Empty : reader.GetString(12);
        var isActive = reader.GetBoolean(13);

        if (!Enum.TryParse(roleRaw, true, out UserRole role) || !Enum.IsDefined(role))
        {
            role = UserRole.Operator;
        }

        var user = new UserProfile
        {
            Id = reader.GetGuid(0),
            CompanyId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            FullName = reader.GetString(2),
            Email = reader.GetString(3),
            EmployeeCode = reader.GetString(4),
            DepartmentId = reader.GetGuid(5),
            SupervisorUserId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
            Role = role,
            Permissions = DeserializePermissions(permissionsRaw)
        };

        user.SetPasswordHash(passwordHash);
        user.SetInterfacePreferences(languageCode, themeCode);
        user.SetGridPreferencesJson(gridPreferencesJson);
        if (!isActive)
        {
            user.Deactivate();
        }

        return user;
    }

    private static string SerializePermissions(IReadOnlyCollection<UserPermission> permissions) =>
        string.Join(',', permissions.Select(x => x.ToString()));

    private static IReadOnlyCollection<UserPermission> DeserializePermissions(string rawPermissions)
    {
        if (string.IsNullOrWhiteSpace(rawPermissions))
        {
            return Array.Empty<UserPermission>();
        }

        return rawPermissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Enum.TryParse(x, true, out UserPermission permission) ? permission : (UserPermission?)null)
            .Where(x => x.HasValue && Enum.IsDefined(x.Value))
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
    }
}

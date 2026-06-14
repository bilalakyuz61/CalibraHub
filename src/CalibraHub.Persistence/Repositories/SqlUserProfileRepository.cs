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
        _tableName = $"[{schema}].[Users]";
    }

    private const string SelectColumns =
        "[Id], [CompanyId], [FullName], [Email], [EmployeeCode], [DepartmentId], [SupervisorUserId], [Role], [Permissions], [PasswordHash], [LanguageCode], [ThemeCode], [GridPreferencesJson], [IsActive], [GrafanaRole], [PhoneNumber]";

    public async Task<IReadOnlyCollection<UserProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var users = new List<UserProfile>();

        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM {_tableName} ORDER BY [FullName];";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public async Task<UserProfile?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM {_tableName} WHERE [Email] = @Email;";
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
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM {_tableName} WHERE [Email] = @Email AND [CompanyId] = @CompanyId;";
        command.Parameters.Add(new SqlParameter("@Email", email));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapUser(reader);
    }

    public async Task<UserProfile?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM {_tableName} WHERE [Id] = @Id;";
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
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // INT IDENTITY — Id girilmez, SCOPE_IDENTITY ile geri çekilir.
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([CompanyId], [FullName], [Email], [EmployeeCode], [DepartmentId], [SupervisorUserId], [Role], [Permissions], [PasswordHash], [LanguageCode], [ThemeCode], [GridPreferencesJson], [IsActive], [GrafanaRole], [PhoneNumber])
            VALUES
                (@CompanyId, @FullName, @Email, @EmployeeCode, @DepartmentId, @SupervisorUserId, @Role, @Permissions, @PasswordHash, @LanguageCode, @ThemeCode, @GridPreferencesJson, @IsActive, @GrafanaRole, @PhoneNumber);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        AddUserParameters(command, userProfile, includeId: false);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var newId = result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        // Id init-only — caller'a yansitmak icin reflection gerekir, ama mevcut akis Id'yi geri okumayi
        // GetByEmail* uzerinden yapar. Yine de eldeki object'in Id'sini set etmek istersek backing field
        // expose etmedigimiz icin atlanir; eldeki referans Id=0 kalir, repository tutarliligi okumayla
        // saglanir.
        _ = newId;
    }

    public async Task UpdateAsync(UserProfile userProfile, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenSystemConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET [CompanyId] = @CompanyId,
                [FullName] = @FullName,
                [Email] = @Email,
                [EmployeeCode] = @EmployeeCode,
                [DepartmentId] = @DepartmentId,
                [SupervisorUserId] = @SupervisorUserId,
                [Role] = @Role,
                [Permissions] = @Permissions,
                [PasswordHash] = @PasswordHash,
                [LanguageCode] = @LanguageCode,
                [ThemeCode] = @ThemeCode,
                [GridPreferencesJson] = @GridPreferencesJson,
                [IsActive] = @IsActive,
                [GrafanaRole] = @GrafanaRole,
                [PhoneNumber] = @PhoneNumber
            WHERE [Id] = @Id;
            """;

        AddUserParameters(command, userProfile, includeId: true);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddUserParameters(SqlCommand command, UserProfile userProfile, bool includeId)
    {
        if (includeId)
            command.Parameters.Add(new SqlParameter("@Id", userProfile.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", userProfile.CompanyId));
        command.Parameters.Add(new SqlParameter("@FullName", userProfile.FullName));
        command.Parameters.Add(new SqlParameter("@Email", userProfile.Email));
        command.Parameters.Add(new SqlParameter("@EmployeeCode", userProfile.EmployeeCode));
        command.Parameters.Add(new SqlParameter("@DepartmentId", (object?)userProfile.DepartmentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SupervisorUserId", (object?)userProfile.SupervisorUserId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Role", userProfile.Role.ToString()));
        command.Parameters.Add(new SqlParameter("@Permissions", SerializePermissions(userProfile.Permissions)));
        command.Parameters.Add(new SqlParameter("@PasswordHash", userProfile.PasswordHash));
        command.Parameters.Add(new SqlParameter("@LanguageCode", userProfile.LanguageCode));
        command.Parameters.Add(new SqlParameter("@ThemeCode", userProfile.ThemeCode));
        command.Parameters.Add(new SqlParameter("@GridPreferencesJson", (object?)userProfile.GridPreferencesJson ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", userProfile.IsActive));
        command.Parameters.Add(new SqlParameter("@GrafanaRole", userProfile.GrafanaRole.HasValue
            ? (object)userProfile.GrafanaRole.Value.ToString()
            : DBNull.Value));
        command.Parameters.Add(new SqlParameter("@PhoneNumber",
            string.IsNullOrWhiteSpace(userProfile.PhoneNumber) ? (object)DBNull.Value : userProfile.PhoneNumber));
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
        GrafanaRole? grafanaRole = null;
        if (!reader.IsDBNull(14))
        {
            var gr = reader.GetString(14);
            if (Enum.TryParse(gr, true, out GrafanaRole parsedGrafana))
                grafanaRole = parsedGrafana;
        }
        string? phoneNumber = reader.IsDBNull(15) ? null : reader.GetString(15);

        if (!Enum.TryParse(roleRaw, true, out UserRole role) || !Enum.IsDefined(role))
        {
            role = UserRole.Operator;
        }

        var user = new UserProfile
        {
            Id = reader.GetInt32(0),
            CompanyId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            FullName = reader.GetString(2),
            Email = reader.GetString(3),
            EmployeeCode = reader.GetString(4),
            DepartmentId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
            SupervisorUserId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            Role = role,
            Permissions = DeserializePermissions(permissionsRaw),
            GrafanaRole = grafanaRole,
            PhoneNumber = phoneNumber,
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

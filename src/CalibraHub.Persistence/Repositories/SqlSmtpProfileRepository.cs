using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Security;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlSmtpProfileRepository : ISmtpProfileRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlSmtpProfileRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[SmtpProfile]";
    }

    public async Task<IReadOnlyCollection<SmtpProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = new List<SmtpProfile>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [Name], [FromEmail], [FromDisplayName], [Host], [Port], [Username], [Password], [AuthMethod], [OAuth2ClientId], [OAuth2ClientSecret], [OAuth2RefreshToken], [UseSsl], [IsActive], [Created]
            FROM {_tableName}
            ORDER BY [Name];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(MapProfile(reader));
        }

        return result;
    }

    public async Task<SmtpProfile?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [Name], [FromEmail], [FromDisplayName], [Host], [Port], [Username], [Password], [AuthMethod], [OAuth2ClientId], [OAuth2ClientSecret], [OAuth2RefreshToken], [UseSsl], [IsActive], [Created]
            FROM {_tableName}
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapProfile(reader);
    }

    public async Task AddAsync(SmtpProfile profile, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([CompanyId], [Name], [FromEmail], [FromDisplayName], [Host], [Port], [Username], [Password], [AuthMethod], [OAuth2ClientId], [OAuth2ClientSecret], [OAuth2RefreshToken], [UseSsl], [IsActive], [Created], [Updated])
            VALUES
                (@EntityCompanyId, @Name, @FromEmail, @FromDisplayName, @Host, @Port, @Username, @Password, @AuthMethod, @OAuth2ClientId, @OAuth2ClientSecret, @OAuth2RefreshToken, @UseSsl, @IsActive, @Created, @Updated);
            """;

        AddCommonParameters(command, profile);
        command.Parameters.Add(new SqlParameter("@Created", profile.Created));
        command.Parameters.Add(new SqlParameter("@Updated", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(SmtpProfile profile, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET [CompanyId] = @EntityCompanyId,
                [Name] = @Name,
                [FromEmail] = @FromEmail,
                [FromDisplayName] = @FromDisplayName,
                [Host] = @Host,
                [Port] = @Port,
                [Username] = @Username,
                [Password] = @Password,
                [AuthMethod] = @AuthMethod,
                [OAuth2ClientId] = @OAuth2ClientId,
                [OAuth2ClientSecret] = @OAuth2ClientSecret,
                [OAuth2RefreshToken] = @OAuth2RefreshToken,
                [UseSsl] = @UseSsl,
                [IsActive] = @IsActive,
                [Updated] = @Updated
            WHERE [Id] = @Id;
            """;

        AddCommonParameters(command, profile);
        command.Parameters.Add(new SqlParameter("@Id", profile.Id));
        command.Parameters.Add(new SqlParameter("@Updated", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonParameters(SqlCommand command, SmtpProfile profile)
    {
        command.Parameters.Add(new SqlParameter("@EntityCompanyId", profile.CompanyId));
        command.Parameters.Add(new SqlParameter("@Name", profile.Name));
        command.Parameters.Add(new SqlParameter("@FromEmail", profile.FromEmail));
        command.Parameters.Add(new SqlParameter("@FromDisplayName", profile.FromDisplayName));
        command.Parameters.Add(new SqlParameter("@Host", profile.Host));
        command.Parameters.Add(new SqlParameter("@Port", profile.Port));
        command.Parameters.Add(new SqlParameter("@Username", profile.Username));
        command.Parameters.Add(new SqlParameter("@Password", IntegratorSecretProtector.Protect(profile.Password)));
        command.Parameters.Add(new SqlParameter("@AuthMethod", profile.AuthMethod));
        command.Parameters.Add(new SqlParameter("@OAuth2ClientId", (object?)profile.OAuth2ClientId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@OAuth2ClientSecret", (object?)profile.OAuth2ClientSecret ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@OAuth2RefreshToken", (object?)profile.OAuth2RefreshToken ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UseSsl", profile.UseSsl));
        command.Parameters.Add(new SqlParameter("@IsActive", profile.IsActive));
    }

    private static SmtpProfile MapProfile(SqlDataReader r)
    {
        var profile = new SmtpProfile
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            CompanyId = r.GetInt32(r.GetOrdinal("CompanyId")),
            Name = r.GetString(r.GetOrdinal("Name")),
            FromEmail = r.GetString(r.GetOrdinal("FromEmail")),
            FromDisplayName = r.IsDBNull(r.GetOrdinal("FromDisplayName")) ? string.Empty : r.GetString(r.GetOrdinal("FromDisplayName")),
            Host = r.GetString(r.GetOrdinal("Host")),
            Username = r.GetString(r.GetOrdinal("Username")),
            Password = IntegratorSecretProtector.Unprotect(r.GetString(r.GetOrdinal("Password"))),
            AuthMethod = r.IsDBNull(r.GetOrdinal("AuthMethod")) ? "Normal" : r.GetString(r.GetOrdinal("AuthMethod")),
            OAuth2ClientId = r.IsDBNull(r.GetOrdinal("OAuth2ClientId")) ? null : r.GetString(r.GetOrdinal("OAuth2ClientId")),
            OAuth2ClientSecret = r.IsDBNull(r.GetOrdinal("OAuth2ClientSecret")) ? null : r.GetString(r.GetOrdinal("OAuth2ClientSecret")),
            OAuth2RefreshToken = r.IsDBNull(r.GetOrdinal("OAuth2RefreshToken")) ? null : r.GetString(r.GetOrdinal("OAuth2RefreshToken")),
            Created = r.GetDateTime(r.GetOrdinal("Created"))
        };

        profile.UpdateTransport(r.GetInt32(r.GetOrdinal("Port")), r.GetBoolean(r.GetOrdinal("UseSsl")));
        if (!r.GetBoolean(r.GetOrdinal("IsActive")))
        {
            profile.Deactivate();
        }

        return profile;
    }
}

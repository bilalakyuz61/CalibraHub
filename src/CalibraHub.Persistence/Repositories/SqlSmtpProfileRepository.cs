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
        _tableName = $"[{schema}].[smtp_profiles]";
    }

    public async Task<IReadOnlyCollection<SmtpProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = new List<SmtpProfile>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [name], [from_email], [from_display_name], [host], [port], [username], [password], [auth_method], [oauth2_client_id], [oauth2_client_secret], [oauth2_refresh_token], [use_ssl], [is_active], [created_at]
            FROM {_tableName}
            ORDER BY [name];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(MapProfile(reader));
        }

        return result;
    }

    public async Task<SmtpProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [name], [from_email], [from_display_name], [host], [port], [username], [password], [auth_method], [oauth2_client_id], [oauth2_client_secret], [oauth2_refresh_token], [use_ssl], [is_active], [created_at]
            FROM {_tableName}
            WHERE [id] = @Id;
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
                ([id], [company_id], [name], [from_email], [from_display_name], [host], [port], [username], [password], [auth_method], [oauth2_client_id], [oauth2_client_secret], [oauth2_refresh_token], [use_ssl], [is_active], [created_at], [updated_at])
            VALUES
                (@Id, @EntityCompanyId, @Name, @FromEmail, @FromDisplayName, @Host, @Port, @Username, @Password, @AuthMethod, @OAuth2ClientId, @OAuth2ClientSecret, @OAuth2RefreshToken, @UseSsl, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        AddCommonParameters(command, profile);
        command.Parameters.Add(new SqlParameter("@CreatedAt", profile.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(SmtpProfile profile, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET [company_id] = @EntityCompanyId,
                [name] = @Name,
                [from_email] = @FromEmail,
                [from_display_name] = @FromDisplayName,
                [host] = @Host,
                [port] = @Port,
                [username] = @Username,
                [password] = @Password,
                [auth_method] = @AuthMethod,
                [oauth2_client_id] = @OAuth2ClientId,
                [oauth2_client_secret] = @OAuth2ClientSecret,
                [oauth2_refresh_token] = @OAuth2RefreshToken,
                [use_ssl] = @UseSsl,
                [is_active] = @IsActive,
                [updated_at] = @UpdatedAt
            WHERE [id] = @Id;
            """;

        AddCommonParameters(command, profile);
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonParameters(SqlCommand command, SmtpProfile profile)
    {
        command.Parameters.Add(new SqlParameter("@Id", profile.Id));
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
            Id = r.GetGuid(r.GetOrdinal("id")),
            CompanyId = r.GetInt32(r.GetOrdinal("company_id")),
            Name = r.GetString(r.GetOrdinal("name")),
            FromEmail = r.GetString(r.GetOrdinal("from_email")),
            FromDisplayName = r.IsDBNull(r.GetOrdinal("from_display_name")) ? string.Empty : r.GetString(r.GetOrdinal("from_display_name")),
            Host = r.GetString(r.GetOrdinal("host")),
            Username = r.GetString(r.GetOrdinal("username")),
            Password = IntegratorSecretProtector.Unprotect(r.GetString(r.GetOrdinal("password"))),
            AuthMethod = r.IsDBNull(r.GetOrdinal("auth_method")) ? "Normal" : r.GetString(r.GetOrdinal("auth_method")),
            OAuth2ClientId = r.IsDBNull(r.GetOrdinal("oauth2_client_id")) ? null : r.GetString(r.GetOrdinal("oauth2_client_id")),
            OAuth2ClientSecret = r.IsDBNull(r.GetOrdinal("oauth2_client_secret")) ? null : r.GetString(r.GetOrdinal("oauth2_client_secret")),
            OAuth2RefreshToken = r.IsDBNull(r.GetOrdinal("oauth2_refresh_token")) ? null : r.GetString(r.GetOrdinal("oauth2_refresh_token")),
            CreatedAt = r.GetDateTime(r.GetOrdinal("created_at"))
        };

        profile.UpdateTransport(r.GetInt32(r.GetOrdinal("port")), r.GetBoolean(r.GetOrdinal("use_ssl")));
        if (!r.GetBoolean(r.GetOrdinal("is_active")))
        {
            profile.Deactivate();
        }

        return profile;
    }
}

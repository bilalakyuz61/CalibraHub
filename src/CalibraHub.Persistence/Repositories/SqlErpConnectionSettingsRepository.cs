using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlErpConnectionSettingsRepository : IErpConnectionSettingsRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlErpConnectionSettingsRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[erp_connection_settings]";
    }

    public async Task<IReadOnlyCollection<ErpConnectionSettings>> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = new List<ErpConnectionSettings>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [provider], [company], [business], [branch], [username], [password], [is_active], [Created]
            FROM {_tableName}
            ORDER BY [company], [business], [branch];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(MapSettings(reader));
        }

        return result;
    }

    public async Task<ErpConnectionSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [provider], [company], [business], [branch], [username], [password], [is_active], [Created]
            FROM {_tableName}
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapSettings(reader);
    }

    public async Task AddAsync(ErpConnectionSettings settings, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([id], [company_id], [provider], [company], [business], [branch], [username], [password], [is_active], [Created], [Updated])
            VALUES
                (@Id, @EntityCompanyId, @Provider, @Company, @Business, @Branch, @Username, @Password, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        AddCommonParameters(command, settings);
        command.Parameters.Add(new SqlParameter("@CreatedAt", settings.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(ErpConnectionSettings settings, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET [company_id] = @EntityCompanyId,
                [provider] = @Provider,
                [company] = @Company,
                [business] = @Business,
                [branch] = @Branch,
                [username] = @Username,
                [password] = @Password,
                [is_active] = @IsActive,
                [Updated] = @UpdatedAt
            WHERE [id] = @Id;
            """;

        AddCommonParameters(command, settings);
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_tableName}
            WHERE [id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonParameters(SqlCommand command, ErpConnectionSettings settings)
    {
        command.Parameters.Add(new SqlParameter("@Id", settings.Id));
        command.Parameters.Add(new SqlParameter("@EntityCompanyId", settings.CompanyId));
        command.Parameters.Add(new SqlParameter("@Provider", settings.Provider));
        command.Parameters.Add(new SqlParameter("@Company", settings.Company));
        command.Parameters.Add(new SqlParameter("@Business", settings.Business));
        command.Parameters.Add(new SqlParameter("@Branch", settings.Branch));
        command.Parameters.Add(new SqlParameter("@Username", settings.Username));
        command.Parameters.Add(new SqlParameter("@Password", settings.Password));
        command.Parameters.Add(new SqlParameter("@IsActive", settings.IsActive));
    }

    private static ErpConnectionSettings MapSettings(SqlDataReader reader)
    {
        var settings = new ErpConnectionSettings
        {
            Id = reader.GetGuid(0),
            CompanyId = reader.GetInt32(1),
            Provider = reader.GetString(2),
            Company = reader.GetString(3),
            Business = reader.GetString(4),
            Branch = reader.GetString(5),
            Username = reader.GetString(6),
            Password = reader.GetString(7),
            CreatedAt = reader.GetFieldValue<DateTime>(9)
        };

        if (!reader.GetBoolean(8))
        {
            settings.Deactivate();
        }

        return settings;
    }
}

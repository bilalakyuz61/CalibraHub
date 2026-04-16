using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlUserSettingRepository : IUserSettingRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlUserSettingRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[user_settings]";
    }

    public async Task<string?> GetAsync(Guid userId, string settingKey, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [setting_value] FROM {_table} WHERE [user_id] = @UserId AND [setting_key] = @Key;";
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        command.Parameters.Add(new SqlParameter("@Key", settingKey));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : result.ToString();
    }

    public async Task SetAsync(Guid userId, string settingKey, string? value, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            MERGE {_table} AS tgt
            USING (SELECT @UserId AS [user_id], @Key AS [setting_key]) AS src
                ON tgt.[user_id] = src.[user_id] AND tgt.[setting_key] = src.[setting_key]
            WHEN MATCHED THEN
                UPDATE SET [setting_value] = @Value, [updated_at] = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT ([id], [user_id], [setting_key], [setting_value], [updated_at])
                VALUES (NEWID(), @UserId, @Key, @Value, GETDATE());
            """;
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        command.Parameters.Add(new SqlParameter("@Key", settingKey));
        command.Parameters.Add(new SqlParameter("@Value", (object?)value ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

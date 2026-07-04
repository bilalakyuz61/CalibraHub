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
        _table = $"[{schema}].[UserSettings]";
    }

    public async Task<string?> GetAsync(int userId, string settingKey, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [SettingValue] FROM {_table} WHERE [UserId] = @UserId AND [SettingKey] = @Key;";
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        command.Parameters.Add(new SqlParameter("@Key", settingKey));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : result.ToString();
    }

    public async Task SetAsync(int userId, string settingKey, string? value, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            MERGE {_table} AS tgt
            USING (SELECT @UserId AS [UserId], @Key AS [SettingKey]) AS src
                ON tgt.[UserId] = src.[UserId] AND tgt.[SettingKey] = src.[SettingKey]
            WHEN MATCHED THEN
                UPDATE SET [SettingValue] = @Value, [Updated] = GETDATE()
            WHEN NOT MATCHED THEN
                INSERT ([UserId], [SettingKey], [SettingValue], [Updated])
                VALUES (@UserId, @Key, @Value, GETDATE());
            """;
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        command.Parameters.Add(new SqlParameter("@Key", settingKey));
        command.Parameters.Add(new SqlParameter("@Value", (object?)value ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

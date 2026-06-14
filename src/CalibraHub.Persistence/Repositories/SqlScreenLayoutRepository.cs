using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlScreenLayoutRepository : IScreenLayoutRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlScreenLayoutRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[ScreenLayoutDefinition]";
    }

    public async Task<ScreenLayoutDefinition?> GetByScreenCodeAsync(string screenCode, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) [Id], [ScreenCode], [LayoutJson], [Created], [Updated]
            FROM {_tableName}
            WHERE [ScreenCode] = @ScreenCode;
            """;
        command.Parameters.Add(new SqlParameter("@ScreenCode", screenCode));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ScreenLayoutDefinition
        {
            Id = reader.GetGuid(0),
            ScreenCode = reader.GetString(1),
            LayoutJson = reader.GetString(2),
            Created = reader.GetFieldValue<DateTime>(3)
        };
    }

    public async Task UpsertAsync(ScreenLayoutDefinition definition, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_tableName} WHERE [ScreenCode] = @ScreenCode)
            BEGIN
                UPDATE {_tableName}
                SET
                    [LayoutJson] = @LayoutJson,
                    [Updated] = @UpdatedAt
                WHERE [ScreenCode] = @ScreenCode;
            END
            ELSE
            BEGIN
                INSERT INTO {_tableName}
                    ([Id], [ScreenCode], [LayoutJson], [Created], [Updated])
                VALUES
                    (@Id, @ScreenCode, @LayoutJson, @CreatedAt, @UpdatedAt);
            END;
            """;

        command.Parameters.Add(new SqlParameter("@Id", definition.Id));
        command.Parameters.Add(new SqlParameter("@ScreenCode", definition.ScreenCode));
        command.Parameters.Add(new SqlParameter("@LayoutJson", definition.LayoutJson));
        command.Parameters.Add(new SqlParameter("@CreatedAt", definition.Created));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

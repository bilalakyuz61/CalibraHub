using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Gate sifresi repository — tek row (id=1). Sistem DB'sinde tutulur, per-company DB'de degil.
/// </summary>
public sealed class SqlGateCredentialsRepository : IGateCredentialsRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlGateCredentialsRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[GateCredential]";
    }

    public async Task<GateCredentials?> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[PasswordHash],[LastChangedAt],[LastChangedFromIp],[Created]
              FROM {_table}
             WHERE [Id] = 1;
            """;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        return new GateCredentials
        {
            Id                = r.GetInt32(0),
            PasswordHash      = r.IsDBNull(1) ? string.Empty : r.GetString(1),
            LastChangedAt     = r.GetDateTime(2),
            LastChangedFromIp = r.IsDBNull(3) ? null : r.GetString(3),
            Created           = r.GetDateTime(4),
        };
    }

    public async Task SaveAsync(GateCredentials credentials, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [Id] = 1)
            BEGIN
                UPDATE {_table}
                   SET [PasswordHash]        = @Hash,
                       [LastChangedAt]       = @LastChanged,
                       [LastChangedFromIp]   = @LastIp
                 WHERE [Id] = 1;
            END
            ELSE
            BEGIN
                INSERT INTO {_table}
                    ([Id],[PasswordHash],[LastChangedAt],[LastChangedFromIp],[Created])
                VALUES
                    (1,@Hash,@LastChanged,@LastIp,GETUTCDATE());
            END;
            """;
        cmd.Parameters.Add(new SqlParameter("@Hash",        credentials.PasswordHash));
        cmd.Parameters.Add(new SqlParameter("@LastChanged", credentials.LastChangedAt));
        cmd.Parameters.Add(new SqlParameter("@LastIp",      (object?)credentials.LastChangedFromIp ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

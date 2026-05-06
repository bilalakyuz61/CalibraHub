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
        _table = $"[{schema}].[gate_credentials]";
    }

    public async Task<GateCredentials?> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[password_hash],[last_changed_at],[last_changed_from_ip],[Created]
              FROM {_table}
             WHERE [id] = 1;
            """;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        return new GateCredentials
        {
            Id                = r.GetInt32(0),
            PasswordHash      = r.IsDBNull(1) ? string.Empty : r.GetString(1),
            LastChangedAt     = r.GetDateTime(2),
            LastChangedFromIp = r.IsDBNull(3) ? null : r.GetString(3),
            CreatedAt         = r.GetDateTime(4),
        };
    }

    public async Task SaveAsync(GateCredentials credentials, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [id] = 1)
            BEGIN
                UPDATE {_table}
                   SET [password_hash]        = @Hash,
                       [last_changed_at]      = @LastChanged,
                       [last_changed_from_ip] = @LastIp
                 WHERE [id] = 1;
            END
            ELSE
            BEGIN
                INSERT INTO {_table}
                    ([id],[password_hash],[last_changed_at],[last_changed_from_ip],[Created])
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

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Lisans kaydi repository — tek row (id=1) ile ugrasir. MERGE pattern ile UPSERT.
/// Tablo per-company DB'de degil sistem DB'sinde tutulur; burada connection
/// factory cluster'in sistem DB'sine baglanir.
/// </summary>
public sealed class SqlLicenseRepository : ILicenseRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlLicenseRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[license_config]";
    }

    public async Task<LicenseRecord?> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[license_key],[secret_encrypted],[is_valid],[expiry_date],[concurrent_limit],[total_user_limit],
                   [last_error],[last_validated_at],[Created],[Updated]
              FROM {_table}
             WHERE [id] = 1;
            """;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        return new LicenseRecord
        {
            Id              = r.GetInt32(0),
            LicenseKey      = r.IsDBNull(1) ? null : r.GetString(1),
            SecretEncrypted = r.IsDBNull(2) ? null : r.GetString(2),
            IsValid         = r.GetBoolean(3),
            ExpiryDate      = r.IsDBNull(4) ? null : r.GetDateTime(4),
            ConcurrentLimit = r.IsDBNull(5) ? null : r.GetInt32(5),
            TotalUserLimit  = r.IsDBNull(6) ? null : r.GetInt32(6),
            LastError       = r.IsDBNull(7) ? null : r.GetString(7),
            LastValidatedAt = r.IsDBNull(8) ? null : r.GetDateTime(8),
            CreatedAt       = r.GetDateTime(9),
            UpdatedAt       = r.GetDateTime(10),
        };
    }

    public async Task SaveAsync(LicenseRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [id] = 1)
            BEGIN
                UPDATE {_table}
                   SET [license_key]       = @Key,
                       [secret_encrypted]  = @Secret,
                       [is_valid]          = @IsValid,
                       [expiry_date]       = @Expiry,
                       [concurrent_limit]  = @Concurrent,
                       [total_user_limit]  = @Total,
                       [last_error]        = @Error,
                       [last_validated_at] = @ValidatedAt,
                       [Updated]        = GETUTCDATE()
                 WHERE [id] = 1;
            END
            ELSE
            BEGIN
                INSERT INTO {_table}
                    ([id],[license_key],[secret_encrypted],[is_valid],[expiry_date],[concurrent_limit],[total_user_limit],
                     [last_error],[last_validated_at],[Created],[Updated])
                VALUES
                    (1,@Key,@Secret,@IsValid,@Expiry,@Concurrent,@Total,@Error,@ValidatedAt,GETUTCDATE(),GETUTCDATE());
            END;
            """;
        cmd.Parameters.Add(new SqlParameter("@Key",         (object?)record.LicenseKey      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Secret",      (object?)record.SecretEncrypted ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsValid",     record.IsValid));
        cmd.Parameters.Add(new SqlParameter("@Expiry",      (object?)record.ExpiryDate      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Concurrent",  (object?)record.ConcurrentLimit ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Total",       (object?)record.TotalUserLimit  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Error",       (object?)record.LastError       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ValidatedAt", (object?)record.LastValidatedAt ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

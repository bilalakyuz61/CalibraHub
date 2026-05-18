using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlIntegrationRecordStatusRepository : IIntegrationRecordStatusRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlIntegrationRecordStatusRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[IntegrationRecordStatus]";
    }

    public async Task<IntegrationRecordStatus?> GetAsync(int integrationId, string recordId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[IntegrationId],[RecordId],[Status],[LastRunId],[LastSentAt],
                         [LastError],[AttemptCount],[SkippedBy],[SkippedAt],[SkipReason],
                         [IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM {_table}
            WHERE [IntegrationId] = @IntegrationId AND [RecordId] = @RecordId AND [IsActive] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        cmd.Parameters.Add(new SqlParameter("@RecordId", recordId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task UpsertRunResultAsync(int integrationId, string recordId,
        IntegrationRecordStatusType status, long? runId, string? error,
        string? actor, CancellationToken ct)
    {
        // Sent/Failed yazar; Skipped uzerine yazmaz (kullanici manuel haric tutmussa
        // run sonucu otomatik geri almasin).
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            MERGE {_table} AS T
            USING (SELECT @IntegrationId AS IntegrationId, @RecordId AS RecordId) AS S
            ON (T.[IntegrationId] = S.IntegrationId AND T.[RecordId] = S.RecordId AND T.[IsActive] = 1)
            WHEN MATCHED AND T.[Status] <> 'Skipped' THEN
                UPDATE SET
                    [Status]       = @Status,
                    [LastRunId]    = @LastRunId,
                    [LastSentAt]   = CASE WHEN @Status = 'Sent' THEN SYSUTCDATETIME() ELSE T.[LastSentAt] END,
                    [LastError]    = @LastError,
                    [AttemptCount] = T.[AttemptCount] + 1,
                    [UpdatedBy]    = @Actor,
                    [Updated]      = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([IntegrationId],[RecordId],[Status],[LastRunId],[LastSentAt],[LastError],
                        [AttemptCount],[IsActive],[CreatedBy],[Created])
                VALUES (@IntegrationId,@RecordId,@Status,@LastRunId,
                        CASE WHEN @Status = 'Sent' THEN SYSUTCDATETIME() ELSE NULL END,
                        @LastError,1,1,@Actor,SYSUTCDATETIME());
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        cmd.Parameters.Add(new SqlParameter("@RecordId", recordId));
        cmd.Parameters.Add(new SqlParameter("@Status", status.ToString()));
        cmd.Parameters.Add(new SqlParameter("@LastRunId", (object?)runId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LastError", (object?)error ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Actor", (object?)actor ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SkipManyAsync(int integrationId, IEnumerable<string> recordIds,
        string? reason, string actor, CancellationToken ct)
    {
        var ids = recordIds?.Where(id => !string.IsNullOrWhiteSpace(id))
                            .Distinct(StringComparer.Ordinal).ToList()
                  ?? new List<string>();
        if (ids.Count == 0) return;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        foreach (var id in ids)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                MERGE {_table} AS T
                USING (SELECT @IntegrationId AS IntegrationId, @RecordId AS RecordId) AS S
                ON (T.[IntegrationId] = S.IntegrationId AND T.[RecordId] = S.RecordId AND T.[IsActive] = 1)
                WHEN MATCHED THEN
                    UPDATE SET
                        [Status]     = 'Skipped',
                        [SkippedBy]  = @Actor,
                        [SkippedAt]  = SYSUTCDATETIME(),
                        [SkipReason] = @Reason,
                        [UpdatedBy]  = @Actor,
                        [Updated]    = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT ([IntegrationId],[RecordId],[Status],[SkippedBy],[SkippedAt],[SkipReason],
                            [IsActive],[CreatedBy],[Created])
                    VALUES (@IntegrationId,@RecordId,'Skipped',@Actor,SYSUTCDATETIME(),@Reason,
                            1,@Actor,SYSUTCDATETIME());
                """;
            cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
            cmd.Parameters.Add(new SqlParameter("@RecordId", id));
            cmd.Parameters.Add(new SqlParameter("@Actor", actor ?? "system"));
            cmd.Parameters.Add(new SqlParameter("@Reason", (object?)reason ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task RestoreManyAsync(int integrationId, IEnumerable<string> recordIds,
        string actor, CancellationToken ct)
    {
        var ids = recordIds?.Where(id => !string.IsNullOrWhiteSpace(id))
                            .Distinct(StringComparer.Ordinal).ToList()
                  ?? new List<string>();
        if (ids.Count == 0) return;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        foreach (var id in ids)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                UPDATE {_table}
                   SET [Status]     = 'Pending',
                       [SkippedBy]  = NULL,
                       [SkippedAt]  = NULL,
                       [SkipReason] = NULL,
                       [UpdatedBy]  = @Actor,
                       [Updated]    = SYSUTCDATETIME()
                 WHERE [IntegrationId] = @IntegrationId
                   AND [RecordId]      = @RecordId
                   AND [IsActive]      = 1
                   AND [Status]        = 'Skipped';
                """;
            cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
            cmd.Parameters.Add(new SqlParameter("@RecordId", id));
            cmd.Parameters.Add(new SqlParameter("@Actor", actor ?? "system"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<IReadOnlyList<IntegrationRecordStatus>> ListByStatusAsync(
        int integrationId, IntegrationRecordStatusType? status, CancellationToken ct)
    {
        var list = new List<IntegrationRecordStatus>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        var where = "[IntegrationId] = @IntegrationId AND [IsActive] = 1";
        if (status.HasValue) where += " AND [Status] = @Status";
        cmd.CommandText = $"""
            SELECT [Id],[IntegrationId],[RecordId],[Status],[LastRunId],[LastSentAt],
                   [LastError],[AttemptCount],[SkippedBy],[SkippedAt],[SkipReason],
                   [IsActive],[CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM {_table}
            WHERE {where}
            ORDER BY [Updated] DESC, [Created] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        if (status.HasValue) cmd.Parameters.Add(new SqlParameter("@Status", status.Value.ToString()));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<IntegrationRecordStatusSummary> GetSummaryAsync(
        int integrationId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Status], COUNT(*) AS [Cnt]
            FROM {_table}
            WHERE [IntegrationId] = @IntegrationId AND [IsActive] = 1
            GROUP BY [Status];
            """;
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        int p = 0, f = 0, s = 0, k = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var st  = r.GetString(0);
            var cnt = r.GetInt32(1);
            switch (st)
            {
                case "Pending": p = cnt; break;
                case "Failed":  f = cnt; break;
                case "Sent":    s = cnt; break;
                case "Skipped": k = cnt; break;
            }
        }
        return new IntegrationRecordStatusSummary(Pending: p, Failed: f, Sent: s, Skipped: k);
    }

    // ── mapping ────────────────────────────────────────────────────────────
    private static IntegrationRecordStatus Map(SqlDataReader r)
    {
        var statusStr = r.GetString(3);
        if (!Enum.TryParse<IntegrationRecordStatusType>(statusStr, ignoreCase: true, out var status))
            status = IntegrationRecordStatusType.Pending;

        return new IntegrationRecordStatus
        {
            Id            = r.GetInt32(0),
            IntegrationId = r.GetInt32(1),
            RecordId      = r.GetString(2),
            Status        = status,
            LastRunId     = r.IsDBNull(4) ? null : r.GetInt64(4),
            LastSentAt    = r.IsDBNull(5) ? null : r.GetDateTime(5),
            LastError     = r.IsDBNull(6) ? null : r.GetString(6),
            AttemptCount  = r.GetInt32(7),
            SkippedBy     = r.IsDBNull(8)  ? null : r.GetString(8),
            SkippedAt     = r.IsDBNull(9)  ? null : r.GetDateTime(9),
            SkipReason    = r.IsDBNull(10) ? null : r.GetString(10),
            IsActive      = r.GetBoolean(11),
            CreatedBy     = r.IsDBNull(12) ? null : r.GetString(12),
            Created       = r.GetDateTime(13),
            UpdatedBy     = r.IsDBNull(14) ? null : r.GetString(14),
            Updated       = r.IsDBNull(15) ? null : r.GetDateTime(15),
        };
    }
}

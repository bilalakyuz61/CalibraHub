using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-08-15 — DataImportJob / DataImportJobColumn / DataImportRun persistence.
/// Per-company şirket DB'sinde izole. Pattern: SqlImportTemplateRepository.
/// </summary>
public sealed class SqlDataImportRepository : IDataImportRepository
{
    private const string JobColumns =
        "[Id],[Name],[ConnectionId],[TargetEntity],[SourceSchema],[SourceObject],[MatchKeyField]," +
        "[MaxRows],[ErrorBehavior],[PreProcedureName],[PreProcedureTarget],[PreProcedureParamsJson]," +
        "[PostProcedureName],[PostProcedureTarget],[PostProcedureParamsJson]," +
        "[IsActive],[Created],[Updated],[CreatedById],[UpdatedById],[SourceFilterJson],[DeactivateAbsent],[UpdateExisting],[InsertNew]";

    private const string RunColumns =
        "[Id],[JobId],[TriggerType],[StartedAt],[FinishedAt],[DurationMs],[Status]," +
        "[RowsRead],[RowsInserted],[RowsUpdated],[RowsFailed],[ErrorMessage]," +
        "[PreProcedureResult],[PostProcedureResult],[TriggeredBy],[RowsDeactivated],[RowsSkipped]";

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _jobTable;
    private readonly string _columnTable;
    private readonly string _runTable;

    public SqlDataImportRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _jobTable = $"[{s}].[DataImportJob]";
        _columnTable = $"[{s}].[DataImportJobColumn]";
        _runTable = $"[{s}].[DataImportRun]";
    }

    // ── İş tanımı ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DataImportJob>> ListJobsAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {JobColumns}
            FROM {_jobTable}
            {(includeInactive ? "" : "WHERE [IsActive] = 1")}
            ORDER BY [Name];
            """;
        var list = new List<DataImportJob>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapJob(r));
        return list;
    }

    public async Task<DataImportJob?> GetJobAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        DataImportJob? job;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT {JobColumns} FROM {_jobTable} WHERE [Id] = @Id;";
            cmd.Parameters.Add(new SqlParameter("@Id", id));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            job = await r.ReadAsync(ct) ? MapJob(r) : null;
        }
        if (job is null) return null;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT [Id],[JobId],[SourceColumn],[TargetKey],[SortOrder]
                FROM {_columnTable}
                WHERE [JobId] = @JobId
                ORDER BY [SortOrder], [Id];
                """;
            cmd.Parameters.Add(new SqlParameter("@JobId", id));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                job.Columns.Add(new DataImportJobColumn
                {
                    Id           = r.GetInt32(0),
                    JobId        = r.GetInt32(1),
                    SourceColumn = r.GetString(2),
                    TargetKey    = r.GetString(3),
                    SortOrder    = r.GetInt32(4),
                });
            }
        }
        return job;
    }

    public async Task<bool> JobNameExistsAsync(string name, int? excludeId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM {_jobTable}
                WHERE [IsActive] = 1
                  AND LOWER([Name]) = LOWER(@Name)
                  AND (@ExcludeId IS NULL OR [Id] <> @ExcludeId)
            ) THEN 1 ELSE 0 END;
            """;
        cmd.Parameters.Add(new SqlParameter("@Name", name));
        cmd.Parameters.Add(new SqlParameter("@ExcludeId", (object?)excludeId ?? DBNull.Value));
        return ((int)(await cmd.ExecuteScalarAsync(ct) ?? 0)) == 1;
    }

    public async Task<int> SaveJobAsync(DataImportJob job, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        // İş başlığı ve kolonları tek transaction — kolonlar yazılamazsa yarım
        // eşlemeli bir iş kalmaz (yarım eşleme sessizce yanlış veri aktarır).
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int id;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                if (job.Id <= 0)
                {
                    cmd.CommandText = $"""
                        INSERT INTO {_jobTable}
                          ([Name],[ConnectionId],[TargetEntity],[SourceSchema],[SourceObject],[MatchKeyField],
                           [MaxRows],[ErrorBehavior],[PreProcedureName],[PreProcedureTarget],[PreProcedureParamsJson],
                           [PostProcedureName],[PostProcedureTarget],[PostProcedureParamsJson],[IsActive],[CreatedById],[SourceFilterJson],[DeactivateAbsent],[UpdateExisting],[InsertNew])
                        OUTPUT INSERTED.[Id]
                        VALUES
                          (@Name,@ConnectionId,@TargetEntity,@SourceSchema,@SourceObject,@MatchKeyField,
                           @MaxRows,@ErrorBehavior,@PreProcedureName,@PreProcedureTarget,@PreProcedureParamsJson,
                           @PostProcedureName,@PostProcedureTarget,@PostProcedureParamsJson,@IsActive,@CreatedById,@SourceFilterJson,@DeactivateAbsent,@UpdateExisting,@InsertNew);
                        """;
                    BindJob(cmd, job);
                    cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)job.CreatedById ?? DBNull.Value));
                    id = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
                }
                else
                {
                    cmd.CommandText = $"""
                        UPDATE {_jobTable}
                        SET [Name] = @Name,
                            [ConnectionId] = @ConnectionId,
                            [TargetEntity] = @TargetEntity,
                            [SourceSchema] = @SourceSchema,
                            [SourceObject] = @SourceObject,
                            [MatchKeyField] = @MatchKeyField,
                            [MaxRows] = @MaxRows,
                            [ErrorBehavior] = @ErrorBehavior,
                            [PreProcedureName] = @PreProcedureName,
                            [PreProcedureTarget] = @PreProcedureTarget,
                            [PreProcedureParamsJson] = @PreProcedureParamsJson,
                            [PostProcedureName] = @PostProcedureName,
                            [PostProcedureTarget] = @PostProcedureTarget,
                            [PostProcedureParamsJson] = @PostProcedureParamsJson,
                            [SourceFilterJson] = @SourceFilterJson,
                            [DeactivateAbsent] = @DeactivateAbsent,
                            [UpdateExisting] = @UpdateExisting,
                            [InsertNew] = @InsertNew,
                            [IsActive] = @IsActive,
                            [UpdatedById] = @UpdatedById,
                            [Updated] = SYSUTCDATETIME()
                        WHERE [Id] = @Id;
                        """;
                    cmd.Parameters.Add(new SqlParameter("@Id", job.Id));
                    BindJob(cmd, job);
                    cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)job.UpdatedById ?? DBNull.Value));
                    await cmd.ExecuteNonQueryAsync(ct);
                    id = job.Id;
                }
            }

            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_columnTable} WHERE [JobId] = @JobId;";
                del.Parameters.Add(new SqlParameter("@JobId", id));
                await del.ExecuteNonQueryAsync(ct);
            }

            var order = 0;
            foreach (var col in job.Columns)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_columnTable} ([JobId],[SourceColumn],[TargetKey],[SortOrder])
                    VALUES (@JobId,@SourceColumn,@TargetKey,@SortOrder);
                    """;
                ins.Parameters.Add(new SqlParameter("@JobId", id));
                ins.Parameters.Add(new SqlParameter("@SourceColumn", col.SourceColumn));
                ins.Parameters.Add(new SqlParameter("@TargetKey", col.TargetKey));
                ins.Parameters.Add(new SqlParameter("@SortOrder", col.SortOrder > 0 ? col.SortOrder : ++order));
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteJobAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Kolonlar FK ON DELETE CASCADE ile gider; çalıştırma logu tarihçe olarak KALIR.
        cmd.CommandText = $"DELETE FROM {_jobTable} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> ToggleJobActiveAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_jobTable} SET [IsActive] = CASE WHEN [IsActive] = 1 THEN 0 ELSE 1 END,
                                   [Updated] = SYSUTCDATETIME()
            OUTPUT INSERTED.[IsActive]
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is bool b && b;
    }

    // ── Çalıştırma logu ──────────────────────────────────────────────────

    public async Task<long> InsertRunAsync(DataImportRun run, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_runTable} ([JobId],[TriggerType],[StartedAt],[Status],[TriggeredBy])
            OUTPUT INSERTED.[Id]
            VALUES (@JobId,@TriggerType,@StartedAt,@Status,@TriggeredBy);
            """;
        cmd.Parameters.Add(new SqlParameter("@JobId", run.JobId));
        cmd.Parameters.Add(new SqlParameter("@TriggerType", (int)run.TriggerType));
        cmd.Parameters.Add(new SqlParameter("@StartedAt", run.StartedAt));
        cmd.Parameters.Add(new SqlParameter("@Status", (int)run.Status));
        cmd.Parameters.Add(new SqlParameter("@TriggeredBy", (object?)run.TriggeredBy ?? DBNull.Value));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    public async Task UpdateRunAsync(DataImportRun run, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_runTable}
            SET [FinishedAt] = @FinishedAt,
                [DurationMs] = @DurationMs,
                [Status] = @Status,
                [RowsRead] = @RowsRead,
                [RowsInserted] = @RowsInserted,
                [RowsUpdated] = @RowsUpdated,
                [RowsFailed] = @RowsFailed,
                [RowsDeactivated] = @RowsDeactivated,
                [RowsSkipped] = @RowsSkipped,
                [ErrorMessage] = @ErrorMessage,
                [PreProcedureResult] = @PreProcedureResult,
                [PostProcedureResult] = @PostProcedureResult
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", run.Id));
        cmd.Parameters.Add(new SqlParameter("@FinishedAt", (object?)run.FinishedAt ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DurationMs", (object?)run.DurationMs ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Status", (int)run.Status));
        cmd.Parameters.Add(new SqlParameter("@RowsRead", run.RowsRead));
        cmd.Parameters.Add(new SqlParameter("@RowsInserted", run.RowsInserted));
        cmd.Parameters.Add(new SqlParameter("@RowsUpdated", run.RowsUpdated));
        cmd.Parameters.Add(new SqlParameter("@RowsFailed", run.RowsFailed));
        cmd.Parameters.Add(new SqlParameter("@RowsDeactivated", run.RowsDeactivated));
        cmd.Parameters.Add(new SqlParameter("@RowsSkipped", run.RowsSkipped));
        cmd.Parameters.Add(new SqlParameter("@ErrorMessage", (object?)run.ErrorMessage ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PreProcedureResult", (object?)run.PreProcedureResult ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PostProcedureResult", (object?)run.PostProcedureResult ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<DataImportRun>> ListRunsAsync(int? jobId, int take, CancellationToken ct)
    {
        if (take <= 0) take = 100;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Take) {RunColumns}
            FROM {_runTable}
            WHERE (@JobId IS NULL OR [JobId] = @JobId)
            ORDER BY [Id] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Take", take));
        cmd.Parameters.Add(new SqlParameter("@JobId", (object?)jobId ?? DBNull.Value));
        var list = new List<DataImportRun>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapRun(r));
        return list;
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────

    private static void BindJob(SqlCommand cmd, DataImportJob j)
    {
        cmd.Parameters.Add(new SqlParameter("@Name", j.Name));
        cmd.Parameters.Add(new SqlParameter("@ConnectionId", j.ConnectionId));
        cmd.Parameters.Add(new SqlParameter("@TargetEntity", j.TargetEntity));
        cmd.Parameters.Add(new SqlParameter("@SourceSchema", j.SourceSchema));
        cmd.Parameters.Add(new SqlParameter("@SourceObject", j.SourceObject));
        cmd.Parameters.Add(new SqlParameter("@MatchKeyField", string.Join(",", j.MatchKeyFields)));
        cmd.Parameters.Add(new SqlParameter("@MaxRows", j.MaxRows));
        cmd.Parameters.Add(new SqlParameter("@ErrorBehavior", (int)j.ErrorBehavior));
        cmd.Parameters.Add(new SqlParameter("@PreProcedureName", (object?)j.PreProcedureName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PreProcedureTarget", (int)j.PreProcedureTarget));
        cmd.Parameters.Add(new SqlParameter("@PreProcedureParamsJson", (object?)j.PreProcedureParamsJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PostProcedureName", (object?)j.PostProcedureName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PostProcedureTarget", (int)j.PostProcedureTarget));
        cmd.Parameters.Add(new SqlParameter("@PostProcedureParamsJson", (object?)j.PostProcedureParamsJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive", j.IsActive));
        cmd.Parameters.Add(new SqlParameter("@SourceFilterJson", (object?)j.SourceFilterJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeactivateAbsent", j.DeactivateAbsent));
        cmd.Parameters.Add(new SqlParameter("@UpdateExisting", j.UpdateExisting));
        cmd.Parameters.Add(new SqlParameter("@InsertNew", j.InsertNew));
    }

    private static DataImportJob MapJob(SqlDataReader r) => new()
    {
        Id                      = r.GetInt32(0),
        Name                    = r.GetString(1),
        ConnectionId            = r.GetInt32(2),
        TargetEntity            = r.GetString(3),
        SourceSchema            = r.GetString(4),
        SourceObject            = r.GetString(5),
        MatchKeyFields          = r.IsDBNull(6)
                                      ? new List<string>()
                                      : r.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
        MaxRows                 = r.GetInt32(7),
        ErrorBehavior           = (DataImportErrorBehavior)r.GetInt32(8),
        PreProcedureName        = r.IsDBNull(9) ? null : r.GetString(9),
        PreProcedureTarget      = (DataImportProcedureTarget)r.GetInt32(10),
        PreProcedureParamsJson  = r.IsDBNull(11) ? null : r.GetString(11),
        PostProcedureName       = r.IsDBNull(12) ? null : r.GetString(12),
        PostProcedureTarget     = (DataImportProcedureTarget)r.GetInt32(13),
        PostProcedureParamsJson = r.IsDBNull(14) ? null : r.GetString(14),
        IsActive                = r.GetBoolean(15),
        Created                 = r.GetDateTime(16),
        Updated                 = r.IsDBNull(17) ? null : r.GetDateTime(17),
        CreatedById             = r.IsDBNull(18) ? null : r.GetInt32(18),
        UpdatedById             = r.IsDBNull(19) ? null : r.GetInt32(19),
        SourceFilterJson        = r.IsDBNull(20) ? null : r.GetString(20),
        DeactivateAbsent        = !r.IsDBNull(21) && r.GetBoolean(21),
        UpdateExisting          = r.IsDBNull(22) || r.GetBoolean(22),
        InsertNew               = r.IsDBNull(23) || r.GetBoolean(23),
    };

    private static DataImportRun MapRun(SqlDataReader r) => new()
    {
        Id                  = r.GetInt64(0),
        JobId               = r.GetInt32(1),
        TriggerType         = (DataImportTriggerType)r.GetInt32(2),
        StartedAt           = r.GetDateTime(3),
        FinishedAt          = r.IsDBNull(4) ? null : r.GetDateTime(4),
        DurationMs          = r.IsDBNull(5) ? null : r.GetInt32(5),
        Status              = (DataImportRunStatus)r.GetInt32(6),
        RowsRead            = r.GetInt32(7),
        RowsInserted        = r.GetInt32(8),
        RowsUpdated         = r.GetInt32(9),
        RowsFailed          = r.GetInt32(10),
        ErrorMessage        = r.IsDBNull(11) ? null : r.GetString(11),
        PreProcedureResult  = r.IsDBNull(12) ? null : r.GetString(12),
        PostProcedureResult = r.IsDBNull(13) ? null : r.GetString(13),
        TriggeredBy         = r.IsDBNull(14) ? null : r.GetString(14),
        RowsDeactivated     = r.IsDBNull(15) ? 0 : r.GetInt32(15),
        RowsSkipped         = r.IsDBNull(16) ? 0 : r.GetInt32(16),
    };
}

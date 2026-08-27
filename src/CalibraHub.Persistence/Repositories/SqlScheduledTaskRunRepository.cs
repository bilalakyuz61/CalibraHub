using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlScheduledTaskRunRepository : IScheduledTaskRunRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _taskTable;

    public SqlScheduledTaskRunRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[ScheduledTaskRun]";
        _taskTable = $"[{schema}].[ScheduledTask]";
    }

    public async Task<int> CreateAsync(ScheduledTaskRun run, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        // CompanyId ebeveyn ScheduledTask'tan turetilir (nullable — builtin/legacy gorevler icin).
        cmd.CommandText = $"""
            INSERT INTO {_table} ([TaskId],[TaskCode],[StartedAt],[CompletedAt],[Status],[Message],[DurationMs],[Trigger],[ExecutedCommand],[CompanyId])
            VALUES (@TaskId,@TaskCode,@StartedAt,@CompletedAt,@Status,@Message,@DurationMs,@Trigger,@ExecutedCommand,
                    (SELECT t.[CompanyId] FROM {_taskTable} t WHERE t.[Id] = @TaskId));
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@TaskId",          run.TaskId));
        cmd.Parameters.Add(new SqlParameter("@TaskCode",        run.TaskCode));
        cmd.Parameters.Add(new SqlParameter("@StartedAt",       run.StartedAt));
        cmd.Parameters.Add(new SqlParameter("@CompletedAt",     (object?)run.CompletedAt ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Status",          run.Status));
        cmd.Parameters.Add(new SqlParameter("@Message",         (object?)run.Message ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DurationMs",      (object?)run.DurationMs ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Trigger",         (int)run.Trigger));
        cmd.Parameters.Add(new SqlParameter("@ExecutedCommand", (object?)run.ExecutedCommand ?? DBNull.Value));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task CompleteAsync(int runId, int status, string? message, int durationMs, string? executedCommand, CancellationToken cancellationToken)
    {
        // executedCommand null ise mevcut degeri koru (COALESCE) — bu sayede dispatcher'in
        // ilki dummy CompleteAsync cagrisi ikinci asamadaki gercek komutu silmez.
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [CompletedAt] = GETUTCDATE(),
                   [Status] = @Status,
                   [Message] = @Message,
                   [DurationMs] = @DurationMs,
                   [ExecutedCommand] = COALESCE(@ExecutedCommand, [ExecutedCommand])
             WHERE [Id] = @Id AND ([CompanyId] = @SessionCompanyId OR [CompanyId] IS NULL);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",              runId));
        cmd.Parameters.Add(new SqlParameter("@Status",          status));
        cmd.Parameters.Add(new SqlParameter("@Message",         (object?)message ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DurationMs",      durationMs));
        cmd.Parameters.Add(new SqlParameter("@ExecutedCommand", (object?)executedCommand ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SessionCompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectColumns = "[Id],[TaskId],[TaskCode],[StartedAt],[CompletedAt],[Status],[Message],[DurationMs],[Trigger],[ExecutedCommand]";

    public async Task<IReadOnlyList<ScheduledTaskRun>> GetRecentByTaskIdAsync(int taskId, int limit, CancellationToken cancellationToken)
    {
        var list = new List<ScheduledTaskRun>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Limit) {SelectColumns}
              FROM {_table}
             WHERE [TaskId] = @TaskId AND ([CompanyId] = @CompanyId OR [CompanyId] IS NULL)
             ORDER BY [StartedAt] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@TaskId", taskId));
        cmd.Parameters.Add(new SqlParameter("@Limit",  limit));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    // elle bakılmalı: kasıtlı olarak CompanyId filtresi YOK — bkz. SqlScheduledTaskRepository.
    // ReleaseStuckLocksAsync'teki gerekçe (Worker HttpContext'siz, bulk bakım işi tüm şirketleri
    // kapsamalı; filtre eklenirse ResolveEffectiveCompanyId() sahip şirkete düşer ve diğer
    // şirketlerin eski run kayıtları asla temizlenmez / asılı kalmış run'ları asla kapanmaz).
    public async Task PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [StartedAt] < @Cutoff;";
        cmd.Parameters.Add(new SqlParameter("@Cutoff", cutoffUtc));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> MarkStuckRunsFailedAsync(DateTime startedBeforeUtc, string message, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [Status] = 1,
                   [CompletedAt] = GETUTCDATE(),
                   [Message] = COALESCE([Message] + N' | ', N'') + @Message
             WHERE [Status] = 2 AND [CompletedAt] IS NULL AND [StartedAt] < @StartedBefore;
            SELECT @@ROWCOUNT;
            """;
        cmd.Parameters.Add(new SqlParameter("@Message",      message));
        cmd.Parameters.Add(new SqlParameter("@StartedBefore", startedBeforeUtc));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static ScheduledTaskRun Map(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        TaskId          = r.GetInt32(1),
        TaskCode        = r.GetString(2),
        StartedAt       = r.GetDateTime(3),
        CompletedAt     = r.IsDBNull(4) ? null : r.GetDateTime(4),
        Status          = r.GetInt32(5),
        Message         = r.IsDBNull(6) ? null : r.GetString(6),
        DurationMs      = r.IsDBNull(7) ? null : r.GetInt32(7),
        Trigger         = (RunTrigger)r.GetInt32(8),
        ExecutedCommand = r.IsDBNull(9) ? null : r.GetString(9),
    };
}

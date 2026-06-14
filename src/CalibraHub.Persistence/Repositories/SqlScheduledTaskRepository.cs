using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlScheduledTaskRepository : IScheduledTaskRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlScheduledTaskRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[ScheduledTask]";
    }

    private const string Columns =
        "[Id],[Name],[Description],[TaskType],[ParametersJson]," +
        "[ScheduleType],[ScheduleExpression],[ScheduleDescription]," +
        "[IsEnabled],[IsRunning]," +
        "[LastRunAt],[LastRunStatus],[LastRunMessage],[LastRunDurationMs]," +
        "[NextRunAt],[Created],[Updated],[CompanyId],[PrerequisiteTaskId]";

    public async Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<ScheduledTask>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM {_table} ORDER BY [Name];";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    public async Task<ScheduledTask?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM {_table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    /// <summary>
    /// UpsertRegistrationAsync built-in tasklarin (Worker startup'inda) idempotent
    /// kaydi icin Name'e gore lookup yapar. UI tarafinda kullaniciya benzer name'li
    /// gorev olustururken uniqueness sorumlulugu yoktur.
    /// </summary>
    public async Task<ScheduledTask?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 {Columns} FROM {_table} WHERE [Name] = @Name ORDER BY [Id];";
        cmd.Parameters.Add(new SqlParameter("@Name", name));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetDueTasksAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var list = new List<ScheduledTask>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        // BUILTIN tipindeki gorevler scheduler tarafindan dispatch EDILMEZ — kendi
        // BackgroundService'leri calismayi yonetir, bu tabloya sadece raporlama icin yazarlar.
        cmd.CommandText = $"""
            SELECT {Columns} FROM {_table}
             WHERE [IsEnabled] = 1
               AND [IsRunning] = 0
               AND [TaskType] <> 0
               AND [ScheduleType] <> 4
               AND ([NextRunAt] IS NOT NULL AND [NextRunAt] <= @Now)
             ORDER BY [NextRunAt];
            """;
        cmd.Parameters.Add(new SqlParameter("@Now", nowUtc));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    public async Task UpsertRegistrationAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        // Built-in tasklar Name uniqueness icin idempotent — ayni isimde 1 row tutar.
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [Name] = @Name)
            BEGIN
                UPDATE {_table}
                   SET [Description] = @Description,
                       [TaskType] = @TaskType,
                       [ParametersJson] = @ParametersJson,
                       [ScheduleType] = @ScheduleType,
                       [ScheduleExpression] = @ScheduleExpression,
                       [ScheduleDescription] = @ScheduleDescription,
                       [IsEnabled] = @IsEnabled,
                       [CompanyId] = @CompanyId,
                       [PrerequisiteTaskId] = @PrerequisiteTaskId,
                       [Updated] = GETUTCDATE()
                 WHERE [Name] = @Name;
            END
            ELSE
            BEGIN
                INSERT INTO {_table}
                    ([Name],[Description],[TaskType],[ParametersJson],
                     [ScheduleType],[ScheduleExpression],[ScheduleDescription],
                     [IsEnabled],[IsRunning],[CompanyId],[PrerequisiteTaskId],[Created],[Updated])
                VALUES
                    (@Name,@Description,@TaskType,@ParametersJson,
                     @ScheduleType,@ScheduleExpression,@ScheduleDescription,
                     @IsEnabled,0,@CompanyId,@PrerequisiteTaskId,GETUTCDATE(),GETUTCDATE());
            END;
            """;
        AddParams(cmd, task);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> SaveAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();

        if (task.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_table}
                   SET [Name] = @Name,
                       [Description] = @Description,
                       [TaskType] = @TaskType,
                       [ParametersJson] = @ParametersJson,
                       [ScheduleType] = @ScheduleType,
                       [ScheduleExpression] = @ScheduleExpression,
                       [ScheduleDescription] = @ScheduleDescription,
                       [IsEnabled] = @IsEnabled,
                       [CompanyId] = @CompanyId,
                       [PrerequisiteTaskId] = @PrerequisiteTaskId,
                       [NextRunAt] = @NextRunAt,
                       [Updated] = GETUTCDATE()
                 WHERE [Id] = @Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", task.Id));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_table}
                    ([Name],[Description],[TaskType],[ParametersJson],
                     [ScheduleType],[ScheduleExpression],[ScheduleDescription],
                     [IsEnabled],[IsRunning],[CompanyId],[PrerequisiteTaskId],[NextRunAt],[Created],[Updated])
                VALUES
                    (@Name,@Description,@TaskType,@ParametersJson,
                     @ScheduleType,@ScheduleExpression,@ScheduleDescription,
                     @IsEnabled,0,@CompanyId,@PrerequisiteTaskId,@NextRunAt,GETUTCDATE(),GETUTCDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        }

        AddParams(cmd, task);
        cmd.Parameters.Add(new SqlParameter("@NextRunAt", (object?)task.NextRunAt ?? DBNull.Value));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task ReportRunAsync(int taskId, int status, string? message, int? durationMs,
        DateTime? nextRunAt, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [LastRunAt] = GETUTCDATE(),
                   [LastRunStatus] = @Status,
                   [LastRunMessage] = @Message,
                   [LastRunDurationMs] = @DurationMs,
                   [NextRunAt] = @NextRunAt,
                   [Updated] = GETUTCDATE()
             WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",         taskId));
        cmd.Parameters.Add(new SqlParameter("@Status",     status));
        cmd.Parameters.Add(new SqlParameter("@Message",    (object?)message ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DurationMs", (object?)durationMs ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@NextRunAt",  (object?)nextRunAt ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireLockAsync(int taskId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        // Atomic UPDATE — IsRunning'i 0→1'e cevirir yalnizca mevcut 0 ise; rowcount=1 ise acquired.
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [IsRunning] = 1, [Updated] = GETUTCDATE()
             WHERE [Id] = @Id AND [IsRunning] = 0;
            SELECT @@ROWCOUNT;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task ReleaseLockAsync(int taskId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [IsRunning] = 0, [Updated] = GETUTCDATE() WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(int taskId, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [IsEnabled] = @Enabled, [Updated] = GETUTCDATE() WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id",      taskId));
        cmd.Parameters.Add(new SqlParameter("@Enabled", enabled));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParams(SqlCommand cmd, ScheduledTask task)
    {
        cmd.Parameters.Add(new SqlParameter("@Name",                task.Name));
        cmd.Parameters.Add(new SqlParameter("@Description",         (object?)task.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TaskType",            (int)task.TaskType));
        cmd.Parameters.Add(new SqlParameter("@ParametersJson",      (object?)task.ParametersJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ScheduleType",        (int)task.ScheduleType));
        cmd.Parameters.Add(new SqlParameter("@ScheduleExpression",  (object?)task.ScheduleExpression ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ScheduleDescription", (object?)task.ScheduleDescription ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsEnabled",           task.IsEnabled));
        cmd.Parameters.Add(new SqlParameter("@CompanyId",           (object?)task.CompanyId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PrerequisiteTaskId",  (object?)task.PrerequisiteTaskId ?? DBNull.Value));
    }

    private static ScheduledTask Map(SqlDataReader r) => new()
    {
        Id                  = r.GetInt32(0),
        Name                = r.GetString(1),
        Description         = r.IsDBNull(2) ? null : r.GetString(2),
        TaskType            = (ScheduledTaskType)r.GetInt32(3),
        ParametersJson      = r.IsDBNull(4) ? null : r.GetString(4),
        ScheduleType        = (ScheduleType)r.GetInt32(5),
        ScheduleExpression  = r.IsDBNull(6) ? null : r.GetString(6),
        ScheduleDescription = r.IsDBNull(7) ? null : r.GetString(7),
        IsEnabled           = r.GetBoolean(8),
        IsRunning           = r.GetBoolean(9),
        LastRunAt           = r.IsDBNull(10) ? null : r.GetDateTime(10),
        LastRunStatus       = r.IsDBNull(11) ? null : r.GetInt32(11),
        LastRunMessage      = r.IsDBNull(12) ? null : r.GetString(12),
        LastRunDurationMs   = r.IsDBNull(13) ? null : r.GetInt32(13),
        NextRunAt           = r.IsDBNull(14) ? null : r.GetDateTime(14),
        Created             = r.GetDateTime(15),
        Updated             = r.IsDBNull(16) ? r.GetDateTime(15) : r.GetDateTime(16),
        CompanyId           = r.IsDBNull(17) ? null : r.GetInt32(17),
        PrerequisiteTaskId  = r.IsDBNull(18) ? null : r.GetInt32(18),
    };
}

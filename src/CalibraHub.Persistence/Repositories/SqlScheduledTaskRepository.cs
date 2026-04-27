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
        _table = $"[{schema}].[scheduled_tasks]";
    }

    private const string Columns =
        "[id],[code],[name],[description],[task_type],[parameters_json]," +
        "[schedule_type],[schedule_expression],[schedule_description]," +
        "[is_enabled],[is_running]," +
        "[last_run_at],[last_run_status],[last_run_message],[last_run_duration_ms]," +
        "[next_run_at],[created_at],[updated_at]";

    public async Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<ScheduledTask>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM {_table} ORDER BY [name];";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    public async Task<ScheduledTask?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM {_table} WHERE [code] = @Code;";
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<ScheduledTask?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM {_table} WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
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
             WHERE [is_enabled] = 1
               AND [is_running] = 0
               AND [task_type] <> 0
               AND [schedule_type] <> 4
               AND ([next_run_at] IS NOT NULL AND [next_run_at] <= @Now)
             ORDER BY [next_run_at];
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
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [code] = @Code)
            BEGIN
                UPDATE {_table}
                   SET [name] = @Name,
                       [description] = @Description,
                       [task_type] = @TaskType,
                       [parameters_json] = @ParametersJson,
                       [schedule_type] = @ScheduleType,
                       [schedule_expression] = @ScheduleExpression,
                       [schedule_description] = @ScheduleDescription,
                       [is_enabled] = @IsEnabled,
                       [updated_at] = GETUTCDATE()
                 WHERE [code] = @Code;
            END
            ELSE
            BEGIN
                INSERT INTO {_table}
                    ([code],[name],[description],[task_type],[parameters_json],
                     [schedule_type],[schedule_expression],[schedule_description],
                     [is_enabled],[is_running],[created_at],[updated_at])
                VALUES
                    (@Code,@Name,@Description,@TaskType,@ParametersJson,
                     @ScheduleType,@ScheduleExpression,@ScheduleDescription,
                     @IsEnabled,0,GETUTCDATE(),GETUTCDATE());
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
                   SET [code] = @Code,
                       [name] = @Name,
                       [description] = @Description,
                       [task_type] = @TaskType,
                       [parameters_json] = @ParametersJson,
                       [schedule_type] = @ScheduleType,
                       [schedule_expression] = @ScheduleExpression,
                       [schedule_description] = @ScheduleDescription,
                       [is_enabled] = @IsEnabled,
                       [next_run_at] = @NextRunAt,
                       [updated_at] = GETUTCDATE()
                 WHERE [id] = @Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", task.Id));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_table}
                    ([code],[name],[description],[task_type],[parameters_json],
                     [schedule_type],[schedule_expression],[schedule_description],
                     [is_enabled],[is_running],[next_run_at],[created_at],[updated_at])
                VALUES
                    (@Code,@Name,@Description,@TaskType,@ParametersJson,
                     @ScheduleType,@ScheduleExpression,@ScheduleDescription,
                     @IsEnabled,0,@NextRunAt,GETUTCDATE(),GETUTCDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        }

        AddParams(cmd, task);
        cmd.Parameters.Add(new SqlParameter("@NextRunAt", (object?)task.NextRunAt ?? DBNull.Value));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task ReportRunAsync(string code, int status, string? message, int? durationMs,
        DateTime? nextRunAt, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [last_run_at] = GETUTCDATE(),
                   [last_run_status] = @Status,
                   [last_run_message] = @Message,
                   [last_run_duration_ms] = @DurationMs,
                   [next_run_at] = @NextRunAt,
                   [updated_at] = GETUTCDATE()
             WHERE [code] = @Code;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code",       code));
        cmd.Parameters.Add(new SqlParameter("@Status",     status));
        cmd.Parameters.Add(new SqlParameter("@Message",    (object?)message ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DurationMs", (object?)durationMs ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@NextRunAt",  (object?)nextRunAt ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireLockAsync(string code, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        // Atomic UPDATE — is_running'i 0→1'e cevirir yalnizca mevcut 0 ise; rowcount=1 ise acquired.
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [is_running] = 1, [updated_at] = GETUTCDATE()
             WHERE [code] = @Code AND [is_running] = 0;
            SELECT @@ROWCOUNT;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task ReleaseLockAsync(string code, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [is_running] = 0, [updated_at] = GETUTCDATE() WHERE [code] = @Code;";
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [is_enabled] = @Enabled, [updated_at] = GETUTCDATE() WHERE [code] = @Code;";
        cmd.Parameters.Add(new SqlParameter("@Code",    code));
        cmd.Parameters.Add(new SqlParameter("@Enabled", enabled));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParams(SqlCommand cmd, ScheduledTask task)
    {
        cmd.Parameters.Add(new SqlParameter("@Code",                task.Code));
        cmd.Parameters.Add(new SqlParameter("@Name",                task.Name));
        cmd.Parameters.Add(new SqlParameter("@Description",         (object?)task.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TaskType",            (int)task.TaskType));
        cmd.Parameters.Add(new SqlParameter("@ParametersJson",      (object?)task.ParametersJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ScheduleType",        (int)task.ScheduleType));
        cmd.Parameters.Add(new SqlParameter("@ScheduleExpression",  (object?)task.ScheduleExpression ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ScheduleDescription", (object?)task.ScheduleDescription ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsEnabled",           task.IsEnabled));
    }

    private static ScheduledTask Map(SqlDataReader r) => new()
    {
        Id                  = r.GetInt32(0),
        Code                = r.GetString(1),
        Name                = r.GetString(2),
        Description         = r.IsDBNull(3) ? null : r.GetString(3),
        TaskType            = (ScheduledTaskType)r.GetInt32(4),
        ParametersJson      = r.IsDBNull(5) ? null : r.GetString(5),
        ScheduleType        = (ScheduleType)r.GetInt32(6),
        ScheduleExpression  = r.IsDBNull(7) ? null : r.GetString(7),
        ScheduleDescription = r.IsDBNull(8) ? null : r.GetString(8),
        IsEnabled           = r.GetBoolean(9),
        IsRunning           = r.GetBoolean(10),
        LastRunAt           = r.IsDBNull(11) ? null : r.GetDateTime(11),
        LastRunStatus       = r.IsDBNull(12) ? null : r.GetInt32(12),
        LastRunMessage      = r.IsDBNull(13) ? null : r.GetString(13),
        LastRunDurationMs   = r.IsDBNull(14) ? null : r.GetInt32(14),
        NextRunAt           = r.IsDBNull(15) ? null : r.GetDateTime(15),
        CreatedAt           = r.GetDateTime(16),
        UpdatedAt           = r.GetDateTime(17),
    };
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Proje görev katmanı (ProjectTask + şablonlar) veri erişimi. Tablolar
/// CalibraDatabaseInitializer.EnsureProjectTaskTablesAsync tarafından kurulur.
/// Per-company DB: SqlServerConnectionFactory.
/// FK kasing: Document PK [id] (küçük harf, legacy); ProjectTask/Users PK [Id].
/// ArgeProject görev-sahası kolonları (SequentialTasks, ProgressPercent) bilinçli olarak
/// BU repo'dan yazılır — SqlArgeProjectRepository'ye dokunulmaz (zarar-vermeme).
/// Sıra anahtarı: (OrderNo, Id) — eşit OrderNo'da Id kırar (deterministik kilit).
/// </summary>
public sealed class SqlProjectTaskRepository : IProjectTaskRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _taskTable;
    private readonly string _tplTable;
    private readonly string _tplLineTable;
    private readonly string _argeTable;
    private readonly string _docTable;
    private readonly string _usersTable;

    public SqlProjectTaskRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _taskTable = $"[{s}].[ProjectTask]";
        _tplTable = $"[{s}].[ProjectTaskTemplate]";
        _tplLineTable = $"[{s}].[ProjectTaskTemplateLine]";
        _argeTable = $"[{s}].[ArgeProject]";
        _docTable = $"[{s}].[Document]";
        _usersTable = $"[{s}].[Users]";
    }

    // ── Görevler ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<ProjectTaskDto>> ListByProjectAsync(int projectId, bool sequential, CancellationToken ct)
    {
        // IsBlocked türetilir: sıralı modda önünde (OrderNo, Id) sırasına göre Açık görev
        // bulunan Açık görev "Sırada Bekliyor" sayılır — DB'de saklanmaz (drift riski sıfır).
        var sql = $"""
            SELECT t.[Id], t.[ProjectId], t.[Title], t.[Description], t.[OrderNo], t.[Status],
                   CASE WHEN @Seq = 1 AND t.[Status] = 0 AND EXISTS (
                            SELECT 1 FROM {_taskTable} p
                            WHERE p.[ProjectId] = t.[ProjectId] AND p.[IsActive] = 1 AND p.[Status] = 0
                              AND (p.[OrderNo] < t.[OrderNo] OR (p.[OrderNo] = t.[OrderNo] AND p.[Id] < t.[Id])))
                        THEN 1 ELSE 0 END AS [IsBlocked],
                   t.[AssignedUserId], u.[FullName] AS [AssignedUserName],
                   t.[TargetDate], t.[CompletedAt], t.[TemplateLineId], t.[Created]
            FROM {_taskTable} t
            LEFT JOIN {_usersTable} u ON u.[Id] = t.[AssignedUserId]
            WHERE t.[ProjectId] = @P AND t.[IsActive] = 1
            ORDER BY t.[OrderNo], t.[Id];
            """;
        var list = new List<ProjectTaskDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", projectId));
        cmd.Parameters.Add(new SqlParameter("@Seq", sequential ? 1 : 0));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ProjectTaskDto(
                Id: r.GetInt32(0),
                ProjectId: r.GetInt32(1),
                Title: r.GetString(2),
                Description: r.IsDBNull(3) ? null : r.GetString(3),
                OrderNo: r.GetInt32(4),
                Status: r.GetByte(5),
                IsBlocked: r.GetInt32(6) == 1,
                AssignedUserId: r.IsDBNull(7) ? null : r.GetInt32(7),
                AssignedUserName: r.IsDBNull(8) ? null : r.GetString(8),
                TargetDate: r.IsDBNull(9) ? null : r.GetDateTime(9),
                CompletedAt: r.IsDBNull(10) ? null : r.GetDateTime(10),
                TemplateLineId: r.IsDBNull(11) ? null : r.GetInt32(11),
                Created: r.GetDateTime(12)));
        }
        return list;
    }

    public async Task<ProjectTask?> GetByIdAsync(int taskId, CancellationToken ct)
    {
        var sql = $"""
            SELECT [Id],[ProjectId],[Title],[Description],[OrderNo],[Status],[AssignedUserId],
                   [TargetDate],[CompletedAt],[CompletedByUserId],[LinkedEntityKind],[LinkedEntityId],
                   [TemplateLineId],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_taskTable} WHERE [Id] = @Id;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadTask(r);
    }

    private static ProjectTask ReadTask(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        ProjectId = r.GetInt32(1),
        Title = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        OrderNo = r.GetInt32(4),
        Status = (ProjectTaskStatus)r.GetByte(5),
        AssignedUserId = r.IsDBNull(6) ? null : r.GetInt32(6),
        TargetDate = r.IsDBNull(7) ? null : r.GetDateTime(7),
        CompletedAt = r.IsDBNull(8) ? null : r.GetDateTime(8),
        CompletedByUserId = r.IsDBNull(9) ? null : r.GetInt32(9),
        LinkedEntityKind = r.IsDBNull(10) ? null : r.GetString(10),
        LinkedEntityId = r.IsDBNull(11) ? null : r.GetInt32(11),
        TemplateLineId = r.IsDBNull(12) ? null : r.GetInt32(12),
        IsActive = r.GetBoolean(13),
        CreatedById = r.IsDBNull(14) ? null : r.GetInt32(14),
        Created = r.GetDateTime(15),
        UpdatedById = r.IsDBNull(16) ? null : r.GetInt32(16),
        Updated = r.IsDBNull(17) ? null : r.GetDateTime(17),
    };

    public async Task<int> InsertAsync(ProjectTask task, CancellationToken ct)
    {
        var sql = $"""
            INSERT INTO {_taskTable}
                ([ProjectId],[Title],[Description],[OrderNo],[Status],[AssignedUserId],[TargetDate],
                 [LinkedEntityKind],[LinkedEntityId],[TemplateLineId],[CreatedById],[Created])
            VALUES
                (@P,@Title,@Desc,@Order,@Status,@Assigned,@Target,@LinkKind,@LinkId,@TplLine,@Cre,SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddTaskParams(cmd, task);
        cmd.Parameters.Add(new SqlParameter("@Cre", (object?)task.CreatedById ?? DBNull.Value));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static void AddTaskParams(SqlCommand cmd, ProjectTask task)
    {
        cmd.Parameters.Add(new SqlParameter("@P", task.ProjectId));
        cmd.Parameters.Add(new SqlParameter("@Title", task.Title));
        cmd.Parameters.Add(new SqlParameter("@Desc", (object?)task.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Order", task.OrderNo));
        cmd.Parameters.Add(new SqlParameter("@Status", (byte)task.Status));
        cmd.Parameters.Add(new SqlParameter("@Assigned", (object?)task.AssignedUserId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Target", (object?)task.TargetDate ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LinkKind", (object?)task.LinkedEntityKind ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LinkId", (object?)task.LinkedEntityId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@TplLine", (object?)task.TemplateLineId ?? DBNull.Value));
    }

    public async Task UpdateAsync(ProjectTask task, CancellationToken ct)
    {
        var sql = $"""
            UPDATE {_taskTable} SET
                [Title]=@Title, [Description]=@Desc, [OrderNo]=@Order, [AssignedUserId]=@Assigned,
                [TargetDate]=@Target, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id AND [IsActive]=1;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", task.Id));
        cmd.Parameters.Add(new SqlParameter("@Title", task.Title));
        cmd.Parameters.Add(new SqlParameter("@Desc", (object?)task.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Order", task.OrderNo));
        cmd.Parameters.Add(new SqlParameter("@Assigned", (object?)task.AssignedUserId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Target", (object?)task.TargetDate ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)task.UpdatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SoftDeleteAsync(int taskId, int? updatedById, CancellationToken ct)
    {
        var sql = $"""
            UPDATE {_taskTable} SET [IsActive]=0, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> TryCompleteGuardedAsync(int taskId, int? userId, bool sequential, CancellationToken ct)
    {
        // Atomik guard: görev hâlâ Açık + (sıra modu kapalı VEYA önünde açık görev yok) ise
        // tamamlar. Tek UPDATE — iki kullanıcının aynı anda kapatma yarışında ikincisi 0 satır görür.
        var sql = $"""
            UPDATE t SET [Status]=1, [CompletedAt]=SYSUTCDATETIME(), [CompletedByUserId]=@U,
                         [UpdatedById]=@U, [Updated]=SYSUTCDATETIME()
            FROM {_taskTable} t
            WHERE t.[Id]=@Id AND t.[IsActive]=1 AND t.[Status]=0
              AND (@Seq = 0 OR NOT EXISTS (
                    SELECT 1 FROM {_taskTable} p
                    WHERE p.[ProjectId] = t.[ProjectId] AND p.[IsActive] = 1 AND p.[Status] = 0
                      AND (p.[OrderNo] < t.[OrderNo] OR (p.[OrderNo] = t.[OrderNo] AND p.[Id] < t.[Id]))));
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        cmd.Parameters.Add(new SqlParameter("@U", (object?)userId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Seq", sequential ? 1 : 0));
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task SetStatusAsync(int taskId, byte status, int? updatedById, CancellationToken ct)
    {
        // Reopen (Açık'a dönüş) tamamlama izlerini temizler; İptal'de izler korunur
        // (görev daha önce tamamlanmış olamaz — servis yalnız Open→Cancelled geçirir).
        var sql = $"""
            UPDATE {_taskTable} SET
                [Status]=@Status,
                [CompletedAt]      = CASE WHEN @Status = 0 THEN NULL ELSE [CompletedAt] END,
                [CompletedByUserId]= CASE WHEN @Status = 0 THEN NULL ELSE [CompletedByUserId] END,
                [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
            WHERE [Id]=@Id AND [IsActive]=1;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        cmd.Parameters.Add(new SqlParameter("@Status", status));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> HasCompletedAfterAsync(int projectId, int orderNo, int taskId, CancellationToken ct)
    {
        var sql = $"""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM {_taskTable}
                WHERE [ProjectId]=@P AND [IsActive]=1 AND [Status]=1
                  AND ([OrderNo] > @Order OR ([OrderNo] = @Order AND [Id] > @Id))
            ) THEN 1 ELSE 0 END;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", projectId));
        cmd.Parameters.Add(new SqlParameter("@Order", orderNo));
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) == 1;
    }

    public async Task<ProjectTask?> GetNextOpenTaskAsync(int projectId, int orderNo, int taskId, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP 1 [Id],[ProjectId],[Title],[Description],[OrderNo],[Status],[AssignedUserId],
                   [TargetDate],[CompletedAt],[CompletedByUserId],[LinkedEntityKind],[LinkedEntityId],
                   [TemplateLineId],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_taskTable}
            WHERE [ProjectId]=@P AND [IsActive]=1 AND [Status]=0
              AND ([OrderNo] > @Order OR ([OrderNo] = @Order AND [Id] > @Id))
            ORDER BY [OrderNo], [Id];
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", projectId));
        cmd.Parameters.Add(new SqlParameter("@Order", orderNo));
        cmd.Parameters.Add(new SqlParameter("@Id", taskId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadTask(r);
    }

    public async Task<int> GetMaxOrderNoAsync(int projectId, CancellationToken ct)
    {
        var sql = $"SELECT ISNULL(MAX([OrderNo]), 0) FROM {_taskTable} WHERE [ProjectId]=@P AND [IsActive]=1;";
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", projectId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<int>> InsertBatchAsync(IReadOnlyList<ProjectTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return Array.Empty<int>();
        var ids = new List<int>(tasks.Count);
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var sql = $"""
                INSERT INTO {_taskTable}
                    ([ProjectId],[Title],[Description],[OrderNo],[Status],[AssignedUserId],[TargetDate],
                     [LinkedEntityKind],[LinkedEntityId],[TemplateLineId],[CreatedById],[Created])
                VALUES
                    (@P,@Title,@Desc,@Order,@Status,@Assigned,@Target,@LinkKind,@LinkId,@TplLine,@Cre,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            foreach (var task in tasks)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                AddTaskParams(cmd, task);
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)task.CreatedById ?? DBNull.Value));
                var newId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
                ids.Add(newId);
            }
            await tx.CommitAsync(ct);
            return ids;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyCollection<MyProjectTaskItem>> ListMineAsync(int userId, CancellationToken ct)
    {
        // Kişiye atanmış aktif görevler; IsBlocked projenin kendi SequentialTasks bayrağına göre türetilir.
        var sql = $"""
            SELECT t.[Id], t.[ProjectId], a.[Name] AS [ProjectName], d.[DocumentNumber],
                   t.[Title], t.[Description], t.[Status],
                   CASE WHEN a.[SequentialTasks] = 1 AND t.[Status] = 0 AND EXISTS (
                            SELECT 1 FROM {_taskTable} p
                            WHERE p.[ProjectId] = t.[ProjectId] AND p.[IsActive] = 1 AND p.[Status] = 0
                              AND (p.[OrderNo] < t.[OrderNo] OR (p.[OrderNo] = t.[OrderNo] AND p.[Id] < t.[Id])))
                        THEN 1 ELSE 0 END AS [IsBlocked],
                   t.[TargetDate], t.[OrderNo]
            FROM {_taskTable} t
            INNER JOIN {_argeTable} a ON a.[DocumentId] = t.[ProjectId]
            INNER JOIN {_docTable} d ON d.[Id] = t.[ProjectId] AND d.[IsActive] = 1
            WHERE t.[AssignedUserId] = @U AND t.[IsActive] = 1
            ORDER BY CASE WHEN t.[Status] = 0 THEN 0 ELSE 1 END, t.[TargetDate], t.[OrderNo], t.[Id];
            """;
        var list = new List<MyProjectTaskItem>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@U", userId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new MyProjectTaskItem(
                TaskId: r.GetInt32(0),
                ProjectId: r.GetInt32(1),
                ProjectName: r.GetString(2),
                DocumentNumber: r.GetString(3),
                Title: r.GetString(4),
                Description: r.IsDBNull(5) ? null : r.GetString(5),
                Status: r.GetByte(6),
                IsBlocked: r.GetInt32(7) == 1,
                TargetDate: r.IsDBNull(8) ? null : r.GetDateTime(8),
                OrderNo: r.GetInt32(9)));
        }
        return list;
    }

    public async Task<IReadOnlyCollection<ProjectTaskUserOption>> GetAssignableUsersAsync(CancellationToken ct)
    {
        var sql = $"SELECT [Id],[FullName] FROM {_usersTable} WHERE [IsActive]=1 ORDER BY [FullName];";
        var list = new List<ProjectTaskUserOption>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ProjectTaskUserOption(r.GetInt32(0), r.GetString(1)));
        }
        return list;
    }

    // ── ArgeProject görev-sahası ────────────────────────────────────────────

    public async Task<bool> GetSequentialFlagAsync(int projectId, CancellationToken ct)
    {
        var sql = $"SELECT [SequentialTasks] FROM {_argeTable} WHERE [DocumentId]=@P;";
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", projectId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null && result != DBNull.Value && Convert.ToBoolean(result);
    }

    public async Task SetSequentialFlagAsync(int projectId, bool enabled, int? updatedById, CancellationToken ct)
    {
        var sql = $"""
            UPDATE {_argeTable} SET [SequentialTasks]=@E, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
            WHERE [DocumentId]=@P;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", projectId));
        cmd.Parameters.Add(new SqlParameter("@E", enabled ? 1 : 0));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<decimal?> RecalcProjectProgressAsync(int projectId, int? updatedById, CancellationToken ct)
    {
        // Payda = Açık + Tamamlandı (İptal düşer). Aktif görev yoksa YAZMAZ ve NULL döner —
        // görevi olmayan projede manuel ilerleme rejimi bozulmaz.
        var sql = $"""
            DECLARE @done INT, @denom INT;
            SELECT @done  = SUM(CASE WHEN [Status] = 1 THEN 1 ELSE 0 END),
                   @denom = SUM(CASE WHEN [Status] IN (0, 1) THEN 1 ELSE 0 END)
            FROM {_taskTable} WHERE [ProjectId] = @P AND [IsActive] = 1;
            IF (@denom IS NULL OR @denom = 0)
                SELECT CAST(NULL AS DECIMAL(5,2));
            ELSE
            BEGIN
                DECLARE @pct DECIMAL(5,2) = ROUND(100.0 * @done / @denom, 2);
                UPDATE {_argeTable} SET [ProgressPercent]=@pct, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
                WHERE [DocumentId]=@P;
                SELECT @pct;
            END
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@P", projectId));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result == DBNull.Value ? null : Convert.ToDecimal(result);
    }

    // ── Şablonlar ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<ProjectTaskTemplateDto>> ListTemplatesAsync(CancellationToken ct)
    {
        var sql = $"""
            SELECT [Id],[Name],[Description],[IsSequentialDefault] FROM {_tplTable}
            WHERE [IsActive]=1 ORDER BY [Name];
            SELECT l.[Id], l.[TemplateId], l.[Title], l.[Description], l.[OrderNo],
                   l.[DefaultAssignedUserId], u.[FullName] AS [DefaultAssignedUserName]
            FROM {_tplLineTable} l
            INNER JOIN {_tplTable} t ON t.[Id] = l.[TemplateId] AND t.[IsActive] = 1
            LEFT JOIN {_usersTable} u ON u.[Id] = l.[DefaultAssignedUserId]
            WHERE l.[IsActive]=1 ORDER BY l.[TemplateId], l.[OrderNo], l.[Id];
            """;
        var heads = new List<(int Id, string Name, string? Desc, bool Seq)>();
        var lines = new Dictionary<int, List<ProjectTaskTemplateLineDto>>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            heads.Add((r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetBoolean(3)));
        }
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var tplId = r.GetInt32(1);
            if (!lines.TryGetValue(tplId, out var bucket))
            {
                bucket = new List<ProjectTaskTemplateLineDto>();
                lines[tplId] = bucket;
            }
            bucket.Add(new ProjectTaskTemplateLineDto(
                Id: r.GetInt32(0),
                Title: r.GetString(2),
                Description: r.IsDBNull(3) ? null : r.GetString(3),
                OrderNo: r.GetInt32(4),
                DefaultAssignedUserId: r.IsDBNull(5) ? null : r.GetInt32(5),
                DefaultAssignedUserName: r.IsDBNull(6) ? null : r.GetString(6)));
        }
        return heads
            .Select(h => new ProjectTaskTemplateDto(h.Id, h.Name, h.Desc, h.Seq,
                lines.TryGetValue(h.Id, out var b) ? b : Array.Empty<ProjectTaskTemplateLineDto>()))
            .ToList();
    }

    public async Task<ProjectTaskTemplateDto?> GetTemplateAsync(int templateId, CancellationToken ct)
    {
        var all = await ListTemplatesAsync(ct);
        return all.FirstOrDefault(t => t.Id == templateId);
    }

    public async Task<int> UpsertTemplateAsync(ProjectTaskTemplate template, IReadOnlyList<ProjectTaskTemplateLine> lines, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int templateId;
            if (template.Id > 0)
            {
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE {_tplTable} SET [Name]=@Name, [Description]=@Desc, [IsSequentialDefault]=@Seq,
                        [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME()
                    WHERE [Id]=@Id AND [IsActive]=1;
                    """;
                upd.Parameters.Add(new SqlParameter("@Id", template.Id));
                upd.Parameters.Add(new SqlParameter("@Name", template.Name));
                upd.Parameters.Add(new SqlParameter("@Desc", (object?)template.Description ?? DBNull.Value));
                upd.Parameters.Add(new SqlParameter("@Seq", template.IsSequentialDefault ? 1 : 0));
                upd.Parameters.Add(new SqlParameter("@Upd", (object?)template.UpdatedById ?? DBNull.Value));
                await upd.ExecuteNonQueryAsync(ct);
                templateId = template.Id;

                // Satırlar topluca yeniden yazılır (DELETE+INSERT) — küçük hacim, basit yaşam döngüsü.
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_tplLineTable} WHERE [TemplateId]=@Id;";
                del.Parameters.Add(new SqlParameter("@Id", templateId));
                await del.ExecuteNonQueryAsync(ct);
            }
            else
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_tplTable} ([Name],[Description],[IsSequentialDefault],[CreatedById],[Created])
                    VALUES (@Name,@Desc,@Seq,@Cre,SYSUTCDATETIME());
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                ins.Parameters.Add(new SqlParameter("@Name", template.Name));
                ins.Parameters.Add(new SqlParameter("@Desc", (object?)template.Description ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Seq", template.IsSequentialDefault ? 1 : 0));
                ins.Parameters.Add(new SqlParameter("@Cre", (object?)template.CreatedById ?? DBNull.Value));
                templateId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
            }

            foreach (var line in lines)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    INSERT INTO {_tplLineTable} ([TemplateId],[Title],[Description],[OrderNo],[DefaultAssignedUserId],[CreatedById],[Created])
                    VALUES (@Tpl,@Title,@Desc,@Order,@DefAssigned,@Cre,SYSUTCDATETIME());
                    """;
                cmd.Parameters.Add(new SqlParameter("@Tpl", templateId));
                cmd.Parameters.Add(new SqlParameter("@Title", line.Title));
                cmd.Parameters.Add(new SqlParameter("@Desc", (object?)line.Description ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Order", line.OrderNo));
                cmd.Parameters.Add(new SqlParameter("@DefAssigned", (object?)line.DefaultAssignedUserId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Cre", (object?)line.CreatedById ?? DBNull.Value));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return templateId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task SoftDeleteTemplateAsync(int templateId, int? updatedById, CancellationToken ct)
    {
        var sql = $"""
            UPDATE {_tplTable} SET [IsActive]=0, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
            UPDATE {_tplLineTable} SET [IsActive]=0, [UpdatedById]=@Upd, [Updated]=SYSUTCDATETIME() WHERE [TemplateId]=@Id;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", templateId));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

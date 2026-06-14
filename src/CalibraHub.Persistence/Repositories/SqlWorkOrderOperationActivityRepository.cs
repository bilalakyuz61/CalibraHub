using System.ComponentModel;
using System.Reflection;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL Server impl of <see cref="IWorkOrderOperationActivityRepository"/>.
/// 2026-05-20 — Faz 1 MVP. ActivityReason JOIN'i Faz 2'de eklenecek (şu an sadece NULL).
/// </summary>
public sealed class SqlWorkOrderOperationActivityRepository : IWorkOrderOperationActivityRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlWorkOrderOperationActivityRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[WorkOrderOperationActivity]";
    }

    public async Task<WorkOrderOperationActivityDto?> GetActiveAsync(int workOrderOperationId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect(
            filter: "WHERE a.[WorkOrderOperationId] = @OpId AND a.[EndedAt] IS NULL");
        cmd.Parameters.AddWithValue("@OpId", workOrderOperationId);
        var list = await ReadListAsync(cmd, ct);
        return list.FirstOrDefault();
    }

    public async Task<IReadOnlyList<WorkOrderOperationActivityDto>> GetHistoryAsync(int workOrderOperationId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect(
            filter: "WHERE a.[WorkOrderOperationId] = @OpId ORDER BY a.[StartedAt] DESC, a.[Id] DESC");
        cmd.Parameters.AddWithValue("@OpId", workOrderOperationId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<bool> EndActiveAsync(int workOrderOperationId, int personnelId, string? notes, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Aktif satırı tek UPDATE ile kapat — filtered unique index zaten 1 satırla sınırlı.
        // Notes verilmişse mevcut Notes ile birleştir (eski not kaybolmasın).
        cmd.CommandText = $@"
            UPDATE {_table}
            SET    [EndedAt]     = SYSUTCDATETIME(),
                   [UpdatedById] = @UpdatedById,
                   [Updated]     = SYSUTCDATETIME(),
                   [Notes]       = CASE
                                     WHEN @Notes IS NULL OR @Notes = N'' THEN [Notes]
                                     WHEN [Notes] IS NULL OR [Notes] = N'' THEN @Notes
                                     ELSE [Notes] + N' | ' + @Notes
                                   END
            WHERE  [WorkOrderOperationId] = @OpId AND [EndedAt] IS NULL;
            SELECT @@ROWCOUNT;";
        cmd.Parameters.AddWithValue("@OpId", workOrderOperationId);
        cmd.Parameters.AddWithValue("@UpdatedById", personnelId);
        cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        var rows = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return rows > 0;
    }

    public async Task<int> StartAsync(WorkOrderOperationActivity activity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_table}
                ([WorkOrderOperationId],[PersonnelId],[ActivityType],[ActivityReasonId],
                 [StartedAt],[EndedAt],[Quantity],[ScrapQuantity],[Notes],
                 [CreatedById],[Created])
            VALUES
                (@OpId,@PersonnelId,@Type,@ReasonId,
                 @StartedAt,NULL,@Quantity,@ScrapQuantity,@Notes,
                 @CreatedById,SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);";
        cmd.Parameters.AddWithValue("@OpId",          activity.WorkOrderOperationId);
        cmd.Parameters.AddWithValue("@PersonnelId",   activity.PersonnelId);
        cmd.Parameters.AddWithValue("@Type",          (byte)activity.ActivityType);
        cmd.Parameters.AddWithValue("@ReasonId",      (object?)activity.ActivityReasonId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StartedAt",     activity.StartedAt);
        cmd.Parameters.AddWithValue("@Quantity",      (object?)activity.Quantity      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ScrapQuantity", (object?)activity.ScrapQuantity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes",         (object?)activity.Notes         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedById",    (object?)activity.CreatedById   ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private string BuildSelect(string filter) => $@"
        SELECT a.[Id], a.[WorkOrderOperationId], a.[PersonnelId],
               p.[FullName] AS PersonnelName,
               a.[ActivityType], a.[ActivityReasonId],
               CAST(NULL AS NVARCHAR(200)) AS ReasonName,  -- Faz 2: ActivityReason JOIN
               a.[StartedAt], a.[EndedAt],
               a.[Quantity], a.[ScrapQuantity], a.[Notes]
        FROM {_table} a
        LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = a.[PersonnelId]
        {filter};";

    private static async Task<List<WorkOrderOperationActivityDto>> ReadListAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<WorkOrderOperationActivityDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var startedAt = r.GetDateTime(7);
            DateTime? endedAt = r.IsDBNull(8) ? null : r.GetDateTime(8);
            int? durSec = endedAt.HasValue
                ? (int)Math.Max(0, (endedAt.Value - startedAt).TotalSeconds)
                : null;
            var type = (WorkOrderActivityType)r.GetByte(4);
            list.Add(new WorkOrderOperationActivityDto(
                Id:                     r.GetInt32(0),
                WorkOrderOperationId:   r.GetInt32(1),
                PersonnelId:            r.GetInt32(2),
                PersonnelName:          r.IsDBNull(3) ? null : r.GetString(3),
                ActivityType:           type,
                ActivityTypeLabel:      DescribeActivityType(type),
                ActivityReasonId:       r.IsDBNull(5) ? null : r.GetInt32(5),
                ActivityReasonName:     r.IsDBNull(6) ? null : r.GetString(6),
                StartedAt:              startedAt,
                EndedAt:                endedAt,
                DurationSeconds:        durSec,
                Quantity:               r.IsDBNull(9)  ? null : r.GetDecimal(9),
                ScrapQuantity:          r.IsDBNull(10) ? null : r.GetDecimal(10),
                Notes:                  r.IsDBNull(11) ? null : r.GetString(11)));
        }
        return list;
    }

    private static string DescribeActivityType(WorkOrderActivityType type)
    {
        var member = typeof(WorkOrderActivityType).GetMember(type.ToString()).FirstOrDefault();
        var attr = member?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? type.ToString();
    }
}

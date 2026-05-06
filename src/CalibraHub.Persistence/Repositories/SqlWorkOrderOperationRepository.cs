using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWorkOrderOperationRepository : IWorkOrderOperationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;
    private readonly string _routingOpTable;

    public SqlWorkOrderOperationRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[WorkOrderOperation]";
        _routingOpTable = $"[{s}].[RoutingOperation]";
    }

    public async Task<IReadOnlyCollection<WorkOrderOperationDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect(filter: "WHERE wo.[WorkOrderId] = @WorkOrderId ORDER BY wo.[Sequence]");
        cmd.Parameters.AddWithValue("@WorkOrderId", workOrderId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<IReadOnlyCollection<WorkOrderOperationDto>> GetQueueByMachineAsync(int machineId, CancellationToken ct)
    {
        // Bekleyen + devam eden operasyonlar (Status: Pending=0 / InProgress=1).
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect(filter: @"
            INNER JOIN [{_schema}].[WorkOrder] w ON w.[Id] = wo.[WorkOrderId]
            WHERE wo.[MachineId] = @MachineId
              AND wo.[Status] IN (0, 1)
              AND w.[Status] IN (1, 2)
              AND w.[IsActive] = 1
            ORDER BY w.[Priority] DESC, w.[OrderDate], wo.[Sequence]");
        // Yukarıdaki INNER JOIN cümlesini SELECT'e dahil etmek için BuildSelect manuel format.
        // Pratik: cümleyi yeniden yaz (direkt schema'yı substitute).
        cmd.CommandText = $@"
            SELECT wo.[Id], wo.[WorkOrderId], wo.[Sequence], wo.[OperationId],
                   op.[Code] AS OpCode, op.[Name] AS OpName,
                   wo.[MachineId], m.[MachineCode], m.[MachineName],
                   wo.[PlannedDuration], wo.[DurationUnit], wo.[ActualDuration],
                   wo.[ProducedQuantity], wo.[ScrapQuantity], wo.[Status],
                   wo.[StartedByPersonnelId],   sp.[FullName] AS StartedByName,   wo.[StartedAt],
                   wo.[CompletedByPersonnelId], cp.[FullName] AS CompletedByName, wo.[CompletedAt],
                   wo.[Notes]
            FROM {_table} wo
            INNER JOIN [{_schema}].[WorkOrder] w  ON w.[Id]  = wo.[WorkOrderId]
            LEFT  JOIN [{_schema}].[Operation] op ON op.[Id] = wo.[OperationId]
            LEFT  JOIN [{_schema}].[Machine]   m  ON m.[Id]  = wo.[MachineId]
            LEFT  JOIN [{_schema}].[Personnel] sp ON sp.[Id] = wo.[StartedByPersonnelId]
            LEFT  JOIN [{_schema}].[Personnel] cp ON cp.[Id] = wo.[CompletedByPersonnelId]
            WHERE wo.[MachineId] = @MachineId
              AND wo.[Status] IN (0, 1)
              AND w.[Status] IN (1, 2)
              AND w.[IsActive] = 1
            ORDER BY w.[Priority] DESC, w.[OrderDate], wo.[Sequence];";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@MachineId", machineId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<WorkOrderOperationDto?> GetAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect(filter: "WHERE wo.[Id] = @Id");
        cmd.Parameters.AddWithValue("@Id", id);
        var list = await ReadListAsync(cmd, ct);
        return list.FirstOrDefault();
    }

    public async Task<int> SaveAsync(WorkOrderOperation e, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (e.Id <= 0)
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([WorkOrderId],[Sequence],[OperationId],[MachineId],
                     [PlannedDuration],[DurationUnit],[Status],[Notes])
                VALUES
                    (@WorkOrderId,@Sequence,@OperationId,@MachineId,
                     @PlannedDuration,@DurationUnit,0,@Notes);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [Sequence]=@Sequence, [OperationId]=@OperationId,
                    [MachineId]=@MachineId, [PlannedDuration]=@PlannedDuration,
                    [DurationUnit]=@DurationUnit, [Notes]=@Notes
                WHERE [Id]=@Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", e.Id);
        }
        cmd.Parameters.AddWithValue("@WorkOrderId", e.WorkOrderId);
        cmd.Parameters.AddWithValue("@Sequence", e.Sequence);
        cmd.Parameters.AddWithValue("@OperationId", e.OperationId);
        cmd.Parameters.AddWithValue("@MachineId", (object?)e.MachineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlannedDuration", (object?)e.PlannedDuration ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationUnit", (byte)e.DurationUnit);
        cmd.Parameters.AddWithValue("@Notes", (object?)e.Notes ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExplodeFromRoutingAsync(int workOrderId, int routingId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Önce mevcut operasyonları temizle (idempotent re-explode).
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_table} WHERE [WorkOrderId] = @WorkOrderId;";
                del.Parameters.AddWithValue("@WorkOrderId", workOrderId);
                await del.ExecuteNonQueryAsync(ct);
            }

            // RoutingOperation'dan WorkOrderOperation'a kopyala.
            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $@"
                    INSERT INTO {_table}
                        ([WorkOrderId],[Sequence],[OperationId],[MachineId],
                         [PlannedDuration],[DurationUnit],[Status],[Notes])
                    SELECT @WorkOrderId, [Sequence], [OperationId], [MachineId],
                           [OverrideDuration], [DurationUnit], 0, [Notes]
                    FROM {_routingOpTable}
                    WHERE [RoutingId] = @RoutingId
                    ORDER BY [Sequence];";
                ins.Parameters.AddWithValue("@WorkOrderId", workOrderId);
                ins.Parameters.AddWithValue("@RoutingId", routingId);
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task StartAsync(int id, int personnelId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Sadece Pending → InProgress. Zaten InProgress'e tekrar Start yapılırsa hiçbir şey değişmez.
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [Status] = 1,
                [StartedByPersonnelId] = COALESCE([StartedByPersonnelId], @PersonnelId),
                [StartedAt] = COALESCE([StartedAt], GETUTCDATE())
            WHERE [Id] = @Id AND [Status] IN (0, 1);";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@PersonnelId", personnelId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PartialCompleteAsync(int id, int personnelId, decimal quantity, decimal? scrap, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [ProducedQuantity] = [ProducedQuantity] + @Qty,
                [ScrapQuantity]    = [ScrapQuantity] + @Scrap,
                [Status] = CASE WHEN [Status] = 0 THEN 1 ELSE [Status] END,
                [StartedByPersonnelId] = COALESCE([StartedByPersonnelId], @PersonnelId),
                [StartedAt]            = COALESCE([StartedAt], GETUTCDATE())
            WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@PersonnelId", personnelId);
        cmd.Parameters.AddWithValue("@Qty", quantity);
        cmd.Parameters.AddWithValue("@Scrap", scrap ?? 0m);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CompleteAsync(int id, int personnelId, decimal? finalQuantity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [Status] = 2,
                [ProducedQuantity] = COALESCE(@FinalQty, [ProducedQuantity]),
                [CompletedByPersonnelId] = @PersonnelId,
                [CompletedAt] = GETUTCDATE(),
                [StartedByPersonnelId] = COALESCE([StartedByPersonnelId], @PersonnelId),
                [StartedAt]            = COALESCE([StartedAt], GETUTCDATE())
            WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@PersonnelId", personnelId);
        cmd.Parameters.AddWithValue("@FinalQty", (object?)finalQuantity ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string BuildSelect(string filter) => $@"
        SELECT wo.[Id], wo.[WorkOrderId], wo.[Sequence], wo.[OperationId],
               op.[Code] AS OpCode, op.[Name] AS OpName,
               wo.[MachineId], m.[MachineCode], m.[MachineName],
               wo.[PlannedDuration], wo.[DurationUnit], wo.[ActualDuration],
               wo.[ProducedQuantity], wo.[ScrapQuantity], wo.[Status],
               wo.[StartedByPersonnelId],   sp.[FullName] AS StartedByName,   wo.[StartedAt],
               wo.[CompletedByPersonnelId], cp.[FullName] AS CompletedByName, wo.[CompletedAt],
               wo.[Notes]
        FROM {_table} wo
        LEFT JOIN [{_schema}].[Operation] op ON op.[Id] = wo.[OperationId]
        LEFT JOIN [{_schema}].[Machine]   m  ON m.[Id]  = wo.[MachineId]
        LEFT JOIN [{_schema}].[Personnel] sp ON sp.[Id] = wo.[StartedByPersonnelId]
        LEFT JOIN [{_schema}].[Personnel] cp ON cp.[Id] = wo.[CompletedByPersonnelId]
        {filter};";

    private static async Task<IReadOnlyCollection<WorkOrderOperationDto>> ReadListAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<WorkOrderOperationDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new WorkOrderOperationDto(
                Id: r.GetInt32(0),
                WorkOrderId: r.GetInt32(1),
                Sequence: r.GetInt32(2),
                OperationId: r.GetInt32(3),
                OperationCode: r.IsDBNull(4) ? null : r.GetString(4),
                OperationName: r.IsDBNull(5) ? null : r.GetString(5),
                MachineId: r.IsDBNull(6) ? null : r.GetInt32(6),
                MachineCode: r.IsDBNull(7) ? null : r.GetString(7),
                MachineName: r.IsDBNull(8) ? null : r.GetString(8),
                PlannedDuration: r.IsDBNull(9) ? null : r.GetDecimal(9),
                DurationUnit: (DurationUnit)r.GetByte(10),
                ActualDuration: r.IsDBNull(11) ? null : r.GetDecimal(11),
                ProducedQuantity: r.GetDecimal(12),
                ScrapQuantity: r.GetDecimal(13),
                Status: (WorkOrderOperationStatus)r.GetByte(14),
                StartedByPersonnelId: r.IsDBNull(15) ? null : r.GetInt32(15),
                StartedByPersonnelName: r.IsDBNull(16) ? null : r.GetString(16),
                StartedAt: r.IsDBNull(17) ? null : r.GetDateTime(17),
                CompletedByPersonnelId: r.IsDBNull(18) ? null : r.GetInt32(18),
                CompletedByPersonnelName: r.IsDBNull(19) ? null : r.GetString(19),
                CompletedAt: r.IsDBNull(20) ? null : r.GetDateTime(20),
                Notes: r.IsDBNull(21) ? null : r.GetString(21)));
        }
        return list;
    }
}

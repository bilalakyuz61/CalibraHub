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
        // 2026-05-20: ReadListAsync 26 sütun (index 0-25) okuyor; bu inline SQL eskiden 22
        // sütun döndürüyordu → IndexOutOfRangeException ("Index was outside the bounds of
        // the array"). BuildSelect ile aynı sütun listesini (mamul + plan miktar context'i
        // dahil) verecek sekilde 4 ek sütun eklendi: WoNumber, ItemCode, ItemName,
        // WoPlannedQty (saha tablet UX'i için iş emri başlığı + ürün adı + plan miktar).
        cmd.CommandText = $@"
            SELECT wo.[Id], wo.[WorkOrderId], wo.[Sequence], wo.[OperationId],
                   op.[Code] AS OpCode, op.[Name] AS OpName,
                   wo.[MachineId], m.[Code], m.[Name],
                   wo.[PlannedDuration], wo.[DurationUnit], wo.[ActualDuration],
                   wo.[ProducedQuantity], wo.[ScrapQuantity], wo.[Status],
                   wo.[StartedByPersonnelId],   sp.[FullName] AS StartedByName,   wo.[StartedAt],
                   wo.[CompletedByPersonnelId], cp.[FullName] AS CompletedByName, wo.[CompletedAt],
                   wo.[Notes],
                   d.[DocumentNumber] AS WoNumber,
                   i.[Code]        AS ItemCode,
                   i.[Name]        AS ItemName,
                   w.[PlannedQuantity] AS WoPlannedQty,
                   -- 2026-05-22 Upstream cap (BuildSelect ile aynı mantık).
                   CASE WHEN EXISTS (
                           SELECT 1 FROM {_table} prev
                           WHERE prev.[WorkOrderId] = wo.[WorkOrderId]
                             AND prev.[Sequence]    < wo.[Sequence])
                        THEN (SELECT ISNULL(SUM(prev.[ProducedQuantity] - ISNULL(prev.[ScrapQuantity], 0)), 0)
                              FROM {_table} prev
                              WHERE prev.[WorkOrderId] = wo.[WorkOrderId]
                                AND prev.[Sequence]    < wo.[Sequence])
                        ELSE w.[PlannedQuantity]
                   END AS UpstreamCap
            FROM {_table} wo
            INNER JOIN [{_schema}].[WorkOrder] w  ON w.[Id]  = wo.[WorkOrderId]
            INNER JOIN [{_schema}].[Document]  d  ON d.[Id]  = w.[DocumentId]
            LEFT  JOIN [{_schema}].[Operation] op ON op.[Id] = wo.[OperationId]
            LEFT  JOIN [{_schema}].[Machine]   m  ON m.[Id]  = wo.[MachineId]
            LEFT  JOIN [{_schema}].[Personnel] sp ON sp.[Id] = wo.[StartedByPersonnelId]
            LEFT  JOIN [{_schema}].[Personnel] cp ON cp.[Id] = wo.[CompletedByPersonnelId]
            LEFT  JOIN [{_schema}].[Items]     i  ON i.[Id]  = w.[ItemId]
            WHERE wo.[MachineId] = @MachineId
              AND wo.[Status] IN (0, 1)
              AND w.[Status] IN (1, 2)
              AND w.[IsActive] = 1
            ORDER BY w.[Priority] DESC, d.[DocumentDate], wo.[Sequence];";
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
            // MachineId fallback: rota satirinda makine tanimli degilse, WO header'daki
            // DefaultMachineId kullanilir. Boylece tek noktadan tum operasyonlara
            // makine atanabilir; rota satirinda override edilmemisse calisir.
            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $@"
                    INSERT INTO {_table}
                        ([WorkOrderId],[Sequence],[OperationId],[MachineId],
                         [PlannedDuration],[DurationUnit],[Status],[Notes])
                    SELECT @WorkOrderId, ro.[Sequence], ro.[OperationId],
                           COALESCE(ro.[MachineId], wo.[DefaultMachineId]),
                           ro.[OverrideDuration], ro.[DurationUnit], 0, ro.[Notes]
                    FROM {_routingOpTable} ro
                    CROSS JOIN (SELECT [DefaultMachineId]
                                FROM [{_schema}].[WorkOrder]
                                WHERE [Id] = @WorkOrderId) wo
                    WHERE ro.[RoutingId] = @RoutingId
                    ORDER BY ro.[Sequence];";
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

    public async Task CompleteAsync(int id, int personnelId, decimal? finalQuantity, DocumentLine? stockLine, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
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

            // Son operasyon tamamlaniyorsa (stockLine dolu) — mamul girisi AYNI transaction'da
            // append edilir. AppendStockLineAsync'in ayni UPDLOCK+HOLDLOCK deseni burada
            // tekrarlanir (SqlDocumentRepository referans alinarak) — Application katmani
            // iki repository arasinda transaction paylasamadigi icin atomiklik burada saglanir.
            if (stockLine is not null)
            {
                var s = _schema.Replace("]", "]]");
                var lineTable = $"[{s}].[DocumentLine]";

                int nextLineNo;
                await using (var selCmd = conn.CreateCommand())
                {
                    selCmd.Transaction = tx;
                    selCmd.CommandText = $"""
                        SELECT ISNULL(MAX([LineNo]), 0) + 1 FROM {lineTable} WITH (UPDLOCK, HOLDLOCK)
                        WHERE [DocumentId] = @DocumentId;
                        """;
                    selCmd.Parameters.AddWithValue("@DocumentId", stockLine.DocumentId);
                    nextLineNo = Convert.ToInt32(await selCmd.ExecuteScalarAsync(ct));
                }

                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                var baseQtyExpr = StockUnitSql.BaseQtyExpr($"[{s}].[Items]", $"[{s}].[ItemUnits]", "@Quantity", "@ItemId", "@UnitId");
                insCmd.CommandText = $"""
                    INSERT INTO {lineTable}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                         [CombinationId],[LocationId],[FromLocationId],[MovementType],[UnitCost],[LotNo],[Notes])
                    VALUES
                        (@DocumentId,@LineNo,@ItemId,@UnitId,@Quantity,{baseQtyExpr},0,0,0,
                         @CombinationId,@LocationId,@FromLocationId,@MovementType,@UnitCost,@LotNo,@Notes);
                    """;
                insCmd.Parameters.AddWithValue("@DocumentId", stockLine.DocumentId);
                insCmd.Parameters.AddWithValue("@LineNo", nextLineNo);
                insCmd.Parameters.AddWithValue("@ItemId", stockLine.ItemId);
                insCmd.Parameters.AddWithValue("@UnitId", (object?)stockLine.UnitId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@Quantity", stockLine.Quantity);
                insCmd.Parameters.AddWithValue("@CombinationId", (object?)stockLine.CombinationId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@LocationId", (object?)stockLine.LocationId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@FromLocationId", (object?)stockLine.FromLocationId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@MovementType", (object?)stockLine.MovementType ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@UnitCost", (object?)stockLine.UnitCost ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@LotNo", (object?)stockLine.LotNo ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@Notes", (object?)stockLine.Notes ?? DBNull.Value);
                await insCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private string BuildSelect(string filter) => $@"
        SELECT wo.[Id], wo.[WorkOrderId], wo.[Sequence], wo.[OperationId],
               op.[Code] AS OpCode, op.[Name] AS OpName,
               wo.[MachineId], m.[Code], m.[Name],
               wo.[PlannedDuration], wo.[DurationUnit], wo.[ActualDuration],
               wo.[ProducedQuantity], wo.[ScrapQuantity], wo.[Status],
               wo.[StartedByPersonnelId],   sp.[FullName] AS StartedByName,   wo.[StartedAt],
               wo.[CompletedByPersonnelId], cp.[FullName] AS CompletedByName, wo.[CompletedAt],
               wo.[Notes],
               -- Faz 3 ShopFloor UX: mamul + planlanan miktar bağlamı
               d.[DocumentNumber] AS WoNumber,
               i.[Code] AS ItemCode, i.[Name] AS ItemName,
               w.[PlannedQuantity] AS WoPlannedQty,
               -- 2026-05-22: Upstream cap — bu op'tan önceki tüm op'ların net üretimi
               -- (Produced - Scrap toplamı). İlk op'ta upstream yok → WO planlanan miktar.
               -- Downstream op'lar bu değeri aşamaz (StartAsync + Partial/Complete validation).
               CASE WHEN EXISTS (
                       SELECT 1 FROM {_table} prev
                       WHERE prev.[WorkOrderId] = wo.[WorkOrderId]
                         AND prev.[Sequence]    < wo.[Sequence])
                    THEN (SELECT ISNULL(SUM(prev.[ProducedQuantity] - ISNULL(prev.[ScrapQuantity], 0)), 0)
                          FROM {_table} prev
                          WHERE prev.[WorkOrderId] = wo.[WorkOrderId]
                            AND prev.[Sequence]    < wo.[Sequence])
                    ELSE w.[PlannedQuantity]
               END AS UpstreamCap
        FROM {_table} wo
        LEFT JOIN [{_schema}].[Operation] op ON op.[Id] = wo.[OperationId]
        LEFT JOIN [{_schema}].[Machine]   m  ON m.[Id]  = wo.[MachineId]
        LEFT JOIN [{_schema}].[Personnel] sp ON sp.[Id] = wo.[StartedByPersonnelId]
        LEFT JOIN [{_schema}].[Personnel] cp ON cp.[Id] = wo.[CompletedByPersonnelId]
        LEFT JOIN [{_schema}].[WorkOrder] w  ON w.[Id]  = wo.[WorkOrderId]
        LEFT JOIN [{_schema}].[Document]  d  ON d.[Id]  = w.[DocumentId]
        LEFT JOIN [{_schema}].[Items]     i  ON i.[Id]  = w.[ItemId]
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
                Code: r.IsDBNull(7) ? null : r.GetString(7),
                Name: r.IsDBNull(8) ? null : r.GetString(8),
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
                Notes: r.IsDBNull(21) ? null : r.GetString(21),
                WorkOrderNumber: r.IsDBNull(22) ? null : r.GetString(22),
                ItemCode: r.IsDBNull(23) ? null : r.GetString(23),
                ItemName: r.IsDBNull(24) ? null : r.GetString(24),
                WorkOrderPlannedQuantity: r.IsDBNull(25) ? 0m : r.GetDecimal(25),
                UpstreamCap: r.IsDBNull(26) ? 0m : r.GetDecimal(26)));
        }
        return list;
    }
}

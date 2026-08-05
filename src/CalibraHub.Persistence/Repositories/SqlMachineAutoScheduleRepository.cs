using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Makine Planlama Faz 3 (2026-08-05) — otomatik çizelgeleme veri katmanı. Bkz.
/// <c>IMachineAutoScheduleRepository</c> XML doc'u. <c>MachineScheduleBlock</c> per-company DB
/// tablosu (CompanyId kolonu yok); <c>WorkOrder</c>/<c>WorkOrderOperation</c>/<c>OperationMachineTime</c>
/// CompanyId taşıyor (mevcut Production repo'larıyla aynı desen — <c>SqlMachineScheduleRepository</c>).
/// </summary>
public sealed class SqlMachineAutoScheduleRepository : IMachineAutoScheduleRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IOperationMachineTimeService _durationResolver;
    private readonly string _schema;

    public SqlMachineAutoScheduleRepository(
        SqlServerConnectionFactory connectionFactory,
        IOperationMachineTimeService durationResolver,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _durationResolver = durationResolver;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    // Ortak "planlanmamış operasyon" satırı (raw okuma — ham kolonlar). GetCandidateWorkOrdersAsync
    // (WO bazlı özet) ve GetUnplannedOperationsAsync (motor girdisi) aynı temel sorguyu paylaşır.
    private async Task<List<RawOpRow>> QueryUnplannedOperationRowsAsync(
        SqlConnection conn, IReadOnlyList<int>? workOrderIdFilter, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var rows = new List<RawOpRow>();

        await using var cmd = conn.CreateCommand();
        var filterSql = "";
        if (workOrderIdFilter is { Count: > 0 })
        {
            var pn = workOrderIdFilter.Select((_, i) => "@wid" + i).ToArray();
            filterSql = $" AND wo.[Id] IN ({string.Join(",", pn)})";
            for (var i = 0; i < workOrderIdFilter.Count; i++)
                cmd.Parameters.AddWithValue(pn[i], workOrderIdFilter[i]);
        }

        cmd.CommandText = $"""
            SELECT woo.[Id], woo.[WorkOrderId], d.[DocumentNumber], i.[Name], op.[Name],
                   woo.[Sequence], woo.[MachineId], wo.[PlannedQuantity],
                   woo.[PlannedDuration], woo.[DurationUnit], woo.[OperationId], wo.[RoutingId], wo.[ItemId],
                   wo.[Priority], wo.[PlannedEndDate]
            FROM {T("WorkOrderOperation")} woo
            INNER JOIN {T("WorkOrder")} wo ON wo.[Id] = woo.[WorkOrderId] AND wo.[CompanyId] = @CompanyId
            INNER JOIN {T("Document")} d ON d.[Id] = wo.[DocumentId]
            INNER JOIN {T("Items")} i ON i.[Id] = wo.[ItemId]
            LEFT JOIN {T("Operation")} op ON op.[Id] = woo.[OperationId] AND op.[CompanyId] = @CompanyId
            WHERE wo.[IsActive] = 1 AND wo.[Status] IN (1,2) -- Released, InProgress
              AND NOT EXISTS (
                  SELECT 1 FROM {T("MachineScheduleBlock")} msb
                  WHERE msb.[WorkOrderOperationId] = woo.[Id] AND msb.[IsActive] = 1
              ){filterSql}
            ORDER BY wo.[Priority] DESC,
                     CASE WHEN wo.[PlannedEndDate] IS NULL THEN 1 ELSE 0 END, wo.[PlannedEndDate] ASC,
                     woo.[Sequence];
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new RawOpRow(
                WooId: r.GetInt32(0),
                WorkOrderId: r.GetInt32(1),
                WorkOrderNo: r.IsDBNull(2) ? null : r.GetString(2),
                ItemName: r.IsDBNull(3) ? null : r.GetString(3),
                OperationName: r.IsDBNull(4) ? null : r.GetString(4),
                Sequence: r.GetInt32(5),
                MachineId: r.IsDBNull(6) ? null : r.GetInt32(6),
                PlannedQuantity: r.IsDBNull(7) ? null : r.GetDecimal(7),
                PlannedDuration: r.IsDBNull(8) ? null : r.GetDecimal(8),
                DurationUnit: r.GetByte(9),
                OperationId: r.GetInt32(10),
                RoutingId: r.IsDBNull(11) ? null : r.GetInt32(11),
                ItemId: r.GetInt32(12),
                Priority: r.GetByte(13),
                PlannedEndDate: r.IsDBNull(14) ? null : r.GetDateTime(14)));
        }
        return rows;
    }

    private sealed record RawOpRow(
        int WooId, int WorkOrderId, string? WorkOrderNo, string? ItemName, string? OperationName,
        int Sequence, int? MachineId, decimal? PlannedQuantity, decimal? PlannedDuration, byte DurationUnit,
        int OperationId, int? RoutingId, int ItemId, byte Priority, DateTime? PlannedEndDate);

    public async Task<IReadOnlyList<AutoScheduleCandidateWorkOrderDto>> GetCandidateWorkOrdersAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var rows = await QueryUnplannedOperationRowsAsync(conn, null, ct);

        // Sıra korunarak WorkOrderId'ye göre grupla (SQL zaten Priority DESC/PlannedEndDate ASC
        // sıralı döndü — Dictionary ile ilk-görülen sırayı koruyoruz, LINQ GroupBy da sıralı kalır).
        var result = new List<AutoScheduleCandidateWorkOrderDto>();
        var index = new Dictionary<int, int>(); // WorkOrderId -> result listesindeki index
        foreach (var row in rows)
        {
            // Öncelik: WorkOrderOperation.PlannedDuration (girilmişse) → resolver fallback zinciri.
            decimal minutes;
            if (row.PlannedDuration is not null)
            {
                minutes = ((DurationUnit)row.DurationUnit) == DurationUnit.Hour
                    ? row.PlannedDuration.Value * 60m
                    : row.PlannedDuration.Value;
            }
            else
            {
                minutes = await _durationResolver.ResolveDurationMinutesAsync(
                    row.OperationId, row.MachineId, row.ItemId, row.RoutingId, row.PlannedQuantity ?? 1m, ct) ?? 0m;
            }
            var setup = await _durationResolver.ResolveSetupMinutesAsync(
                row.OperationId, row.MachineId, row.ItemId, row.RoutingId, ct) ?? 0m;
            var total = minutes + setup;

            if (index.TryGetValue(row.WorkOrderId, out var idx))
            {
                var existing = result[idx];
                result[idx] = existing with
                {
                    UnplannedOpCount = existing.UnplannedOpCount + 1,
                    TotalMinutes = existing.TotalMinutes + total,
                };
            }
            else
            {
                index[row.WorkOrderId] = result.Count;
                result.Add(new AutoScheduleCandidateWorkOrderDto(
                    WorkOrderId: row.WorkOrderId,
                    WorkOrderNo: row.WorkOrderNo,
                    ItemName: row.ItemName,
                    Priority: row.Priority,
                    PlannedEndDate: row.PlannedEndDate,
                    UnplannedOpCount: 1,
                    TotalMinutes: total));
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<AutoScheduleOperationInputDto>> GetUnplannedOperationsAsync(
        IReadOnlyList<int> workOrderIds, CancellationToken ct)
    {
        if (workOrderIds.Count == 0) return Array.Empty<AutoScheduleOperationInputDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var rows = await QueryUnplannedOperationRowsAsync(conn, workOrderIds, ct);
        return rows.Select(row => new AutoScheduleOperationInputDto(
            WorkOrderOperationId: row.WooId,
            WorkOrderId: row.WorkOrderId,
            WorkOrderNo: row.WorkOrderNo,
            ItemName: row.ItemName,
            OperationName: row.OperationName,
            Sequence: row.Sequence,
            MachineId: row.MachineId,
            OperationId: row.OperationId,
            ItemId: row.ItemId,
            RoutingId: row.RoutingId,
            PlannedQuantity: row.PlannedQuantity,
            PlannedDuration: row.PlannedDuration,
            DurationUnit: row.DurationUnit,
            Priority: row.Priority,
            PlannedEndDate: row.PlannedEndDate)).ToList();
    }

    public async Task<IReadOnlyList<int>> GetCandidateMachineIdsAsync(int operationId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Grup satırları (MachineGroupId dolu) hariç — kullanıcı kararı: "grup-satırı hariç,
        // distinct MachineId". Makine grubu genişletmesi Faz 3 kapsamı dışında.
        cmd.CommandText = $"""
            SELECT DISTINCT [MachineId]
            FROM {T("OperationMachineTime")}
            WHERE [CompanyId] = @CompanyId AND [OperationId] = @OperationId AND [IsActive] = 1
              AND [MachineId] IS NOT NULL AND [MachineGroupId] IS NULL
            ORDER BY [MachineId];
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", operationId);
        var list = new List<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetInt32(0));
        return list;
    }

    public async Task<IReadOnlyList<OccupiedBlockDto>> GetOccupiedBlocksAsync(DateTime fromUtc, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [MachineId], [StartUtc], [EndUtc]
            FROM {T("MachineScheduleBlock")}
            WHERE [IsActive] = 1 AND [EndUtc] > @FromUtc
            ORDER BY [MachineId], [StartUtc];
            """;
        cmd.Parameters.AddWithValue("@FromUtc", fromUtc);
        var list = new List<OccupiedBlockDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new OccupiedBlockDto(
                MachineId: r.GetInt32(0),
                StartUtc: DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc),
                EndUtc: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc)));
        }
        return list;
    }

    public async Task<int> InsertBlocksAsync(IReadOnlyList<PlannedOperationBlocksDto> operations, int? userId, CancellationToken ct)
    {
        if (operations.Count == 0) return 0;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        var created = 0;
        try
        {
            foreach (var op in operations)
            {
                // Production segmentleri ÖNCE eklenir; ilk eklenenin Id'si setup child'ların
                // ParentBlockId'si olur (bkz. IMachineAutoScheduleRepository XML doc'u).
                var productions = op.Segments.Where(s => s.BlockType == (byte)MachineScheduleBlockType.Production).ToList();
                var setups = op.Segments.Where(s => s.BlockType == (byte)MachineScheduleBlockType.Setup).ToList();

                int? firstProdId = null;
                foreach (var seg in productions)
                {
                    var id = await InsertBlockAsync(conn, tx, op.WorkOrderOperationId, seg, parentBlockId: null, userId, ct);
                    firstProdId ??= id;
                    created++;
                }
                foreach (var seg in setups)
                {
                    await InsertBlockAsync(conn, tx, op.WorkOrderOperationId, seg, parentBlockId: firstProdId, userId, ct);
                    created++;
                }
            }
            await tx.CommitAsync(ct);
            return created;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<int> InsertBlockAsync(
        SqlConnection conn, SqlTransaction tx, int workOrderOperationId, PlannedSegmentDto seg,
        int? parentBlockId, int? userId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            INSERT INTO {T("MachineScheduleBlock")}
                ([MachineId],[WorkOrderOperationId],[StartUtc],[EndUtc],[BlockType],[Status],[ParentBlockId],
                 [IsActive],[CreatedById],[Created])
            VALUES
                (@MachineId,@WorkOrderOperationId,@StartUtc,@EndUtc,@BlockType,@Status,@ParentBlockId,
                 1,@CreatedById,SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.AddWithValue("@MachineId", seg.MachineId);
        cmd.Parameters.AddWithValue("@WorkOrderOperationId", workOrderOperationId);
        cmd.Parameters.AddWithValue("@StartUtc", seg.StartUtc);
        cmd.Parameters.AddWithValue("@EndUtc", seg.EndUtc);
        cmd.Parameters.AddWithValue("@BlockType", seg.BlockType);
        cmd.Parameters.AddWithValue("@Status", (byte)MachineScheduleBlockStatus.Planned);
        cmd.Parameters.Add(new SqlParameter("@ParentBlockId", (object?)parentBlockId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }
}

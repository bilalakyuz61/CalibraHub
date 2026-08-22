using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
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
    /// <summary><paramref name="excludeAnyActiveBlock"/>=true (Faz 3 Otomatik Yerleştir, mevcut
    /// davranış): "planlanmamış" = hiç aktif bloğu olmayan operasyon. <paramref name="excludeAnyActiveBlock"/>=false
    /// (Blok Kilitle + Yeniden Çizelgele, 2026-08-22): "yeniden çizelgelenebilir" = Kilitli(2)/Onaylı(3)
    /// aktif bloğu OLMAYAN operasyon (planlanmamış + yalnız-Planlı(1) dahil).</summary>
    private async Task<List<RawOpRow>> QueryUnplannedOperationRowsAsync(
        SqlConnection conn, IReadOnlyList<int>? workOrderIdFilter, bool excludeAnyActiveBlock, CancellationToken ct)
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

        var blockExistsCondition = excludeAnyActiveBlock
            ? "msb.[IsActive] = 1"
            : "msb.[IsActive] = 1 AND msb.[Status] IN (2,3)"; // Locked, Confirmed

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
                  WHERE msb.[WorkOrderOperationId] = woo.[Id] AND {blockExistsCondition}
              ){filterSql}
            -- wo.[Id] kesin tiebreaker: Priority (0/1/2) + PlannedEndDate (sık NULL) eşitliği NORM olduğundan
            -- stabil sıra şart — yoksa Preview ile Apply farklı kapasite tahsisi üretebilir (HIGH review bulgusu).
            -- Ayrıca aynı WO'nun satırlarını bitişik yapıp motorun "bitişik" varsayımını gerçek kılar.
            ORDER BY wo.[Priority] DESC,
                     CASE WHEN wo.[PlannedEndDate] IS NULL THEN 1 ELSE 0 END, wo.[PlannedEndDate] ASC,
                     wo.[Id], woo.[Sequence];
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
        var rows = await QueryUnplannedOperationRowsAsync(conn, null, excludeAnyActiveBlock: true, ct);

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
        var rows = await QueryUnplannedOperationRowsAsync(conn, workOrderIds, excludeAnyActiveBlock: true, ct);
        return MapOperationInputRows(rows);
    }

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — bkz. <c>IMachineAutoScheduleRepository</c>
    /// XML doc'u. İş emri filtresi YOK (tüm açık iş emirleri).</summary>
    public async Task<IReadOnlyList<AutoScheduleOperationInputDto>> GetReschedulableOperationsAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var rows = await QueryUnplannedOperationRowsAsync(conn, null, excludeAnyActiveBlock: false, ct);
        return MapOperationInputRows(rows);
    }

    private static List<AutoScheduleOperationInputDto> MapOperationInputRows(List<RawOpRow> rows)
        => rows.Select(row => new AutoScheduleOperationInputDto(
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

    public async Task<IReadOnlyList<int>> GetCandidateMachineIdsAsync(int operationId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Grup satırları (MachineGroupId dolu) hariç — kullanıcı kararı: "grup-satırı hariç,
        // distinct MachineId". Makine grubu genişletmesi Faz 3 kapsamı dışında. Machine.IsActive
        // ile join edilir — pasifleştirilmiş bir makineye öneri üretilmesin (defansif doğrulama).
        cmd.CommandText = $"""
            SELECT DISTINCT t.[MachineId]
            FROM {T("OperationMachineTime")} t
            INNER JOIN {T("Machine")} m ON m.[Id] = t.[MachineId] AND m.[CompanyId] = @CompanyId AND m.[IsActive] = 1
            WHERE t.[CompanyId] = @CompanyId AND t.[OperationId] = @OperationId AND t.[IsActive] = 1
              AND t.[MachineId] IS NOT NULL AND t.[MachineGroupId] IS NULL
            ORDER BY t.[MachineId];
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", operationId);
        var list = new List<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetInt32(0));
        return list;
    }

    public async Task<IReadOnlyList<OccupiedBlockDto>> GetOccupiedBlocksAsync(DateTime fromUtc, bool lockedConfirmedOnly, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var statusFilter = lockedConfirmedOnly ? " AND [Status] IN (2,3)" : ""; // Locked, Confirmed
        cmd.CommandText = $"""
            SELECT [MachineId], [StartUtc], [EndUtc]
            FROM {T("MachineScheduleBlock")}
            WHERE [IsActive] = 1 AND [EndUtc] > @FromUtc{statusFilter}
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
        try
        {
            var created = await InsertOperationsAsync(conn, tx, operations, userId, ct);
            await tx.CommitAsync(ct);
            return created;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — bkz. <c>IMachineAutoScheduleRepository</c>
    /// XML doc'u. Kilitli/Onaylı bloğu OLMAYAN açık-WO operasyonlarına ait aktif Status=Planned bloklar
    /// <see cref="ReleasableBlockPredicate"/> ile TANIMLANIR — <see cref="CountReleasableBlocksAsync"/>
    /// ile AYNI predikat (önizleme sayımı ile gerçek silme birbirini tutar).</summary>
    public async Task<(int CreatedCount, int ReleasedCount)> ApplyRescheduleAsync(
        IReadOnlyList<PlannedOperationBlocksDto> operations, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int released;
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"""
                    UPDATE b
                    SET b.[IsActive] = 0, b.[UpdatedById] = @UpdatedById, b.[Updated] = SYSUTCDATETIME()
                    FROM {T("MachineScheduleBlock")} b
                    WHERE {ReleasableBlockPredicate()};
                    """;
                del.Parameters.AddWithValue("@CompanyId", companyId);
                del.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
                released = await del.ExecuteNonQueryAsync(ct);
            }

            var created = await InsertOperationsAsync(conn, tx, operations, userId, ct);

            await tx.CommitAsync(ct);
            return (created, released);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — bkz. <c>IMachineAutoScheduleRepository</c>
    /// XML doc'u. Persist ETMEZ (Önizleme sayımı) — <see cref="ApplyRescheduleAsync"/> ile AYNI predikat.</summary>
    public async Task<int> CountReleasableBlocksAsync(CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*)
            FROM {T("MachineScheduleBlock")} b
            WHERE {ReleasableBlockPredicate()};
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>"Serbest set" predikatı — <c>b</c> alias'lı bir <c>MachineScheduleBlock</c> satırının
    /// yeniden çizelgelemede soft-delete edilecek "kilitli-olmayan Planlı blok" olup olmadığını
    /// tanımlar: aktif + Status=Planned(1) + açık bir WO'nun (Released/InProgress, IsActive) operasyonuna
    /// ait + o operasyonun Kilitli(2)/Onaylı(3) aktif bloğu YOK. Bakım/Duruş bloğu gibi
    /// WorkOrderOperationId'si NULL olan bloklar EXISTS koşulunda doğal olarak elenir (dokunulmaz).
    /// Parametre: <c>@CompanyId</c> çağıran tarafından eklenir.</summary>
    private string ReleasableBlockPredicate() => $"""
        b.[IsActive] = 1 AND b.[Status] = 1
          AND EXISTS (
              SELECT 1 FROM {T("WorkOrderOperation")} woo
              INNER JOIN {T("WorkOrder")} wo ON wo.[Id] = woo.[WorkOrderId] AND wo.[CompanyId] = @CompanyId
              WHERE woo.[Id] = b.[WorkOrderOperationId] AND wo.[IsActive] = 1 AND wo.[Status] IN (1,2)
                AND NOT EXISTS (
                    SELECT 1 FROM {T("MachineScheduleBlock")} msb2
                    WHERE msb2.[WorkOrderOperationId] = woo.[Id] AND msb2.[IsActive] = 1 AND msb2.[Status] IN (2,3)
                )
          )
        """;

    /// <summary>Bir operasyon grubunun (Setup/Production segmentleri) bloklarını AYNI transaction
    /// üzerinde ekler — <see cref="InsertBlocksAsync"/> (Faz 3) ve <see cref="ApplyRescheduleAsync"/>
    /// (Yeniden Çizelgele) TARAFINDAN paylaşılır.</summary>
    private async Task<int> InsertOperationsAsync(
        SqlConnection conn, SqlTransaction tx, IReadOnlyList<PlannedOperationBlocksDto> operations, int? userId, CancellationToken ct)
    {
        var created = 0;
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
        return created;
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

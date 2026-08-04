using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Makine Planlama (Üretim Çizelgeleme) — Faz 1 Manuel (2026-08-04). Bkz. <c>MachineScheduleBlock</c>
/// entity XML doc'u + <c>MachineScheduleContracts.cs</c>. <c>MachineScheduleBlock</c> per-company DB
/// tablosu (CompanyId kolonu yok) — filtrelenmez. <c>Machine</c>/<c>Operation</c>/<c>WorkOrder</c>
/// tabloları CompanyId kolonu taşıyor ve mevcut Production repo'ları (SqlWorkOrderRepository,
/// SqlOperationMachineTimeRepository) bu kolonu filtrelediği için aynı desen burada da korunur.
/// </summary>
public sealed class SqlMachineScheduleRepository : IMachineScheduleRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IOperationMachineTimeService _durationResolver;
    private readonly string _schema;

    public SqlMachineScheduleRepository(
        SqlServerConnectionFactory connectionFactory,
        IOperationMachineTimeService durationResolver,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _durationResolver = durationResolver;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    public async Task<MachineScheduleDataDto> GetScheduleDataAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        var machines = new List<MachineDto>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT [Id], [Code], [Name], [HourlyCapacity]
                FROM {T("Machine")}
                WHERE [CompanyId] = @CompanyId AND [IsActive] = 1
                ORDER BY [SortOrder], [Code];
                """;
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                machines.Add(new MachineDto(
                    Id: r.GetInt32(0),
                    Code: r.GetString(1),
                    Name: r.IsDBNull(2) ? null : r.GetString(2),
                    HourlyCapacity: r.IsDBNull(3) ? null : r.GetDecimal(3)));
            }
        }

        var blocks = new List<ScheduleBlockDto>();
        await using (var cmd = conn.CreateCommand())
        {
            // Pencere kesişimi: StartUtc < @To AND EndUtc > @From. Etiketler (workOrderNo/itemName/
            // operationName/quantity) WorkOrderOperationId NULL ise (bakım/duruş bloğu) hepsi null kalır.
            cmd.CommandText = $"""
                SELECT b.[Id], b.[MachineId], b.[WorkOrderOperationId], b.[StartUtc], b.[EndUtc],
                       b.[BlockType], b.[Status], b.[Notes],
                       d.[DocumentNumber] AS WorkOrderNo, i.[Name] AS ItemName, op.[Name] AS OperationName,
                       wo.[PlannedQuantity] AS Quantity
                FROM {T("MachineScheduleBlock")} b
                LEFT JOIN {T("WorkOrderOperation")} woo ON woo.[Id] = b.[WorkOrderOperationId]
                LEFT JOIN {T("WorkOrder")} wo ON wo.[Id] = woo.[WorkOrderId] AND wo.[CompanyId] = @CompanyId
                LEFT JOIN {T("Document")} d ON d.[Id] = wo.[DocumentId]
                LEFT JOIN {T("Items")} i ON i.[Id] = wo.[ItemId]
                LEFT JOIN {T("Operation")} op ON op.[Id] = woo.[OperationId] AND op.[CompanyId] = @CompanyId
                WHERE b.[IsActive] = 1 AND b.[StartUtc] < @To AND b.[EndUtc] > @From
                ORDER BY b.[MachineId], b.[StartUtc];
                """;
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            cmd.Parameters.AddWithValue("@From", fromUtc);
            cmd.Parameters.AddWithValue("@To", toUtc);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                blocks.Add(new ScheduleBlockDto(
                    Id: r.GetInt32(0),
                    MachineId: r.GetInt32(1),
                    WorkOrderOperationId: r.IsDBNull(2) ? null : r.GetInt32(2),
                    StartUtc: DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc),
                    EndUtc: DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc),
                    BlockType: r.GetByte(5),
                    Status: r.GetByte(6),
                    Notes: r.IsDBNull(7) ? null : r.GetString(7),
                    WorkOrderNo: r.IsDBNull(8) ? null : r.GetString(8),
                    ItemName: r.IsDBNull(9) ? null : r.GetString(9),
                    OperationName: r.IsDBNull(10) ? null : r.GetString(10),
                    Quantity: r.IsDBNull(11) ? null : r.GetDecimal(11)));
            }
        }

        // Unplanned kuyruğu — pencereden bağımsız (tüm açık/yayımlanmış iş emirlerinin henüz
        // aktif bloğu olmayan operasyonları). Ham satırlar reader kapandıktan SONRA resolver'a
        // gönderilir (aynı connection üzerinde açık reader varken yeni komut çalıştırılamaz).
        var unplannedRaw = new List<(int WooId, int WorkOrderId, string? WorkOrderNo, string? ItemName,
            string? OperationName, int Sequence, int? MachineId, decimal? PlannedQuantity,
            decimal? PlannedDuration, byte DurationUnit, int OperationId, int? RoutingId, int ItemId)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT woo.[Id], woo.[WorkOrderId], d.[DocumentNumber], i.[Name], op.[Name],
                       woo.[Sequence], woo.[MachineId], wo.[PlannedQuantity],
                       woo.[PlannedDuration], woo.[DurationUnit], woo.[OperationId], wo.[RoutingId], wo.[ItemId]
                FROM {T("WorkOrderOperation")} woo
                INNER JOIN {T("WorkOrder")} wo ON wo.[Id] = woo.[WorkOrderId] AND wo.[CompanyId] = @CompanyId
                INNER JOIN {T("Document")} d ON d.[Id] = wo.[DocumentId]
                INNER JOIN {T("Items")} i ON i.[Id] = wo.[ItemId]
                LEFT JOIN {T("Operation")} op ON op.[Id] = woo.[OperationId] AND op.[CompanyId] = @CompanyId
                WHERE wo.[IsActive] = 1 AND wo.[Status] IN (1,2) -- Released, InProgress
                  AND NOT EXISTS (
                      SELECT 1 FROM {T("MachineScheduleBlock")} msb
                      WHERE msb.[WorkOrderOperationId] = woo.[Id] AND msb.[IsActive] = 1
                  )
                ORDER BY wo.[PlannedStartDate], woo.[Sequence];
                """;
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                unplannedRaw.Add((
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
                    ItemId: r.GetInt32(12)));
            }
        }

        var unplanned = new List<UnplannedOperationDto>();
        foreach (var row in unplannedRaw)
        {
            // Öncelik: WorkOrderOperation.PlannedDuration (girilmişse). NULL ise resolver fallback
            // (OperationMachineTime → RoutingOperation.OverrideDuration → Operation.StandardDuration).
            decimal? estimatedMinutes;
            if (row.PlannedDuration is not null)
            {
                estimatedMinutes = ((DurationUnit)row.DurationUnit) == DurationUnit.Hour
                    ? row.PlannedDuration.Value * 60m
                    : row.PlannedDuration.Value;
            }
            else
            {
                estimatedMinutes = await _durationResolver.ResolveDurationMinutesAsync(
                    row.OperationId, row.MachineId, row.ItemId, row.RoutingId, row.PlannedQuantity ?? 1m, ct);
            }

            unplanned.Add(new UnplannedOperationDto(
                WorkOrderOperationId: row.WooId,
                WorkOrderId: row.WorkOrderId,
                WorkOrderNo: row.WorkOrderNo,
                ItemName: row.ItemName,
                OperationName: row.OperationName,
                Sequence: row.Sequence,
                MachineId: row.MachineId,
                PlannedQuantity: row.PlannedQuantity,
                EstimatedMinutes: estimatedMinutes));
        }

        return new MachineScheduleDataDto(machines, blocks, unplanned);
    }

    public async Task<SaveScheduleBlockResult> SaveBlockAsync(SaveScheduleBlockRequest request, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        int id;

        if (request.Id <= 0)
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = $"""
                INSERT INTO {T("MachineScheduleBlock")}
                    ([MachineId],[WorkOrderOperationId],[StartUtc],[EndUtc],[BlockType],[Status],[Notes],
                     [IsActive],[CreatedById],[Created])
                VALUES
                    (@MachineId,@WorkOrderOperationId,@StartUtc,@EndUtc,@BlockType,@Status,@Notes,
                     1,@CreatedById,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            AddSaveParams(ins, request);
            ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            id = (int)(await ins.ExecuteScalarAsync(ct))!;
        }
        else
        {
            await using var upd = conn.CreateCommand();
            upd.CommandText = $"""
                UPDATE {T("MachineScheduleBlock")}
                SET [MachineId] = @MachineId, [WorkOrderOperationId] = @WorkOrderOperationId,
                    [StartUtc] = @StartUtc, [EndUtc] = @EndUtc, [BlockType] = @BlockType, [Status] = @Status,
                    [Notes] = @Notes, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
                WHERE [Id] = @Id AND [IsActive] = 1;
                """;
            AddSaveParams(upd, request);
            upd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            upd.Parameters.AddWithValue("@Id", request.Id);
            await upd.ExecuteNonQueryAsync(ct);
            id = request.Id;
        }

        // Çakışma: aynı makinede zaman örtüşen DİĞER aktif bloklar. Manuel planlamada bilinçli olarak
        // engellenmez — kayıt her koşulda yapılır, çakışma yalnızca uyarı olarak döner.
        var conflicts = new List<ScheduleBlockConflictDto>();
        await using (var cf = conn.CreateCommand())
        {
            cf.CommandText = $"""
                SELECT b.[Id], d.[DocumentNumber], b.[StartUtc], b.[EndUtc]
                FROM {T("MachineScheduleBlock")} b
                LEFT JOIN {T("WorkOrderOperation")} woo ON woo.[Id] = b.[WorkOrderOperationId]
                LEFT JOIN {T("WorkOrder")} wo ON wo.[Id] = woo.[WorkOrderId]
                LEFT JOIN {T("Document")} d ON d.[Id] = wo.[DocumentId]
                WHERE b.[MachineId] = @MachineId AND b.[Id] <> @Id AND b.[IsActive] = 1
                  AND b.[StartUtc] < @EndUtc AND b.[EndUtc] > @StartUtc;
                """;
            cf.Parameters.AddWithValue("@MachineId", request.MachineId);
            cf.Parameters.AddWithValue("@Id", id);
            cf.Parameters.AddWithValue("@StartUtc", request.StartUtc);
            cf.Parameters.AddWithValue("@EndUtc", request.EndUtc);
            await using var r = await cf.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                conflicts.Add(new ScheduleBlockConflictDto(
                    Id: r.GetInt32(0),
                    WorkOrderNo: r.IsDBNull(1) ? null : r.GetString(1),
                    StartUtc: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    EndUtc: DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)));
            }
        }

        return new SaveScheduleBlockResult(true, id, conflicts);
    }

    private static void AddSaveParams(SqlCommand cmd, SaveScheduleBlockRequest request)
    {
        cmd.Parameters.AddWithValue("@MachineId", request.MachineId);
        cmd.Parameters.Add(new SqlParameter("@WorkOrderOperationId", (object?)request.WorkOrderOperationId ?? DBNull.Value));
        cmd.Parameters.AddWithValue("@StartUtc", request.StartUtc);
        cmd.Parameters.AddWithValue("@EndUtc", request.EndUtc);
        cmd.Parameters.AddWithValue("@BlockType", request.BlockType);
        cmd.Parameters.AddWithValue("@Status", request.Status);
        cmd.Parameters.Add(new SqlParameter("@Notes", (object?)request.Notes ?? DBNull.Value));
    }

    public async Task DeleteBlockAsync(int id, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {T("MachineScheduleBlock")}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

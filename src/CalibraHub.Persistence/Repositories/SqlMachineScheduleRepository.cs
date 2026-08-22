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
    // Faz 2 (2026-08-05) — haftalık müsaitlik + tatil verisi (Gantt gölgeleme) bu repodan okunur.
    private readonly IMachineCalendarRepository _calendar;
    private readonly string _schema;

    public SqlMachineScheduleRepository(
        SqlServerConnectionFactory connectionFactory,
        IOperationMachineTimeService durationResolver,
        IMachineCalendarRepository calendar,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _durationResolver = durationResolver;
        _calendar = calendar;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    public async Task<MachineScheduleDataDto> GetScheduleDataAsync(DateTime fromUtc, DateTime toUtc, int? scenarioId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        var machines = new List<ScheduleMachineDto>();
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
                machines.Add(new ScheduleMachineDto(
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
                       wo.[PlannedQuantity] AS Quantity, b.[ParentBlockId]
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
                    Quantity: r.IsDBNull(11) ? null : r.GetDecimal(11),
                    ParentBlockId: r.IsDBNull(12) ? null : r.GetInt32(12)));
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

        // Faz 2 (2026-08-05) — Gantt gölgeleme verisi (frontend hesaplar, burada yalnız ham veri).
        // Vardiya Senaryoları (2026-08-05) eklentisi: scenarioId null ise varsayılan senaryo türetilir.
        var workWindows = await _calendar.ListWorkWindowsAsync(ct, scenarioId);
        var holidays = await _calendar.ListHolidaysAsync(ct);

        return new MachineScheduleDataDto(machines, blocks, unplanned, workWindows, holidays);
    }

    public async Task<SaveScheduleBlockResult> SaveBlockAsync(SaveScheduleBlockRequest request, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        int id;
        // Faz 2 (2026-08-05) — yeni ÜRETİM bloğu (WorkOrderOperationId dolu) yerleşince otomatik
        // "Hazırlık" (Setup) child bloğu üretilir. Sadece CREATE'te tetiklenir; UPDATE'te mevcut
        // child (varsa) süresi korunarak yeniden çapalanır (bkz. ReanchorSetupChildAsync).
        var isNewProductionBlock = request.Id <= 0
            && request.BlockType == (byte)MachineScheduleBlockType.Production
            && request.WorkOrderOperationId is > 0;

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

            if (request.BlockType == (byte)MachineScheduleBlockType.Production)
                await ReanchorSetupChildAsync(conn, id, request.MachineId, request.StartUtc, userId, ct);
            else
                // Blok Production'dan başka bir türe (Bakım/Duruş/Hazırlık) çevrildiyse bağlı
                // setup child anlamsızlaşır — yetim "Hazırlık" bloğu bırakmamak için soft-delete et.
                await SoftDeleteSetupChildrenAsync(conn, id, userId, ct);
        }

        if (isNewProductionBlock)
            await CreateSetupChildIfNeededAsync(conn, id, request, userId, ct);

        // Bağlı setup child (varsa) üretim bloğundan ÖNCEye uzanır; çakışma taraması onun erken
        // aralığını da kapsamalı ve child'ın kendisi hariç tutulmalı (yoksa setup bloğu başka bir
        // bloğun üstüne binse bile uyarı dönmezdi — MEDIUM review bulgusu).
        int? childId = null;
        var scanStart = request.StartUtc;
        await using (var cs = conn.CreateCommand())
        {
            cs.CommandText = $"""
                SELECT TOP 1 [Id], [StartUtc] FROM {T("MachineScheduleBlock")}
                WHERE [ParentBlockId] = @Id AND [IsActive] = 1;
                """;
            cs.Parameters.AddWithValue("@Id", id);
            await using var cr = await cs.ExecuteReaderAsync(ct);
            if (await cr.ReadAsync(ct))
            {
                childId = cr.GetInt32(0);
                var childStart = DateTime.SpecifyKind(cr.GetDateTime(1), DateTimeKind.Utc);
                if (childStart < scanStart) scanStart = childStart;
            }
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
                WHERE b.[MachineId] = @MachineId AND b.[Id] <> @Id
                  AND (@ChildId IS NULL OR b.[Id] <> @ChildId) AND b.[IsActive] = 1
                  AND b.[StartUtc] < @EndUtc AND b.[EndUtc] > @ScanStart;
                """;
            cf.Parameters.AddWithValue("@MachineId", request.MachineId);
            cf.Parameters.AddWithValue("@Id", id);
            cf.Parameters.Add(new SqlParameter("@ChildId", (object?)childId ?? DBNull.Value));
            cf.Parameters.AddWithValue("@ScanStart", scanStart);
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

    /// <summary>Faz 2 (2026-08-05) — yeni üretim bloğunun operasyonu için setup süresi resolve
    /// edilir (OperationMachineTime.SetupDuration, en spesifik eşleşme); pozitifse
    /// StartUtc'den ÖNCEye bitişik "Hazırlık" (Setup) child bloğu eklenir. Süre yoksa/0 ise no-op.</summary>
    private async Task CreateSetupChildIfNeededAsync(SqlConnection conn, int prodBlockId, SaveScheduleBlockRequest request, int? userId, CancellationToken ct)
    {
        if (request.WorkOrderOperationId is not > 0) return;

        var ctx = await GetOperationContextAsync(conn, request.WorkOrderOperationId.Value, ct);
        if (ctx is null) return;

        var setupMinutes = await _durationResolver.ResolveSetupMinutesAsync(
            ctx.Value.OperationId, request.MachineId, ctx.Value.ItemId, ctx.Value.RoutingId, ct);
        if (setupMinutes is not > 0m) return;

        var setupStart = request.StartUtc.AddMinutes(-(double)setupMinutes.Value);

        await using var ins = conn.CreateCommand();
        ins.CommandText = $"""
            INSERT INTO {T("MachineScheduleBlock")}
                ([MachineId],[WorkOrderOperationId],[StartUtc],[EndUtc],[BlockType],[Status],[Notes],[ParentBlockId],
                 [IsActive],[CreatedById],[Created])
            VALUES
                (@MachineId,@WorkOrderOperationId,@StartUtc,@EndUtc,@BlockType,@Status,@Notes,@ParentBlockId,
                 1,@CreatedById,SYSUTCDATETIME());
            """;
        ins.Parameters.AddWithValue("@MachineId", request.MachineId);
        ins.Parameters.AddWithValue("@WorkOrderOperationId", request.WorkOrderOperationId.Value);
        ins.Parameters.AddWithValue("@StartUtc", setupStart);
        ins.Parameters.AddWithValue("@EndUtc", request.StartUtc);
        ins.Parameters.AddWithValue("@BlockType", (byte)MachineScheduleBlockType.Setup);
        ins.Parameters.AddWithValue("@Status", (byte)MachineScheduleBlockStatus.Planned);
        ins.Parameters.AddWithValue("@Notes", "Hazırlık");
        ins.Parameters.AddWithValue("@ParentBlockId", prodBlockId);
        ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await ins.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Faz 2 (2026-08-05) — bir üretim bloğu taşındığında/güncellendiğinde bağlı aktif
    /// setup child'ı (varsa) yeniden çapalar: child SÜRESİ korunur, EndUtc=yeni üretim başlangıcı,
    /// StartUtc=EndUtc-süre. Makine değiştiyse child'ın makinesi de eşitlenir.</summary>
    private async Task ReanchorSetupChildAsync(SqlConnection conn, int prodBlockId, int newMachineId, DateTime newProdStart, int? userId, CancellationToken ct)
    {
        int? childId = null;
        DateTime childStart = default, childEnd = default;
        await using (var sel = conn.CreateCommand())
        {
            sel.CommandText = $"""
                SELECT TOP 1 [Id], [StartUtc], [EndUtc]
                FROM {T("MachineScheduleBlock")}
                WHERE [ParentBlockId] = @ParentId AND [IsActive] = 1;
                """;
            sel.Parameters.AddWithValue("@ParentId", prodBlockId);
            await using var r = await sel.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                childId = r.GetInt32(0);
                childStart = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc);
                childEnd = DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc);
            }
        }
        if (childId is null) return;

        var duration = childEnd - childStart;
        var newEnd = newProdStart;
        var newStart = newProdStart - duration;

        await using var upd = conn.CreateCommand();
        upd.CommandText = $"""
            UPDATE {T("MachineScheduleBlock")}
            SET [MachineId] = @MachineId, [StartUtc] = @StartUtc, [EndUtc] = @EndUtc,
                [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        upd.Parameters.AddWithValue("@MachineId", newMachineId);
        upd.Parameters.AddWithValue("@StartUtc", newStart);
        upd.Parameters.AddWithValue("@EndUtc", newEnd);
        upd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        upd.Parameters.AddWithValue("@Id", childId.Value);
        await upd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Faz 2 (2026-08-05) — bir bloğa bağlı aktif setup child'ları soft-delete eder
    /// (blok Production dışı türe çevrildiğinde yetim "Hazırlık" bloğu kalmasın).</summary>
    private async Task SoftDeleteSetupChildrenAsync(SqlConnection conn, int parentBlockId, int? userId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {T("MachineScheduleBlock")}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [ParentBlockId] = @ParentId AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@ParentId", parentBlockId);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Faz 2 (2026-08-05) — WorkOrderOperation → (OperationId, ItemId, RoutingId) bağlamı,
    /// setup resolver'ın <c>ResolveSetupMinutesAsync</c> çağrısı için. Bulunamazsa null.</summary>
    private async Task<(int OperationId, int ItemId, int? RoutingId)?> GetOperationContextAsync(SqlConnection conn, int workOrderOperationId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT woo.[OperationId], wo.[ItemId], wo.[RoutingId]
            FROM {T("WorkOrderOperation")} woo
            INNER JOIN {T("WorkOrder")} wo ON wo.[Id] = woo.[WorkOrderId] AND wo.[CompanyId] = @CompanyId
            WHERE woo.[Id] = @Id;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@Id", workOrderOperationId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return (r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2));
    }

    public async Task DeleteBlockAsync(int id, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Faz 2 (2026-08-05) — parent silinince bağlı setup child'ı da (ParentBlockId=@Id) aynı
        // UPDATE ile soft-delete edilir.
        cmd.CommandText = $"""
            UPDATE {T("MachineScheduleBlock")}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE ([Id] = @Id OR [ParentBlockId] = @Id) AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — bkz. <c>IMachineScheduleRepository</c>
    /// XML doc'u. <c>[ParentBlockId] IS NULL</c> koşulu setup child'ları hariç tutar (parent ile
    /// hareket eder, elle kilitlenemez) — eşleşen satır yoksa (bulunamadı/pasif/setup-child) 0 satır
    /// etkilenir → false.</summary>
    public async Task<bool> SetBlockStatusAsync(int id, byte status, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Kilit durumu setup child'a da CASCADE edilir (parent + [ParentBlockId]=@Id) — yoksa
        // parent kilitliyken Planlı kalan setup child reschedule occupied'ine girmez ve motor o
        // hazırlık penceresine başka op yerleştirip çift-rezervasyon üretir (HIGH review). EXISTS
        // guard'ı @Id'nin aktif bir PARENT (setup child değil) olmasını şart koşar — değilse 0 satır.
        cmd.CommandText = $"""
            UPDATE {T("MachineScheduleBlock")}
            SET [Status] = @Status, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [IsActive] = 1 AND ([Id] = @Id OR [ParentBlockId] = @Id)
              AND EXISTS (SELECT 1 FROM {T("MachineScheduleBlock")} p
                          WHERE p.[Id] = @Id AND p.[ParentBlockId] IS NULL AND p.[IsActive] = 1);
            """;
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        cmd.Parameters.AddWithValue("@Id", id);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    /// <summary>Toplu durum değişimi — <see cref="SetBlockStatusAsync"/> ile AYNI kural (setup
    /// child'lar <c>[ParentBlockId] IS NULL</c> ile hariç tutulur).</summary>
    public async Task<int> BulkSetBlockStatusAsync(IReadOnlyList<int> ids, byte status, int? userId, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var pn = ids.Select((_, i) => "@id" + i).ToArray();
        // Verilen parent id'ler + onların setup child'ları (ParentBlockId IN) birlikte set edilir
        // (kilit durumu cascade — HIGH review). Frontend yalnız parent id gönderir (setup child'lar hariç).
        cmd.CommandText = $"""
            UPDATE {T("MachineScheduleBlock")}
            SET [Status] = @Status, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [IsActive] = 1
              AND ([Id] IN ({string.Join(",", pn)}) OR [ParentBlockId] IN ({string.Join(",", pn)}));
            """;
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(pn[i], ids[i]);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Kapasite/Yük Raporu (backend, 2026-08-22) — bkz. <c>IMachineScheduleRepository</c> XML doc'u.</summary>
    public async Task<IReadOnlyList<CapacityBlockDto>> ListBlocksForCapacityReportAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT b.[MachineId], b.[StartUtc], b.[EndUtc], b.[BlockType]
            FROM {T("MachineScheduleBlock")} b
            INNER JOIN {T("Machine")} m ON m.[Id] = b.[MachineId] AND m.[CompanyId] = @CompanyId AND m.[IsActive] = 1
            WHERE b.[IsActive] = 1 AND b.[StartUtc] < @To AND b.[EndUtc] > @From;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@From", fromUtc);
        cmd.Parameters.AddWithValue("@To", toUtc);

        var list = new List<CapacityBlockDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new CapacityBlockDto(
                MachineId: r.GetInt32(0),
                StartUtc: DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc),
                EndUtc: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                BlockType: r.GetByte(3)));
        }
        return list;
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlOperationMachineTimeRepository : IOperationMachineTimeRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlOperationMachineTimeRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{_schema.Replace("]", "]]")}].[OperationMachineTime]";
    }

    public async Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, int? routingId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Machine / CardGroup (makine grubu + ürün grubu) / Items / Unit LEFT JOIN — hepsi opsiyonel FK.
        // MachineGroupCode ve ItemGroupCode = CardGroup.Code + (varsa) " - " + Description birleşik gösterim.
        // RoutingId filtresi (3-değerli mantık): @RoutingId NULL ise yalnız RoutingId IS NULL (ortak) satırlar;
        // dolu ise ortak (RoutingId IS NULL) + o rotaya özel (RoutingId = @RoutingId) satırlar döner.
        cmd.CommandText = $@"
            SELECT t.[Id], t.[OperationId], op.[Code] AS OpCode, op.[Name] AS OpName,
                   t.[RoutingId],
                   t.[MachineId], m.[Code], m.[Name],
                   t.[MachineGroupId], mg.[Code] + ISNULL(N' - ' + NULLIF(mg.[Description], N''), N'') AS MachineGroupCode,
                   t.[ItemId], i.[Code] AS ItemCode, i.[Name] AS ItemName,
                   t.[ItemGroupId], ig.[Code] + ISNULL(N' - ' + NULLIF(ig.[Description], N''), N'') AS ItemGroupCode,
                   t.[UnitId], u.[Code] AS UnitCode, u.[Name] AS UnitName,
                   t.[Quantity], t.[DurationPerUnit], t.[DurationUnit],
                   t.[IsActive], t.[Created], t.[Updated]
            FROM {_table} t
            LEFT JOIN [{_schema}].[Operation] op ON op.[Id] = t.[OperationId]
            LEFT JOIN [{_schema}].[Machine] m ON m.[Id] = t.[MachineId]
            LEFT JOIN [{_schema}].[CardGroup] mg ON mg.[Id] = t.[MachineGroupId]
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = t.[ItemId]
            LEFT JOIN [{_schema}].[CardGroup] ig ON ig.[Id] = t.[ItemGroupId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = t.[UnitId]
            WHERE t.[CompanyId] = @CompanyId AND t.[OperationId] = @OperationId
                  AND (t.[RoutingId] IS NULL OR t.[RoutingId] = @RoutingId)
            ORDER BY t.[RoutingId], i.[Code], m.[Code];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", operationId);
        cmd.Parameters.AddWithValue("@RoutingId", (object?)routingId ?? DBNull.Value);

        var list = new List<OperationMachineTimeDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new OperationMachineTimeDto(
                Id: r.GetInt32(0),
                OperationId: r.GetInt32(1),
                OperationCode: r.IsDBNull(2) ? null : r.GetString(2),
                OperationName: r.IsDBNull(3) ? null : r.GetString(3),
                RoutingId: r.IsDBNull(4) ? null : r.GetInt32(4),
                MachineId: r.IsDBNull(5) ? null : r.GetInt32(5),
                Code: r.IsDBNull(6) ? null : r.GetString(6),
                Name: r.IsDBNull(7) ? null : r.GetString(7),
                MachineGroupId: r.IsDBNull(8) ? null : r.GetInt32(8),
                MachineGroupCode: r.IsDBNull(9) ? null : r.GetString(9),
                ItemId: r.IsDBNull(10) ? null : r.GetInt32(10),
                ItemCode: r.IsDBNull(11) ? null : r.GetString(11),
                ItemName: r.IsDBNull(12) ? null : r.GetString(12),
                ItemGroupId: r.IsDBNull(13) ? null : r.GetInt32(13),
                ItemGroupCode: r.IsDBNull(14) ? null : r.GetString(14),
                UnitId: r.IsDBNull(15) ? null : r.GetInt32(15),
                UnitCode: r.IsDBNull(16) ? null : r.GetString(16),
                UnitName: r.IsDBNull(17) ? null : r.GetString(17),
                Quantity: r.GetDecimal(18),
                DurationPerUnit: r.GetDecimal(19),
                DurationUnit: (DurationUnit)r.GetByte(20),
                IsActive: r.GetBoolean(21),
                Created: r.GetDateTime(22),
                Updated: r.IsDBNull(23) ? null : r.GetDateTime(23)));
        }
        return list;
    }

    public async Task<int> SaveAsync(OperationMachineTime e, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (e.Id <= 0)
        {
            cmd.CommandText = $@"
                INSERT INTO {_table}
                    ([CompanyId],[OperationId],[RoutingId],[MachineId],[MachineGroupId],[ItemId],[ItemGroupId],[UnitId],
                     [Quantity],[DurationPerUnit],[DurationUnit],[IsActive],[Created])
                VALUES (@CompanyId,@OperationId,@RoutingId,@MachineId,@MachineGroupId,@ItemId,@ItemGroupId,@UnitId,
                        @Quantity,@DurationPerUnit,@DurationUnit,@IsActive,GETUTCDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [OperationId]=@OperationId,[RoutingId]=@RoutingId,
                    [MachineId]=@MachineId,[MachineGroupId]=@MachineGroupId,
                    [ItemId]=@ItemId,[ItemGroupId]=@ItemGroupId,[UnitId]=@UnitId,
                    [Quantity]=@Quantity,[DurationPerUnit]=@DurationPerUnit,[DurationUnit]=@DurationUnit,
                    [IsActive]=@IsActive,[Updated]=GETUTCDATE()
                WHERE [Id]=@Id AND [CompanyId]=@CompanyId;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", e.Id);
        }
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", e.OperationId);
        cmd.Parameters.AddWithValue("@RoutingId", (object?)e.RoutingId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MachineId", (object?)e.MachineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MachineGroupId", (object?)e.MachineGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ItemId", (object?)e.ItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ItemGroupId", (object?)e.ItemGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UnitId", (object?)e.UnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Quantity", e.Quantity);
        cmd.Parameters.AddWithValue("@DurationPerUnit", e.DurationPerUnit);
        cmd.Parameters.AddWithValue("@DurationUnit", (byte)e.DurationUnit);
        cmd.Parameters.AddWithValue("@IsActive", e.IsActive);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int?> GetCardGroupCardTypeAsync(int cardGroupId, CancellationToken ct)
    {
        // CardGroup per-company DB'de yaşar (CompanyId kolonu yok). CardType TINYINT.
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [CardType] FROM [{_schema}].[CardGroup] WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", cardGroupId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    public async Task<bool> IsUnitInItemSetAsync(int itemId, int unitId, CancellationToken ct)
    {
        // Birim ya ürünün baz birimidir (Items.UnitId) ya da ürünün alternatif birim
        // satırlarından biridir (ItemUnits.UnitId). Her ikisi de per-company DB'de.
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT CASE WHEN
                EXISTS (SELECT 1 FROM [{_schema}].[Items] WHERE [Id]=@ItemId AND [UnitId]=@UnitId)
             OR EXISTS (SELECT 1 FROM [{_schema}].[ItemUnits] WHERE [ItemId]=@ItemId AND [UnitId]=@UnitId)
            THEN 1 ELSE 0 END;";
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@UnitId", unitId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value && Convert.ToInt32(result) == 1;
    }

    // ── Makine Planlama süre resolver (Faz 1, 2026-08-04) ──────────────────────────────────

    public async Task<OperationMachineTimeMatchDto?> FindBestMachineTimeMatchAsync(
        int operationId, int? machineId, int? itemId, int? routingId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // 3-degerli eslesme (ListByOperationAsync ile ayni desen): MachineId/ItemId/RoutingId NULL
        // ise "genel" satir olarak her zaman eslesir; dolu ise ya @param'a esit ya NULL (genel) olmali.
        // Siralama: en spesifik (NULL olmayan alan sayisi en fazla) satir ONCE gelir.
        cmd.CommandText = $@"
            SELECT TOP 1 [Id], [MachineId], [ItemId], [RoutingId], [Quantity], [DurationPerUnit], [DurationUnit]
            FROM {_table}
            WHERE [CompanyId] = @CompanyId AND [OperationId] = @OperationId AND [IsActive] = 1
              AND ([MachineId] = @MachineId OR [MachineId] IS NULL)
              AND ([ItemId] = @ItemId OR [ItemId] IS NULL)
              AND ([RoutingId] = @RoutingId OR [RoutingId] IS NULL)
            ORDER BY
              CASE WHEN [MachineId] IS NOT NULL THEN 0 ELSE 1 END,
              CASE WHEN [ItemId] IS NOT NULL THEN 0 ELSE 1 END,
              CASE WHEN [RoutingId] IS NOT NULL THEN 0 ELSE 1 END;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@OperationId", operationId);
        cmd.Parameters.AddWithValue("@MachineId", (object?)machineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ItemId", (object?)itemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RoutingId", (object?)routingId ?? DBNull.Value);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new OperationMachineTimeMatchDto(
            Id: r.GetInt32(0),
            MachineId: r.IsDBNull(1) ? null : r.GetInt32(1),
            ItemId: r.IsDBNull(2) ? null : r.GetInt32(2),
            RoutingId: r.IsDBNull(3) ? null : r.GetInt32(3),
            Quantity: r.GetDecimal(4),
            DurationPerUnit: r.GetDecimal(5),
            DurationUnit: (DurationUnit)r.GetByte(6));
    }

    public async Task<decimal?> GetRoutingOperationOverrideMinutesAsync(int routingId, int operationId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 1 [OverrideDuration], [DurationUnit]
            FROM [{_schema}].[RoutingOperation]
            WHERE [RoutingId] = @RoutingId AND [OperationId] = @OperationId AND [OverrideDuration] IS NOT NULL;";
        cmd.Parameters.AddWithValue("@RoutingId", routingId);
        cmd.Parameters.AddWithValue("@OperationId", operationId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var duration = r.GetDecimal(0);
        var unit = (DurationUnit)r.GetByte(1);
        return unit == DurationUnit.Hour ? duration * 60m : duration;
    }

    public async Task<decimal?> GetOperationStandardMinutesAsync(int operationId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [StandardDuration], [DurationUnit]
            FROM [{_schema}].[Operation]
            WHERE [Id] = @OperationId AND [CompanyId] = @CompanyId AND [StandardDuration] IS NOT NULL;";
        cmd.Parameters.AddWithValue("@OperationId", operationId);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var duration = r.GetDecimal(0);
        var unit = (DurationUnit)r.GetByte(1);
        return unit == DurationUnit.Hour ? duration * 60m : duration;
    }
}

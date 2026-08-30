using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// WorkOrderComponent persistence (Faz 2 — BOM patlatma çıktısı).
/// Tablo schema: WorkOrderComponent (Id, WorkOrderId, ItemId, ConfigId, RequiredQuantity,
/// IssuedQuantity, ScrapRate, UnitId, Notes, Created, Updated).
/// Listing JOIN ile Items + ItemConfiguration + Unit tablolarından display kolonları çeker.
/// </summary>
public sealed class SqlWorkOrderComponentRepository : IWorkOrderComponentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlWorkOrderComponentRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[WorkOrderComponent]";
    }

    public async Task<IReadOnlyCollection<WorkOrderComponentDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT c.[Id], c.[WorkOrderId], c.[ItemId], i.[Code] AS ItemCode, i.[Name] AS ItemName,
                   c.[ConfigId], cfg.[RecordCode] AS ConfigCode,
                   c.[RequiredQuantity], c.[IssuedQuantity], c.[ScrapRate],
                   c.[UnitId], u.[Code] AS UnitCode,
                   c.[Notes], c.[Created], c.[Updated],
                   ISNULL(i.[TrackingType], 'None') AS TrackingType, ISNULL(i.[AutoSerial], 0) AS AutoSerial,
                   c.[FromLocationId], loc.[LocationCode] AS FromLocationCode, loc.[LocationName] AS FromLocationName,
                   c.[ComponentBomId], cb.[VersionCode] AS ComponentBomVersionCode
            FROM {_table} c
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = c.[ItemId]
            LEFT JOIN [{_schema}].[ItemConfiguration] cfg ON cfg.[Id] = c.[ConfigId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = c.[UnitId]
            LEFT JOIN [{_schema}].[Location] loc ON loc.[Id] = c.[FromLocationId]
            LEFT JOIN [{_schema}].[BOM] cb ON cb.[Id] = c.[ComponentBomId]
            WHERE c.[WorkOrderId] = @WorkOrderId AND c.[CompanyId] = @CompanyId
            ORDER BY c.[Id];";
        cmd.Parameters.AddWithValue("@WorkOrderId", workOrderId);
        cmd.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());

        var list = new List<WorkOrderComponentDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new WorkOrderComponentDto(
                Id:               r.GetInt32(0),
                WorkOrderId:      r.GetInt32(1),
                ItemId:           r.GetInt32(2),
                ItemCode:         r.IsDBNull(3) ? null : r.GetString(3),
                ItemName:         r.IsDBNull(4) ? null : r.GetString(4),
                ConfigId:         r.IsDBNull(5) ? null : r.GetInt32(5),
                ConfigCode:       r.IsDBNull(6) ? null : r.GetString(6),
                RequiredQuantity: r.GetDecimal(7),
                IssuedQuantity:   r.GetDecimal(8),
                ScrapRate:        r.GetDecimal(9),
                UnitId:           r.IsDBNull(10) ? null : r.GetInt32(10),
                UnitCode:         r.IsDBNull(11) ? null : r.GetString(11),
                Notes:            r.IsDBNull(12) ? null : r.GetString(12),
                Created:          r.GetDateTime(13),
                Updated:          r.IsDBNull(14) ? null : r.GetDateTime(14),
                TrackingType:     r.IsDBNull(15) ? "None" : r.GetString(15),
                AutoSerial:       !r.IsDBNull(16) && r.GetBoolean(16),
                FromLocationId:   r.IsDBNull(17) ? null : r.GetInt32(17),
                FromLocationCode: r.IsDBNull(18) ? null : r.GetString(18),
                FromLocationName: r.IsDBNull(19) ? null : r.GetString(19),
                ComponentBomId:   r.IsDBNull(20) ? null : r.GetInt32(20),
                ComponentBomVersionCode: r.IsDBNull(21) ? null : r.GetString(21)));
        }
        return list;
    }

    public async Task<WorkOrderComponentDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT c.[Id], c.[WorkOrderId], c.[ItemId], i.[Code] AS ItemCode, i.[Name] AS ItemName,
                   c.[ConfigId], cfg.[RecordCode] AS ConfigCode,
                   c.[RequiredQuantity], c.[IssuedQuantity], c.[ScrapRate],
                   c.[UnitId], u.[Code] AS UnitCode,
                   c.[Notes], c.[Created], c.[Updated],
                   ISNULL(i.[TrackingType], 'None') AS TrackingType, ISNULL(i.[AutoSerial], 0) AS AutoSerial,
                   c.[FromLocationId], loc.[LocationCode] AS FromLocationCode, loc.[LocationName] AS FromLocationName,
                   c.[ComponentBomId], cb.[VersionCode] AS ComponentBomVersionCode
            FROM {_table} c
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = c.[ItemId]
            LEFT JOIN [{_schema}].[ItemConfiguration] cfg ON cfg.[Id] = c.[ConfigId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = c.[UnitId]
            LEFT JOIN [{_schema}].[Location] loc ON loc.[Id] = c.[FromLocationId]
            LEFT JOIN [{_schema}].[BOM] cb ON cb.[Id] = c.[ComponentBomId]
            WHERE c.[Id] = @Id AND c.[CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new WorkOrderComponentDto(
            Id:               r.GetInt32(0),
            WorkOrderId:      r.GetInt32(1),
            ItemId:           r.GetInt32(2),
            ItemCode:         r.IsDBNull(3) ? null : r.GetString(3),
            ItemName:         r.IsDBNull(4) ? null : r.GetString(4),
            ConfigId:         r.IsDBNull(5) ? null : r.GetInt32(5),
            ConfigCode:       r.IsDBNull(6) ? null : r.GetString(6),
            RequiredQuantity: r.GetDecimal(7),
            IssuedQuantity:   r.GetDecimal(8),
            ScrapRate:        r.GetDecimal(9),
            UnitId:           r.IsDBNull(10) ? null : r.GetInt32(10),
            UnitCode:         r.IsDBNull(11) ? null : r.GetString(11),
            Notes:            r.IsDBNull(12) ? null : r.GetString(12),
            Created:          r.GetDateTime(13),
            Updated:          r.IsDBNull(14) ? null : r.GetDateTime(14),
            TrackingType:     r.IsDBNull(15) ? "None" : r.GetString(15),
            AutoSerial:       !r.IsDBNull(16) && r.GetBoolean(16),
            FromLocationId:   r.IsDBNull(17) ? null : r.GetInt32(17),
            FromLocationCode: r.IsDBNull(18) ? null : r.GetString(18),
            FromLocationName: r.IsDBNull(19) ? null : r.GetString(19),
            ComponentBomId:   r.IsDBNull(20) ? null : r.GetInt32(20),
            ComponentBomVersionCode: r.IsDBNull(21) ? null : r.GetString(21));
    }

    public async Task ReplaceForWorkOrderAsync(int workOrderId, IReadOnlyCollection<WorkOrderComponent> components, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Mevcut bileşenleri sil (idempotent re-explode)
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {_table} WHERE [WorkOrderId] = @WorkOrderId AND [CompanyId] = @CompanyId;";
                del.Parameters.AddWithValue("@WorkOrderId", workOrderId);
                del.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
                await del.ExecuteNonQueryAsync(ct);
            }

            // 2) Yeni listeyi yaz
            foreach (var c in components)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $@"
                    INSERT INTO {_table}
                        ([WorkOrderId],[ItemId],[ConfigId],[RequiredQuantity],
                         [IssuedQuantity],[ScrapRate],[UnitId],[FromLocationId],[Notes],[ComponentBomId],[Created],[CompanyId])
                    VALUES
                        (@WorkOrderId,@ItemId,@ConfigId,@RequiredQuantity,
                         @IssuedQuantity,@ScrapRate,@UnitId,@FromLocationId,@Notes,@ComponentBomId,SYSUTCDATETIME(),
                         (SELECT p.[CompanyId] FROM [{_schema}].[WorkOrder] p WHERE p.[Id] = @WorkOrderId));";
                ins.Parameters.AddWithValue("@WorkOrderId", workOrderId);
                ins.Parameters.AddWithValue("@ItemId", c.ItemId);
                ins.Parameters.AddWithValue("@ConfigId", (object?)c.ConfigId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@RequiredQuantity", c.RequiredQuantity);
                ins.Parameters.AddWithValue("@IssuedQuantity", c.IssuedQuantity);
                ins.Parameters.AddWithValue("@ScrapRate", c.ScrapRate);
                ins.Parameters.AddWithValue("@UnitId", (object?)c.UnitId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@FromLocationId", (object?)c.FromLocationId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@Notes", (object?)c.Notes ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ComponentBomId", (object?)c.ComponentBomId ?? DBNull.Value);
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

    public async Task DeleteByWorkOrderAsync(int workOrderId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [WorkOrderId] = @WorkOrderId AND [CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@WorkOrderId", workOrderId);
        cmd.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── İş emri bazında bileşen özelleştirme (2026-08-06) ──

    public async Task<int> AddAsync(WorkOrderComponent c, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_table}
                ([WorkOrderId],[ItemId],[ConfigId],[RequiredQuantity],
                 [IssuedQuantity],[ScrapRate],[UnitId],[FromLocationId],[Notes],[ComponentBomId],[Created],[CompanyId])
            VALUES
                (@WorkOrderId,@ItemId,@ConfigId,@RequiredQuantity,
                 0,@ScrapRate,@UnitId,@FromLocationId,@Notes,@ComponentBomId,SYSUTCDATETIME(),
                 (SELECT p.[CompanyId] FROM [{_schema}].[WorkOrder] p WHERE p.[Id] = @WorkOrderId));
            SELECT CAST(SCOPE_IDENTITY() AS INT);";
        cmd.Parameters.AddWithValue("@WorkOrderId", c.WorkOrderId);
        cmd.Parameters.AddWithValue("@ItemId", c.ItemId);
        cmd.Parameters.AddWithValue("@ConfigId", (object?)c.ConfigId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ComponentBomId", (object?)c.ComponentBomId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RequiredQuantity", c.RequiredQuantity);
        cmd.Parameters.AddWithValue("@ScrapRate", c.ScrapRate);
        cmd.Parameters.AddWithValue("@UnitId", (object?)c.UnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FromLocationId", (object?)c.FromLocationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)c.Notes ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateAsync(int componentId, decimal requiredQuantity, decimal scrapRate, string? notes, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [RequiredQuantity] = @Qty, [ScrapRate] = @Scrap, [Notes] = @Notes,
                [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", componentId);
        cmd.Parameters.AddWithValue("@Qty", requiredQuantity);
        cmd.Parameters.AddWithValue("@Scrap", scrapRate);
        cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int componentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", componentId);
        cmd.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Bileşenin planlı sarf lokasyonunu (FromLocationId) günceller — İş Emri ekranında
    /// kullanıcının ExplodeBom'un item-default önerisini override etmesi için (2026-07-31).
    /// locationId null gönderilirse kayıt NULL'a döner (sarf motoru WO deposuna düşer).
    /// </summary>
    public async Task UpdateFromLocationAsync(int componentId, int? locationId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [FromLocationId] = @Loc, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.AddWithValue("@Id", componentId);
        cmd.Parameters.AddWithValue("@Loc", (object?)locationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IssueAsync(int componentId, decimal quantity, int personnelId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int itemId, documentId;
            int? configId, unitId, warehouseLocationId, componentFromLocationId;
            string tracking; string? itemCode;
            var issueCompanyId = _connectionFactory.ResolveEffectiveCompanyId();
            await using (var selCmd = conn.CreateCommand())
            {
                selCmd.Transaction = tx;
                selCmd.CommandText = $@"
                    SELECT c.[ItemId], c.[ConfigId], c.[UnitId], w.[DocumentId], w.[WarehouseLocationId],
                           ISNULL(i.[TrackingType], 'None'), i.[Code], c.[FromLocationId]
                    FROM {_table} c
                    INNER JOIN [{_schema}].[WorkOrder] w ON w.[Id] = c.[WorkOrderId]
                    LEFT JOIN [{_schema}].[Items] i ON i.[Id] = c.[ItemId]
                    WHERE c.[Id] = @Id AND c.[CompanyId] = @CompanyId;";
                selCmd.Parameters.AddWithValue("@Id", componentId);
                selCmd.Parameters.AddWithValue("@CompanyId", issueCompanyId);
                await using var r = await selCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) throw new InvalidOperationException("Bileşen bulunamadı.");
                itemId = r.GetInt32(0);
                configId = r.IsDBNull(1) ? null : r.GetInt32(1);
                unitId = r.IsDBNull(2) ? null : r.GetInt32(2);
                documentId = r.GetInt32(3);
                warehouseLocationId = r.IsDBNull(4) ? null : r.GetInt32(4);
                tracking = r.GetString(5);
                itemCode = r.IsDBNull(6) ? null : r.GetString(6);
                componentFromLocationId = r.IsDBNull(7) ? null : r.GetInt32(7);
            }

            // Kaynak lokasyon: bileşenin planlı FromLocationId'si varsa o, yoksa İş Emri'nin
            // genel deposu (IssueWorkOrderConsumptionAsync ile simetrik: line.FromLocationId ?? woLocationId).
            var effectiveLocationId = componentFromLocationId ?? warehouseLocationId;

            // Bütünlük (2026-07-10): lot/seri-takipli bileşen bu lot/serisiz yoldan sarf edilirse
            // lot/seri bakiyesi fiziksel stoktan sapar (DocumentLineSerial/LotId yazılmaz). Bu
            // bileşenler İş Emri ekranındaki "Sarf Gir" akışından (IssueWorkOrderConsumptionAsync,
            // lot/seri seçimli) sarf edilmelidir.
            if (!string.Equals(tracking, "None", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"'{itemCode ?? ("#" + itemId)}' {(string.Equals(tracking, "Serial", StringComparison.OrdinalIgnoreCase) ? "seri" : "lot")} takipli — " +
                    "sarf, İş Emri ekranındaki 'Sarf Gir' üzerinden lot/seri seçimiyle yapılmalıdır.");

            await using (var updCmd = conn.CreateCommand())
            {
                updCmd.Transaction = tx;
                updCmd.CommandText = $@"
                    UPDATE {_table}
                    SET [IssuedQuantity] = [IssuedQuantity] + @Qty, [Updated] = SYSUTCDATETIME()
                    WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
                updCmd.Parameters.AddWithValue("@Id", componentId);
                updCmd.Parameters.AddWithValue("@Qty", quantity);
                updCmd.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
                await updCmd.ExecuteNonQueryAsync(ct);
            }

            // AppendStockLineAsync ile ayni UPDLOCK+HOLDLOCK deseni — SqlDocumentRepository
            // referans alinarak burada tekrarlanir (ayni sebep: Application katmani iki
            // repository arasinda transaction paylasamiyor, atomiklik repository icinde saglanir).
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
                selCmd.Parameters.AddWithValue("@DocumentId", documentId);
                nextLineNo = Convert.ToInt32(await selCmd.ExecuteScalarAsync(ct));
            }

            await using (var insCmd = conn.CreateCommand())
            {
                insCmd.Transaction = tx;
                var baseQtyExpr = StockUnitSql.BaseQtyExpr($"[{s}].[Items]", $"[{s}].[ItemUnits]", "@Quantity", "@ItemId", "@UnitId");
                insCmd.CommandText = $@"
                    INSERT INTO {lineTable}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                         [CombinationId],[FromLocationId],[MovementType],[Notes])
                    VALUES
                        (@DocumentId,@LineNo,@ItemId,@UnitId,@Quantity,{baseQtyExpr},0,0,0,
                         @CombinationId,@FromLocationId,@MovementType,@Notes);";
                insCmd.Parameters.AddWithValue("@DocumentId", documentId);
                insCmd.Parameters.AddWithValue("@LineNo", nextLineNo);
                insCmd.Parameters.AddWithValue("@ItemId", itemId);
                insCmd.Parameters.AddWithValue("@UnitId", (object?)unitId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@Quantity", quantity);
                insCmd.Parameters.AddWithValue("@CombinationId", (object?)configId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@FromLocationId", (object?)effectiveLocationId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@MovementType", (byte)StockMovementType.Issue);
                insCmd.Parameters.AddWithValue("@Notes", $"Malzeme sarfı — Personnel #{personnelId}");
                await insCmd.ExecuteNonQueryAsync(ct);
            }

            // Eksi bakiye kontrolü — üretim sarfı (Issue) kaynak depoyu azaltır (tarih: bugün)
            if (effectiveLocationId is > 0)
            {
                var companyId = _connectionFactory.ResolveCurrentCompanyId();
                await NegativeBalanceGuard.EnsureAsync(conn, tx, _schema, companyId, itemId, effectiveLocationId.Value, DateTime.Today, ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}

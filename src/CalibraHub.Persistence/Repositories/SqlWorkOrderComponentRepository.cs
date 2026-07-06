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
                   c.[Notes], c.[Created], c.[Updated]
            FROM {_table} c
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = c.[ItemId]
            LEFT JOIN [{_schema}].[ItemConfiguration] cfg ON cfg.[Id] = c.[ConfigId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = c.[UnitId]
            WHERE c.[WorkOrderId] = @WorkOrderId
            ORDER BY c.[Id];";
        cmd.Parameters.AddWithValue("@WorkOrderId", workOrderId);

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
                Updated:          r.IsDBNull(14) ? null : r.GetDateTime(14)));
        }
        return list;
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
                del.CommandText = $"DELETE FROM {_table} WHERE [WorkOrderId] = @WorkOrderId;";
                del.Parameters.AddWithValue("@WorkOrderId", workOrderId);
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
                         [IssuedQuantity],[ScrapRate],[UnitId],[Notes],[Created])
                    VALUES
                        (@WorkOrderId,@ItemId,@ConfigId,@RequiredQuantity,
                         @IssuedQuantity,@ScrapRate,@UnitId,@Notes,SYSUTCDATETIME());";
                ins.Parameters.AddWithValue("@WorkOrderId", workOrderId);
                ins.Parameters.AddWithValue("@ItemId", c.ItemId);
                ins.Parameters.AddWithValue("@ConfigId", (object?)c.ConfigId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@RequiredQuantity", c.RequiredQuantity);
                ins.Parameters.AddWithValue("@IssuedQuantity", c.IssuedQuantity);
                ins.Parameters.AddWithValue("@ScrapRate", c.ScrapRate);
                ins.Parameters.AddWithValue("@UnitId", (object?)c.UnitId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@Notes", (object?)c.Notes ?? DBNull.Value);
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
        cmd.CommandText = $"DELETE FROM {_table} WHERE [WorkOrderId] = @WorkOrderId;";
        cmd.Parameters.AddWithValue("@WorkOrderId", workOrderId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IssueAsync(int componentId, decimal quantity, int personnelId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int itemId, documentId;
            int? configId, unitId, warehouseLocationId;
            await using (var selCmd = conn.CreateCommand())
            {
                selCmd.Transaction = tx;
                selCmd.CommandText = $@"
                    SELECT c.[ItemId], c.[ConfigId], c.[UnitId], w.[DocumentId], w.[WarehouseLocationId]
                    FROM {_table} c
                    INNER JOIN [{_schema}].[WorkOrder] w ON w.[Id] = c.[WorkOrderId]
                    WHERE c.[Id] = @Id;";
                selCmd.Parameters.AddWithValue("@Id", componentId);
                await using var r = await selCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) throw new InvalidOperationException("Bileşen bulunamadı.");
                itemId = r.GetInt32(0);
                configId = r.IsDBNull(1) ? null : r.GetInt32(1);
                unitId = r.IsDBNull(2) ? null : r.GetInt32(2);
                documentId = r.GetInt32(3);
                warehouseLocationId = r.IsDBNull(4) ? null : r.GetInt32(4);
            }

            await using (var updCmd = conn.CreateCommand())
            {
                updCmd.Transaction = tx;
                updCmd.CommandText = $@"
                    UPDATE {_table}
                    SET [IssuedQuantity] = [IssuedQuantity] + @Qty, [Updated] = SYSUTCDATETIME()
                    WHERE [Id] = @Id;";
                updCmd.Parameters.AddWithValue("@Id", componentId);
                updCmd.Parameters.AddWithValue("@Qty", quantity);
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
                insCmd.Parameters.AddWithValue("@FromLocationId", (object?)warehouseLocationId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@MovementType", (byte)StockMovementType.Issue);
                insCmd.Parameters.AddWithValue("@Notes", $"Malzeme sarfı — Personnel #{personnelId}");
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
}

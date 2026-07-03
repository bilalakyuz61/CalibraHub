using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Sayım "Yansıt" — bkz. IInventoryCountRepository. Güncel bakiye StockBalances ile aynı
/// MovementType tabanlı UNION mantığıyla, tek Item+Location'a daraltılarak hesaplanır
/// (staleness önlemi — Draft'taki SystemQtyAtCount snapshot'ı DEĞİL, Yansıt anındaki canlı
/// bakiye kullanılır). Fark satırı ekleme UPDLOCK+HOLDLOCK deseni SqlDocumentRepository ile aynı.
/// </summary>
public sealed class SqlInventoryCountRepository : IInventoryCountRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlInventoryCountRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    public async Task<int> ApplyAsync(int documentId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Optimistic-lock: Draft ise Applied'a çevir, değilse (zaten yansıtılmış) hiçbir
            // satır yazmadan hata fırlat. Fark satırları BU UPDATE'ten SONRA hesaplanır — çift
            // tıklama/network retry senaryosunda ikinci çağrı burada @@ROWCOUNT=0 ile durur.
            int countId;
            int? locationId;
            await using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.Transaction = tx;
                lockCmd.CommandText = $"""
                    UPDATE {T("InventoryCount")}
                    SET [Status] = 1, [Updated] = SYSUTCDATETIME()
                    OUTPUT INSERTED.[Id], INSERTED.[LocationId]
                    WHERE [DocumentId] = @DocId AND [Status] = 0;
                    """;
                lockCmd.Parameters.AddWithValue("@DocId", documentId);
                await using var r = await lockCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                    throw new InvalidOperationException("Bu sayım zaten yansıtılmış veya bulunamadı.");
                countId = r.GetInt32(0);
                locationId = r.IsDBNull(1) ? null : r.GetInt32(1);
            }

            // 2) Taslak satırları oku
            var lines = new List<(int ItemId, int? ConfigId, int? UnitId, decimal CountedQty)>();
            await using (var selCmd = conn.CreateCommand())
            {
                selCmd.Transaction = tx;
                selCmd.CommandText = $"""
                    SELECT [ItemId],[ConfigId],[UnitId],[CountedQty]
                    FROM {T("InventoryCountLine")} WHERE [InventoryCountId] = @CountId;
                    """;
                selCmd.Parameters.AddWithValue("@CountId", countId);
                await using var r = await selCmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    lines.Add((
                        r.GetInt32(0),
                        r.IsDBNull(1) ? null : r.GetInt32(1),
                        r.IsDBNull(2) ? null : r.GetInt32(2),
                        r.GetDecimal(3)));
                }
            }

            var writtenCount = 0;
            foreach (var line in lines)
            {
                var liveBalance = await GetLiveBalanceAsync(conn, tx, companyId, line.ItemId, locationId, ct);
                var variance = line.CountedQty - liveBalance;
                if (variance == 0) continue;

                await AppendAdjustLineAsync(conn, tx, documentId, line.ItemId, line.ConfigId, line.UnitId,
                    Math.Abs(variance), positiveVariance: variance > 0, locationId, ct);
                writtenCount++;
            }

            await tx.CommitAsync(ct);
            return writtenCount;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<decimal> GetLiveBalanceAsync(
        SqlConnection conn, SqlTransaction tx, int companyId, int itemId, int? locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT
                ISNULL(SUM(CASE WHEN dl.[MovementType] = 2 AND dl.[LocationId] = @LocId THEN dl.[Quantity] ELSE 0 END), 0)
              - ISNULL(SUM(CASE WHEN dl.[MovementType] = 1 AND dl.[FromLocationId] = @LocId THEN dl.[Quantity] ELSE 0 END), 0)
              + ISNULL(SUM(CASE WHEN dl.[MovementType] = 3 AND dl.[LocationId] = @LocId THEN dl.[Quantity] ELSE 0 END), 0)
              - ISNULL(SUM(CASE WHEN dl.[MovementType] = 3 AND dl.[FromLocationId] = @LocId THEN dl.[Quantity] ELSE 0 END), 0)
              + ISNULL(SUM(CASE WHEN dl.[MovementType] = 4 AND dl.[LocationId] = @LocId THEN dl.[Quantity] ELSE 0 END), 0)
              - ISNULL(SUM(CASE WHEN dl.[MovementType] = 4 AND dl.[FromLocationId] = @LocId THEN dl.[Quantity] ELSE 0 END), 0)
            FROM {T("DocumentLine")} dl
            INNER JOIN {T("Document")} d ON d.[id] = dl.[DocumentId]
            WHERE dl.[ItemId] = @ItemId AND d.[CompanyId] = @CompanyId AND d.[IsActive] = 1
              AND (dl.[LocationId] = @LocId OR dl.[FromLocationId] = @LocId);
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@LocId", (object?)locationId ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    private async Task AppendAdjustLineAsync(
        SqlConnection conn, SqlTransaction tx, int documentId, int itemId, int? configId, int? unitId,
        decimal quantity, bool positiveVariance, int? locationId, CancellationToken ct)
    {
        var lineTable = T("DocumentLine");
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

        await using var insCmd = conn.CreateCommand();
        insCmd.Transaction = tx;
        insCmd.CommandText = $"""
            INSERT INTO {lineTable}
                ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[UnitPrice],[DiscountRate],[LineTotal],
                 [CombinationId],[LocationId],[FromLocationId],[MovementType],[Notes])
            VALUES
                (@DocumentId,@LineNo,@ItemId,@UnitId,@Quantity,0,0,0,
                 @CombinationId,@ToLoc,@FromLoc,4,@Notes);
            """;
        insCmd.Parameters.AddWithValue("@DocumentId", documentId);
        insCmd.Parameters.AddWithValue("@LineNo", nextLineNo);
        insCmd.Parameters.AddWithValue("@ItemId", itemId);
        insCmd.Parameters.AddWithValue("@UnitId", (object?)unitId ?? DBNull.Value);
        insCmd.Parameters.AddWithValue("@Quantity", quantity);
        insCmd.Parameters.AddWithValue("@CombinationId", (object?)configId ?? DBNull.Value);
        // Fazla çıktı (positiveVariance) → LocationId (giriş yönü); eksik çıktı → FromLocationId (çıkış yönü).
        insCmd.Parameters.AddWithValue("@ToLoc", positiveVariance ? (object?)locationId ?? DBNull.Value : DBNull.Value);
        insCmd.Parameters.AddWithValue("@FromLoc", !positiveVariance ? (object?)locationId ?? DBNull.Value : DBNull.Value);
        insCmd.Parameters.AddWithValue("@Notes", "Sayım farkı — Yansıt");
        await insCmd.ExecuteNonQueryAsync(ct);
    }
}

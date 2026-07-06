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

            // 2) Taslak satırları oku — satır kendi lokasyonunu taşıyabilir (NULL = header)
            var lines = new List<(int ItemId, int? ConfigId, int? UnitId, decimal CountedQty, int? LineLocationId)>();
            await using (var selCmd = conn.CreateCommand())
            {
                selCmd.Transaction = tx;
                selCmd.CommandText = $"""
                    SELECT [ItemId],[ConfigId],[UnitId],[CountedQty],[LocationId]
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
                        r.GetDecimal(3),
                        r.IsDBNull(4) ? null : r.GetInt32(4)));
                }
            }

            var writtenCount = 0;
            foreach (var line in lines)
            {
                var effectiveLocation = line.LineLocationId ?? locationId;
                var liveBalance = await GetLiveBalanceAsync(conn, tx, companyId, line.ItemId, effectiveLocation, ct);
                // CountedQty girilen birimde; liveBalance baz birimde. Karşılaştırma için
                // sayılan miktarı da baz birime çevir (girilen birim = ana birim ise çarpan 1).
                var factor = await ResolveBaseFactorAsync(conn, tx, line.ItemId, line.UnitId, ct);
                var countedBase = line.CountedQty * factor;
                var variance = countedBase - liveBalance;
                if (variance == 0) continue;

                // unitId: null — variance baz birimde hesaplandi (countedBase - liveBalance);
                // NULL birim "baz birim" demektir (StockUnitSql: carpan 1, BaseQuantity = Quantity).
                await AppendAdjustLineAsync(conn, tx, documentId, line.ItemId, line.ConfigId, null,
                    Math.Abs(variance), positiveVariance: variance > 0, effectiveLocation, "Sayım farkı — Yansıt", ct);
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
        // BaseQuantity: ana birime normalize edilmis miktar (farkli birimler tutarli toplansin)
        cmd.CommandText = $"""
            SELECT
                ISNULL(SUM(CASE WHEN dl.[MovementType] = 2 AND dl.[LocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END), 0)
              - ISNULL(SUM(CASE WHEN dl.[MovementType] = 1 AND dl.[FromLocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END), 0)
              + ISNULL(SUM(CASE WHEN dl.[MovementType] = 3 AND dl.[LocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END), 0)
              - ISNULL(SUM(CASE WHEN dl.[MovementType] = 3 AND dl.[FromLocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END), 0)
              + ISNULL(SUM(CASE WHEN dl.[MovementType] = 4 AND dl.[LocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END), 0)
              - ISNULL(SUM(CASE WHEN dl.[MovementType] = 4 AND dl.[FromLocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END), 0)
            FROM {T("DocumentLine")} dl
            INNER JOIN {T("Document")} d ON d.[Id] = dl.[DocumentId]
            WHERE dl.[ItemId] = @ItemId AND d.[CompanyId] = @CompanyId AND d.[IsActive] = 1
              AND (dl.[LocationId] = @LocId OR dl.[FromLocationId] = @LocId);
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@LocId", (object?)locationId ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    /// <summary>
    /// Girilen birimin baz-birim çarpanını çözer — StockUnitSql.BaseQtyExpr ile
    /// aynı semantik: birim NULL veya malzemenin ana birimi (Items.UnitId) ise 1,
    /// aksi halde ItemUnits.Multiplier ("1 alternatif birim = Multiplier baz birim"),
    /// tanımsızsa fallback 1. Sayılan miktar bu çarpanla baz birime çevrilip canlı
    /// bakiye (BaseQuantity toplamı) ile karşılaştırılır.
    /// </summary>
    private async Task<decimal> ResolveBaseFactorAsync(
        SqlConnection conn, SqlTransaction tx, int itemId, int? unitId, CancellationToken ct)
    {
        if (unitId is null) return 1m;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT TOP 1 CASE WHEN i.[UnitId] = @UnitId THEN 1
                              ELSE ISNULL(iu.[Multiplier], 1) END
            FROM {T("Items")} i
            LEFT JOIN {T("ItemUnits")} iu ON iu.[ItemId] = i.[Id] AND iu.[UnitId] = @UnitId
            WHERE i.[Id] = @ItemId;
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@UnitId", unitId.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 1m : Convert.ToDecimal(result);
    }

    /// <summary>
    /// Sayım bağlantısız bakiye sıfırlama — sayım deposundaki TÜM canlı bakiyeleri
    /// (Item + Kombinasyon + Birim kırılımında) sıfırlayan Adjust satırları yazar.
    /// </summary>
    public Task<int> ZeroLocationBalancesAsync(int documentId, CancellationToken ct)
        => ZeroBalancesCoreAsync(documentId, onlyUncounted: false,
            "Bakiye sıfırlama — sayım bağlantısız", ct);

    /// <summary>
    /// Sayılmayan stokların sıfırlanması — bakiyesi olup sayım kalemlerinde yer almayan
    /// (ItemId + ConfigId eşleşmesi) stoklara sıfırlama Adjust satırları yazar.
    /// </summary>
    public Task<int> ZeroUncountedAsync(int documentId, CancellationToken ct)
        => ZeroBalancesCoreAsync(documentId, onlyUncounted: true,
            "Sayılmayan stok sıfırlama", ct);

    private async Task<int> ZeroBalancesCoreAsync(
        int documentId, bool onlyUncounted, string notes, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Sayım başlığını çöz — depo zorunlu
            int countId;
            int? locationId;
            await using (var hdrCmd = conn.CreateCommand())
            {
                hdrCmd.Transaction = tx;
                hdrCmd.CommandText = $"""
                    SELECT [Id],[LocationId] FROM {T("InventoryCount")} WHERE [DocumentId] = @DocId;
                    """;
                hdrCmd.Parameters.AddWithValue("@DocId", documentId);
                await using var r = await hdrCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                    throw new InvalidOperationException("Sayım kaydı bulunamadı.");
                countId = r.GetInt32(0);
                locationId = r.IsDBNull(1) ? null : r.GetInt32(1);
            }
            if (!locationId.HasValue)
                throw new InvalidOperationException("Sayım deposu seçilmemiş — önce belgeyi depo seçerek kaydedin.");

            // 2) Depodaki net bakiyeler (Item + Kombinasyon + Birim kırılımı). onlyUncounted=true
            // ise sayım kalemlerinde geçen (ItemId, ConfigId) çiftleri hariç tutulur.
            var uncountedFilter = onlyUncounted
                ? $"""
                    AND NOT EXISTS (SELECT 1 FROM {T("InventoryCountLine")} icl
                                    WHERE icl.[InventoryCountId] = @CountId
                                      AND icl.[ItemId] = dl.[ItemId]
                                      AND ISNULL(icl.[ConfigId], 0) = ISNULL(dl.[CombinationId], 0))
                   """
                : "";

            // BaseQuantity: ana birime normalize edilmis miktar. Birim kirilimi kaldirildi —
            // farkli birimler baz birimde toplanip tek (Item+Config) bakiyesi cikarilir.
            var balances = new List<(int ItemId, int? ConfigId, decimal Balance)>();
            await using (var balCmd = conn.CreateCommand())
            {
                balCmd.Transaction = tx;
                balCmd.CommandText = $"""
                    SELECT dl.[ItemId], dl.[CombinationId],
                           SUM(CASE WHEN dl.[MovementType] = 2 AND dl.[LocationId]     = @LocId THEN dl.[BaseQuantity] ELSE 0 END)
                         - SUM(CASE WHEN dl.[MovementType] = 1 AND dl.[FromLocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END)
                         + SUM(CASE WHEN dl.[MovementType] IN (3,4) AND dl.[LocationId]     = @LocId THEN dl.[BaseQuantity] ELSE 0 END)
                         - SUM(CASE WHEN dl.[MovementType] IN (3,4) AND dl.[FromLocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END) AS Bal
                    FROM {T("DocumentLine")} dl
                    INNER JOIN {T("Document")} d ON d.[Id] = dl.[DocumentId]
                    WHERE d.[CompanyId] = @CompanyId AND d.[IsActive] = 1
                      AND (dl.[LocationId] = @LocId OR dl.[FromLocationId] = @LocId){uncountedFilter}
                    GROUP BY dl.[ItemId], dl.[CombinationId]
                    HAVING SUM(CASE WHEN dl.[MovementType] = 2 AND dl.[LocationId]     = @LocId THEN dl.[BaseQuantity] ELSE 0 END)
                         - SUM(CASE WHEN dl.[MovementType] = 1 AND dl.[FromLocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END)
                         + SUM(CASE WHEN dl.[MovementType] IN (3,4) AND dl.[LocationId]     = @LocId THEN dl.[BaseQuantity] ELSE 0 END)
                         - SUM(CASE WHEN dl.[MovementType] IN (3,4) AND dl.[FromLocationId] = @LocId THEN dl.[BaseQuantity] ELSE 0 END) <> 0;
                    """;
                balCmd.Parameters.AddWithValue("@CompanyId", companyId);
                balCmd.Parameters.AddWithValue("@LocId", locationId.Value);
                if (onlyUncounted) balCmd.Parameters.AddWithValue("@CountId", countId);
                await using var r = await balCmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    balances.Add((
                        r.GetInt32(0),
                        r.IsDBNull(1) ? null : r.GetInt32(1),
                        r.GetDecimal(2)));
                }
            }

            // 3) Her bakiyeyi tersine çeviren Adjust satırı: pozitif bakiye → çıkış, negatif → giriş.
            // unitId=null → baz birim (bakiye BaseQuantity toplamıdır).
            var writtenCount = 0;
            foreach (var b in balances)
            {
                await AppendAdjustLineAsync(conn, tx, documentId, b.ItemId, b.ConfigId, null,
                    Math.Abs(b.Balance), positiveVariance: b.Balance < 0, locationId, notes, ct);
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

    private async Task AppendAdjustLineAsync(
        SqlConnection conn, SqlTransaction tx, int documentId, int itemId, int? configId, int? unitId,
        decimal quantity, bool positiveVariance, int? locationId, string notes, CancellationToken ct)
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

        // BaseQuantity: ana birime normalize edilmiş miktar — TÜM diğer DocumentLine INSERT
        // yollarıyla (SqlDocumentRepository / SqlStockDocRepository) aynı StockUnitSql.BaseQtyExpr
        // deseni. İki çağrı yolu farklı davranır, ikisini de bu ifade doğru kapsar:
        //   • ApplyAsync (Yansıt): UnitId=null → çarpan 1 → BaseQuantity = Quantity (miktar zaten baz birimde).
        //   • ZeroBalancesCoreAsync (Sıfırla): UnitId dolu olabilir — bakiye ham Quantity'den birim
        //     kırılımıyla (GROUP BY UnitId) gelir → BaseQuantity = Quantity × birim çarpanı olmalı.
        // Sabit "@Quantity" yazmak bu yolda alternatif birimli (koli/metre) stokta yanlış baz
        // bakiye üretir; StockUnitSql.BaseQtyExpr her iki senaryoda da doğru değeri hesaplar.
        var baseQtyExpr = StockUnitSql.BaseQtyExpr(T("Items"), T("ItemUnits"), "@Quantity", "@ItemId", "@UnitId");
        await using var insCmd = conn.CreateCommand();
        insCmd.Transaction = tx;
        insCmd.CommandText = $"""
            INSERT INTO {lineTable}
                ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                 [CombinationId],[LocationId],[FromLocationId],[MovementType],[Notes])
            VALUES
                (@DocumentId,@LineNo,@ItemId,@UnitId,@Quantity,{baseQtyExpr},0,0,0,
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
        insCmd.Parameters.AddWithValue("@Notes", notes);
        await insCmd.ExecuteNonQueryAsync(ct);
    }
}

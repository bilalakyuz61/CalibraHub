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

    public async Task<byte?> GetStatusAsync(int documentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Status] FROM {T("InventoryCount")} WHERE [DocumentId] = @DocId;";
        cmd.Parameters.AddWithValue("@DocId", documentId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToByte(result);
    }

    public async Task<IReadOnlySet<int>> GetAppliedDocumentIdsAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [DocumentId] FROM {T("InventoryCount")} WHERE [Status] = 1;";
        var set = new HashSet<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) set.Add(r.GetInt32(0));
        return set;
    }

    public async Task<int> RevertAsync(int documentId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Optimistic-lock: yalnızca Applied ise Draft'a çevir. 0 satır → zaten taslak/yok.
            //    Belge tarihini de al (eksi bakiye kontrolü ileriye-dönük bu tarihten bakar).
            DateTime docDate;
            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE {T("InventoryCount")}
                    SET [Status] = 0, [Updated] = SYSUTCDATETIME()
                    WHERE [DocumentId] = @DocId AND [Status] = 1;
                    IF @@ROWCOUNT = 0 SELECT CAST(NULL AS DATETIME);
                    ELSE SELECT [DocumentDate] FROM {T("Document")} WHERE [Id] = @DocId;
                    """;
                upd.Parameters.AddWithValue("@DocId", documentId);
                var dObj = await upd.ExecuteScalarAsync(ct);
                if (dObj is null or DBNull)
                    throw new InvalidOperationException("Bu sayım yansıtılmamış veya bulunamadı.");
                docDate = Convert.ToDateTime(dObj);
            }

            // 2) Silinecek düzeltme satırlarının etkilediği (item, lokasyon) çiftlerini topla —
            //    DELETE'ten ÖNCE (sonra kayıt kalmaz). Stok ARTIRAN (giriş yönü) bir yansıtmayı
            //    geri almak bakiyeyi düşürür; bu depoda eksiye düşerse iptal engellenmeli.
            //    Not: Sayım düzeltme satırları LotId taşımaz / seri çözmez (AppendAdjustLineAsync)
            //    → lot/seri bakiyesi etkilenmez, yalnızca genel (item, lokasyon) bakiyesi kontrol edilir.
            var affected = new HashSet<(int ItemId, int LocationId)>();
            await using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = $"""
                    SELECT DISTINCT [ItemId], [LocationId], [FromLocationId]
                    FROM {T("DocumentLine")}
                    WHERE [DocumentId] = @DocId AND [MovementType] = 4 AND [ItemId] IS NOT NULL;
                    """;
                sel.Parameters.AddWithValue("@DocId", documentId);
                await using var r = await sel.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var itemId = r.GetInt32(0);
                    if (!r.IsDBNull(1)) affected.Add((itemId, r.GetInt32(1)));
                    if (!r.IsDBNull(2)) affected.Add((itemId, r.GetInt32(2)));
                }
            }

            // 3) Bu sayım fişinin ürettiği tüm stok hareketlerini sil (Yansıt farkları +
            //    İşlemler sekmesi sıfırlamaları — hepsi MovementType=4). Ham sayım kalemleri
            //    (InventoryCountLine) dokunulmaz → fiş temiz taslağa döner.
            int removed;
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {T("DocumentLine")} WHERE [DocumentId] = @DocId AND [MovementType] = 4;";
                del.Parameters.AddWithValue("@DocId", documentId);
                removed = await del.ExecuteNonQueryAsync(ct);
            }

            // 4) Eksi bakiye kontrolü (silme SONRASI güncel bakiye üzerinden). Şirket/lokasyon
            //    parametrelerine saygılı — kontrol kapalıysa no-op. Negatifse NegativeBalanceException
            //    fırlatır → tx geri alınır → iptal engellenir, kullanıcı net mesaj alır.
            foreach (var (itemId, locationId) in affected)
                await NegativeBalanceGuard.EnsureAsync(conn, tx, _schema, companyId, itemId, locationId, docDate.Date, ct);

            await tx.CommitAsync(ct);
            return removed;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

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

            // 2) Taslak satırları oku — satır kendi lokasyonunu taşıyabilir (NULL = header).
            //    İzlenebilirlik: lot-takipli kalemde LotBreakdown (JSON) ile per-lot düzeltme.
            var lines = new List<(int ItemId, int? ConfigId, int? UnitId, decimal CountedQty, int? LineLocationId, string? TrackingType, string? LotBreakdown)>();
            await using (var selCmd = conn.CreateCommand())
            {
                selCmd.Transaction = tx;
                selCmd.CommandText = $"""
                    SELECT l.[ItemId], l.[ConfigId], l.[UnitId], l.[CountedQty], l.[LocationId],
                           ISNULL(i.[TrackingType], 'None') AS TrackingType, l.[LotBreakdown]
                    FROM {T("InventoryCountLine")} l
                    LEFT JOIN {T("Items")} i ON i.[Id] = l.[ItemId]
                    WHERE l.[InventoryCountId] = @CountId;
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
                        r.IsDBNull(4) ? null : r.GetInt32(4),
                        r.IsDBNull(5) ? "None" : r.GetString(5),
                        r.IsDBNull(6) ? null : r.GetString(6)));
                }
            }

            var writtenCount = 0;
            foreach (var line in lines)
            {
                var effectiveLocation = line.LineLocationId ?? locationId;

                // ── Lot-takipli kalem → per-lot varyans (lot bazında bakiye düzeltmesi) ──
                if (string.Equals(line.TrackingType, "Lot", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(line.LotBreakdown))
                {
                    writtenCount += await ApplyLotVarianceAsync(conn, tx, documentId, companyId,
                        line.ItemId, line.ConfigId, line.UnitId, effectiveLocation, line.LotBreakdown!, ct);
                    continue;
                }

                // ── Diğer (izlenebilirsiz + seri) → toplam-miktar varyansı ──
                // NOT: seri-takipli kalemde seri BAKIYE uzlaştırması (bulundu/eksik) ayrı iş
                // (serilerde lokasyon kolonu yok); şimdilik toplam miktar düzeltmesi yazılır.
                var liveBalance = await GetLiveBalanceAsync(conn, tx, companyId, line.ItemId, effectiveLocation, ct);
                var factor = await ResolveBaseFactorAsync(conn, tx, line.ItemId, line.UnitId, ct);
                var countedBase = line.CountedQty * factor;
                var variance = countedBase - liveBalance;
                if (variance == 0) continue;

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
        decimal quantity, bool positiveVariance, int? locationId, string notes, CancellationToken ct,
        int? lotId = null, string? lotNo = null)
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
                 [CombinationId],[LocationId],[FromLocationId],[MovementType],[LotId],[LotNo],[Notes])
            VALUES
                (@DocumentId,@LineNo,@ItemId,@UnitId,@Quantity,{baseQtyExpr},0,0,0,
                 @CombinationId,@ToLoc,@FromLoc,4,@LotId,@LotNo,@Notes);
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
        insCmd.Parameters.AddWithValue("@LotId", (object?)lotId ?? DBNull.Value);
        insCmd.Parameters.AddWithValue("@LotNo", (object?)lotNo ?? DBNull.Value);
        insCmd.Parameters.AddWithValue("@Notes", notes);
        await insCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Lot-takipli sayım satırının per-lot bakiye düzeltmesi. Sayılan her lot için
    /// (sayılan − o lotun bakiyesi) varyansı; ayrıca depoda bakiyesi olup sayımda olmayan
    /// lotlar sıfırlanır (eksik). Her lot için LotId'li Adjust(4) hareketi yazılır.
    /// </summary>
    private async Task<int> ApplyLotVarianceAsync(
        SqlConnection conn, SqlTransaction tx, int documentId, int companyId,
        int itemId, int? configId, int? unitId, int? location, string lotBreakdownJson, CancellationToken ct)
    {
        List<CalibraHub.Application.Contracts.StockLotBreakdownItem> breakdown;
        try { breakdown = System.Text.Json.JsonSerializer.Deserialize<List<CalibraHub.Application.Contracts.StockLotBreakdownItem>>(lotBreakdownJson) ?? new(); }
        catch { breakdown = new(); }
        if (breakdown.Count == 0) return 0;

        var factor = await ResolveBaseFactorAsync(conn, tx, itemId, unitId, ct);
        // Sayılan: lotNo → baz-birim toplam
        var counted = breakdown
            .Where(b => !string.IsNullOrWhiteSpace(b.LotNo) && b.Qty > 0)
            .GroupBy(b => b.LotNo.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty) * factor, StringComparer.OrdinalIgnoreCase);

        var systemLots = await GetLotBalancesAsync(conn, tx, companyId, itemId, location, ct);
        var written = 0;
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in counted)
        {
            handled.Add(kv.Key);
            var sys = systemLots.FirstOrDefault(s => string.Equals(s.LotNo, kv.Key, StringComparison.OrdinalIgnoreCase));
            var sysBal = sys.LotId > 0 ? sys.Balance : 0m;
            var variance = kv.Value - sysBal;
            if (variance == 0) continue;
            var lotId = sys.LotId > 0 ? sys.LotId : await ResolveOrCreateLotAsync(conn, tx, itemId, kv.Key, ct);
            await AppendAdjustLineAsync(conn, tx, documentId, itemId, configId, null,
                Math.Abs(variance), positiveVariance: variance > 0, location, $"Sayım lot farkı ({kv.Key}) — Yansıt", ct, lotId, kv.Key);
            written++;
        }
        // Sayılmayan lotlar → sıfırla (variance = 0 - balance = -balance)
        foreach (var s in systemLots)
        {
            if (s.Balance == 0 || handled.Contains(s.LotNo)) continue;
            await AppendAdjustLineAsync(conn, tx, documentId, itemId, configId, null,
                Math.Abs(s.Balance), positiveVariance: s.Balance < 0, location, $"Sayım lot eksiği ({s.LotNo}) — Yansıt", ct, s.LotId, s.LotNo);
            written++;
        }
        return written;
    }

    /// <summary>Depodaki (location) item lot bakiyeleri — (LotId, LotNo, baz-birim bakiye).</summary>
    private async Task<List<(int LotId, string LotNo, decimal Balance)>> GetLotBalancesAsync(
        SqlConnection conn, SqlTransaction tx, int companyId, int itemId, int? location, CancellationToken ct)
    {
        var list = new List<(int, string, decimal)>();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT dl.[LotId], lot.[LotNo],
                ISNULL(SUM(CASE WHEN dl.[MovementType] IN (2,3,4) AND dl.[LocationId]     = @Loc THEN dl.[BaseQuantity] ELSE 0 END),0)
              - ISNULL(SUM(CASE WHEN dl.[MovementType] IN (1,3,4) AND dl.[FromLocationId] = @Loc THEN dl.[BaseQuantity] ELSE 0 END),0) AS Bal
            FROM {T("DocumentLine")} dl
            INNER JOIN {T("Document")} d ON d.[Id] = dl.[DocumentId]
            INNER JOIN {T("Lot")} lot ON lot.[Id] = dl.[LotId]
            WHERE dl.[ItemId] = @ItemId AND d.[CompanyId] = @Cid AND d.[IsActive] = 1
              AND dl.[LotId] IS NOT NULL AND (dl.[LocationId] = @Loc OR dl.[FromLocationId] = @Loc)
            GROUP BY dl.[LotId], lot.[LotNo];
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@Cid", companyId);
        cmd.Parameters.AddWithValue("@Loc", (object?)location ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? 0m : r.GetDecimal(2)));
        return list;
    }

    /// <summary>Lot bul (item+lotNo), yoksa oluştur — sayımda bulunan yeni lot (fazla) için.</summary>
    private async Task<int> ResolveOrCreateLotAsync(SqlConnection conn, SqlTransaction tx, int itemId, string lotNo, CancellationToken ct)
    {
        await using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = $"SELECT TOP 1 [Id] FROM {T("Lot")} WHERE [ItemId] = @It AND [LotNo] = @Lot;";
            find.Parameters.AddWithValue("@It", itemId);
            find.Parameters.AddWithValue("@Lot", lotNo);
            if (await find.ExecuteScalarAsync(ct) is int id) return id;
        }
        await using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = $"INSERT INTO {T("Lot")} ([ItemId],[LotNo]) VALUES (@It,@Lot); SELECT CAST(SCOPE_IDENTITY() AS INT);";
        ins.Parameters.AddWithValue("@It", itemId);
        ins.Parameters.AddWithValue("@Lot", lotNo);
        return Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
    }
}

using CalibraHub.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Karşılama defteri (DocumentLineFulfillment) üzerindeki paylaşılan SQL işlemleri.
///
/// Neden ayrı bir yardımcı: defteri İKİ repo değiştirir — <see cref="SqlDocumentRepository"/>
/// (satın alma teklif/sipariş/talep) ve <see cref="SqlStockDocRepository"/> (transfer/ambar
/// çıkış). İkisi de AYNI fiziksel dbo.Document tablosuna yazar (2026-07-02 konsolidasyonu),
/// ama ayrı silme kapılarından geçer. İkisi de ters çevirmeyi KENDİ transaction'ı içinde
/// yapmak zorunda: "belge silindi ama karşılama geri alınmadı" yarım durumu tam olarak
/// düzeltmeye çalıştığımız hatanın kendisidir.
/// <c>NegativeBalanceGuard</c> ile aynı desen (conn + tx + schema).
///
/// Toplamlar (DocumentLine.FulfilledFromStock / FulfilledByPurchase) TÜRETİLMİŞ değerdir:
/// defterdeki aktif satırların toplamı. Doğruluk kaynağı defterdir; kolonlar okuma kolaylığı
/// için tutulan önbellektir ve her değişiklikten sonra yeniden hesaplanır.
/// </summary>
internal static class FulfillmentLedger
{
    /// <summary>
    /// Verilen ihtiyaç satırlarının toplamlarını DEFTERDEN yeniden hesaplayan UPDATE üretir.
    ///
    /// LEFT JOIN kritik: defterde hiç aktif kaydı kalmamış satıra 0 yazılabilmesi için.
    /// INNER JOIN olsaydı ters çevirmeden sonra kayıtsız kalan satır güncellenmez, eski toplam
    /// donar ve satır sonsuza dek "karşılanmış" görünürdü.
    /// </summary>
    /// <param name="preserveClosed">
    /// true → FulfillmentStatus = 3 (kullanıcının kapattığı satır) 3 olarak korunur. Ters
    /// çevirme yolunda kullanılır: kapatma ayrı bir karardır, karşılamanın geri alınması onu
    /// geçersiz kılmaz.
    /// false → durum tamamen yeniden hesaplanır, kapatma silinir (satır yeniden açılır). Yeni
    /// karşılama yolunda kullanılır; CloseLineFulfillmentAsync sözleşmesiyle kasıtlı olarak aynı.
    /// </param>
    public static string BuildRecalcSql(
        string ledgerTable,
        string lineTable,
        IReadOnlyCollection<int> lineIds,
        bool preserveClosed,
        out List<SqlParameter> parameters)
    {
        parameters = new List<SqlParameter>(lineIds.Count);
        var names = new List<string>(lineIds.Count);
        var i = 0;
        foreach (var id in lineIds)
        {
            var p = "@RL" + i++;
            names.Add(p);
            parameters.Add(new SqlParameter(p, id));
        }
        var inList = string.Join(", ", names);

        // Tip → kova eşlemesi tek doğruluk kaynağından gelir (FulfillmentSourceKinds);
        // SQL'e elle sayı gömülmez, yeni tür eklendiğinde burası kendiliğinden doğru kalır.
        var stockTypes    = string.Join(", ", FulfillmentSourceKinds.StockSide);
        var purchaseTypes = string.Join(", ", FulfillmentSourceKinds.PurchaseSide);

        var closedGuard = preserveClosed
            ? "WHEN dl.[FulfillmentStatus] = 3 THEN 3"
            : "";

        return $"""
            WITH agg AS (
                SELECT [RequestLineId],
                       SUM(CASE WHEN [FulfillmentType] IN ({stockTypes})    THEN [Quantity] ELSE 0 END) AS FromStock,
                       SUM(CASE WHEN [FulfillmentType] IN ({purchaseTypes}) THEN [Quantity] ELSE 0 END) AS ByPurchase
                  FROM {ledgerTable}
                 WHERE [IsActive] = 1 AND [RequestLineId] IN ({inList})
                 GROUP BY [RequestLineId]
            )
            UPDATE dl
               SET [FulfilledFromStock]  = ISNULL(agg.FromStock, 0),
                   [FulfilledByPurchase] = ISNULL(agg.ByPurchase, 0),
                   [FulfillmentStatus]   = CASE
                       {closedGuard}
                       WHEN dl.[Quantity] > 0
                            AND (ISNULL(agg.FromStock, 0) + ISNULL(agg.ByPurchase, 0)) >= dl.[Quantity] THEN 2
                       WHEN (ISNULL(agg.FromStock, 0) + ISNULL(agg.ByPurchase, 0)) > 0              THEN 1
                       ELSE 0
                   END
              FROM {lineTable} dl
              LEFT JOIN agg ON agg.[RequestLineId] = dl.[Id]
             WHERE dl.[Id] IN ({inList});
            """;
    }

    /// <summary>
    /// Bir karşılama belgesinin defterdeki katkısını pasifleştirir ve etkilenen ihtiyaç
    /// satırlarının toplamlarını yeniden hesaplar — ÇAĞIRANIN transaction'ı içinde.
    ///
    /// Ters çevirme YALNIZCA <paramref name="refDocId"/> ile yapılır, karşılama türüne göre
    /// filtrelenmez. Gerekçe: 2026-07-02'de stock_doc/stock_doc_line emekliye ayrıldı; transfer
    /// ve ambar çıkış fişleri de artık dbo.Document tablosunda tutuluyor (bkz.
    /// SqlStockDocRepository sınıf başlığı). Tek IDENTITY Id uzayı olduğu için bir belge Id'si
    /// tek bir belgeye aittir — çakışma yoktur.
    ///
    /// Tür filtresi eklemek AKTİF ZARARLI olurdu: filtre "hangi tablo"yu değil "hangi silme
    /// kapısından geçildiği"ni bölerdi. Kapı ile karşılama türü eşleşmezse (örn. bir depo fişi
    /// Id'si Sales silme endpoint'ine düşerse) ters çevirme SESSİZCE atlanır ve ihtiyaç satırı
    /// sonsuza dek karşılanmış kalırdı — tam olarak bu defterin düzelttiği hata.
    /// </summary>
    /// <returns>Toplamları geri alınan ihtiyaç satırı sayısı (defterde kayıt yoksa 0).</returns>
    public static async Task<int> ReverseByDocumentAsync(
        SqlConnection conn,
        SqlTransaction tx,
        string schema,
        int refDocId,
        int? userId,
        CancellationToken ct)
    {
        if (refDocId <= 0) return 0;

        var ledgerTable = $"[{schema}].[DocumentLineFulfillment]";
        var lineTable   = $"[{schema}].[DocumentLine]";

        // 1) Etkilenen ihtiyaç satırlarını pasifleştirmeden ÖNCE tespit et (sonra bulunamazlar)
        var lineIds = new List<int>();
        await using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = $"""
                SELECT DISTINCT [RequestLineId]
                  FROM {ledgerTable}
                 WHERE [IsActive] = 1
                   AND [RefDocId] = @RefDocId;
                """;
            find.Parameters.Add(new SqlParameter("@RefDocId", refDocId));
            await using var r = await find.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) lineIds.Add(r.GetInt32(0));
        }

        if (lineIds.Count == 0) return 0;

        // 2) Bu belgenin katkısını pasifleştir
        await using (var deactivate = conn.CreateCommand())
        {
            deactivate.Transaction = tx;
            deactivate.CommandText = $"""
                UPDATE {ledgerTable}
                   SET [IsActive]    = 0,
                       [UpdatedById] = @User,
                       [Updated]     = SYSUTCDATETIME()
                 WHERE [IsActive] = 1
                   AND [RefDocId] = @RefDocId;
                """;
            deactivate.Parameters.Add(new SqlParameter("@RefDocId", refDocId));
            deactivate.Parameters.Add(new SqlParameter("@User", (object?)userId ?? DBNull.Value));
            await deactivate.ExecuteNonQueryAsync(ct);
        }

        // 3) Toplamları yeniden hesapla — kullanıcının kapattığı satırlar kapalı kalır
        await using (var recalc = conn.CreateCommand())
        {
            recalc.Transaction = tx;
            recalc.CommandText = BuildRecalcSql(ledgerTable, lineTable, lineIds, preserveClosed: true, out var ps);
            foreach (var p in ps) recalc.Parameters.Add(p);
            await recalc.ExecuteNonQueryAsync(ct);
        }

        return lineIds.Count;
    }

    /// <summary>
    /// 2026-07-20 (Madde 1) — Bir belgenin (<paramref name="refDocId"/>) defterdeki AKTİF
    /// katkısını malzeme (ItemId) bazında toplar: RequestLineId → DocumentLine.ItemId join'i
    /// ile "bu belge hangi malzemeden ne kadar İhtiyaç karşılıyor" sorusuna cevap verir —
    /// karşılayan belge düzenlenirken miktar tabanı (floor) kontrolü için kullanılır.
    /// <paramref name="tx"/> null verilebilir (bağımsız salt-okunur çağrı); dolu verilirse o
    /// transaction içinde okunur (SqlStockDocRepository.SaveDirectDocAsync mid-transaction).
    /// </summary>
    public static async Task<Dictionary<int, (string? MaterialCode, string? MaterialName, decimal FloorQty)>>
        GetContributionByItemAsync(
            SqlConnection conn, SqlTransaction? tx, string schema, int refDocId, CancellationToken ct)
    {
        var result = new Dictionary<int, (string? MaterialCode, string? MaterialName, decimal FloorQty)>();
        if (refDocId <= 0) return result;

        var ledgerTable = $"[{schema}].[DocumentLineFulfillment]";
        var lineTable   = $"[{schema}].[DocumentLine]";
        var itemsTable  = $"[{schema}].[Items]";

        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        // Items tablosunun fiziksel kolonları [Code]/[Name] — [MaterialCode]/[MaterialName]
        // yalnızca diğer sorgularda alias'tır, gerçek kolon değil (bkz. SqlDocumentRepository
        // ana kalem sorgusu: i.[Code] AS [material_code]). Fiziksel adı kullan, aksi halde
        // "Invalid column name" ile HER belge düzenleme-kaydı patlar.
        cmd.CommandText = $"""
            SELECT rl.[ItemId], it.[Code], it.[Name], SUM(f.[Quantity]) AS FloorQty
              FROM {ledgerTable} f
              INNER JOIN {lineTable}  rl ON rl.[Id] = f.[RequestLineId]
              INNER JOIN {itemsTable} it ON it.[Id] = rl.[ItemId]
             WHERE f.[IsActive] = 1 AND f.[RefDocId] = @RefDocId
             GROUP BY rl.[ItemId], it.[Code], it.[Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@RefDocId", refDocId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var itemId = r.GetInt32(0);
            var code   = r.IsDBNull(1) ? null : r.GetString(1);
            var name   = r.IsDBNull(2) ? null : r.GetString(2);
            var qty    = r.GetDecimal(3);
            result[itemId] = (code, name, qty);
        }
        return result;
    }

    /// <summary>
    /// 2026-07-20 (Madde 3) — Bir belgenin (<paramref name="refDocId"/>) defterde şu an AKTİF
    /// katkısı olan İhtiyaç Kaydı satır Id'lerini (RequestLineId, distinct) döner. Silme/ters
    /// çevirme ÖNCESİ audit "eski durum" snapshot'ı almak için — salt-okunur ön-kontrol,
    /// <see cref="ReverseByDocumentAsync"/>'in davranışına dokunmaz (aynı WHERE koşulu, adım 1
    /// ile birebir aynı sorgu — kasıtlı olarak ayrı tutuldu, ReverseByDocumentAsync'in
    /// transaction/mutasyon akışı bu metoda bağımlı değil).
    /// </summary>
    public static async Task<List<int>> GetAffectedLineIdsAsync(
        SqlConnection conn, SqlTransaction? tx, string schema, int refDocId, CancellationToken ct)
    {
        var lineIds = new List<int>();
        if (refDocId <= 0) return lineIds;

        var ledgerTable = $"[{schema}].[DocumentLineFulfillment]";
        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT DISTINCT [RequestLineId]
              FROM {ledgerTable}
             WHERE [IsActive] = 1
               AND [RefDocId] = @RefDocId;
            """;
        cmd.Parameters.Add(new SqlParameter("@RefDocId", refDocId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lineIds.Add(r.GetInt32(0));
        return lineIds;
    }

    /// <summary>
    /// 2026-07-20 (Madde 2) — Karşılama defterine kayıt yazar ve etkilenen İhtiyaç Kaydı
    /// satırlarının toplamlarını DEFTERDEN yeniden hesaplar — ÇAĞIRANIN AÇIK transaction'ı
    /// içinde. Belge kaydıyla AYNI transaction'da çağrılmak üzere tasarlandı:
    /// SqlDocumentRepository.SaveLinesAsync ve SqlStockDocRepository.SaveDirectDocAsync
    /// buradan çağırır — böylece "belge commit oldu, defter yazımı ayrı/başarısız" yarım
    /// durumu oluşamaz (ikisi ya birlikte commit olur ya da birlikte rollback edilir).
    ///
    /// <paramref name="entries"/> <see cref="PendingFulfillmentEntry"/> kullanır (RefDocId
    /// TAŞIMAZ) — belge Id'si INSERT anında elde olduğu için (yeni belgede) çağıran henüz
    /// bilmeyebilir; tek doğruluk kaynağı bu metodun <paramref name="refDocId"/> parametresidir.
    /// Mevcut (transaction dışı) <see cref="AddFulfillmentEntriesAsync"/> — entry başına
    /// RefDocId taşıyan eski <see cref="FulfillmentEntry"/> sözleşmesini korur, buradan
    /// ÇAĞRILMAZ (kasıtlı — davranışı bu değişiklikten etkilenmesin).
    /// </summary>
    public static async Task InsertEntriesAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int refDocId,
        IReadOnlyCollection<PendingFulfillmentEntry> entries, int? userId, CancellationToken ct)
    {
        if (entries is null || entries.Count == 0 || refDocId <= 0) return;

        var ledgerTable = $"[{schema}].[DocumentLineFulfillment]";
        var lineTable   = $"[{schema}].[DocumentLine]";
        var lineIds = entries.Select(e => e.RequestLineId).Distinct().ToList();

        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            var values = new List<string>(entries.Count);
            var i = 0;
            foreach (var e in entries)
            {
                values.Add($"(@Q{i}, @K{i}, @D{i}, @V{i}, @L{i}, @N{i}, 1, @User, SYSUTCDATETIME())");
                insert.Parameters.Add(new SqlParameter($"@Q{i}", e.RequestLineId));
                insert.Parameters.Add(new SqlParameter($"@K{i}", (byte)e.Kind));
                insert.Parameters.Add(new SqlParameter($"@D{i}", refDocId));
                insert.Parameters.Add(new SqlParameter($"@V{i}", e.Quantity));
                insert.Parameters.Add(new SqlParameter($"@L{i}", (object?)e.RefDocLineId ?? DBNull.Value));
                insert.Parameters.Add(new SqlParameter($"@N{i}", (object?)e.Notes ?? DBNull.Value));
                i++;
            }
            insert.Parameters.Add(new SqlParameter("@User", (object?)userId ?? DBNull.Value));
            insert.CommandText = $"""
                INSERT INTO {ledgerTable}
                    ([RequestLineId], [FulfillmentType], [RefDocId], [Quantity], [RefDocLineId], [Notes], [IsActive], [CreatedById], [Created])
                VALUES {string.Join(",\n           ", values)};
                """;
            await insert.ExecuteNonQueryAsync(ct);
        }

        await using (var recalc = conn.CreateCommand())
        {
            recalc.Transaction = tx;
            recalc.CommandText = BuildRecalcSql(ledgerTable, lineTable, lineIds, preserveClosed: false, out var ps);
            foreach (var p in ps) recalc.Parameters.Add(p);
            await recalc.ExecuteNonQueryAsync(ct);
        }
    }
}

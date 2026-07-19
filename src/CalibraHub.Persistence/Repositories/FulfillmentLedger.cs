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
}

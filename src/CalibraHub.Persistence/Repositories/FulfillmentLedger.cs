using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

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
    /// <param name="lineLinks">
    /// FAZ 1b-ii (2026-07-20, "C: karşılama" dual-write) — opsiyonel, best-effort.
    /// Dolu verilirse, defter ters çevirme BAŞARIYLA TAMAMLANDIKTAN SONRA
    /// <c>DocumentLineLink</c>'teki aynı belgeye (TargetDocId=refDocId) ait aktif karşılama
    /// link'leri de pasifleştirilir — kendi try/catch'i içinde, asla fırlatmaz (bkz.
    /// <c>WorkOrderService</c>'teki B mekanizmasıyla birebir aynı defensive desen).
    /// NULL geçilirse (mevcut çağıranların hepsi bugün NULL geçer) davranış Faz 0/1a ile
    /// birebir aynı kalır.
    /// </param>
    /// <param name="logger">Link reverse hatası olursa loglanacak logger (opsiyonel).</param>
    /// <returns>Toplamları geri alınan ihtiyaç satırı sayısı (defterde kayıt yoksa 0).</returns>
    public static async Task<int> ReverseByDocumentAsync(
        SqlConnection conn,
        SqlTransaction tx,
        string schema,
        int refDocId,
        int? userId,
        CancellationToken ct,
        IDocumentLineLinkRepository? lineLinks = null,
        ILogger? logger = null)
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

        // 4) DocumentLineLink dual-write reverse (FAZ 1b-ii, 2026-07-20) — defter ters çevirme
        // TAMAMLANDIKTAN SONRA, best-effort. Bu metodun kendi transaction'ını (conn/tx) KULLANMAZ —
        // ReverseByTargetAsync kendi bağlantısını açar (bkz. SqlDocumentLineLinkRepository), tasarım
        // §8 "aynı transaction'a sokma" kararıyla birebir. linkType=null: bu refDocId (Transfer/
        // PurchaseQuote/PurchaseOrder/PurchaseDemand/StockIssue belgesi) hiçbir zaman BAŞKA bir
        // LinkType'ın TargetDocId'si olamaz — WorkOrderAlloc(20)'nin TargetDocId'si yalnız
        // WorkOrder'ın KENDİ Document satırıdır (ayrı bir INSERT ile üretilir, Document.Id tekil
        // IDENTITY olduğundan aynı Id iki farklı belgeye ait olamaz); Derivation(10) henüz hiçbir
        // yazma yolunda üretilmiyor (Faz "A" kapsam dışı). Dolayısıyla bu refDocId için
        // TargetDocId eşleşen tüm aktif link satırları HER ZAMAN karşılama (1-7) türündendir.
        if (lineLinks is not null)
        {
            try
            {
                await lineLinks.ReverseByTargetAsync(refDocId, userId, linkType: null, ct);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "[FulfillmentLedger] DocumentLineLink reverse edilemedi: RefDocId {RefDocId}",
                    refDocId);
            }
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
    /// ÇAĞRILMAZ (kasıtlı — davranışı bu değişiklikten etkilenmesin). NOT (2026-07-20, FAZ
    /// 1b-ii): dolayısıyla <c>AddFulfillmentEntriesAsync</c> üzerinden yazılan karşılama
    /// girdileri bu metodun altındaki DocumentLineLink dual-write'ına DA girmez — kapsam
    /// dışı bırakıldı (bkz. görev raporu).
    /// </summary>
    /// <param name="lineLinks">
    /// FAZ 1b-ii (2026-07-20, "C: karşılama" dual-write) — opsiyonel, best-effort. Dolu
    /// verilirse, defter INSERT+recalc BAŞARIYLA TAMAMLANDIKTAN SONRA her <paramref
    /// name="entries"/> öğesi <c>DocumentLineLink</c>'e <c>LinkType</c> 1-7 (FulfillmentType
    /// ile sayısal olarak aynı) olarak paralel yazılır — bkz. <see cref="TryLinkFulfillmentEntriesAsync"/>.
    /// </param>
    /// <param name="logger">Link insert hatası olursa loglanacak logger (opsiyonel).</param>
    public static async Task InsertEntriesAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int refDocId,
        IReadOnlyCollection<PendingFulfillmentEntry> entries, int? userId, CancellationToken ct,
        IDocumentLineLinkRepository? lineLinks = null, ILogger? logger = null)
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

        // DocumentLineLink dual-write (FAZ 1b-ii, 2026-07-20, "C: karşılama" mekanizması) —
        // defter INSERT+recalc TAMAMLANDIKTAN SONRA, best-effort. WorkOrderService.
        // TryLinkWorkOrderAllocAsync (B mekanizması) ile birebir aynı defensive desen.
        await TryLinkFulfillmentEntriesAsync(conn, tx, schema, refDocId, entries, userId, lineLinks, logger, ct);
    }

    /// <summary>
    /// FAZ 1b-ii (2026-07-20) — <see cref="InsertEntriesAsync"/>'in yazdığı her
    /// <see cref="PendingFulfillmentEntry"/>'yi <c>DocumentLineLink</c>'e paralel yazar
    /// (eşleme: tasarım §5 "C → Link" satırı, <c>CalibraDatabaseInitializer</c>'ın backfill-C
    /// bloğuyla — <c>rdl.[DocumentId]</c> join'i — birebir aynı). <paramref name="lineLinks"/>
    /// NULL ise no-op. Tüm gövde TEK try/catch içindedir: <c>SourceDocId</c> çözümü
    /// (ambient <paramref name="conn"/>/<paramref name="tx"/> üzerinden OKUMA — DocumentLine
    /// tablosuna, recalc'ın az önce güncellediği AYNI satırlara, AYNI oturumdan; çapraz bağlantı
    /// kilit beklemesi/deadlock riski YOK) dahil hiçbir adım ana karşılama işlemini etkilemez;
    /// asıl link YAZIMI ise (<see cref="IDocumentLineLinkRepository.InsertOrAccumulateAsync"/>)
    /// kendi bağlantısını açar — ana <paramref name="tx"/>'e GİRMEZ (tasarım §8).
    /// </summary>
    private static async Task TryLinkFulfillmentEntriesAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int refDocId,
        IReadOnlyCollection<PendingFulfillmentEntry> entries, int? userId,
        IDocumentLineLinkRepository? lineLinks, ILogger? logger, CancellationToken ct)
    {
        if (lineLinks is null) return;
        try
        {
            // SourceDocId: her RequestLineId'nin bağlı olduğu İhtiyaç Kaydı belgesi.
            // CalibraDatabaseInitializer backfill-C: "rdl.[DocumentId]" (DocumentLine rdl ON
            // rdl.Id = f.RequestLineId) ile birebir aynı çözümleme, tek farkla: burada N
            // RequestLineId için TEK toplu sorgu (N+1 önleme).
            var lineTable = $"[{schema}].[DocumentLine]";
            var lineIds = entries.Select(e => e.RequestLineId).Distinct().ToList();
            var docIdByLine = new Dictionary<int, int>(lineIds.Count);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                var names = new List<string>(lineIds.Count);
                for (var i = 0; i < lineIds.Count; i++)
                {
                    var p = "@SL" + i;
                    names.Add(p);
                    cmd.Parameters.Add(new SqlParameter(p, lineIds[i]));
                }
                cmd.CommandText = $"""
                    SELECT [Id], [DocumentId] FROM {lineTable} WHERE [Id] IN ({string.Join(", ", names)});
                    """;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    docIdByLine[r.GetInt32(0)] = r.GetInt32(1);
            }

            foreach (var e in entries)
            {
                // RequestLineId'nin DocumentId'si bulunamadıysa (olağan akışta olmaması gerekir —
                // FK ile az önce referans verildi) bu TEK entry'nin link yazımı atlanır; asıl
                // karşılama kaydı zaten defterde yazılı, yalnız aynadaki bu satır eksik kalır.
                if (!docIdByLine.TryGetValue(e.RequestLineId, out var sourceDocId)) continue;

                await lineLinks.InsertOrAccumulateAsync(new DocumentLineLinkEntry(
                    (LinkType)e.Kind,
                    SourceLineId: e.RequestLineId,
                    SourceDocId: sourceDocId,
                    TargetLineId: e.RefDocLineId,
                    TargetDocId: refDocId,
                    TargetWorkOrderId: null,
                    Quantity: e.Quantity,
                    Notes: e.Notes), userId, ct);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "[FulfillmentLedger] DocumentLineLink insert edilemedi: RefDocId {RefDocId}, {Count} entry",
                refDocId, entries.Count);
        }
    }
}

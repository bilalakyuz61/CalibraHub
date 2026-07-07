using CalibraHub.Application.Constants;
using CalibraHub.Domain.Exceptions;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Eksi bakiye kontrolü — stok-azaltıcı hareket yazan transaction İÇİNDE çağrılır (yeni
/// satırlar zaten INSERT edildiğinden hesaba dahil olur). Katmanlı çözümleme:
///   1) Şirket ana anahtarı (STOCK / NEG_BALANCE_CONTROL) kapalı → hiç kontrol yok (no-op).
///   2) Açık → Location.AllowNegativeBalance (null ise STOCK / NEG_BALANCE_ALLOW_DEFAULT).
///   3) İzin yoksa: hareket tarihinden İTİBAREN ileriye dönük en düşük bakiye negatifse
///      NegativeBalanceException fırlatır (tarih bazlı zincirleme kontrol → tx geri alınır).
///
/// İşaret konvansiyonu GetLiveBalance ile birebir aynı (BaseQuantity ana birimde):
///   +  MovementType IN (2,3,4) AND LocationId     = L   (giriş / transfer-hedef / düzeltme-hedef)
///   -  MovementType IN (1,3,4) AND FromLocationId = L   (çıkış / transfer-kaynak / düzeltme-kaynak)
/// </summary>
internal static class NegativeBalanceGuard
{
    /// <summary>Azaltıcı hareketin geçerli olup olmadığını doğrular; değilse fırlatır.</summary>
    public static async Task EnsureAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int companyId,
        int itemId, int locationId, DateTime fromDate, CancellationToken ct)
    {
        if (itemId <= 0 || locationId <= 0) return;

        // 1) Şirket ana anahtarı
        if (!await GetBoolParamAsync(conn, tx, schema, companyId, StockParameters.NegBalanceControlKey, ct))
            return;

        // 2) Depo izni (null → şirket varsayılanı)
        bool? locAllow = await GetLocationAllowAsync(conn, tx, schema, locationId, ct);
        bool allowed = locAllow ?? await GetBoolParamAsync(conn, tx, schema, companyId, StockParameters.NegBalanceAllowDefaultKey, ct);
        if (allowed) return;

        // 3) İleriye dönük en düşük FİZİKSEL bakiye
        decimal minForward = await MinForwardBalanceAsync(conn, tx, schema, companyId, itemId, locationId, fromDate.Date, ct);

        // 3b) Satış siparişi rezervasyonu açıksa: kullanılabilir = fiziksel − rezerve.
        //     Açık (teslim edilmemiş) satış siparişi miktarları o depoda bakiyeyi bağlar.
        if (await GetBoolParamAsync(conn, tx, schema, companyId, StockParameters.SalesOrderAffectsStockKey, ct))
            minForward -= await OpenSalesReservationAsync(conn, tx, schema, companyId, itemId, locationId, ct);

        if (minForward < 0m)
        {
            var (itemLabel, locLabel) = await FetchLabelsAsync(conn, tx, schema, itemId, locationId, ct);
            throw new NegativeBalanceException(itemLabel, locLabel, -minForward);
        }
    }

    /// <summary>Açık (Status ≠ İptal/Red, teslim edilmemiş) satış siparişi satırlarının bu depoya
    /// rezerve ettiği toplam ana-birim miktar. Depo = satır LocationId, yoksa belge LocationId.</summary>
    private static async Task<decimal> OpenSalesReservationAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int companyId, int itemId, int locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(SUM(dl.[BaseQuantity] - dl.[DeliveredQuantity]), 0)
            FROM [{schema}].[DocumentLine] dl
            INNER JOIN [{schema}].[Document] doc ON doc.[Id] = dl.[DocumentId]
            INNER JOIN [{schema}].[DocumentType] dt ON dt.[Id] = doc.[DocumentTypeId]
            WHERE dt.[Code] = N'satis_siparisi'
              AND doc.[CompanyId] = @Cid AND doc.[IsActive] = 1
              AND doc.[Status] NOT IN (3, 5)          -- Rejected(3), Cancelled(5) hariç = açık sipariş
              AND dl.[ItemId] = @ItemId
              AND ISNULL(dl.[LocationId], doc.[LocationId]) = @L
              AND dl.[BaseQuantity] > dl.[DeliveredQuantity];
            """;
        cmd.Parameters.AddWithValue("@Cid", companyId);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@L", locationId);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? 0m : Convert.ToDecimal(r);
    }

    private static async Task<bool> GetBoolParamAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int companyId, string key, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT TOP 1 [ParamValue] FROM [{schema}].[CompanyParameter] WHERE [CompanyId]=@c AND [FormCode]=@f AND [ParamKey]=@k;";
        cmd.Parameters.AddWithValue("@c", companyId);
        cmd.Parameters.AddWithValue("@f", StockParameters.FormCode);
        cmd.Parameters.AddWithValue("@k", key);
        var v = await cmd.ExecuteScalarAsync(ct) as string;
        return v is not null && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
    }

    private static async Task<bool?> GetLocationAllowAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT [AllowNegativeBalance] FROM [{schema}].[Location] WHERE [Id]=@id;";
        cmd.Parameters.AddWithValue("@id", locationId);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? null : Convert.ToBoolean(r);
    }

    private static async Task<decimal> MinForwardBalanceAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int companyId, int itemId, int locationId, DateTime fromDate, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            WITH d AS (
                SELECT CONVERT(DATE, doc.[DocumentDate]) AS md,
                       SUM(CASE WHEN dl.[MovementType] IN (2,3,4) AND dl.[LocationId]     = @L THEN dl.[BaseQuantity]
                                WHEN dl.[MovementType] IN (1,3,4) AND dl.[FromLocationId] = @L THEN -dl.[BaseQuantity]
                                ELSE 0 END) AS dq
                FROM [{schema}].[DocumentLine] dl
                INNER JOIN [{schema}].[Document] doc ON doc.[Id] = dl.[DocumentId]
                WHERE dl.[ItemId] = @ItemId AND doc.[CompanyId] = @Cid AND doc.[IsActive] = 1
                  AND dl.[MovementType] IN (1,2,3,4)
                  AND (dl.[LocationId] = @L OR dl.[FromLocationId] = @L)
                GROUP BY CONVERT(DATE, doc.[DocumentDate])
            ),
            r AS (
                SELECT md, SUM(dq) OVER (ORDER BY md ROWS UNBOUNDED PRECEDING) AS bal FROM d
            )
            SELECT MIN(v) FROM (
                SELECT bal AS v FROM r WHERE md >= @FromDate
                UNION ALL
                SELECT ISNULL((SELECT SUM(dq) FROM d WHERE md <= @FromDate), 0)   -- @FromDate'e kadarki açılış bakiyesi
            ) x;
            """;
        cmd.Parameters.AddWithValue("@L", locationId);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@Cid", companyId);
        cmd.Parameters.AddWithValue("@FromDate", fromDate);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? 0m : Convert.ToDecimal(r);
    }

    private static async Task<(string item, string loc)> FetchLabelsAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int itemId, int locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT (SELECT TOP 1 ISNULL([Name], [Code]) FROM [{schema}].[Items] WHERE [Id]=@i) AS item_label,
                   (SELECT TOP 1 ISNULL([LocationName], [LocationCode]) FROM [{schema}].[Location] WHERE [Id]=@l) AS loc_label;
            """;
        cmd.Parameters.AddWithValue("@i", itemId);
        cmd.Parameters.AddWithValue("@l", locationId);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        string item = "#" + itemId, loc = "#" + locationId;
        if (await rd.ReadAsync(ct))
        {
            if (!rd.IsDBNull(0)) item = rd.GetString(0);
            if (!rd.IsDBNull(1)) loc = rd.GetString(1);
        }
        return (item, loc);
    }
}

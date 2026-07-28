using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Yükleme Planlama Merkezi + Stok Rezervasyonu — Faz 1 (2026-07-28). Bkz. <c>StockReservation</c>
/// entity XML doc'u + <c>ShipmentPlanningContracts.cs</c>. Available bakiye tanımı:
///   kullanılabilir = fiziksel_bakiye(item, location) − Σ StockReservation.BaseQuantity
///                     WHERE ItemId=@i AND LocationId=@l AND Status=Active AND IsActive=1
/// Fiziksel bakiye işaret konvansiyonu <c>NegativeBalanceGuard</c> ile BİREBİR AYNI
/// (MovementType 2/3/4 + LocationId → +, 1/3/4 + FromLocationId → −); tarih zincirlemesi
/// YOK (Faz 1 basit tut — güncel toplam bakiye), yalnız <c>NegativeBalanceGuard</c>'ın
/// SALES_ORDER_AFFECTS_STOCK (açık sipariş rezervasyonu) kavramına HİÇ dokunulmaz — o ayrı
/// bir mekanizmadır, bu modülün StockReservation tablosuyla karışmaz.
/// </summary>
public sealed class SqlStockReservationRepository : IStockReservationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlStockReservationRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    public async Task<IReadOnlyList<OpenOrderLineForReservationDto>> GetOpenOrderLinesAsync(
        string? materialSearch, string? orderNumber, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var list = new List<OpenOrderLineForReservationDto>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                dl.[Id]                AS LineId,
                d.[Id]                 AS OrderDocumentId,
                d.[DocumentNumber]     AS OrderNumber,
                d.[DocumentDate]       AS OrderDate,
                i.[Id]                 AS ItemId,
                i.[Code]               AS MaterialCode,
                i.[Name]               AS MaterialName,
                i.[TypeId]             AS ItemTypeId,
                dl.[UnitId]            AS UnitId,
                u.[Code]               AS UnitCode,
                dl.[Quantity]          AS OrderQty,
                dl.[BaseQuantity]      AS OrderBaseQty,
                dl.[DeliveredQuantity] AS DeliveredBaseQty,
                ISNULL(dl.[LocationId], d.[LocationId]) AS EffLocationId,
                dl.[Notes]             AS LineNotes,
                ISNULL(rsvLine.Reserved, 0) AS ReservedLineBase,
                ISNULL(bal.PhysicalBase, 0) AS PhysicalBase,
                ISNULL(rsvLoc.Reserved, 0)  AS ReservedLocBase
            FROM {T("DocumentLine")} dl
            INNER JOIN {T("Document")}     d  ON d.[Id]  = dl.[DocumentId]
            INNER JOIN {T("DocumentType")} dt ON dt.[Id] = d.[DocumentTypeId]
            INNER JOIN {T("Items")}        i  ON i.[Id]  = dl.[ItemId]
            LEFT  JOIN {T("Unit")}         u  ON u.[Id]  = dl.[UnitId]
            OUTER APPLY (
                SELECT SUM(sr.[BaseQuantity]) AS Reserved
                FROM {T("StockReservation")} sr
                WHERE sr.[OrderLineId] = dl.[Id] AND sr.[Status] = 1 AND sr.[IsActive] = 1
            ) rsvLine
            OUTER APPLY (
                SELECT SUM(CASE WHEN sm.[MovementType] IN (2,3,4) AND sm.[LocationId]     = ISNULL(dl.[LocationId], d.[LocationId]) THEN sm.[BaseQuantity]
                                 WHEN sm.[MovementType] IN (1,3,4) AND sm.[FromLocationId] = ISNULL(dl.[LocationId], d.[LocationId]) THEN -sm.[BaseQuantity]
                                 ELSE 0 END) AS PhysicalBase
                FROM {T("DocumentLine")} sm
                INNER JOIN {T("Document")} smd ON smd.[Id] = sm.[DocumentId]
                WHERE sm.[ItemId] = dl.[ItemId] AND smd.[CompanyId] = @Cid AND smd.[IsActive] = 1
                  AND sm.[MovementType] IN (1,2,3,4)
                  AND (sm.[LocationId] = ISNULL(dl.[LocationId], d.[LocationId]) OR sm.[FromLocationId] = ISNULL(dl.[LocationId], d.[LocationId]))
            ) bal
            OUTER APPLY (
                SELECT SUM(sr2.[BaseQuantity]) AS Reserved
                FROM {T("StockReservation")} sr2
                WHERE sr2.[ItemId] = dl.[ItemId] AND sr2.[LocationId] = ISNULL(dl.[LocationId], d.[LocationId])
                  AND sr2.[Status] = 1 AND sr2.[IsActive] = 1
            ) rsvLoc
            WHERE dt.[Code] = N'satis_siparisi'
              AND d.[CompanyId] = @Cid AND d.[IsActive] = 1
              AND dl.[MovementType] IS NULL AND dl.[ItemId] IS NOT NULL
              AND dl.[BaseQuantity] > dl.[DeliveredQuantity]
              AND (@MatSearch IS NULL OR i.[Code] LIKE @MatSearch OR i.[Name] LIKE @MatSearch)
              AND (@OrderNo IS NULL OR d.[DocumentNumber] LIKE @OrderNo)
            ORDER BY d.[DocumentDate] DESC, d.[DocumentNumber], dl.[LineNo];
            """;

        cmd.Parameters.AddWithValue("@Cid", companyId);
        cmd.Parameters.Add(new SqlParameter("@MatSearch",
            string.IsNullOrWhiteSpace(materialSearch) ? (object)DBNull.Value : $"%{materialSearch.Trim()}%"));
        cmd.Parameters.Add(new SqlParameter("@OrderNo",
            string.IsNullOrWhiteSpace(orderNumber) ? (object)DBNull.Value : $"%{orderNumber.Trim()}%"));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var orderQty      = r.GetDecimal(r.GetOrdinal("OrderQty"));
            var orderBaseQty  = r.GetDecimal(r.GetOrdinal("OrderBaseQty"));
            var deliveredBase = r.GetDecimal(r.GetOrdinal("DeliveredBaseQty"));
            var reservedLineBase = r.IsDBNull(r.GetOrdinal("ReservedLineBase")) ? 0m : r.GetDecimal(r.GetOrdinal("ReservedLineBase"));
            var physicalBase  = r.IsDBNull(r.GetOrdinal("PhysicalBase")) ? 0m : r.GetDecimal(r.GetOrdinal("PhysicalBase"));
            var reservedLocBase = r.IsDBNull(r.GetOrdinal("ReservedLocBase")) ? 0m : r.GetDecimal(r.GetOrdinal("ReservedLocBase"));

            // Gösterim <-> baz birim çevrim faktörü — StockUnitSql/ItemUnits'e tekrar sorgu atmak yerine
            // satırın kendi Quantity/BaseQuantity oranı kullanılır (GetOrderOpenLinesAsync ile aynı desen).
            var factor = orderQty != 0m ? orderBaseQty / orderQty : 1m;

            var deliveredDisplay = factor != 0m ? Math.Round(deliveredBase / factor, 4) : deliveredBase;
            var reservedDisplay  = factor != 0m ? Math.Round(reservedLineBase / factor, 4) : reservedLineBase;
            var openQty = Math.Max(0m, orderQty - deliveredDisplay - reservedDisplay);
            var availableBase = physicalBase - reservedLocBase;
            var availableDisplay = factor != 0m ? Math.Round(availableBase / factor, 4) : availableBase;

            var itemTypeIdOrd = r.GetOrdinal("ItemTypeId");
            int? itemTypeId = r.IsDBNull(itemTypeIdOrd) ? null : r.GetInt32(itemTypeIdOrd);

            var locOrd = r.GetOrdinal("EffLocationId");
            int? locationId = r.IsDBNull(locOrd) ? null : r.GetInt32(locOrd);

            list.Add(new OpenOrderLineForReservationDto(
                LineId: r.GetInt32(r.GetOrdinal("LineId")),
                OrderDocumentId: r.GetInt32(r.GetOrdinal("OrderDocumentId")),
                OrderNumber: r.GetString(r.GetOrdinal("OrderNumber")),
                OrderDate: r.GetDateTime(r.GetOrdinal("OrderDate")),
                ItemId: r.GetInt32(r.GetOrdinal("ItemId")),
                MaterialCode: r.IsDBNull(r.GetOrdinal("MaterialCode")) ? null : r.GetString(r.GetOrdinal("MaterialCode")),
                MaterialName: r.IsDBNull(r.GetOrdinal("MaterialName")) ? null : r.GetString(r.GetOrdinal("MaterialName")),
                UnitId: r.IsDBNull(r.GetOrdinal("UnitId")) ? null : r.GetInt32(r.GetOrdinal("UnitId")),
                UnitCode: r.IsDBNull(r.GetOrdinal("UnitCode")) ? null : r.GetString(r.GetOrdinal("UnitCode")),
                OrderQty: orderQty,
                DeliveredQty: deliveredDisplay,
                ReservedQty: reservedDisplay,
                OpenQty: openQty,
                AvailableStock: availableDisplay,
                IsKit: ItemTypeCatalog.IsKit(itemTypeId),
                LocationId: locationId,
                LineNotes: r.IsDBNull(r.GetOrdinal("LineNotes")) ? null : r.GetString(r.GetOrdinal("LineNotes"))));
        }

        return list;
    }

    private sealed record OrderLineRow(
        int LineId, int OrderDocumentId, int ItemId, int? ItemTypeId, int? UnitId,
        decimal Qty, decimal BaseQty, decimal DeliveredBase, int? CombinationId,
        int? EffLocationId, decimal ReservedLineBase);

    public async Task<CreateReservationResult> CreateReservationsAsync(
        CreateReservationRequest request, int? userId, CancellationToken ct)
    {
        var created = new List<CreateReservationResultItem>();
        var skipped = new List<CreateReservationSkippedItem>();

        var lines = (request?.Lines ?? new List<CreateReservationLineRequest>())
            .Where(l => l.OrderLineId > 0)
            .ToList();
        if (lines.Count == 0)
            return new CreateReservationResult(true, created, skipped);

        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var lineIds = lines.Select(l => l.OrderLineId).Distinct().ToList();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Talep edilen sipariş kalemlerinin hepsini TEK sorguda çek (kit tipi + mevcut
            //    rezervasyon toplamı dahil). Yalnız açık (MovementType IS NULL) satis_siparisi
            //    kalemleri geçerlidir — başka belge tipinin/kaleminin id'si gönderilse dahi
            //    bu filtre yüzünden bulunamaz (skip).
            var rows = new Dictionary<int, OrderLineRow>();
            await using (var fetch = conn.CreateCommand())
            {
                fetch.Transaction = tx;
                var paramList = string.Join(",", lineIds.Select((_, i) => $"@lid{i}"));
                fetch.CommandText = $"""
                    SELECT dl.[Id], d.[Id] AS OrderDocId, dl.[ItemId], i.[TypeId], dl.[UnitId],
                           dl.[Quantity], dl.[BaseQuantity], dl.[DeliveredQuantity], dl.[CombinationId],
                           ISNULL(dl.[LocationId], d.[LocationId]) AS EffLocationId,
                           ISNULL(rsvLine.Reserved, 0) AS ReservedLineBase
                    FROM {T("DocumentLine")} dl
                    INNER JOIN {T("Document")}     d  ON d.[Id]  = dl.[DocumentId]
                    INNER JOIN {T("DocumentType")} dt ON dt.[Id] = d.[DocumentTypeId]
                    INNER JOIN {T("Items")}        i  ON i.[Id]  = dl.[ItemId]
                    OUTER APPLY (
                        SELECT SUM(sr.[BaseQuantity]) AS Reserved
                        FROM {T("StockReservation")} sr
                        WHERE sr.[OrderLineId] = dl.[Id] AND sr.[Status] = 1 AND sr.[IsActive] = 1
                    ) rsvLine
                    WHERE dl.[Id] IN ({paramList})
                      AND dt.[Code] = N'satis_siparisi'
                      AND d.[CompanyId] = @Cid AND d.[IsActive] = 1
                      AND dl.[MovementType] IS NULL;
                    """;
                fetch.Parameters.AddWithValue("@Cid", companyId);
                for (var i = 0; i < lineIds.Count; i++)
                    fetch.Parameters.Add(new SqlParameter($"@lid{i}", lineIds[i]));

                await using var rd = await fetch.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    rows[rd.GetInt32(0)] = new OrderLineRow(
                        LineId: rd.GetInt32(0),
                        OrderDocumentId: rd.GetInt32(1),
                        ItemId: rd.GetInt32(2),
                        ItemTypeId: rd.IsDBNull(3) ? null : rd.GetInt32(3),
                        UnitId: rd.IsDBNull(4) ? null : rd.GetInt32(4),
                        Qty: rd.GetDecimal(5),
                        BaseQty: rd.GetDecimal(6),
                        DeliveredBase: rd.GetDecimal(7),
                        CombinationId: rd.IsDBNull(8) ? null : rd.GetInt32(8),
                        EffLocationId: rd.IsDBNull(9) ? null : rd.GetInt32(9),
                        ReservedLineBase: rd.GetDecimal(10));
                }
            }

            // 2) (ItemId, LocationId) çifti başına fiziksel bakiye + mevcut aktif rezervasyon —
            //    bir kere sorgulanır, bu istek içindeki yeni eklemeler committedThisBatch ile takip edilir
            //    (aynı istekte aynı malzeme/depoyu hedefleyen birden fazla satır varsa üzerine binmesin diye).
            var balanceCache = new Dictionary<(int ItemId, int LocationId), (decimal Physical, decimal ExistingReserved)>();
            var committedThisBatch = new Dictionary<(int ItemId, int LocationId), decimal>();

            foreach (var req in lines)
            {
                if (!rows.TryGetValue(req.OrderLineId, out var row))
                {
                    skipped.Add(new CreateReservationSkippedItem(req.OrderLineId, "Sipariş kalemi bulunamadı veya artık açık değil."));
                    continue;
                }

                if (ItemTypeCatalog.IsKit(row.ItemTypeId))
                {
                    skipped.Add(new CreateReservationSkippedItem(req.OrderLineId, "Kit rezervasyonu sonraki fazda."));
                    continue;
                }

                var targetLocationId = request!.LocationId ?? row.EffLocationId;
                if (targetLocationId is null or <= 0)
                {
                    skipped.Add(new CreateReservationSkippedItem(req.OrderLineId, "Rezervasyon deposu belirlenemedi (sipariş kaleminde/başlığında depo yok)."));
                    continue;
                }

                if (req.Qty <= 0)
                {
                    skipped.Add(new CreateReservationSkippedItem(req.OrderLineId, "Miktar sıfır veya negatif olamaz."));
                    continue;
                }

                var factor = row.Qty != 0m ? row.BaseQty / row.Qty : 1m;
                var deliveredDisplay = factor != 0m ? Math.Round(row.DeliveredBase / factor, 4) : row.DeliveredBase;
                var reservedDisplay  = factor != 0m ? Math.Round(row.ReservedLineBase / factor, 4) : row.ReservedLineBase;
                var openDisplay = row.Qty - deliveredDisplay - reservedDisplay;

                if (req.Qty > openDisplay + 0.0001m)
                {
                    skipped.Add(new CreateReservationSkippedItem(req.OrderLineId,
                        $"Talep edilen miktar açık sipariş miktarını aşıyor (açık: {openDisplay:0.####})."));
                    continue;
                }

                var baseQtyForRequest = req.Qty * factor;
                var locKey = (row.ItemId, targetLocationId.Value);
                if (!balanceCache.TryGetValue(locKey, out var bal))
                {
                    var physical = await GetPhysicalBalanceAsync(conn, tx, companyId, row.ItemId, targetLocationId.Value, ct);
                    var existingReserved = await GetActiveReservedAsync(conn, tx, row.ItemId, targetLocationId.Value, ct);
                    bal = (physical, existingReserved);
                    balanceCache[locKey] = bal;
                }
                committedThisBatch.TryGetValue(locKey, out var committed);
                var available = bal.Physical - bal.ExistingReserved - committed;

                if (baseQtyForRequest > available + 0.0001m)
                {
                    var availableDisplay = factor != 0m ? Math.Round(available / factor, 4) : available;
                    skipped.Add(new CreateReservationSkippedItem(req.OrderLineId,
                        $"Yetersiz kullanılabilir stok (kullanılabilir: {availableDisplay:0.####})."));
                    continue;
                }

                await using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = $"""
                        INSERT INTO {T("StockReservation")}
                            ([OrderDocumentId],[OrderLineId],[ItemId],[LocationId],[CombinationId],[UnitId],
                             [Quantity],[BaseQuantity],[Status],[PlannedShipDate],[Notes],[IsActive],
                             [CreatedById],[Created])
                        VALUES
                            (@OrderDocumentId,@OrderLineId,@ItemId,@LocationId,@CombinationId,@UnitId,
                             @Quantity,@BaseQuantity,1,@PlannedShipDate,@Notes,1,
                             @CreatedById,SYSUTCDATETIME());
                        """;
                    ins.Parameters.AddWithValue("@OrderDocumentId", row.OrderDocumentId);
                    ins.Parameters.AddWithValue("@OrderLineId", row.LineId);
                    ins.Parameters.AddWithValue("@ItemId", row.ItemId);
                    ins.Parameters.AddWithValue("@LocationId", targetLocationId.Value);
                    ins.Parameters.Add(new SqlParameter("@CombinationId", (object?)row.CombinationId ?? DBNull.Value));
                    ins.Parameters.Add(new SqlParameter("@UnitId", (object?)row.UnitId ?? DBNull.Value));
                    ins.Parameters.AddWithValue("@Quantity", req.Qty);
                    ins.Parameters.AddWithValue("@BaseQuantity", baseQtyForRequest);
                    ins.Parameters.Add(new SqlParameter("@PlannedShipDate", (object?)request!.PlannedShipDate ?? DBNull.Value));
                    ins.Parameters.Add(new SqlParameter("@Notes", (object?)request!.Notes ?? DBNull.Value));
                    ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
                    await ins.ExecuteNonQueryAsync(ct);
                }

                committedThisBatch[locKey] = committed + baseQtyForRequest;
                created.Add(new CreateReservationResultItem(req.OrderLineId, req.Qty, null));
            }

            await tx.CommitAsync(ct);
            return new CreateReservationResult(true, created, skipped);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<decimal> GetPhysicalBalanceAsync(
        SqlConnection conn, SqlTransaction tx, int companyId, int itemId, int locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(SUM(CASE WHEN sm.[MovementType] IN (2,3,4) AND sm.[LocationId]     = @L THEN sm.[BaseQuantity]
                                    WHEN sm.[MovementType] IN (1,3,4) AND sm.[FromLocationId] = @L THEN -sm.[BaseQuantity]
                                    ELSE 0 END), 0)
            FROM {T("DocumentLine")} sm
            INNER JOIN {T("Document")} smd ON smd.[Id] = sm.[DocumentId]
            WHERE sm.[ItemId] = @ItemId AND smd.[CompanyId] = @Cid AND smd.[IsActive] = 1
              AND sm.[MovementType] IN (1,2,3,4)
              AND (sm.[LocationId] = @L OR sm.[FromLocationId] = @L);
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@Cid", companyId);
        cmd.Parameters.AddWithValue("@L", locationId);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? 0m : Convert.ToDecimal(v);
    }

    private async Task<decimal> GetActiveReservedAsync(
        SqlConnection conn, SqlTransaction tx, int itemId, int locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(SUM([BaseQuantity]), 0) FROM {T("StockReservation")}
            WHERE [ItemId] = @ItemId AND [LocationId] = @L AND [Status] = 1 AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@L", locationId);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? 0m : Convert.ToDecimal(v);
    }

    public async Task<int> CancelReservationsAsync(
        IReadOnlyList<int> reservationIds, int? userId, CancellationToken ct)
    {
        var ids = (reservationIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return 0;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var paramList = string.Join(",", ids.Select((_, i) => $"@id{i}"));
        cmd.CommandText = $"""
            UPDATE {T("StockReservation")}
            SET [Status] = 3, [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] IN ({paramList}) AND [Status] = 1 AND [IsActive] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.Add(new SqlParameter($"@id{i}", ids[i]));

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<StockReservationDto>> GetReservationsAsync(
        int? orderDocumentId, int? orderLineId, CancellationToken ct)
    {
        var list = new List<StockReservationDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT r.[Id], r.[OrderDocumentId], r.[OrderLineId], r.[ItemId], i.[Code], i.[Name],
                   r.[LocationId], ISNULL(loc.[LocationName], loc.[LocationCode]) AS LocationLabel,
                   r.[Quantity], r.[BaseQuantity], r.[Status], r.[PlannedShipDate], r.[Notes], r.[Created]
            FROM {T("StockReservation")} r
            LEFT JOIN {T("Items")}    i   ON i.[Id]   = r.[ItemId]
            LEFT JOIN {T("Location")} loc ON loc.[Id] = r.[LocationId]
            WHERE r.[IsActive] = 1
              AND (@OrderDocumentId IS NULL OR r.[OrderDocumentId] = @OrderDocumentId)
              AND (@OrderLineId IS NULL OR r.[OrderLineId] = @OrderLineId)
            ORDER BY r.[Created] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@OrderDocumentId", (object?)orderDocumentId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@OrderLineId", (object?)orderLineId ?? DBNull.Value));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new StockReservationDto(
                Id: r.GetInt32(0),
                OrderDocumentId: r.GetInt32(1),
                OrderLineId: r.GetInt32(2),
                ItemId: r.GetInt32(3),
                MaterialCode: r.IsDBNull(4) ? null : r.GetString(4),
                MaterialName: r.IsDBNull(5) ? null : r.GetString(5),
                LocationId: r.GetInt32(6),
                LocationName: r.IsDBNull(7) ? null : r.GetString(7),
                Quantity: r.GetDecimal(8),
                BaseQuantity: r.GetDecimal(9),
                Status: r.GetByte(10),
                PlannedShipDate: r.IsDBNull(11) ? null : r.GetDateTime(11),
                Notes: r.IsDBNull(12) ? null : r.GetString(12),
                Created: r.GetDateTime(13)));
        }
        return list;
    }
}

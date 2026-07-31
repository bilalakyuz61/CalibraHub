using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
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
    private readonly IDocumentNumberService _numberService;
    private readonly string _schema;

    public SqlStockReservationRepository(
        SqlServerConnectionFactory connectionFactory,
        IDocumentNumberService numberService,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _numberService = numberService;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    public async Task<IReadOnlyList<OpenOrderLineForReservationDto>> GetOpenOrderLinesAsync(
        string? materialSearch, string? orderNumber, CancellationToken ct)
    {
        // Per-company DB: iş belgeleri kendi şirket DB'sinde; CompanyId FİLTRELENMEZ
        // (Document.CompanyId DB-lokal sabit, oturum claim'i ile eşleşmeyebilir → yanlış eleme).
        // Referans: PurchaseController.AllOpenRequestLines de CompanyId filtresi kullanmaz.
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
                WHERE sm.[ItemId] = dl.[ItemId] AND smd.[IsActive] = 1
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
              AND d.[IsActive] = 1
              AND dl.[MovementType] IS NULL AND dl.[ItemId] IS NOT NULL
              AND dl.[BaseQuantity] > dl.[DeliveredQuantity]
              AND (@MatSearch IS NULL OR i.[Code] LIKE @MatSearch OR i.[Name] LIKE @MatSearch)
              AND (@OrderNo IS NULL OR d.[DocumentNumber] LIKE @OrderNo)
            ORDER BY d.[DocumentDate] DESC, d.[DocumentNumber], dl.[LineNo];
            """;

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
                      AND d.[IsActive] = 1
                      AND dl.[MovementType] IS NULL;
                    """;
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
                    var physical = await GetPhysicalBalanceAsync(conn, tx, row.ItemId, targetLocationId.Value, ct);
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
        SqlConnection conn, SqlTransaction tx, int itemId, int locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(SUM(CASE WHEN sm.[MovementType] IN (2,3,4) AND sm.[LocationId]     = @L THEN sm.[BaseQuantity]
                                    WHEN sm.[MovementType] IN (1,3,4) AND sm.[FromLocationId] = @L THEN -sm.[BaseQuantity]
                                    ELSE 0 END), 0)
            FROM {T("DocumentLine")} sm
            INNER JOIN {T("Document")} smd ON smd.[Id] = sm.[DocumentId]
            WHERE sm.[ItemId] = @ItemId AND smd.[IsActive] = 1
              AND sm.[MovementType] IN (1,2,3,4)
              AND (sm.[LocationId] = @L OR sm.[FromLocationId] = @L);
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
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

    // ── Faz 2 (2026-07-31) — "Yükle": rezervasyon → satış irsaliyesi ──────────────────────────
    // Referans: SqlStockDocRepository.ConvertOrderToDeliveryAsync (:875) — aynı irsaliye satırı
    // yazım deseni (MovementType=1 çıkış, FromLocationId, SourceLineId, DeliveredQuantity artışı,
    // NegativeBalanceGuard) buraya BİREBİR uyarlandı. Farklar: (1) kaynak StockReservation satırları
    // (sipariş kaleminin TÜM açığı değil, yalnız o rezervasyonun miktarı) — TAM yükleme, kısmi yok;
    // (2) cari (Document.ContactId) başına TEK irsaliyede toplama (çok-sipariş olabilir) — SaveDeliveryFifoAsync
    // (:1420) ile aynı "tek kaynak sipariş → ParentDocumentId, çoklu → null" kuralı; (3) kit/seri
    // KAPSAM DIŞI (Faz 1 rezervasyonlar zaten kit-dışı, seri Faz 3) — DocumentLineLink dual-write ve
    // ResolveOrderSerialsToIssuedAsync bilinçli olarak YAPILMADI (bu akış onlara dokunmuyor).

    private sealed record ShipReservationRow(
        int ReservationId, int OrderDocumentId, int OrderLineId, int ItemId, int LocationId,
        int? CombinationId, int? UnitId, decimal Qty, decimal BaseQty,
        decimal OrderQty, decimal UnitPrice, decimal DiscRate, decimal OrderLineTotal,
        int ContactId, string? ContactName);

    public async Task<ShipReservationsResult> ShipReservationsAsync(
        IReadOnlyList<int> reservationIds, int? userId, CancellationToken ct)
    {
        var ids = (reservationIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();
        var skipped = new List<ShipReservationsSkippedItem>();
        var deliveries = new List<ShipReservationsDeliveryDto>();
        if (ids.Count == 0)
            return new ShipReservationsResult(true, deliveries, skipped);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var companyId = _connectionFactory.ResolveCurrentCompanyId();

            // 1) Durum ön-kontrolü — bulunamayan / zaten yüklenmiş / iptal edilmiş rezervasyonlar
            //    net reason ile atlanır (sessiz atlama YOK, CLAUDE.md kural #3).
            var statusById = new Dictionary<int, (byte Status, bool IsActive)>();
            await using (var st = conn.CreateCommand())
            {
                st.Transaction = tx;
                var pn = ids.Select((_, i) => "@id" + i).ToArray();
                st.CommandText = $"SELECT [Id],[Status],[IsActive] FROM {T("StockReservation")} WHERE [Id] IN ({string.Join(",", pn)});";
                for (var i = 0; i < ids.Count; i++) st.Parameters.AddWithValue(pn[i], ids[i]);
                await using var r = await st.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    statusById[r.GetInt32(0)] = (r.GetByte(1), r.GetBoolean(2));
            }

            var eligibleIds = new List<int>();
            foreach (var id in ids)
            {
                if (!statusById.TryGetValue(id, out var s))
                { skipped.Add(new ShipReservationsSkippedItem(id, "Rezervasyon bulunamadı.")); continue; }
                if (s.Status == (byte)StockReservationStatus.Shipped)
                { skipped.Add(new ShipReservationsSkippedItem(id, "Rezervasyon zaten yüklenmiş.")); continue; }
                if (s.Status == (byte)StockReservationStatus.Cancelled || !s.IsActive)
                { skipped.Add(new ShipReservationsSkippedItem(id, "Rezervasyon iptal edilmiş.")); continue; }
                eligibleIds.Add(id);
            }

            if (eligibleIds.Count == 0)
            {
                await tx.CommitAsync(ct);
                return new ShipReservationsResult(true, deliveries, skipped);
            }

            // 2) Detay + sipariş kalemi fiyat/cari bilgisi (yalnız hâlâ geçerli satis_siparisi kalemi).
            var rows = new List<ShipReservationRow>();
            await using (var d = conn.CreateCommand())
            {
                d.Transaction = tx;
                var pn = eligibleIds.Select((_, i) => "@rid" + i).ToArray();
                d.CommandText = $"""
                    SELECT sr.[Id], sr.[OrderDocumentId], sr.[OrderLineId], sr.[ItemId], sr.[LocationId],
                           sr.[CombinationId], sr.[UnitId], sr.[Quantity], sr.[BaseQuantity],
                           dl.[Quantity] AS OrderQty, dl.[UnitPrice], dl.[DiscountRate], dl.[LineTotal] AS OrderLineTotal,
                           ISNULL(d.[ContactId], 0) AS ContactId, ca.[AccountTitle] AS ContactName
                    FROM {T("StockReservation")} sr
                    INNER JOIN {T("DocumentLine")} dl ON dl.[Id] = sr.[OrderLineId]
                    INNER JOIN {T("Document")} d       ON d.[Id]  = sr.[OrderDocumentId]
                    INNER JOIN {T("DocumentType")} dt  ON dt.[Id] = d.[DocumentTypeId]
                    LEFT JOIN {T("Contact")} ca         ON ca.[Id] = d.[ContactId]
                    WHERE sr.[Id] IN ({string.Join(",", pn)})
                      AND dt.[Code] = N'satis_siparisi' AND d.[IsActive] = 1;
                    """;
                for (var i = 0; i < eligibleIds.Count; i++) d.Parameters.AddWithValue(pn[i], eligibleIds[i]);
                await using var r = await d.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    rows.Add(new ShipReservationRow(
                        ReservationId: r.GetInt32(0),
                        OrderDocumentId: r.GetInt32(1),
                        OrderLineId: r.GetInt32(2),
                        ItemId: r.GetInt32(3),
                        LocationId: r.GetInt32(4),
                        CombinationId: r.IsDBNull(5) ? null : r.GetInt32(5),
                        UnitId: r.IsDBNull(6) ? null : r.GetInt32(6),
                        Qty: r.GetDecimal(7),
                        BaseQty: r.GetDecimal(8),
                        OrderQty: r.GetDecimal(9),
                        UnitPrice: r.GetDecimal(10),
                        DiscRate: r.GetDecimal(11),
                        OrderLineTotal: r.GetDecimal(12),
                        ContactId: r.GetInt32(13),
                        ContactName: r.IsDBNull(14) ? null : r.GetString(14)));
                }
            }

            var foundIds = rows.Select(x => x.ReservationId).ToHashSet();
            foreach (var id in eligibleIds)
                if (!foundIds.Contains(id))
                    skipped.Add(new ShipReservationsSkippedItem(id, "Sipariş kalemi veya siparişi artık geçerli değil."));

            var validRows = new List<ShipReservationRow>();
            foreach (var row in rows)
            {
                if (row.ContactId <= 0)
                    skipped.Add(new ShipReservationsSkippedItem(row.ReservationId, "Siparişte cari tanımlı değil; irsaliye oluşturulamadı."));
                else
                    validRows.Add(row);
            }

            if (validRows.Count == 0)
            {
                await tx.CommitAsync(ct);
                return new ShipReservationsResult(true, deliveries, skipped);
            }

            // 3) Cari (Document.ContactId) başına grupla → grup başına TEK irsaliye.
            foreach (var group in validRows.GroupBy(x => x.ContactId))
            {
                var contactId = group.Key;
                var groupRows = group.ToList();
                var distinctOrderIds = groupRows.Select(x => x.OrderDocumentId).Distinct().ToList();
                int? parentDocumentId = distinctOrderIds.Count == 1 ? distinctOrderIds[0] : null;

                var docNo = await ResolveDocNoAsync(conn, tx, contactId, userId, DateTime.Today, ct);

                var lineTotals = new decimal[groupRows.Count];
                decimal subTotal = 0m;
                for (var i = 0; i < groupRows.Count; i++)
                {
                    var row = groupRows[i];
                    var lineTotal = row.OrderQty != 0m
                        ? Math.Round(row.OrderLineTotal * row.Qty / row.OrderQty, 4)
                        : row.OrderLineTotal;
                    lineTotals[i] = lineTotal;
                    subTotal += lineTotal;
                }
                subTotal = Math.Round(subTotal, 4);

                int docId;
                await using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    // ContactName Document'ta KOLON DEĞİL (ContactId JOIN'iyle çözülür) — yazılmaz.
                    ins.CommandText = $"""
                        INSERT INTO {T("Document")}
                            ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[LocationId],
                             [ContactId],[SubTotal],[DiscountRate],[DiscountAmount],[TaxRate],[TaxAmount],[GrandTotal],
                             [Notes],[Status],[CreatedById],[Created],[IsActive],[ParentDocumentId])
                        SELECT @CompanyId, @DocNo, dt.[Id], @DocDate, NULL,
                               @ContactId, @SubTotal, 0, 0, 0, 0, @SubTotal,
                               @Notes, N'Draft', @CreatedById, SYSUTCDATETIME(), 1, @ParentId
                        FROM {T("DocumentType")} dt WHERE dt.[Code] = N'satis_irsaliyesi';
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                        """;
                    ins.Parameters.AddWithValue("@CompanyId", companyId);
                    ins.Parameters.AddWithValue("@DocNo", docNo);
                    ins.Parameters.AddWithValue("@DocDate", DateTime.Today);
                    ins.Parameters.AddWithValue("@ContactId", contactId);
                    ins.Parameters.AddWithValue("@SubTotal", subTotal);
                    ins.Parameters.AddWithValue("@Notes", "Yükleme planı → irsaliye");
                    ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
                    ins.Parameters.Add(new SqlParameter("@ParentId", (object?)parentDocumentId ?? DBNull.Value));
                    docId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
                }

                var lineNo = 1;
                var decreases = new HashSet<(int ItemId, int LocId)>();
                for (var i = 0; i < groupRows.Count; i++)
                {
                    var row = groupRows[i];
                    await using (var li = conn.CreateCommand())
                    {
                        li.Transaction = tx;
                        li.CommandText = $"""
                            INSERT INTO {T("DocumentLine")}
                                ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                                 [CombinationId],[FromLocationId],[LocationId],[MovementType],[SourceLineId],[Notes])
                            VALUES
                                (@DocId,@LineNo,@ItemId,@UnitId,@Qty,@BaseQty,@UnitPrice,@DiscRate,@LineTotal,
                                 @CombId,@FromLoc,NULL,1,@SourceLineId,@Notes);
                            """;
                        li.Parameters.AddWithValue("@DocId", docId);
                        li.Parameters.AddWithValue("@LineNo", lineNo++);
                        li.Parameters.AddWithValue("@ItemId", row.ItemId);
                        li.Parameters.Add(new SqlParameter("@UnitId", (object?)row.UnitId ?? DBNull.Value));
                        li.Parameters.AddWithValue("@Qty", row.Qty);
                        li.Parameters.AddWithValue("@BaseQty", row.BaseQty);
                        li.Parameters.AddWithValue("@UnitPrice", row.UnitPrice);
                        li.Parameters.AddWithValue("@DiscRate", row.DiscRate);
                        li.Parameters.AddWithValue("@LineTotal", lineTotals[i]);
                        li.Parameters.Add(new SqlParameter("@CombId", (object?)row.CombinationId ?? DBNull.Value));
                        li.Parameters.AddWithValue("@FromLoc", row.LocationId);
                        li.Parameters.AddWithValue("@SourceLineId", row.OrderLineId);
                        li.Parameters.AddWithValue("@Notes", "Yükleme planı → irsaliye");
                        await li.ExecuteNonQueryAsync(ct);
                    }

                    await using (var upd = conn.CreateCommand())
                    {
                        upd.Transaction = tx;
                        upd.CommandText = $"UPDATE {T("DocumentLine")} SET [DeliveredQuantity] = [DeliveredQuantity] + @Add WHERE [Id] = @LineId;";
                        upd.Parameters.AddWithValue("@Add", row.BaseQty);
                        upd.Parameters.AddWithValue("@LineId", row.OrderLineId);
                        await upd.ExecuteNonQueryAsync(ct);
                    }

                    await using (var rup = conn.CreateCommand())
                    {
                        rup.Transaction = tx;
                        rup.CommandText = $"""
                            UPDATE {T("StockReservation")}
                            SET [Status] = 2, [ShippedDocumentId] = @DocId, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
                            WHERE [Id] = @Id;
                            """;
                        rup.Parameters.AddWithValue("@DocId", docId);
                        rup.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
                        rup.Parameters.AddWithValue("@Id", row.ReservationId);
                        await rup.ExecuteNonQueryAsync(ct);
                    }

                    decreases.Add((row.ItemId, row.LocationId));
                }

                // 4) Eksi bakiye kontrolü — çıkış (satış irsaliyesi), ConvertOrderToDeliveryAsync ile aynı guard.
                foreach (var (it, loc) in decreases)
                    await NegativeBalanceGuard.EnsureAsync(conn, tx, _schema, companyId, it, loc, DateTime.Today, ct);

                var contactName = groupRows.Select(x => x.ContactName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                deliveries.Add(new ShipReservationsDeliveryDto(docId, docNo, contactId, contactName, groupRows.Count));
            }

            await tx.CommitAsync(ct);
            return new ShipReservationsResult(true, deliveries, skipped);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Belge numarasını DocumentType.Code üzerinden çözer (kural motoru → yoksa SIR-YYYY-NNNN).
    /// SqlStockDocRepository.ResolveDocNoByCodeAsync ile aynı desen (kod tekrarı — iki repo arasında
    /// paylaşılan yer yok, ConvertOrderToDeliveryAsync'teki private helper'a erişilemiyor).</summary>
    private async Task<string> ResolveDocNoAsync(
        SqlConnection conn, SqlTransaction tx, int? contactId, int? createdById, DateTime docDate, CancellationToken ct)
    {
        const string typeCode = "satis_irsaliyesi";
        const string prefix = "SIR";
        await using (var typeCmd = conn.CreateCommand())
        {
            typeCmd.Transaction = tx;
            typeCmd.CommandText = $"SELECT [Id] FROM {T("DocumentType")} WHERE [Code] = @Code;";
            typeCmd.Parameters.AddWithValue("@Code", typeCode);
            var typeIdObj = await typeCmd.ExecuteScalarAsync(ct);
            if (typeIdObj is int typeId)
            {
                var ruleNo = await _numberService.GenerateNextAsync(
                    new DocumentNumberContext(typeId, contactId, null, createdById, null, docDate), ct);
                if (!string.IsNullOrWhiteSpace(ruleNo)) return ruleNo;
            }
        }
        var year = docDate.Year;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING([DocumentNumber], LEN(@Prefix) + 7, 10) AS INT)), 0) + 1
            FROM {T("Document")}
            WHERE [DocumentNumber] LIKE @Prefix + '-' + CAST(@Year AS NVARCHAR(4)) + '-%';
            """;
        cmd.Parameters.AddWithValue("@Prefix", prefix);
        cmd.Parameters.AddWithValue("@Year", year);
        var seq = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return $"{prefix}-{year}-{seq:D4}";
    }
}

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

    // ── Faz 1.5 (2026-08-03) — KİT tam-set rezervasyon ortak yardımcıları ─────────────────────
    // Bileşen satırı: DocumentLineKitComponent (donmuş snapshot). "Lazy freeze": bir kit sipariş
    // kalemi henüz snapshot taşımıyorsa (kit teklif aşamasında dondurulmamış / eski veri) canlı
    // aktif ItemKit içeriğinden dondurulur — bu modülün her giriş noktası (liste, rezerve, yükle)
    // aynı garantiyle çalışır: EnsureKitSnapshotAsync çağrıldıktan sonra snapshot ya doludur ya da
    // gerçekten aktif bir kit tanımı yoktur (liste 0 döner, çağıran net hata/atlama üretir).
    private sealed record KitComponentRow(int CompItemId, int? ConfigId, decimal PerKit, int? UnitId, string? CompName);

    private async Task<List<KitComponentRow>> EnsureKitSnapshotAsync(
        SqlConnection conn, SqlTransaction? tx, int orderLineId, int kitItemId, CancellationToken ct)
    {
        async Task<List<KitComponentRow>> ReadSnapshotAsync()
        {
            var rows = new List<KitComponentRow>();
            await using var sel = conn.CreateCommand();
            sel.Transaction = tx;
            sel.CommandText = $"""
                SELECT s.[ComponentItemId], s.[ConfigId], s.[Quantity], ci.[UnitId], ci.[Name]
                FROM {T("DocumentLineKitComponent")} s
                LEFT JOIN {T("Items")} ci ON ci.[Id] = s.[ComponentItemId]
                WHERE s.[DocumentLineId] = @L
                ORDER BY s.[Id];
                """;
            sel.Parameters.AddWithValue("@L", orderLineId);
            await using var r = await sel.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add(new KitComponentRow(
                    r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetDecimal(2),
                    r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetString(4)));
            return rows;
        }

        var existing = await ReadSnapshotAsync();
        if (existing.Count > 0) return existing;

        // Snapshot yok — canlı aktif kit tanımından (ItemKit/ItemKitLine, MAX Id WHERE IsActive=1)
        // dondur. DocumentService.SaveQuoteAsync'teki freeze-on-first mantığıyla AYNI kaynak.
        int? versionNo = null;
        var liveComps = new List<(int CompItemId, int? ConfigId, decimal Qty)>();
        await using (var kc = conn.CreateCommand())
        {
            kc.Transaction = tx;
            kc.CommandText = $"""
                SELECT k.[VersionNo], l.[ItemId], l.[ConfigId], l.[Quantity]
                FROM {T("ItemKit")} k
                INNER JOIN {T("ItemKitLine")} l ON l.[ItemKitId] = k.[Id]
                WHERE k.[IsActive] = 1 AND k.[ItemId] = @KitItemId
                  AND k.[Id] = (SELECT MAX([Id]) FROM {T("ItemKit")} WHERE [ItemId] = @KitItemId AND [IsActive] = 1)
                ORDER BY l.[Id];
                """;
            kc.Parameters.AddWithValue("@KitItemId", kitItemId);
            await using var r = await kc.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                versionNo ??= r.GetInt32(0);
                liveComps.Add((r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetDecimal(3)));
            }
        }
        if (liveComps.Count == 0) return existing; // kit değil ya da aktif tanım/bileşen yok — boş döner

        foreach (var c in liveComps)
        {
            await using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = $"""
                INSERT INTO {T("DocumentLineKitComponent")}
                    ([DocumentLineId],[KitItemId],[KitVersionNo],[ComponentItemId],[ConfigId],[Quantity],[Created])
                VALUES (@L,@K,@V,@C,@Cfg,@Q,SYSUTCDATETIME());
                """;
            ins.Parameters.AddWithValue("@L", orderLineId);
            ins.Parameters.AddWithValue("@K", kitItemId);
            ins.Parameters.AddWithValue("@V", versionNo ?? 1);
            ins.Parameters.AddWithValue("@C", c.CompItemId);
            ins.Parameters.Add(new SqlParameter("@Cfg", (object?)c.ConfigId ?? DBNull.Value));
            ins.Parameters.AddWithValue("@Q", c.Qty);
            await ins.ExecuteNonQueryAsync(ct);
        }

        return await ReadSnapshotAsync();
    }

    /// <summary>Bir kit sipariş kalemine (KitOrderLineId) bağlı aktif bileşen rezervasyonlarından
    /// SET sayısını türetir: Σ(BaseQuantity WHERE ItemId=refComponentItemId) / perKit. Kit hepsi-ya-hiç
    /// atomik yazıldığından herhangi bir bileşenin oranı aynı set sayısını verir.</summary>
    private async Task<decimal> GetKitReservedSetsAsync(
        SqlConnection conn, SqlTransaction? tx, int kitOrderLineId, int refComponentItemId, decimal perKit, CancellationToken ct)
    {
        if (perKit <= 0m) return 0m;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(SUM([BaseQuantity]), 0) FROM {T("StockReservation")}
            WHERE [KitOrderLineId] = @L AND [ItemId] = @Comp AND [Status] = 1 AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@L", kitOrderLineId);
        cmd.Parameters.AddWithValue("@Comp", refComponentItemId);
        var v = await cmd.ExecuteScalarAsync(ct);
        var reservedBase = v is null or DBNull ? 0m : Convert.ToDecimal(v);
        return Math.Round(reservedBase / perKit, 4);
    }

    public async Task<IReadOnlyList<OpenOrderLineForReservationDto>> GetOpenOrderLinesAsync(
        string? materialSearch, string? orderNumber, CancellationToken ct)
    {
        // Per-company DB: iş belgeleri kendi şirket DB'sinde; CompanyId FİLTRELENMEZ
        // (Document.CompanyId DB-lokal sabit, oturum claim'i ile eşleşmeyebilir → yanlış eleme).
        // Referans: PurchaseController.AllOpenRequestLines de CompanyId filtresi kullanmaz.
        var list = new List<OpenOrderLineForReservationDto>();
        // Faz 1.5 — kit satırlarının indeksleri + patlatma için gereken ham veriler; reader kapandıktan
        // SONRA (yeni komutlar açmak için) ikinci geçişte işlenir (aynı connection üzerinde açık reader varken
        // yeni komut çalıştırılamaz).
        var kitPostProcess = new List<(int Index, int LineId, int KitItemId, int? LocationId, decimal OrderQtySets, decimal DeliveredDisplay)>();

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
                -- KitOrderLineId IS NULL: kit bileşen rezervasyonları (ItemId=bileşen, ayrı bir birim
                -- taşır) bu kalemin kendi (kit-dışı) rezerve toplamına karışmasın. Kit satırının kendi
                -- rezerve SET sayısı ayrı bir sorguyla (GetKitReservedSetsAsync, ikinci geçiş) hesaplanır.
                SELECT SUM(sr.[BaseQuantity]) AS Reserved
                FROM {T("StockReservation")} sr
                WHERE sr.[OrderLineId] = dl.[Id] AND sr.[Status] = 1 AND sr.[IsActive] = 1
                  AND sr.[KitOrderLineId] IS NULL
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
            var isKit = ItemTypeCatalog.IsKit(itemTypeId);

            var locOrd = r.GetOrdinal("EffLocationId");
            int? locationId = r.IsDBNull(locOrd) ? null : r.GetInt32(locOrd);

            var lineId = r.GetInt32(r.GetOrdinal("LineId"));
            var itemId = r.GetInt32(r.GetOrdinal("ItemId"));

            list.Add(new OpenOrderLineForReservationDto(
                LineId: lineId,
                OrderDocumentId: r.GetInt32(r.GetOrdinal("OrderDocumentId")),
                OrderNumber: r.GetString(r.GetOrdinal("OrderNumber")),
                OrderDate: r.GetDateTime(r.GetOrdinal("OrderDate")),
                ItemId: itemId,
                MaterialCode: r.IsDBNull(r.GetOrdinal("MaterialCode")) ? null : r.GetString(r.GetOrdinal("MaterialCode")),
                MaterialName: r.IsDBNull(r.GetOrdinal("MaterialName")) ? null : r.GetString(r.GetOrdinal("MaterialName")),
                UnitId: r.IsDBNull(r.GetOrdinal("UnitId")) ? null : r.GetInt32(r.GetOrdinal("UnitId")),
                UnitCode: r.IsDBNull(r.GetOrdinal("UnitCode")) ? null : r.GetString(r.GetOrdinal("UnitCode")),
                OrderQty: orderQty,
                DeliveredQty: deliveredDisplay,
                ReservedQty: reservedDisplay,
                OpenQty: openQty,
                AvailableStock: availableDisplay,
                IsKit: isKit,
                LocationId: locationId,
                LineNotes: r.IsDBNull(r.GetOrdinal("LineNotes")) ? null : r.GetString(r.GetOrdinal("LineNotes"))));

            if (isKit)
                kitPostProcess.Add((list.Count - 1, lineId, itemId, locationId, orderQty, deliveredDisplay));
        }

        // ── Faz 1.5 — kit satırları için bileşen-min available/reserved SET hesabı (ikinci geçiş,
        // reader kapandıktan sonra; aynı connection üzerinde art arda komut çalıştırılabilir). ──
        foreach (var kp in kitPostProcess)
        {
            var comps = await EnsureKitSnapshotAsync(conn, null, kp.LineId, kp.KitItemId, ct);
            decimal availableSets = 0m;
            decimal reservedSets = 0m;

            if (comps.Count > 0 && kp.LocationId is > 0)
            {
                var loc = kp.LocationId.Value;
                decimal? minSets = null;
                foreach (var c in comps)
                {
                    if (c.PerKit <= 0m) continue;
                    var physical = await GetPhysicalBalanceAsync(conn, null, c.CompItemId, loc, ct);
                    var reserved = await GetActiveReservedAsync(conn, null, c.CompItemId, loc, ct);
                    var availableComp = physical - reserved;
                    var setsForComp = Math.Max(0m, Math.Floor(availableComp / c.PerKit));
                    minSets = minSets is null ? setsForComp : Math.Min(minSets.Value, setsForComp);
                }
                availableSets = minSets ?? 0m;

                var refComp = comps[0];
                reservedSets = await GetKitReservedSetsAsync(conn, null, kp.LineId, refComp.CompItemId, refComp.PerKit, ct);
            }

            var openSets = Math.Max(0m, kp.OrderQtySets - kp.DeliveredDisplay - reservedSets);
            list[kp.Index] = list[kp.Index] with
            {
                AvailableStock = availableSets,
                OpenQty = openSets,
                ReservedQty = reservedSets,
                AvailableSets = availableSets,
                ReservedSets = reservedSets,
            };
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
                          AND sr.[KitOrderLineId] IS NULL
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
                    await ReserveKitLineAsync(conn, tx, row, req, request!, userId, balanceCache, committedThisBatch, created, skipped, ct);
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

    /// <summary>
    /// Faz 1.5 — KİT tam-set rezervasyonu (hepsi-ya-hiç, atomik). <paramref name="req"/>.Qty burada
    /// SET SAYISI olarak yorumlanır (tam sayı zorunlu). Snapshot yoksa EnsureKitSnapshotAsync ile
    /// canlı aktif kit içeriğinden dondurulur (lazy freeze). Her bileşenin (fiziksel−rezerve)/perKit
    /// tabanındaki kullanılabilir set sayısı talebi karşılamıyorsa TÜM kit reddedilir — hiçbir
    /// bileşen için kısmi rezervasyon yazılmaz (set bütünlüğü, CLAUDE.md kural #3: sessiz atlama yok).
    /// </summary>
    private async Task ReserveKitLineAsync(
        SqlConnection conn, SqlTransaction tx, OrderLineRow row, CreateReservationLineRequest req,
        CreateReservationRequest request, int? userId,
        Dictionary<(int ItemId, int LocationId), (decimal Physical, decimal ExistingReserved)> balanceCache,
        Dictionary<(int ItemId, int LocationId), decimal> committedThisBatch,
        List<CreateReservationResultItem> created, List<CreateReservationSkippedItem> skipped,
        CancellationToken ct)
    {
        var targetLocationId = request.LocationId ?? row.EffLocationId;
        if (targetLocationId is null or <= 0)
        {
            skipped.Add(new CreateReservationSkippedItem(req.OrderLineId,
                "Rezervasyon deposu belirlenemedi (sipariş kaleminde/başlığında depo yok)."));
            return;
        }

        var setsRaw = req.Qty;
        var setsRounded = Math.Round(setsRaw, 0, MidpointRounding.AwayFromZero);
        if (Math.Abs(setsRaw - setsRounded) > 0.0001m || setsRounded <= 0m)
        {
            skipped.Add(new CreateReservationSkippedItem(req.OrderLineId,
                $"Kit yalnız tam set olarak rezerve edilebilir (girilen: {setsRaw:0.####})."));
            return;
        }
        var sets = setsRounded;

        var comps = await EnsureKitSnapshotAsync(conn, tx, row.LineId, row.ItemId, ct);
        if (comps.Count == 0)
        {
            skipped.Add(new CreateReservationSkippedItem(req.OrderLineId,
                "Kit içeriği bulunamadı (tanım aktif değil veya bileşen yok)."));
            return;
        }

        // Açık SET kontrolü — kit satırının kendi orderQty/delivered/mevcut-rezerve(set) karşılaştırması.
        var refComp = comps[0];
        var existingReservedSets = await GetKitReservedSetsAsync(conn, tx, row.LineId, refComp.CompItemId, refComp.PerKit, ct);
        var factor = row.Qty != 0m ? row.BaseQty / row.Qty : 1m;
        var deliveredSetsDisplay = factor != 0m ? Math.Round(row.DeliveredBase / factor, 4) : row.DeliveredBase;
        var openSets = row.Qty - deliveredSetsDisplay - existingReservedSets;
        if (sets > openSets + 0.0001m)
        {
            skipped.Add(new CreateReservationSkippedItem(req.OrderLineId,
                $"Talep edilen set sayısı açık sipariş miktarını aşıyor (açık: {openSets:0.####} set)."));
            return;
        }

        // Hepsi-ya-hiç: her bileşenin kullanılabilir stoğu (talep×perKit) karşılamalı.
        var perComponentNeed = new List<(KitComponentRow Comp, decimal NeedBase)>();
        string? insufficientReason = null;
        foreach (var c in comps)
        {
            var needBase = Math.Round(sets * c.PerKit, 4);
            var locKeyC = (c.CompItemId, targetLocationId.Value);
            if (!balanceCache.TryGetValue(locKeyC, out var balC))
            {
                var physicalC = await GetPhysicalBalanceAsync(conn, tx, c.CompItemId, targetLocationId.Value, ct);
                var existingReservedC = await GetActiveReservedAsync(conn, tx, c.CompItemId, targetLocationId.Value, ct);
                balC = (physicalC, existingReservedC);
                balanceCache[locKeyC] = balC;
            }
            committedThisBatch.TryGetValue(locKeyC, out var committedC);
            var availableC = balC.Physical - balC.ExistingReserved - committedC;
            perComponentNeed.Add((c, needBase));

            if (needBase > availableC + 0.0001m && insufficientReason is null)
            {
                var availableSetsForComp = c.PerKit != 0m ? Math.Floor(availableC / c.PerKit) : 0m;
                insufficientReason = $"Yetersiz set (kullanılabilir: {availableSetsForComp:0.####}; kısıtlayan bileşen: {c.CompName ?? ("#" + c.CompItemId)}).";
            }
        }
        if (insufficientReason != null)
        {
            skipped.Add(new CreateReservationSkippedItem(req.OrderLineId, insufficientReason));
            return;
        }

        foreach (var (c, needBase) in perComponentNeed)
        {
            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("StockReservation")}
                        ([OrderDocumentId],[OrderLineId],[ItemId],[LocationId],[CombinationId],[UnitId],
                         [Quantity],[BaseQuantity],[Status],[KitOrderLineId],[PlannedShipDate],[Notes],[IsActive],
                         [CreatedById],[Created])
                    VALUES
                        (@OrderDocumentId,@OrderLineId,@ItemId,@LocationId,@CombinationId,@UnitId,
                         @Quantity,@BaseQuantity,1,@KitOrderLineId,@PlannedShipDate,@Notes,1,
                         @CreatedById,SYSUTCDATETIME());
                    """;
                ins.Parameters.AddWithValue("@OrderDocumentId", row.OrderDocumentId);
                ins.Parameters.AddWithValue("@OrderLineId", row.LineId);
                ins.Parameters.AddWithValue("@ItemId", c.CompItemId);
                ins.Parameters.AddWithValue("@LocationId", targetLocationId.Value);
                ins.Parameters.Add(new SqlParameter("@CombinationId", (object?)c.ConfigId ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@UnitId", (object?)c.UnitId ?? DBNull.Value));
                // Bileşende ayrı gösterim birimi yok — Quantity=BaseQuantity (Items.UnitId zaten baz birim).
                ins.Parameters.AddWithValue("@Quantity", needBase);
                ins.Parameters.AddWithValue("@BaseQuantity", needBase);
                ins.Parameters.AddWithValue("@KitOrderLineId", row.LineId);
                ins.Parameters.Add(new SqlParameter("@PlannedShipDate", (object?)request.PlannedShipDate ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Notes", (object?)request.Notes ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
                await ins.ExecuteNonQueryAsync(ct);
            }

            var locKeyC = (c.CompItemId, targetLocationId.Value);
            committedThisBatch.TryGetValue(locKeyC, out var committedPrev);
            committedThisBatch[locKeyC] = committedPrev + needBase;
        }

        created.Add(new CreateReservationResultItem(req.OrderLineId, sets, null));
    }

    private async Task<decimal> GetPhysicalBalanceAsync(
        SqlConnection conn, SqlTransaction? tx, int itemId, int locationId, CancellationToken ct)
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
        SqlConnection conn, SqlTransaction? tx, int itemId, int locationId, CancellationToken ct)
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
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Faz 1.5 — Kit set bütünlüğü: id'lerden biri kit bileşeniyse (KitOrderLineId dolu),
            // AYNI KitOrderLineId'ye ait TÜM aktif bileşen rezervasyonları da iptal kapsamına
            // genişletilir (yarım set kalmaz — CANCEL de CREATE/SHIP ile aynı bütünlüğü korur).
            var kitOrderLineIds = new HashSet<int>();
            await using (var kcmd = conn.CreateCommand())
            {
                kcmd.Transaction = tx;
                var pn = ids.Select((_, i) => "@id" + i).ToArray();
                kcmd.CommandText = $"""
                    SELECT DISTINCT [KitOrderLineId] FROM {T("StockReservation")}
                    WHERE [Id] IN ({string.Join(",", pn)}) AND [KitOrderLineId] IS NOT NULL;
                    """;
                for (var i = 0; i < ids.Count; i++) kcmd.Parameters.AddWithValue(pn[i], ids[i]);
                await using var kr = await kcmd.ExecuteReaderAsync(ct);
                while (await kr.ReadAsync(ct)) kitOrderLineIds.Add(kr.GetInt32(0));
            }

            if (kitOrderLineIds.Count > 0)
            {
                var kk = kitOrderLineIds.ToList();
                await using var expandCmd = conn.CreateCommand();
                expandCmd.Transaction = tx;
                var pn = kk.Select((_, i) => "@k" + i).ToArray();
                expandCmd.CommandText = $"""
                    SELECT [Id] FROM {T("StockReservation")}
                    WHERE [KitOrderLineId] IN ({string.Join(",", pn)}) AND [Status] = 1 AND [IsActive] = 1;
                    """;
                for (var i = 0; i < kk.Count; i++) expandCmd.Parameters.AddWithValue(pn[i], kk[i]);
                await using var er = await expandCmd.ExecuteReaderAsync(ct);
                while (await er.ReadAsync(ct)) ids.Add(er.GetInt32(0));
                ids = ids.Distinct().ToList();
            }

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            var paramList = string.Join(",", ids.Select((_, i) => $"@id{i}"));
            cmd.CommandText = $"""
                UPDATE {T("StockReservation")}
                SET [Status] = 3, [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
                WHERE [Id] IN ({paramList}) AND [Status] = 1 AND [IsActive] = 1;
                """;
            cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            for (var i = 0; i < ids.Count; i++)
                cmd.Parameters.Add(new SqlParameter($"@id{i}", ids[i]));

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return affected;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
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
                   r.[Quantity], r.[BaseQuantity], r.[Status], r.[PlannedShipDate], r.[Notes], r.[Created],
                   r.[KitOrderLineId], kc.[Quantity] AS PerKit
            FROM {T("StockReservation")} r
            LEFT JOIN {T("Items")}    i   ON i.[Id]   = r.[ItemId]
            LEFT JOIN {T("Location")} loc ON loc.[Id] = r.[LocationId]
            -- Faz 1.5 — kit bileşen satırında "set" gösterimi: kendi snapshot bileşeninin perKit'i.
            LEFT JOIN {T("DocumentLineKitComponent")} kc
                ON kc.[DocumentLineId] = r.[KitOrderLineId] AND kc.[ComponentItemId] = r.[ItemId]
                   AND (kc.[ConfigId] = r.[CombinationId] OR (kc.[ConfigId] IS NULL AND r.[CombinationId] IS NULL))
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
            var kitOrderLineIdOrd = r.GetOrdinal("KitOrderLineId");
            int? kitOrderLineId = r.IsDBNull(kitOrderLineIdOrd) ? null : r.GetInt32(kitOrderLineIdOrd);
            var perKitOrd = r.GetOrdinal("PerKit");
            var baseQuantity = r.GetDecimal(9);
            decimal? kitSets = (!r.IsDBNull(perKitOrd) && r.GetDecimal(perKitOrd) > 0m)
                ? Math.Round(baseQuantity / r.GetDecimal(perKitOrd), 4)
                : null;

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
                BaseQuantity: baseQuantity,
                Status: r.GetByte(10),
                PlannedShipDate: r.IsDBNull(11) ? null : r.GetDateTime(11),
                Notes: r.IsDBNull(12) ? null : r.GetString(12),
                Created: r.GetDateTime(13),
                KitOrderLineId: kitOrderLineId,
                KitSets: kitSets));
        }
        return list;
    }

    // ── Faz 2 (2026-07-31) — "Yükle": rezervasyon → satış irsaliyesi ──────────────────────────
    // Referans: SqlStockDocRepository.ConvertOrderToDeliveryAsync (:875) — aynı irsaliye satırı
    // yazım deseni (MovementType=1 çıkış, FromLocationId, SourceLineId, DeliveredQuantity artışı,
    // NegativeBalanceGuard) buraya BİREBİR uyarlandı. Farklar: (1) kaynak StockReservation satırları
    // (sipariş kaleminin TÜM açığı değil, yalnız o rezervasyonun miktarı) — TAM yükleme, kısmi yok;
    // (2) cari (Document.ContactId) başına TEK irsaliyede toplama (çok-sipariş olabilir) — SaveDeliveryFifoAsync
    // (:1420) ile aynı "tek kaynak sipariş → ParentDocumentId, çoklu → null" kuralı; (3) seri KAPSAM
    // DIŞI (Faz 3) — DocumentLineLink dual-write ve ResolveOrderSerialsToIssuedAsync bilinçli olarak
    // YAPILMADI (bu akış onlara dokunmuyor). (4) Faz 1.5 (2026-08-03) — kit bileşen rezervasyonları
    // (KitOrderLineId dolu) SqlStockDocRepository.ConvertOrderToDeliveryAsync'in kit patlatma deseniyle
    // (:1234) BİREBİR aynı yazılır: KİT BAŞLIK satırı (stok etkisiz, fiyatlı) + BİLEŞEN satırları
    // (gerçek stok çıkışı, fiyat 0, KitParentLineId=başlık). Set bütünlüğü: bir KitOrderLineId'nin
    // TÜM bileşenleri seçili DEĞİLSE o kit grubu TAMAMEN atlanır (yarım set irsaliyeye yazılmaz).

    private sealed record ShipReservationRow(
        int ReservationId, int OrderDocumentId, int OrderLineId, int ItemId, int LocationId,
        int? CombinationId, int? UnitId, decimal Qty, decimal BaseQty,
        decimal OrderQty, decimal UnitPrice, decimal DiscRate, decimal OrderLineTotal,
        int ContactId, string? ContactName,
        /// <summary>Faz 1.5 — dolu ise bu satır bir kit BİLEŞENİDİR (ItemId=bileşen); değer = kit
        /// sipariş kaleminin (DocumentLine.Id) kendisi (sr.OrderLineId ile AYNI).</summary>
        int? KitOrderLineId,
        /// <summary>Faz 1.5 — kit satırının KENDİ Item/Unit/Combination bilgisi (dl JOIN'i zaten kit
        /// satırına eşleniyor çünkü sr.OrderLineId=kit kalemi); kit BAŞLIK satırı yazımında kullanılır.</summary>
        int OrderLineItemId, int? OrderLineUnitId, int? OrderLineCombinationId);

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
                           ISNULL(d.[ContactId], 0) AS ContactId, ca.[AccountTitle] AS ContactName,
                           sr.[KitOrderLineId], dl.[ItemId] AS OrderLineItemId, dl.[UnitId] AS OrderLineUnitId,
                           dl.[CombinationId] AS OrderLineCombinationId
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
                        ContactName: r.IsDBNull(14) ? null : r.GetString(14),
                        KitOrderLineId: r.IsDBNull(15) ? null : r.GetInt32(15),
                        OrderLineItemId: r.GetInt32(16),
                        OrderLineUnitId: r.IsDBNull(17) ? null : r.GetInt32(17),
                        OrderLineCombinationId: r.IsDBNull(18) ? null : r.GetInt32(18)));
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

            // 2b) Faz 1.5 — kit set bütünlüğü: bir KitOrderLineId'nin (kit bileşen grubu) TÜM aktif
            //     bileşenleri seçili DEĞİLSE (kısmi seçim/kısmen zaten yüklenmiş/iptal), o grubun
            //     TÜM satırları Skipped'e düşer — yarım set irsaliyeye yazılmaz (CLAUDE.md kural #3).
            var excludedKitReservationIds = new HashSet<int>();
            foreach (var kg in validRows.Where(r => r.KitOrderLineId.HasValue).GroupBy(r => r.KitOrderLineId!.Value))
            {
                var kitOrderLineId = kg.Key;
                var kitRowsForGroup = kg.ToList();
                var kitItemId = kitRowsForGroup[0].OrderLineItemId;
                var snapshotComps = await EnsureKitSnapshotAsync(conn, tx, kitOrderLineId, kitItemId, ct);
                var expectedKeys = snapshotComps.Select(c => (c.CompItemId, c.ConfigId)).ToHashSet();
                var presentKeys = kitRowsForGroup.Select(r => (r.ItemId, r.CombinationId)).ToHashSet();
                var complete = expectedKeys.Count > 0 && expectedKeys.SetEquals(presentKeys);
                if (!complete)
                {
                    foreach (var row in kitRowsForGroup)
                    {
                        skipped.Add(new ShipReservationsSkippedItem(row.ReservationId,
                            "Kit setinin tüm bileşenleri seçili değil; kit yalnız TAM set olarak yüklenebilir."));
                        excludedKitReservationIds.Add(row.ReservationId);
                    }
                }
            }
            if (excludedKitReservationIds.Count > 0)
                validRows = validRows.Where(r => !excludedKitReservationIds.Contains(r.ReservationId)).ToList();

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

                // Faz 1.5 — kit bileşen satırlarını normal satırlardan ayır: kit grubu TEK DocumentLine
                // grubu (başlık+bileşenler) olarak yazılır, normal satırlar mevcut 1:1 desende kalır.
                var kitSubGroups = groupRows.Where(x => x.KitOrderLineId.HasValue)
                    .GroupBy(x => x.KitOrderLineId!.Value).ToList();
                var normalGroupRows = groupRows.Where(x => !x.KitOrderLineId.HasValue).ToList();

                var normalLineTotals = new decimal[normalGroupRows.Count];
                decimal subTotal = 0m;
                for (var i = 0; i < normalGroupRows.Count; i++)
                {
                    var row = normalGroupRows[i];
                    var lineTotal = row.OrderQty != 0m
                        ? Math.Round(row.OrderLineTotal * row.Qty / row.OrderQty, 4)
                        : row.OrderLineTotal;
                    normalLineTotals[i] = lineTotal;
                    subTotal += lineTotal;
                }

                // Kit grupları — sets = herhangi bir bileşenin BaseQuantity/PerKit oranı (hepsi-ya-hiç
                // atomik yazıldığından her bileşen aynı oranı verir; min() güvenlik için). Kit LineTotal,
                // kit sipariş satırının LineTotal'ından set oranıyla türetilir (bileşen satırları fiyatsız, 0).
                var kitSetsByGroup = new Dictionary<int, decimal>();
                var kitLineTotalByGroup = new Dictionary<int, decimal>();
                foreach (var kg in kitSubGroups)
                {
                    var kitOrderLineId = kg.Key;
                    var rowsForKit = kg.ToList();
                    var first = rowsForKit[0];
                    var perKitByComp = (await EnsureKitSnapshotAsync(conn, tx, kitOrderLineId, first.OrderLineItemId, ct))
                        .ToDictionary(c => (c.CompItemId, c.ConfigId), c => c.PerKit);

                    decimal? sets = null;
                    foreach (var row in rowsForKit)
                    {
                        if (perKitByComp.TryGetValue((row.ItemId, row.CombinationId), out var perKit) && perKit > 0m)
                        {
                            var s = Math.Round(row.BaseQty / perKit, 4);
                            sets = sets is null ? s : Math.Min(sets.Value, s);
                        }
                    }
                    var setsFinal = sets ?? 0m;
                    kitSetsByGroup[kitOrderLineId] = setsFinal;

                    var kitLineTotal = first.OrderQty != 0m
                        ? Math.Round(first.OrderLineTotal * setsFinal / first.OrderQty, 4)
                        : first.OrderLineTotal;
                    kitLineTotalByGroup[kitOrderLineId] = kitLineTotal;
                    subTotal += kitLineTotal;
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

                // ── Normal (kit-dışı) satırlar — mevcut davranış AYNEN (1 rezervasyon = 1 belge satırı). ──
                for (var i = 0; i < normalGroupRows.Count; i++)
                {
                    var row = normalGroupRows[i];
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
                        li.Parameters.AddWithValue("@LineTotal", normalLineTotals[i]);
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

                // ── Kit grupları — KİT BAŞLIK satırı (stok etkisiz, fiyatlı) + BİLEŞEN satırları (gerçek
                // stok çıkışı, fiyat 0, KitParentLineId=başlık). SqlStockDocRepository.ConvertOrderToDeliveryAsync
                // kit patlatma deseniyle (:1234) BİREBİR aynı — lot/seri KAPSAM DIŞI (Faz 3, bkz. sınıf üstü not). ──
                foreach (var kg in kitSubGroups)
                {
                    var kitOrderLineId = kg.Key;
                    var rowsForKit = kg.ToList();
                    var first = rowsForKit[0];
                    var setsFinal = kitSetsByGroup[kitOrderLineId];
                    var kitLineTotal = kitLineTotalByGroup[kitOrderLineId];

                    int headerLineId;
                    await using (var hk = conn.CreateCommand())
                    {
                        hk.Transaction = tx;
                        hk.CommandText = $"""
                            INSERT INTO {T("DocumentLine")}
                                ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                                 [CombinationId],[MovementType],[SourceLineId],[Notes])
                            VALUES
                                (@DocId,@LineNo,@ItemId,@UnitId,@Qty,@BaseQty,@UnitPrice,@DiscRate,@LineTotal,
                                 @CombId,NULL,@SourceLineId,@Notes);
                            SELECT CAST(SCOPE_IDENTITY() AS INT);
                            """;
                        hk.Parameters.AddWithValue("@DocId", docId);
                        hk.Parameters.AddWithValue("@LineNo", lineNo++);
                        hk.Parameters.AddWithValue("@ItemId", first.OrderLineItemId);
                        hk.Parameters.Add(new SqlParameter("@UnitId", (object?)first.OrderLineUnitId ?? DBNull.Value));
                        hk.Parameters.AddWithValue("@Qty", setsFinal);
                        hk.Parameters.AddWithValue("@BaseQty", setsFinal);
                        hk.Parameters.AddWithValue("@UnitPrice", first.UnitPrice);
                        hk.Parameters.AddWithValue("@DiscRate", first.DiscRate);
                        hk.Parameters.AddWithValue("@LineTotal", kitLineTotal);
                        hk.Parameters.Add(new SqlParameter("@CombId", (object?)first.OrderLineCombinationId ?? DBNull.Value));
                        hk.Parameters.AddWithValue("@SourceLineId", kitOrderLineId);
                        hk.Parameters.AddWithValue("@Notes", $"Yükleme planı → irsaliye (kit, {setsFinal:0.####} set)");
                        headerLineId = Convert.ToInt32(await hk.ExecuteScalarAsync(ct));
                    }

                    foreach (var row in rowsForKit)
                    {
                        await using (var cl = conn.CreateCommand())
                        {
                            cl.Transaction = tx;
                            cl.CommandText = $"""
                                INSERT INTO {T("DocumentLine")}
                                    ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                                     [CombinationId],[FromLocationId],[MovementType],[KitParentLineId],[Notes])
                                VALUES
                                    (@DocId,@LineNo,@ItemId,@UnitId,@Qty,@BaseQty,0,0,0,
                                     @CombId,@FromLoc,1,@KitParent,@Notes);
                                """;
                            cl.Parameters.AddWithValue("@DocId", docId);
                            cl.Parameters.AddWithValue("@LineNo", lineNo++);
                            cl.Parameters.AddWithValue("@ItemId", row.ItemId);
                            cl.Parameters.Add(new SqlParameter("@UnitId", (object?)row.UnitId ?? DBNull.Value));
                            cl.Parameters.AddWithValue("@Qty", row.Qty);
                            cl.Parameters.AddWithValue("@BaseQty", row.BaseQty);
                            cl.Parameters.Add(new SqlParameter("@CombId", (object?)row.CombinationId ?? DBNull.Value));
                            cl.Parameters.AddWithValue("@FromLoc", row.LocationId);
                            cl.Parameters.AddWithValue("@KitParent", headerLineId);
                            cl.Parameters.AddWithValue("@Notes", "Kit bileşeni — Yükleme planı → irsaliye");
                            await cl.ExecuteNonQueryAsync(ct);
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

                    // Kit sipariş kalemi DeliveredQuantity += setsFinal (kit satırında miktar birimi =
                    // set, ConvertOrderToDeliveryAsync ile AYNI konvansiyon — bileşen bazlı DEĞİL).
                    await using (var updKit = conn.CreateCommand())
                    {
                        updKit.Transaction = tx;
                        updKit.CommandText = $"UPDATE {T("DocumentLine")} SET [DeliveredQuantity] = [DeliveredQuantity] + @Add WHERE [Id] = @LineId;";
                        updKit.Parameters.AddWithValue("@Add", setsFinal);
                        updKit.Parameters.AddWithValue("@LineId", kitOrderLineId);
                        await updKit.ExecuteNonQueryAsync(ct);
                    }
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

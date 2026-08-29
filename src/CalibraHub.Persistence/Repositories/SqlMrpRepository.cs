using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// MRP veri erişimi (2026-08-29). Tüm okumalar TOPLUdur — bir koşu yüzlerce sipariş satırı
/// içerebilir; malzeme başına sorgu açmak (N+1) burada yasaktır.
///
/// <para><b>Bakiye konvansiyonu</b> <c>NegativeBalanceGuard</c> / <c>SqlStockReservationRepository</c>
/// ile BİREBİR AYNIDIR: <c>+ MovementType IN (2,3,4) AND LocationId = L</c>,
/// <c>- MovementType IN (1,3,4) AND FromLocationId = L</c>, kaynak <c>DocumentLine</c> +
/// <c>Document</c> (StockMovement tablosu DEĞİL), toplanan alan <c>BaseQuantity</c>.
/// İkinci bir bakiye tanımı üretmek, aynı stok için iki farklı rakam demek olurdu.</para>
///
/// <para><b>Açık sipariş konvansiyonu</b> da kanoniktir: <c>BaseQuantity − DeliveredQuantity</c>,
/// <c>Status NOT IN ('Rejected','Cancelled')</c> — <c>vw_ItemOpenSalesQty</c> ile aynı tanım.</para>
/// </summary>
public sealed class SqlMrpRepository : IMrpRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlMrpRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    /// <inheritdoc />
    public async Task<IReadOnlyList<MrpOpenOrderLineDto>> ListOpenSalesOrderLinesAsync(
        IReadOnlyCollection<int>? lineIds, int? documentId, string? search, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        var ids = (lineIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var lineFilter = ids.Count > 0
            ? $" AND dl.[Id] IN ({string.Join(",", ids.Select((_, i) => $"@lid{i}"))})"
            : string.Empty;
        var docFilter = documentId is > 0 ? " AND d.[Id] = @DocId" : string.Empty;
        var searchFilter = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : " AND (i.[Code] LIKE @Search OR i.[Name] LIKE @Search OR d.[DocumentNumber] LIKE @Search)";

        cmd.CommandText = $"""
            SELECT
                dl.[Id]                AS LineId,
                d.[Id]                 AS DocumentId,
                d.[DocumentNumber]     AS DocumentNumber,
                -- Cari adı Contact.AccountTitle'dan gelir; Document.ContactName kolonu Faz 2'de
                -- DROP edildi (bkz. SqlDocumentRepository.cs:252). Doğrudan okumak runtime'da
                -- "Invalid column name" verirdi.
                ca.[AccountTitle]      AS ContactName,
                d.[DeliveryDate]       AS DeliveryDate,
                i.[Id]                 AS ItemId,
                i.[Code]               AS ItemCode,
                i.[Name]               AS ItemName,
                i.[TypeId]             AS ItemTypeId,
                ISNULL(i.[WorkOrderSplitPolicy], N'PerOrderLine') AS SplitPolicy,
                dl.[CombinationId]     AS ConfigId,
                dl.[UnitId]            AS UnitId,
                u.[Code]               AS UnitCode,
                ISNULL(dl.[LocationId], d.[LocationId]) AS EffLocationId,
                dl.[Quantity]          AS OrderQty,
                dl.[BaseQuantity]      AS OrderBaseQty,
                dl.[DeliveredQuantity] AS DeliveredBaseQty,
                ISNULL(rsv.Reserved, 0)   AS ReservedBase,
                ISNULL(alloc.Allocated, 0) AS AllocatedQty
            FROM {T("DocumentLine")} dl
            INNER JOIN {T("Document")}     d  ON d.[Id]  = dl.[DocumentId]
            INNER JOIN {T("DocumentType")} dt ON dt.[Id] = d.[DocumentTypeId]
            INNER JOIN {T("Items")}        i  ON i.[Id]  = dl.[ItemId]
            LEFT  JOIN {T("Unit")}         u  ON u.[Id]  = dl.[UnitId]
            LEFT  JOIN {T("Contact")}      ca ON ca.[Id] = d.[ContactId]
            OUTER APPLY (
                -- Satırın KENDİ aktif rezervasyonu (kit bileşen satırları hariç: onlar farklı
                -- malzeme/birim taşır, bu kalemin rezerve toplamına karışmamalı).
                SELECT SUM(sr.[BaseQuantity]) AS Reserved
                FROM {T("StockReservation")} sr
                WHERE sr.[OrderLineId] = dl.[Id] AND sr.[Status] = 1 AND sr.[IsActive] = 1
                  AND sr.[KitOrderLineId] IS NULL
            ) rsv
            OUTER APPLY (
                -- İş emrine ZATEN tahsis edilmiş miktar (iptal edilmiş emirler hariç).
                -- tenant-ok: WorkOrder üzerinden süzülüyor (w.CompanyId).
                SELECT SUM(ws.[AllocatedQuantity]) AS Allocated
                FROM {T("WorkOrderSource")} ws
                INNER JOIN {T("WorkOrder")} w ON w.[Id] = ws.[WorkOrderId]
                WHERE ws.[SourceLineId] = dl.[Id]
                  AND w.[IsActive] = 1 AND w.[Status] <> 5
                  AND w.[CompanyId] = @CompanyId
            ) alloc
            WHERE dt.[Code] = N'satis_siparisi'
              AND d.[IsActive] = 1
              AND d.[CompanyId] = @CompanyId
              AND d.[Status] NOT IN (N'Rejected', N'Cancelled')
              AND dl.[MovementType] IS NULL
              AND dl.[ItemId] IS NOT NULL
              AND dl.[BaseQuantity] > dl.[DeliveredQuantity]
              {lineFilter}{docFilter}{searchFilter}
            ORDER BY d.[DeliveryDate], d.[DocumentNumber], dl.[LineNo];
            """;

        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.Add(new SqlParameter($"@lid{i}", ids[i]));
        if (documentId is > 0) cmd.Parameters.Add(new SqlParameter("@DocId", documentId.Value));
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.Add(new SqlParameter("@Search", "%" + search.Trim() + "%"));

        var result = new List<MrpOpenOrderLineDto>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var orderQty      = rd.GetDecimal(15);
            var orderBaseQty  = rd.GetDecimal(16);
            var deliveredBase = rd.GetDecimal(17);
            var reservedBase  = rd.GetDecimal(18);
            var allocated     = rd.GetDecimal(19);

            // Gösterim ↔ baz birim çevrimi. Kalem 10 KOLİ = 100 AD ise factor = 10; teslim/rezerve
            // miktarları BAZ birimde tutulur, kullanıcıya kalemin kendi biriminde gösterilir.
            var factor = orderQty != 0m ? orderBaseQty / orderQty : 1m;
            decimal ToDisplay(decimal baseQty) => factor != 0m ? Math.Round(baseQty / factor, 4) : baseQty;

            var deliveredDisplay = ToDisplay(deliveredBase);
            var reservedDisplay  = ToDisplay(reservedBase);
            var openDisplay      = Math.Max(0m, orderQty - deliveredDisplay);
            var typeId           = rd.IsDBNull(8) ? (int?)null : rd.GetInt32(8);

            result.Add(new MrpOpenOrderLineDto(
                LineId:            rd.GetInt32(0),
                DocumentId:        rd.GetInt32(1),
                DocumentNumber:    rd.IsDBNull(2) ? string.Empty : rd.GetString(2),
                ContactName:       rd.IsDBNull(3) ? null : rd.GetString(3),
                DeliveryDate:      rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                ItemId:            rd.GetInt32(5),
                ItemCode:          rd.IsDBNull(6) ? string.Empty : rd.GetString(6),
                ItemName:          rd.IsDBNull(7) ? string.Empty : rd.GetString(7),
                ConfigId:          rd.IsDBNull(10) ? null : rd.GetInt32(10),
                UnitId:            rd.IsDBNull(11) ? null : rd.GetInt32(11),
                UnitCode:          rd.IsDBNull(12) ? null : rd.GetString(12),
                LocationId:        rd.IsDBNull(13) ? null : rd.GetInt32(13),
                OrderQuantity:     orderQty,
                DeliveredQuantity: deliveredDisplay,
                ReservedQuantity:  reservedDisplay,
                AllocatedQuantity: allocated,
                OpenQuantity:      openDisplay,
                SplitPolicy:       rd.IsDBNull(9) ? WorkOrderSplitPolicyCatalog.Default : rd.GetString(9),
                IsProducible:      ItemTypeCatalog.IsProducible(typeId)));
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MrpAvailabilityRow>> GetAvailabilityAsync(
        IReadOnlyCollection<(int ItemId, int LocationId)> keys, CancellationToken ct)
    {
        var list = (keys ?? Array.Empty<(int, int)>())
            .Where(k => k.Item1 > 0 && k.Item2 > 0).Distinct().ToList();
        if (list.Count == 0) return Array.Empty<MrpAvailabilityRow>();

        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // (ItemId, LocationId) çiftleri VALUES tablosu olarak geçirilir → tek sorguda
        // tüm çiftlerin bakiyesi. Çift başına sorgu açmak N+1 olurdu.
        var values = string.Join(",", list.Select((_, i) => $"(@it{i}, @lo{i})"));
        cmd.CommandText = $"""
            WITH k([ItemId],[LocationId]) AS ( SELECT * FROM (VALUES {values}) v([ItemId],[LocationId]) )
            SELECT k.[ItemId], k.[LocationId],
                   ISNULL(bal.PhysicalBase, 0) AS PhysicalBase,
                   ISNULL(rsv.Reserved, 0)     AS ReservedBase
            FROM k
            OUTER APPLY (
                SELECT
                    ISNULL((SELECT SUM(si.[BaseQuantity])
                              FROM {T("DocumentLine")} si
                              INNER JOIN {T("Document")} sid ON sid.[Id] = si.[DocumentId]
                             WHERE si.[ItemId] = k.[ItemId] AND sid.[IsActive] = 1
                               AND sid.[CompanyId] = @CompanyId
                               AND si.[MovementType] IN (2,3,4) AND si.[LocationId] = k.[LocationId]), 0)
                  -
                    ISNULL((SELECT SUM(so.[BaseQuantity])
                              FROM {T("DocumentLine")} so
                              INNER JOIN {T("Document")} sod ON sod.[Id] = so.[DocumentId]
                             WHERE so.[ItemId] = k.[ItemId] AND sod.[IsActive] = 1
                               AND sod.[CompanyId] = @CompanyId
                               AND so.[MovementType] IN (1,3,4) AND so.[FromLocationId] = k.[LocationId]), 0)
                  AS PhysicalBase
            ) bal
            OUTER APPLY (
                -- tenant-ok: StockReservation INSERT'i CompanyId YAZMIYOR (bkz.
                -- SqlStockReservationRepository CreateReservationsAsync kolon listesi) → kolon
                -- yeni satırlarda NULL kalır. Buraya kiracı süzgeci koymak rezervasyonları
                -- sessizce ELER ve MRP rezerve edilmiş stok için de iş emri açardı. Süzme
                -- (ItemId, LocationId) üzerinden dolaylı yapılır; mevcut kanonik sorgu
                -- (GetActiveReservedAsync) da aynen böyledir.
                SELECT SUM(sr.[BaseQuantity]) AS Reserved
                FROM {T("StockReservation")} sr
                WHERE sr.[ItemId] = k.[ItemId] AND sr.[LocationId] = k.[LocationId]
                  AND sr.[Status] = 1 AND sr.[IsActive] = 1
            ) rsv;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        for (var i = 0; i < list.Count; i++)
        {
            cmd.Parameters.Add(new SqlParameter($"@it{i}", list[i].ItemId));
            cmd.Parameters.Add(new SqlParameter($"@lo{i}", list[i].LocationId));
        }

        var rows = new List<MrpAvailabilityRow>(list.Count);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            rows.Add(new MrpAvailabilityRow(rd.GetInt32(0), rd.GetInt32(1), rd.GetDecimal(2), rd.GetDecimal(3)));
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MrpOpenWorkOrderRow>> GetOpenWorkOrdersAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken ct)
    {
        var ids = (itemIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<MrpOpenWorkOrderRow>();

        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var ps = string.Join(",", ids.Select((_, i) => $"@it{i}"));

        cmd.CommandText = $"""
            SELECT w.[Id], d.[DocumentNumber], w.[ItemId], w.[ConfigId],
                   w.[PlannedQuantity],
                   w.[PlannedQuantity] - w.[ProducedQuantity] - w.[ScrapQuantity] AS RemainingQty,
                   w.[PlannedQuantity] - ISNULL(src.Allocated, 0) - ISNULL(peg.Pegged, 0) AS UnpeggedQty,
                   w.[PlannedEndDate], w.[Status]
            FROM {T("WorkOrder")} w
            LEFT JOIN {T("Document")} d ON d.[Id] = w.[DocumentId]
            OUTER APPLY (
                -- tenant-ok: ebeveyn WorkOrder zaten CompanyId ile süzülüyor.
                SELECT SUM(ws.[AllocatedQuantity]) AS Allocated
                FROM {T("WorkOrderSource")} ws WHERE ws.[WorkOrderId] = w.[Id]
            ) src
            OUTER APPLY (
                -- tenant-ok: ebeveyn WorkOrder zaten CompanyId ile süzülüyor.
                SELECT SUM(p.[Quantity]) AS Pegged
                FROM {T("WorkOrderPeg")} p WHERE p.[WorkOrderId] = w.[Id] AND p.[IsActive] = 1
            ) peg
            WHERE w.[IsActive] = 1
              AND w.[CompanyId] = @CompanyId
              AND w.[Status] IN (0, 1, 2)   -- Planned, Released, InProgress
              AND w.[ItemId] IN ({ps})
            ORDER BY w.[PlannedEndDate], w.[Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.Add(new SqlParameter($"@it{i}", ids[i]));

        var rows = new List<MrpOpenWorkOrderRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            rows.Add(new MrpOpenWorkOrderRow(
                WorkOrderId:        rd.GetInt32(0),
                DocumentNumber:     rd.IsDBNull(1) ? null : rd.GetString(1),
                ItemId:             rd.GetInt32(2),
                ConfigId:           rd.IsDBNull(3) ? null : rd.GetInt32(3),
                PlannedQuantity:    rd.GetDecimal(4),
                RemainingQuantity:  rd.GetDecimal(5),
                UnpeggedQuantity:   rd.GetDecimal(6),
                PlannedEndDate:     rd.IsDBNull(7) ? null : rd.GetDateTime(7),
                Status:             rd.GetByte(8)));
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MrpOpenPurchaseRow>> GetOpenPurchaseSupplyAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken ct)
    {
        var ids = (itemIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<MrpOpenPurchaseRow>();

        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var ps = string.Join(",", ids.Select((_, i) => $"@it{i}"));

        // View kiracı kolonunu projeksiyona alır (vw_ItemOpenSalesQty'nin aksine) → süzülebilir.
        cmd.CommandText = $"""
            SELECT [ItemId], [OpenQty], [EarliestExpectedDate]
            FROM {T("vw_ItemOpenPurchaseQty")}
            WHERE [CompanyId] = @CompanyId AND [ItemId] IN ({ps});
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.Add(new SqlParameter($"@it{i}", ids[i]));

        var rows = new List<MrpOpenPurchaseRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            rows.Add(new MrpOpenPurchaseRow(
                rd.GetInt32(0),
                rd.IsDBNull(1) ? 0m : rd.GetDecimal(1),
                rd.IsDBNull(2) ? null : rd.GetDateTime(2)));
        return rows;
    }

    /// <inheritdoc />
    public async Task<int> CreateRunAsync(
        string sourceScope, IReadOnlyCollection<int> selectedLineIds,
        IReadOnlyList<MrpRunLineRecord> lines, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int runId;
            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("MrpRun")}
                        ([CompanyId],[RunDate],[Status],[SourceScope],[SelectedLineIds],[CreatedById],[Created])
                    VALUES (@CompanyId, SYSUTCDATETIME(), 0, @Scope, @Lines, @User, SYSUTCDATETIME());
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                ins.Parameters.Add(new SqlParameter("@CompanyId", companyId));
                ins.Parameters.Add(new SqlParameter("@Scope", sourceScope));
                ins.Parameters.Add(new SqlParameter("@Lines", JsonSerializer.Serialize(selectedLineIds)));
                ins.Parameters.Add(new SqlParameter("@User", (object?)userId ?? DBNull.Value));
                runId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
            }

            // Satırlar yazılış sırasına göre eklenir; ParentRunLineId Faz 4'te ikinci geçişle
            // doldurulacak (Faz 2'de tüm satırlar Level 0, ebeveyn yok).
            foreach (var l in lines)
            {
                await using var li = conn.CreateCommand();
                li.Transaction = tx;
                li.CommandText = $"""
                    INSERT INTO {T("MrpRunLine")}
                        ([CompanyId],[MrpRunId],[Level],[ItemId],[ConfigId],[ActionType],
                         [GrossQuantity],[OnHandApplied],[OpenSupplyApplied],[NetQuantity],
                         [PlannedStartDate],[PlannedEndDate],[ParentRunLineId],[TargetWorkOrderId],
                         [PegJson],[Message],[CreatedById],[Created])
                    VALUES (@CompanyId,@RunId,@Level,@ItemId,@ConfigId,@ActionType,
                            @Gross,@OnHand,@Supply,@Net,
                            @Start,@End,@Parent,@Target,
                            @Peg,@Msg,@User,SYSUTCDATETIME());
                    """;
                li.Parameters.Add(new SqlParameter("@CompanyId", companyId));
                li.Parameters.Add(new SqlParameter("@RunId", runId));
                li.Parameters.Add(new SqlParameter("@Level", (byte)l.Level));
                li.Parameters.Add(new SqlParameter("@ItemId", l.ItemId));
                li.Parameters.Add(new SqlParameter("@ConfigId", (object?)l.ConfigId ?? DBNull.Value));
                li.Parameters.Add(new SqlParameter("@ActionType", l.ActionType));
                li.Parameters.Add(new SqlParameter("@Gross", l.GrossQuantity));
                li.Parameters.Add(new SqlParameter("@OnHand", l.OnHandApplied));
                li.Parameters.Add(new SqlParameter("@Supply", l.OpenSupplyApplied));
                li.Parameters.Add(new SqlParameter("@Net", l.NetQuantity));
                li.Parameters.Add(new SqlParameter("@Start", (object?)l.PlannedStartDate ?? DBNull.Value));
                li.Parameters.Add(new SqlParameter("@End", (object?)l.PlannedEndDate ?? DBNull.Value));
                li.Parameters.Add(new SqlParameter("@Parent", (object?)l.ParentRunLineId ?? DBNull.Value));
                li.Parameters.Add(new SqlParameter("@Target", (object?)l.TargetWorkOrderId ?? DBNull.Value));
                li.Parameters.Add(new SqlParameter("@Peg", (object?)l.PegJson ?? DBNull.Value));
                li.Parameters.Add(new SqlParameter("@Msg", (object?)l.Message ?? DBNull.Value));
                li.Parameters.Add(new SqlParameter("@User", (object?)userId ?? DBNull.Value));
                await li.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return runId;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* rollback hatası orijinal hatayı gizlemesin */ }
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(int Id, MrpRunStatus Status, DateTime RunDate)?> GetRunAsync(int runId, CancellationToken ct)
    {
        if (runId <= 0) return null;
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[Status],[RunDate] FROM {T("MrpRun")} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        cmd.Parameters.Add(new SqlParameter("@Id", runId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return null;
        return (rd.GetInt32(0), (MrpRunStatus)rd.GetByte(1), rd.GetDateTime(2));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MrpRunLineRecord>> GetRunLinesAsync(int runId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Level],[ParentRunLineId],[ItemId],[ConfigId],[ActionType],
                   [GrossQuantity],[OnHandApplied],[OpenSupplyApplied],[NetQuantity],
                   [PlannedStartDate],[PlannedEndDate],[TargetWorkOrderId],[PegJson],
                   [CreatedWorkOrderId],[CreatedDocumentId],[Message]
            FROM {T("MrpRunLine")}
            WHERE [MrpRunId] = @RunId AND [CompanyId] = @CompanyId AND [IsActive] = 1
            ORDER BY [Level], [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@RunId", runId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        var rows = new List<MrpRunLineRecord>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            rows.Add(new MrpRunLineRecord(
                Id:                rd.GetInt32(0),
                Level:             rd.GetByte(1),
                ParentRunLineId:   rd.IsDBNull(2) ? null : rd.GetInt32(2),
                ItemId:            rd.GetInt32(3),
                ConfigId:          rd.IsDBNull(4) ? null : rd.GetInt32(4),
                ActionType:        rd.GetString(5),
                GrossQuantity:     rd.GetDecimal(6),
                OnHandApplied:     rd.GetDecimal(7),
                OpenSupplyApplied: rd.GetDecimal(8),
                NetQuantity:       rd.GetDecimal(9),
                PlannedStartDate:  rd.IsDBNull(10) ? null : rd.GetDateTime(10),
                PlannedEndDate:    rd.IsDBNull(11) ? null : rd.GetDateTime(11),
                TargetWorkOrderId: rd.IsDBNull(12) ? null : rd.GetInt32(12),
                PegJson:           rd.IsDBNull(13) ? null : rd.GetString(13),
                CreatedWorkOrderId: rd.IsDBNull(14) ? null : rd.GetInt32(14),
                CreatedDocumentId:  rd.IsDBNull(15) ? null : rd.GetInt32(15),
                Message:           rd.IsDBNull(16) ? null : rd.GetString(16),
                LocationId:        null));
        return rows;
    }

    /// <inheritdoc />
    public async Task<bool> TryMarkRunAppliedAsync(int runId, string? summaryJson, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Koşullu UPDATE = tek adımda "hâlâ Draft mı" kontrolü + geçiş. Önce SELECT sonra
        // UPDATE yapılsaydı iki eşzamanlı apply arasında yarış olurdu.
        cmd.CommandText = $"""
            UPDATE {T("MrpRun")}
               SET [Status] = 1, [AppliedAt] = SYSUTCDATETIME(), [Summary] = @Summary,
                   [UpdatedById] = @User, [Updated] = SYSUTCDATETIME()
             WHERE [Id] = @Id AND [CompanyId] = @CompanyId AND [Status] = 0;
            SELECT @@ROWCOUNT;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", runId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@Summary", (object?)summaryJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@User", (object?)userId ?? DBNull.Value));
        var affected = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task DiscardRunAsync(int runId, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {T("MrpRun")}
               SET [Status] = 2, [UpdatedById] = @User, [Updated] = SYSUTCDATETIME()
             WHERE [Id] = @Id AND [CompanyId] = @CompanyId AND [Status] = 0;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", runId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@User", (object?)userId ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task SetRunLineResultAsync(int runLineId, int? workOrderId, int? documentId, string? message, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {T("MrpRunLine")}
               SET [CreatedWorkOrderId] = @Wo, [CreatedDocumentId] = @Doc,
                   [Message] = ISNULL(@Msg, [Message]), [Updated] = SYSUTCDATETIME()
             WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", runLineId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@Wo", (object?)workOrderId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Doc", (object?)documentId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Msg", (object?)message ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-07-02: stock_doc/stock_doc_line emekliye ayrildi (stok hareketi konsolidasyonu).
/// IStockDocRepository sozlesmesi AYNEN korunuyor — WarehouseController/PurchaseController/
/// InventoryCountImportHandler hic degismeden calismaya devam eder. Arka planda:
///   - TRANSFER / STOCK_IN / STOCK_OUT → Document + DocumentLine (MovementType hemen set edilir,
///     mevcut UX ile ayni — serbestce duzenlenebilir/silinebilir, "draft" kavramı YOK, hep canli).
///   - INVENTORY_COUNT → Document + InventoryCount (Status=Draft) + InventoryCountLine (ham
///     sayim girisi — stok hareketi YARATMAZ). Fark satirlarini DocumentLine'a yazan "Yansıt"
///     akisi ayri bir serviste (bkz. IInventoryCountService.ApplyAsync).
/// </summary>
public sealed class SqlStockDocRepository : IStockDocRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IDocumentNumberService _numberService;
    private readonly string _schema;

    public SqlStockDocRepository(
        SqlServerConnectionFactory connectionFactory,
        IDocumentNumberService numberService,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _numberService = numberService;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    private static string TypeCodeFor(string docType) => docType switch
    {
        "TRANSFER" => "depo_transfer",
        "STOCK_IN" => "depo_giris",
        "STOCK_OUT" => "depo_cikis",
        "INVENTORY_COUNT" => "sayim",
        _ => throw new ArgumentException($"Bilinmeyen depo belge tipi: {docType}"),
    };

    private static byte? MovementTypeFor(string docType) => docType switch
    {
        "STOCK_IN" => (byte)2,  // Receipt
        "STOCK_OUT" => (byte)1, // Issue
        "TRANSFER" => (byte)3,  // Transfer
        _ => null,              // INVENTORY_COUNT: DocumentLine kullanilmaz
    };

    public async Task<IReadOnlyList<StockDocDto>> GetByTypeAsync(string docType, CancellationToken ct)
        => await GetByTypesAsync([docType], ct);

    public async Task<IReadOnlyList<StockDocDto>> GetByTypesAsync(IEnumerable<string> docTypes, CancellationToken ct)
    {
        var types = docTypes.ToList();
        if (types.Contains("INVENTORY_COUNT"))
            return await GetInventoryCountDocsAsync(ct);
        return await GetDirectDocsAsync(types, ct);
    }

    private async Task<IReadOnlyList<StockDocDto>> GetDirectDocsAsync(IReadOnlyList<string> docTypes, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var codeList = docTypes.Select(TypeCodeFor).ToList();
        var paramList = string.Join(",", codeList.Select((_, i) => $"@t{i}"));

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT d.[Id], d.[CompanyId], dt.[Code], d.[DocumentNumber], d.[DocumentDate],
                   NULL AS FromLocationId, NULL AS FromLocationName,
                   d.[LocationId], loc.[LocationName] AS ToLocationName,
                   d.[Notes], d.[CreatedById], d.[Created], d.[IsActive],
                   (SELECT COUNT(*) FROM {T("DocumentLine")} dl WHERE dl.[DocumentId] = d.[Id]) AS LineCount,
                   d.[ParentDocumentId] AS ArgeProjectId, ap.[Name] AS ArgeProjectName
            FROM {T("Document")} d
            INNER JOIN {T("DocumentType")} dt ON dt.[Id] = d.[DocumentTypeId]
            LEFT JOIN {T("Location")} loc ON loc.[Id] = d.[LocationId]
            LEFT JOIN {T("ArgeProject")} ap ON ap.[DocumentId] = d.[ParentDocumentId]
            WHERE d.[CompanyId] = @CompanyId AND dt.[Code] IN ({paramList}) AND d.[IsActive] = 1
            ORDER BY d.[Created] DESC;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        for (var i = 0; i < codeList.Count; i++)
            cmd.Parameters.AddWithValue($"@t{i}", codeList[i]);

        var result = new List<StockDocDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(ReadDoc(r));
        return result;
    }

    private async Task<IReadOnlyList<StockDocDto>> GetInventoryCountDocsAsync(CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT d.[Id], d.[CompanyId], N'INVENTORY_COUNT' AS DocType, d.[DocumentNumber], d.[DocumentDate],
                   ic.[LocationId], loc.[LocationName] AS FromLocationName,
                   NULL AS ToLocationId, NULL AS ToLocationName,
                   d.[Notes], d.[CreatedById], d.[Created], d.[IsActive],
                   (SELECT COUNT(*) FROM {T("InventoryCountLine")} l WHERE l.[InventoryCountId] = ic.[Id]) AS LineCount,
                   NULL AS ArgeProjectId, NULL AS ArgeProjectName
            FROM {T("InventoryCount")} ic
            INNER JOIN {T("Document")} d ON d.[Id] = ic.[DocumentId]
            LEFT JOIN {T("Location")} loc ON loc.[Id] = ic.[LocationId]
            WHERE d.[CompanyId] = @CompanyId AND d.[IsActive] = 1
            ORDER BY d.[Created] DESC;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);

        var result = new List<StockDocDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(ReadDoc(r));
        return result;
    }

    public async Task<StockDocDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        if (await IsInventoryCountAsync(id, ct))
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT d.[Id], d.[CompanyId], N'INVENTORY_COUNT' AS DocType, d.[DocumentNumber], d.[DocumentDate],
                       ic.[LocationId], loc.[LocationName] AS FromLocationName,
                       NULL AS ToLocationId, NULL AS ToLocationName,
                       d.[Notes], d.[CreatedById], d.[Created], d.[IsActive],
                       (SELECT COUNT(*) FROM {T("InventoryCountLine")} l WHERE l.[InventoryCountId] = ic.[Id]) AS LineCount,
                       NULL AS ArgeProjectId, NULL AS ArgeProjectName
                FROM {T("InventoryCount")} ic
                INNER JOIN {T("Document")} d ON d.[Id] = ic.[DocumentId]
                LEFT JOIN {T("Location")} loc ON loc.[Id] = ic.[LocationId]
                WHERE d.[Id] = @Id AND d.[CompanyId] = @CompanyId;
                """;
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? ReadDoc(r) : null;
        }
        else
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT d.[Id], d.[CompanyId], dt.[Code], d.[DocumentNumber], d.[DocumentDate],
                       NULL AS FromLocationId, NULL AS FromLocationName,
                       d.[LocationId], loc.[LocationName] AS ToLocationName,
                       d.[Notes], d.[CreatedById], d.[Created], d.[IsActive],
                       (SELECT COUNT(*) FROM {T("DocumentLine")} dl WHERE dl.[DocumentId] = d.[Id]) AS LineCount,
                       d.[ParentDocumentId] AS ArgeProjectId, ap.[Name] AS ArgeProjectName
                FROM {T("Document")} d
                INNER JOIN {T("DocumentType")} dt ON dt.[Id] = d.[DocumentTypeId]
                LEFT JOIN {T("Location")} loc ON loc.[Id] = d.[LocationId]
                LEFT JOIN {T("ArgeProject")} ap ON ap.[DocumentId] = d.[ParentDocumentId]
                WHERE d.[Id] = @Id AND d.[CompanyId] = @CompanyId;
                """;
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? ReadDoc(r) : null;
        }
    }

    public async Task<IReadOnlyList<StockDocLineDto>> GetLinesAsync(int docId, CancellationToken ct)
    {
        if (await IsInventoryCountAsync(docId, ct))
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT l.[Id], ic.[DocumentId], CAST(ROW_NUMBER() OVER (ORDER BY l.[Id]) AS INT) AS [LineNo], l.[ItemId],
                       i.[Code] AS material_code, i.[Name] AS material_name,
                       l.[UnitId], u.[Code] AS unit_code,
                       l.[CountedQty], l.[ConfigId],
                       cfg.[RecordCode] AS combination_code,
                       l.[Notes],
                       COALESCE(l.[LocationId], ic.[LocationId]) AS FromLocationId,
                       loc.[LocationName] AS FromLocationName,
                       NULL AS ToLocationId, NULL AS ToLocationName,
                       NULL AS UnitCost, NULL AS LotNo,
                       NULL AS TrackingType, CAST(0 AS BIT) AS AutoSerial
                FROM {T("InventoryCountLine")} l
                INNER JOIN {T("InventoryCount")} ic ON ic.[Id] = l.[InventoryCountId]
                LEFT JOIN {T("Items")} i ON i.[Id] = l.[ItemId]
                LEFT JOIN {T("Unit")} u ON u.[Id] = l.[UnitId]
                LEFT JOIN {T("ItemConfiguration")} cfg ON cfg.[Id] = l.[ConfigId]
                LEFT JOIN {T("Location")} loc ON loc.[Id] = COALESCE(l.[LocationId], ic.[LocationId])
                WHERE ic.[DocumentId] = @DocId
                ORDER BY l.[Id];
                """;
            cmd.Parameters.AddWithValue("@DocId", docId);
            return await ReadLinesAsync(cmd, ct);
        }
        else
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT l.[Id], l.[DocumentId], l.[LineNo], l.[ItemId],
                       i.[Code] AS material_code, i.[Name] AS material_name,
                       l.[UnitId], u.[Code] AS unit_code,
                       l.[Quantity], l.[CombinationId],
                       cfg.[RecordCode] AS combination_code,
                       l.[Notes],
                       l.[FromLocationId], fl.[LocationName] AS from_location_name,
                       l.[LocationId],     tl.[LocationName] AS to_location_name,
                       l.[UnitCost], l.[LotNo],
                       ISNULL(i.[TrackingType], 'None') AS TrackingType,
                       ISNULL(i.[AutoSerial], 0) AS AutoSerial
                FROM {T("DocumentLine")} l
                LEFT JOIN {T("Items")} i ON i.[Id] = l.[ItemId]
                LEFT JOIN {T("Unit")} u ON u.[Id] = l.[UnitId]
                LEFT JOIN {T("ItemConfiguration")} cfg ON cfg.[Id] = l.[CombinationId]
                LEFT JOIN {T("Location")} fl ON fl.[Id] = l.[FromLocationId]
                LEFT JOIN {T("Location")} tl ON tl.[Id] = l.[LocationId]
                WHERE l.[DocumentId] = @DocId
                ORDER BY l.[LineNo];
                """;
            cmd.Parameters.AddWithValue("@DocId", docId);
            var lines = await ReadLinesAsync(cmd, ct);
            return await AttachSerialsAsync(conn, docId, lines, ct);
        }
    }

    /// <summary>Belgenin satırlarına bağlı seri no'ları (DocumentLineSerial) DTO'lara işler —
    /// edit ekranı seri listelerini yeniden yükleyebilsin diye.</summary>
    private async Task<IReadOnlyList<StockDocLineDto>> AttachSerialsAsync(
        SqlConnection conn, int docId, IReadOnlyList<StockDocLineDto> lines, CancellationToken ct)
    {
        if (lines.Count == 0) return lines;
        var map = new Dictionary<int, List<string>>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT dls.[DocumentLineId], s.[SerialNo]
                FROM {T("DocumentLineSerial")} dls
                INNER JOIN {T("ItemSerial")} s ON s.[Id] = dls.[SerialId]
                INNER JOIN {T("DocumentLine")} dl ON dl.[Id] = dls.[DocumentLineId]
                WHERE dl.[DocumentId] = @DocId
                ORDER BY s.[SerialNo];
                """;
            cmd.Parameters.AddWithValue("@DocId", docId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var lid = r.GetInt32(0);
                if (!map.TryGetValue(lid, out var list)) map[lid] = list = new List<string>();
                list.Add(r.GetString(1));
            }
        }
        if (map.Count == 0) return lines;
        return lines.Select(l => map.TryGetValue(l.Id, out var s) ? l with { Serials = s } : l).ToList();
    }

    public async Task<(int Id, string DocNo)> SaveAsync(SaveStockDocRequest request, int? createdById, CancellationToken ct)
    {
        if (request.DocType == "INVENTORY_COUNT")
            return await SaveInventoryCountAsync(request, createdById, ct);
        return await SaveDirectDocAsync(request, createdById, ct);
    }

    private async Task<(int Id, string DocNo)> SaveDirectDocAsync(SaveStockDocRequest request, int? createdById, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var typeCode = TypeCodeFor(request.DocType);
        var movementType = MovementTypeFor(request.DocType);
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int docId;
            string docNo;
            var notesWithRef = CombineNotesWithRef(request.Notes, request.RefNo);

            if (request.Id.HasValue && request.Id.Value > 0)
            {
                docId = request.Id.Value;
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE {T("Document")} SET
                        [DocumentDate] = @DocDate,
                        [LocationId]   = @ToLoc,
                        [Notes]        = @Notes,
                        [ParentDocumentId] = @ArgeProjectId
                    WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
                    SELECT [DocumentNumber] FROM {T("Document")} WHERE [Id] = @Id;
                    """;
                upd.Parameters.AddWithValue("@Id", docId);
                upd.Parameters.AddWithValue("@CompanyId", companyId);
                upd.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
                upd.Parameters.AddWithValue("@ToLoc", (object?)request.ToLocationId ?? DBNull.Value);
                upd.Parameters.AddWithValue("@Notes", (object?)notesWithRef ?? DBNull.Value);
                upd.Parameters.AddWithValue("@ArgeProjectId", (object?)request.ArgeProjectId ?? DBNull.Value);
                docNo = (string?)await upd.ExecuteScalarAsync(ct) ?? "";
            }
            else
            {
                docNo = await ResolveDocNoAsync(conn, tx, request.DocType, createdById, request.DocDate, ct);
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("Document")}
                        ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[LocationId],
                         [Notes],[Status],[CreatedById],[Created],[IsActive],[ParentDocumentId])
                    SELECT @CompanyId, @DocNo, dt.[Id], @DocDate, @ToLoc,
                           @Notes, N'Draft', @CreatedById, SYSUTCDATETIME(), 1, @ArgeProjectId
                    FROM {T("DocumentType")} dt WHERE dt.[Code] = @TypeCode;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                ins.Parameters.AddWithValue("@CompanyId", companyId);
                ins.Parameters.AddWithValue("@DocNo", docNo);
                ins.Parameters.AddWithValue("@TypeCode", typeCode);
                ins.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
                ins.Parameters.AddWithValue("@ToLoc", (object?)request.ToLocationId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@Notes", (object?)notesWithRef ?? DBNull.Value);
                ins.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ArgeProjectId", (object?)request.ArgeProjectId ?? DBNull.Value);
                docId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
            }

            // Seri durum geri alma (yeniden kayıt): bu belgenin ÇIKIŞ satırlarının Issued yaptığı
            // serileri stoğa döndür; GİRİŞ satırlarının yarattığı serileri aday listesine al —
            // satırlar silinip yeniden yazıldıktan sonra hiçbir satıra bağlı kalmayanlar emekli edilir.
            // (Yeni belgede eski satır yok → her iki sorgu no-op.)
            await using (var revert = conn.CreateCommand())
            {
                revert.Transaction = tx;
                revert.CommandText = $"""
                    UPDATE s SET s.[Status] = 1, s.[Updated] = SYSUTCDATETIME()
                    FROM {T("ItemSerial")} s
                    INNER JOIN {T("DocumentLineSerial")} dls ON dls.[SerialId] = s.[Id]
                    INNER JOIN {T("DocumentLine")} dl ON dl.[Id] = dls.[DocumentLineId]
                    WHERE dl.[DocumentId] = @DocId AND dl.[MovementType] = 1 AND s.[Status] = 2;
                    """;
                revert.Parameters.AddWithValue("@DocId", docId);
                await revert.ExecuteNonQueryAsync(ct);
            }
            var receiptSerialCandidates = new List<int>();
            await using (var cand = conn.CreateCommand())
            {
                cand.Transaction = tx;
                cand.CommandText = $"""
                    SELECT DISTINCT dls.[SerialId]
                    FROM {T("DocumentLineSerial")} dls
                    INNER JOIN {T("DocumentLine")} dl ON dl.[Id] = dls.[DocumentLineId]
                    WHERE dl.[DocumentId] = @DocId AND dl.[MovementType] = 2;
                    """;
                cand.Parameters.AddWithValue("@DocId", docId);
                await using var cr = await cand.ExecuteReaderAsync(ct);
                while (await cr.ReadAsync(ct)) receiptSerialCandidates.Add(cr.GetInt32(0));
            }

            // Kalemler: sil + yeniden ekle (mevcut stock_doc_line davranışıyla aynı — bu belge
            // tipleri icin "draft" kavramı yok, MovementType hemen set edilir).
            // Satır↔seri bağları (DocumentLineSerial) FK ON DELETE CASCADE ile birlikte silinir.
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {T("DocumentLine")} WHERE [DocumentId] = @DocId;";
                del.Parameters.AddWithValue("@DocId", docId);
                await del.ExecuteNonQueryAsync(ct);
            }

            var lineNo = 1;
            // Eksi bakiye kontrolü: azaltılan (item, kaynak lokasyon) çiftleri (STOCK_OUT / TRANSFER)
            var decreases = new HashSet<(int ItemId, int LocationId)>();
            // Lot bakiye kontrolü: azaltılan (item, lot, kaynak lokasyon) üçlüleri
            var lotDecreases = new HashSet<(int ItemId, int LotId, int LocationId)>();
            foreach (var line in request.Lines ?? [])
            {
                var itemId = line.ItemId;
                if ((!itemId.HasValue || itemId.Value <= 0) && !string.IsNullOrWhiteSpace(line.MaterialCode))
                    itemId = await ResolveItemIdByCodeAsync(conn, tx, line.MaterialCode!, ct);
                if (!itemId.HasValue || itemId.Value <= 0) continue;
                if (line.Qty <= 0) continue;

                // Lot çözümleme — lot-takipli stokta (TrackingType='Lot') zorunlu:
                // girişte yoksa oluşturulur, çıkış/transferde mevcut lot şarttır.
                var (lotId, lotNo) = await ResolveLotForLineAsync(
                    conn, tx, itemId.Value, line.MaterialCode, line.LotNo, movementType, createdById, ct);

                await using var lineIns = conn.CreateCommand();
                lineIns.Transaction = tx;
                lineIns.CommandText = $"""
                    INSERT INTO {T("DocumentLine")}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                         [CombinationId],[FromLocationId],[LocationId],[MovementType],[UnitCost],[LotId],[LotNo],[Notes])
                    VALUES
                        (@DocId,@LineNo,@ItemId,@UnitId,@Qty,{StockUnitSql.BaseQtyExpr(T("Items"), T("ItemUnits"), "@Qty", "@ItemId", "@UnitId")},0,0,0,
                         @CombId,@FromLoc,@ToLoc,@MovementType,
                         COALESCE(@UnitCost, (SELECT TOP 1 pl.[Price] FROM {T("PriceList")} pl
                                              WHERE pl.[ItemId] = @ItemId AND pl.[PriceType] = N'm' AND pl.[IsActive] = 1
                                                AND pl.[ValidFrom] <= @DocDate AND (pl.[ValidTo] IS NULL OR pl.[ValidTo] >= @DocDate)
                                              ORDER BY pl.[ValidFrom] DESC)),
                         @LotId, @LotNo, @Notes);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                lineIns.Parameters.AddWithValue("@DocId", docId);
                lineIns.Parameters.AddWithValue("@LineNo", lineNo++);
                lineIns.Parameters.AddWithValue("@ItemId", itemId.Value);
                lineIns.Parameters.AddWithValue("@UnitId", (object?)line.UnitId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Qty", line.Qty);
                lineIns.Parameters.AddWithValue("@CombId", (object?)line.CombinationId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@FromLoc", (object?)(line.FromLocationId ?? request.FromLocationId) ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@ToLoc", (object?)(line.ToLocationId ?? request.ToLocationId) ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@MovementType", (object?)movementType ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@UnitCost", (object?)line.UnitCost ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@LotId", (object?)lotId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@LotNo", (object?)lotNo ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Notes", (object?)line.Notes ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
                var lineId = Convert.ToInt32(await lineIns.ExecuteScalarAsync(ct));

                // Seri çözümleme — seri-takipli stokta (TrackingType='Serial') zorunlu:
                // girişte liste boşsa AutoSerial açık stok için otomatik üretilir;
                // çıkış/transferde stoktaki (InStock) serilerden adet kadar seçim şarttır.
                await ResolveSerialsForLineAsync(
                    conn, tx, lineId, itemId.Value, line.MaterialCode, line.Qty, line.Serials,
                    lotId, movementType, request.DocDate.Date, createdById, ct);

                // Azaltıcı hareket (çıkış=1 / transfer-kaynak=3) → kaynak lokasyonu kontrol kuyruğuna al
                if (movementType is 1 or 3)
                {
                    var fromLoc = line.FromLocationId ?? request.FromLocationId;
                    if (fromLoc is > 0)
                    {
                        decreases.Add((itemId.Value, fromLoc.Value));
                        if (lotId.HasValue) lotDecreases.Add((itemId.Value, lotId.Value, fromLoc.Value));
                    }
                }
            }

            // Tüm satırlar tx içinde yazıldı → eksi bakiye kontrolü (yeni satırlar hesaba dahil)
            foreach (var (dItem, dLoc) in decreases)
                await NegativeBalanceGuard.EnsureAsync(conn, tx, _schema, companyId, dItem, dLoc, request.DocDate.Date, ct);
            // Lot bakiyesi: azaltılan her lot kaynak lokasyonda eksiye düşemez (yeni satırlar dahil)
            foreach (var (dItem, dLot, dLoc) in lotDecreases)
                await EnsureLotBalanceAsync(conn, tx, companyId, dItem, dLot, dLoc, ct);

            // Düzenlemede listeden çıkarılan giriş serileri: yeniden yazım sonrası hiçbir
            // satıra bağlı kalmadıysa emekli et (fantom "stokta" seri kalmasın).
            foreach (var sid in receiptSerialCandidates)
            {
                await using var ret = conn.CreateCommand();
                ret.Transaction = tx;
                ret.CommandText = $"""
                    UPDATE {T("ItemSerial")} SET [IsActive] = 0, [Updated] = SYSUTCDATETIME()
                    WHERE [Id] = @Sid
                      AND NOT EXISTS (SELECT 1 FROM {T("DocumentLineSerial")} WHERE [SerialId] = @Sid);
                    """;
                ret.Parameters.AddWithValue("@Sid", sid);
                await ret.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return (docId, docNo);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<(int Id, string DocNo)> SaveInventoryCountAsync(SaveStockDocRequest request, int? createdById, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int docId;
            int countId;
            string docNo;
            var notesWithRef = CombineNotesWithRef(request.Notes, request.RefNo);

            if (request.Id.HasValue && request.Id.Value > 0)
            {
                docId = request.Id.Value;
                // Yansıtılmış (Applied) sayım immutable — kalem DELETE/INSERT snapshot'ı bozar.
                // Yalnızca Draft düzenlenebilir (aşağıdaki header UPDATE zaten WHERE Status=0).
                await using (var st = conn.CreateCommand())
                {
                    st.Transaction = tx;
                    st.CommandText = $"SELECT [Status] FROM {T("InventoryCount")} WHERE [DocumentId] = @Id;";
                    st.Parameters.AddWithValue("@Id", docId);
                    var stObj = await st.ExecuteScalarAsync(ct);
                    if (stObj is not (null or DBNull) && Convert.ToInt32(stObj) != 0)
                        throw new InvalidOperationException("Yansıtılmış sayım düzenlenemez.");
                }
                await using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = $"""
                        UPDATE {T("Document")} SET [DocumentDate] = @DocDate, [Notes] = @Notes
                        WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
                        UPDATE {T("InventoryCount")} SET [LocationId] = @LocId
                        WHERE [DocumentId] = @Id AND [Status] = 0;
                        SELECT [DocumentNumber] FROM {T("Document")} WHERE [Id] = @Id;
                        """;
                    upd.Parameters.AddWithValue("@Id", docId);
                    upd.Parameters.AddWithValue("@CompanyId", companyId);
                    upd.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
                    // Sayım lokasyonu UI/import handler tarafından FromLocationId'de gönderilir
                    // (bkz. InventoryEdit.cshtml, InventoryCountImportHandler.cs) — ToLocationId değil.
                    upd.Parameters.AddWithValue("@LocId", (object?)request.FromLocationId ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@Notes", (object?)notesWithRef ?? DBNull.Value);
                    docNo = (string?)await upd.ExecuteScalarAsync(ct) ?? "";
                }
                await using (var getCount = conn.CreateCommand())
                {
                    getCount.Transaction = tx;
                    getCount.CommandText = $"SELECT [Id] FROM {T("InventoryCount")} WHERE [DocumentId] = @Id;";
                    getCount.Parameters.AddWithValue("@Id", docId);
                    countId = Convert.ToInt32(await getCount.ExecuteScalarAsync(ct));
                }
            }
            else
            {
                docNo = await ResolveDocNoAsync(conn, tx, request.DocType, createdById, request.DocDate, ct);
                await using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = $"""
                        INSERT INTO {T("Document")}
                            ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[Notes],[Status],[CreatedById],[Created],[IsActive])
                        SELECT @CompanyId, @DocNo, dt.[Id], @DocDate, @Notes, N'Draft', @CreatedById, SYSUTCDATETIME(), 1
                        FROM {T("DocumentType")} dt WHERE dt.[Code] = N'sayim';
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                        """;
                    ins.Parameters.AddWithValue("@CompanyId", companyId);
                    ins.Parameters.AddWithValue("@DocNo", docNo);
                    ins.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
                    ins.Parameters.AddWithValue("@Notes", (object?)notesWithRef ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
                    docId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
                }
                await using (var insCount = conn.CreateCommand())
                {
                    insCount.Transaction = tx;
                    insCount.CommandText = $"""
                        INSERT INTO {T("InventoryCount")} ([DocumentId],[LocationId],[CountDate],[Status],[CreatedById],[Created])
                        VALUES (@DocId, @LocId, @DocDate, 0, @CreatedById, SYSUTCDATETIME());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                        """;
                    insCount.Parameters.AddWithValue("@DocId", docId);
                    insCount.Parameters.AddWithValue("@LocId", (object?)request.FromLocationId ?? DBNull.Value);
                    insCount.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
                    insCount.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
                    countId = Convert.ToInt32(await insCount.ExecuteScalarAsync(ct));
                }
            }

            // Draft'ta ise serbestce sil + yeniden ekle. Status != Draft ise (zaten Yansitilmis)
            // asagidaki DELETE hicbir satiri etkilemez ama INSERT'ler duplicate yaratir —
            // caller (WarehouseController) Applied sayimi zaten duzenlemeye acmamali.
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {T("InventoryCountLine")} WHERE [InventoryCountId] = @CountId;";
                del.Parameters.AddWithValue("@CountId", countId);
                await del.ExecuteNonQueryAsync(ct);
            }

            foreach (var line in request.Lines ?? [])
            {
                // ItemId gelmemişse MaterialCode'dan çöz — rehber modal bazı akışlarda
                // yalnızca kod alanını doldurur (Id kolonu görünmeyen rehber view'ları).
                var itemId = line.ItemId;
                if ((!itemId.HasValue || itemId.Value <= 0) && !string.IsNullOrWhiteSpace(line.MaterialCode))
                    itemId = await ResolveItemIdByCodeAsync(conn, tx, line.MaterialCode!, ct);
                if (!itemId.HasValue || itemId.Value <= 0) continue;
                // Sıfır sayım geçerli giriştir ("saydım, yok") — yalnızca negatif atlanır.
                if (line.Qty < 0) continue;

                await using var lineIns = conn.CreateCommand();
                lineIns.Transaction = tx;
                lineIns.CommandText = $"""
                    INSERT INTO {T("InventoryCountLine")}
                        ([InventoryCountId],[ItemId],[ConfigId],[UnitId],[CountedQty],[Notes],[LocationId])
                    VALUES
                        (@CountId,@ItemId,@ConfigId,@UnitId,@Qty,@Notes,@LocId);
                    """;
                lineIns.Parameters.AddWithValue("@CountId", countId);
                lineIns.Parameters.AddWithValue("@ItemId", itemId.Value);
                lineIns.Parameters.AddWithValue("@ConfigId", (object?)line.CombinationId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@UnitId", (object?)line.UnitId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Qty", line.Qty);
                lineIns.Parameters.AddWithValue("@Notes", (object?)line.Notes ?? DBNull.Value);
                // Kalem bazinda depo — satirda yoksa header (sayim deposu) devralinir
                lineIns.Parameters.AddWithValue("@LocId", (object?)(line.FromLocationId ?? request.FromLocationId) ?? DBNull.Value);
                await lineIns.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return (docId, docNo);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<(int Id, string DocNo)> DeliverSalesOrderAsync(int salesOrderId, int? createdById, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Açık satırlar (ticari satır=MovementType NULL, açık = BaseQuantity - DeliveredQuantity > 0).
            //    Depo = satır LocationId, yoksa siparişin belge LocationId'si.
            var open = new List<(int LineId, int ItemId, int? CombId, int? LocId, decimal OpenBase)>();
            await using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = $"""
                    SELECT dl.[Id], dl.[ItemId], dl.[CombinationId],
                           ISNULL(dl.[LocationId], doc.[LocationId]) AS LocId,
                           (dl.[BaseQuantity] - dl.[DeliveredQuantity]) AS OpenBase
                    FROM {T("DocumentLine")} dl
                    INNER JOIN {T("Document")} doc ON doc.[Id] = dl.[DocumentId]
                    WHERE dl.[DocumentId] = @OrderId AND doc.[CompanyId] = @Cid
                      AND dl.[MovementType] IS NULL AND dl.[ItemId] IS NOT NULL
                      AND dl.[BaseQuantity] > dl.[DeliveredQuantity];
                    """;
                sel.Parameters.AddWithValue("@OrderId", salesOrderId);
                sel.Parameters.AddWithValue("@Cid", companyId);
                await using var r = await sel.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    open.Add((r.GetInt32(0), r.GetInt32(1),
                             r.IsDBNull(2) ? null : r.GetInt32(2),
                             r.IsDBNull(3) ? null : r.GetInt32(3),
                             r.GetDecimal(4)));
            }
            if (open.Count == 0)
                throw new InvalidOperationException("Teslim edilecek açık kalem yok.");
            foreach (var o in open)
                if (!o.LocId.HasValue)
                    throw new InvalidOperationException("Bazı sipariş kalemlerinde depo tanımlı değil; teslimat için kalem veya belge deposu gerekli.");

            // 2) Yeni STOCK_OUT (depo_cikis) çıkış belgesi — siparişe ParentDocumentId ile bağlı
            var docNo = await ResolveDocNoAsync(conn, tx, "STOCK_OUT", createdById, DateTime.Today, ct);
            int docId;
            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("Document")}
                        ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[LocationId],
                         [Notes],[Status],[CreatedById],[Created],[IsActive],[ParentDocumentId])
                    SELECT @CompanyId, @DocNo, dt.[Id], @DocDate, NULL,
                           @Notes, N'Draft', @CreatedById, SYSUTCDATETIME(), 1, @OrderId
                    FROM {T("DocumentType")} dt WHERE dt.[Code] = N'depo_cikis';
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                ins.Parameters.AddWithValue("@CompanyId", companyId);
                ins.Parameters.AddWithValue("@DocNo", docNo);
                ins.Parameters.AddWithValue("@DocDate", DateTime.Today);
                ins.Parameters.AddWithValue("@Notes", $"Satış siparişi teslimatı (#{salesOrderId})");
                ins.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
                ins.Parameters.AddWithValue("@OrderId", salesOrderId);
                docId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
            }

            // 3) Her açık satır → ana birimde çıkış hareketi (MovementType=1) + sipariş satırı DeliveredQuantity=BaseQuantity
            var lineNo = 1;
            var decreases = new HashSet<(int ItemId, int LocId)>();
            foreach (var o in open)
            {
                await using (var li = conn.CreateCommand())
                {
                    li.Transaction = tx;
                    li.CommandText = $"""
                        INSERT INTO {T("DocumentLine")}
                            ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                             [CombinationId],[FromLocationId],[LocationId],[MovementType],[Notes])
                        VALUES
                            (@DocId,@LineNo,@ItemId,NULL,@Qty,@Qty,0,0,0,@CombId,@FromLoc,NULL,1,@Notes);
                        """;
                    li.Parameters.AddWithValue("@DocId", docId);
                    li.Parameters.AddWithValue("@LineNo", lineNo++);
                    li.Parameters.AddWithValue("@ItemId", o.ItemId);
                    li.Parameters.AddWithValue("@Qty", o.OpenBase);
                    li.Parameters.AddWithValue("@CombId", (object?)o.CombId ?? DBNull.Value);
                    li.Parameters.AddWithValue("@FromLoc", o.LocId!.Value);
                    li.Parameters.AddWithValue("@Notes", $"Sipariş #{salesOrderId} teslimat");
                    await li.ExecuteNonQueryAsync(ct);
                }
                await using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = $"UPDATE {T("DocumentLine")} SET [DeliveredQuantity] = [BaseQuantity] WHERE [Id] = @LineId;";
                    upd.Parameters.AddWithValue("@LineId", o.LineId);
                    await upd.ExecuteNonQueryAsync(ct);
                }
                decreases.Add((o.ItemId, o.LocId.Value));
            }

            // 4) Eksi bakiye kontrolü (fiziksel çıkış) — yeni satırlar + serbest kalan rezervasyon tx içinde
            foreach (var (it, loc) in decreases)
                await NegativeBalanceGuard.EnsureAsync(conn, tx, _schema, companyId, it, loc, DateTime.Today, ct);

            await tx.CommitAsync(ct);
            return (docId, docNo);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Seri durum düzeltmeleri (belge pasifleşince miktar bakiyesi zaten IsActive=1
            // filtresiyle geri döner; seri durumları da tutarlı kalsın):
            // 1) Çıkış satırlarının Issued yaptığı seriler stoğa geri döner.
            await using (var revert = conn.CreateCommand())
            {
                revert.Transaction = tx;
                revert.CommandText = $"""
                    UPDATE s SET s.[Status] = 1, s.[Updated] = SYSUTCDATETIME()
                    FROM {T("ItemSerial")} s
                    INNER JOIN {T("DocumentLineSerial")} dls ON dls.[SerialId] = s.[Id]
                    INNER JOIN {T("DocumentLine")} dl ON dl.[Id] = dls.[DocumentLineId]
                    WHERE dl.[DocumentId] = @Id AND dl.[MovementType] = 1 AND s.[Status] = 2;
                    """;
                revert.Parameters.AddWithValue("@Id", id);
                await revert.ExecuteNonQueryAsync(ct);
            }
            // 2) Giriş satırlarının yarattığı seriler: başka aktif belgede bağı yoksa emekli edilir.
            await using (var retire = conn.CreateCommand())
            {
                retire.Transaction = tx;
                retire.CommandText = $"""
                    UPDATE s SET s.[IsActive] = 0, s.[Updated] = SYSUTCDATETIME()
                    FROM {T("ItemSerial")} s
                    WHERE s.[Id] IN (
                            SELECT dls.[SerialId]
                            FROM {T("DocumentLineSerial")} dls
                            INNER JOIN {T("DocumentLine")} dl ON dl.[Id] = dls.[DocumentLineId]
                            WHERE dl.[DocumentId] = @Id AND dl.[MovementType] = 2)
                      AND NOT EXISTS (
                            SELECT 1
                            FROM {T("DocumentLineSerial")} dls2
                            INNER JOIN {T("DocumentLine")} dl2 ON dl2.[Id] = dls2.[DocumentLineId]
                            INNER JOIN {T("Document")} d2 ON d2.[Id] = dl2.[DocumentId]
                            WHERE dls2.[SerialId] = s.[Id] AND dl2.[DocumentId] <> @Id AND d2.[IsActive] = 1);
                    """;
                retire.Parameters.AddWithValue("@Id", id);
                await retire.ExecuteNonQueryAsync(ct);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {T("Document")} SET [IsActive]=0 WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────────

    private async Task<bool> IsInventoryCountAsync(int documentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM {T("InventoryCount")} WHERE [DocumentId] = @Id;";
        cmd.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>
    /// Lot-takipli stokta (Items.TrackingType='Lot') satır lotunu çözer: giriş (2) → lot yoksa
    /// oluşturur; çıkış/transfer (1/3) → mevcut lot şart (yazım hatası yeni lot üretmesin).
    /// Lot-takipli değilse serbest metin LotNo aynen korunur (legacy davranış, LotId=null).
    /// Dönen LotNo, DocumentLine'a yazılacak denormalize display kopyasıdır.
    /// </summary>
    private async Task<(int? LotId, string? LotNo)> ResolveLotForLineAsync(
        SqlConnection conn, SqlTransaction tx, int itemId, string? materialCode,
        string? rawLotNo, byte? movementType, int? createdById, CancellationToken ct)
    {
        var lotNo = string.IsNullOrWhiteSpace(rawLotNo) ? null : rawLotNo.Trim();

        await using (var typeCmd = conn.CreateCommand())
        {
            typeCmd.Transaction = tx;
            typeCmd.CommandText = $"SELECT ISNULL([TrackingType], 'None') FROM {T("Items")} WHERE [Id] = @Id;";
            typeCmd.Parameters.AddWithValue("@Id", itemId);
            var tracking = await typeCmd.ExecuteScalarAsync(ct) as string;
            if (!string.Equals(tracking, "Lot", StringComparison.OrdinalIgnoreCase))
                return (null, lotNo);
        }

        var label = string.IsNullOrWhiteSpace(materialCode) ? $"#{itemId}" : materialCode.Trim();
        if (lotNo is null)
            throw new InvalidOperationException(
                $"'{label}' lot takipli bir stok — satırda Lot / Parti No girilmesi zorunlu.");

        await using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = $"SELECT TOP 1 [Id] FROM {T("Lot")} WHERE [ItemId] = @ItemId AND [LotNo] = @LotNo;";
            find.Parameters.AddWithValue("@ItemId", itemId);
            find.Parameters.AddWithValue("@LotNo", lotNo);
            if (await find.ExecuteScalarAsync(ct) is int existingId)
                return (existingId, lotNo);
        }

        if (movementType is 1 or 3)
            throw new InvalidOperationException(
                $"'{label}' için '{lotNo}' lotu bulunamadı — çıkış/transfer yalnızca mevcut bir lottan yapılabilir. " +
                "Önce ambar girişiyle lotu oluşturun.");

        await using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = $"""
            INSERT INTO {T("Lot")} ([ItemId],[LotNo],[CreatedById]) VALUES (@ItemId, @LotNo, @CreatedById);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        ins.Parameters.AddWithValue("@ItemId", itemId);
        ins.Parameters.AddWithValue("@LotNo", lotNo);
        ins.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
        return (Convert.ToInt32(await ins.ExecuteScalarAsync(ct)), lotNo);
    }

    /// <summary>
    /// Azaltılan (stok, lot, kaynak lokasyon) için toplam lot bakiyesi (yeni satırlar dahil,
    /// ana birim) eksiye düşemez. Lot-takip öncesi LotId'siz eski hareketler lot bakiyesine
    /// sayılmaz — takibe yeni alınan stokta önce lotlu giriş/düzeltme yapılmalıdır.
    /// İşaret konvansiyonu NegativeBalanceGuard ile birebir aynıdır.
    /// </summary>
    private async Task EnsureLotBalanceAsync(
        SqlConnection conn, SqlTransaction tx, int companyId, int itemId, int lotId, int locationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT
                ISNULL(SUM(CASE WHEN dl.[MovementType] IN (2,3,4) AND dl.[LocationId]     = @L THEN dl.[BaseQuantity]
                                WHEN dl.[MovementType] IN (1,3,4) AND dl.[FromLocationId] = @L THEN -dl.[BaseQuantity]
                                ELSE 0 END), 0) AS bal,
                (SELECT TOP 1 [LotNo] FROM {T("Lot")} WHERE [Id] = @LotId) AS lot_no,
                (SELECT TOP 1 ISNULL([Name], [Code]) FROM {T("Items")} WHERE [Id] = @ItemId) AS item_label,
                (SELECT TOP 1 ISNULL([LocationName], [LocationCode]) FROM {T("Location")} WHERE [Id] = @L) AS loc_label
            FROM {T("DocumentLine")} dl
            INNER JOIN {T("Document")} doc ON doc.[Id] = dl.[DocumentId]
            WHERE dl.[ItemId] = @ItemId AND dl.[LotId] = @LotId
              AND doc.[CompanyId] = @Cid AND doc.[IsActive] = 1
              AND dl.[MovementType] IN (1,2,3,4)
              AND (dl.[LocationId] = @L OR dl.[FromLocationId] = @L);
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@LotId", lotId);
        cmd.Parameters.AddWithValue("@L", locationId);
        cmd.Parameters.AddWithValue("@Cid", companyId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return;
        var bal = r.IsDBNull(0) ? 0m : r.GetDecimal(0);
        if (bal >= 0m) return;
        var lotNo = r.IsDBNull(1) ? $"#{lotId}" : r.GetString(1);
        var itemLabel = r.IsDBNull(2) ? $"#{itemId}" : r.GetString(2);
        var locLabel = r.IsDBNull(3) ? $"#{locationId}" : r.GetString(3);
        throw new InvalidOperationException(
            $"Lot bakiyesi yetersiz: {itemLabel} — Lot '{lotNo}' ({locLabel}). Eksik: {-bal:N2} ana birim. " +
            "Not: Lot takibi öncesi lotsuz girişler lot bakiyesine sayılmaz.");
    }

    /// <summary>
    /// Seri-takipli stokta (Items.TrackingType='Serial') satırın serilerini çözer ve satıra bağlar
    /// (DocumentLineSerial). Kurallar: miktar tam sayı, seri sayısı = miktar. GİRİŞ (2): seri
    /// listesi boşsa ve stok AutoSerial ise ItemCode-yyMMdd-NNN üretilir; Issued/pasif seri
    /// girişle stoğa döner (iade), başka belgeyle stokta olan seri tekrar giremez.
    /// ÇIKIŞ (1): seriler InStock olmalı → Issued. TRANSFER (3): InStock şart, durum değişmez.
    /// Seri-takipli olmayan stokta no-op.
    /// </summary>
    private async Task ResolveSerialsForLineAsync(
        SqlConnection conn, SqlTransaction tx, int lineId, int itemId, string? materialCode,
        decimal qty, IReadOnlyList<string>? rawSerials, int? lotId, byte? movementType,
        DateTime docDate, int? createdById, CancellationToken ct)
    {
        string tracking = "None"; var autoSerial = false;
        var itemCode = string.IsNullOrWhiteSpace(materialCode) ? $"#{itemId}" : materialCode.Trim();
        await using (var info = conn.CreateCommand())
        {
            info.Transaction = tx;
            info.CommandText = $"SELECT ISNULL([TrackingType],'None'), ISNULL([AutoSerial],0), [Code] FROM {T("Items")} WHERE [Id] = @Id;";
            info.Parameters.AddWithValue("@Id", itemId);
            await using var ir = await info.ExecuteReaderAsync(ct);
            if (await ir.ReadAsync(ct))
            {
                tracking = ir.GetString(0);
                autoSerial = ir.GetBoolean(1);
                if (!ir.IsDBNull(2)) itemCode = ir.GetString(2);
            }
        }
        if (!string.Equals(tracking, "Serial", StringComparison.OrdinalIgnoreCase)) return;

        if (qty != decimal.Truncate(qty))
            throw new InvalidOperationException($"'{itemCode}' seri takipli — miktar tam sayı olmalı (girilen: {qty:0.##}).");
        var count = (int)qty;

        var serials = (rawSerials ?? Array.Empty<string>())
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (serials.Distinct(StringComparer.OrdinalIgnoreCase).Count() != serials.Count)
            throw new InvalidOperationException($"'{itemCode}' satırında tekrarlanan seri no var.");

        if (movementType == 2 && serials.Count == 0 && autoSerial)
            serials = await GenerateAutoSerialsAsync(conn, tx, itemId, itemCode, count, docDate, ct);

        if (serials.Count != count)
            throw new InvalidOperationException(
                $"'{itemCode}' seri takipli — {count} adet için {serials.Count} seri girildi. " +
                (movementType == 2
                    ? (autoSerial
                        ? "Otomatik üretim için seri listesini tamamen boş bırakın ya da adet kadar seri girin."
                        : "Satırdaki Seri butonundan adet kadar seri no girin.")
                    : "Çıkış/transferde stoktaki serilerden adet kadar seçim zorunlu."));

        foreach (var serialNo in serials)
        {
            var serialId = 0; byte status = 0; var isActive = false;
            await using (var find = conn.CreateCommand())
            {
                find.Transaction = tx;
                find.CommandText = $"SELECT [Id], [Status], [IsActive] FROM {T("ItemSerial")} WHERE [ItemId] = @ItemId AND [SerialNo] = @SerialNo;";
                find.Parameters.AddWithValue("@ItemId", itemId);
                find.Parameters.AddWithValue("@SerialNo", serialNo);
                await using var fr = await find.ExecuteReaderAsync(ct);
                if (await fr.ReadAsync(ct))
                {
                    serialId = fr.GetInt32(0);
                    status = fr.GetByte(1);
                    isActive = fr.GetBoolean(2);
                }
            }

            if (movementType == 2) // Giriş
            {
                if (serialId == 0)
                {
                    await using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = $"""
                        INSERT INTO {T("ItemSerial")} ([ItemId],[SerialNo],[LotId],[Status],[CreatedById])
                        VALUES (@ItemId, @SerialNo, @LotId, 1, @CreatedById);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                        """;
                    ins.Parameters.AddWithValue("@ItemId", itemId);
                    ins.Parameters.AddWithValue("@SerialNo", serialNo);
                    ins.Parameters.AddWithValue("@LotId", (object?)lotId ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
                    serialId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
                }
                else if (!isActive || status == 2)
                {
                    // Emekli/çıkmış seri yeniden girişle stoğa döner (iade senaryosu)
                    await using var upd = conn.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText = $"""
                        UPDATE {T("ItemSerial")}
                        SET [Status] = 1, [IsActive] = 1, [LotId] = COALESCE(@LotId, [LotId]), [Updated] = SYSUTCDATETIME()
                        WHERE [Id] = @Id;
                        """;
                    upd.Parameters.AddWithValue("@Id", serialId);
                    upd.Parameters.AddWithValue("@LotId", (object?)lotId ?? DBNull.Value);
                    await upd.ExecuteNonQueryAsync(ct);
                }
                else if (status == 1)
                {
                    // InStock seri: başka aktif belgenin girişine bağlıysa ikinci kez giremez;
                    // bağsızsa (bu belgenin önceki kaydından — delete+reinsert bağı düşürdü) yeniden bağlanır.
                    bool linkedElsewhere;
                    await using (var chk = conn.CreateCommand())
                    {
                        chk.Transaction = tx;
                        chk.CommandText = $"""
                            SELECT COUNT(1) FROM {T("DocumentLineSerial")} dls
                            INNER JOIN {T("DocumentLine")} dl ON dl.[Id] = dls.[DocumentLineId]
                            INNER JOIN {T("Document")} d ON d.[Id] = dl.[DocumentId]
                            WHERE dls.[SerialId] = @Sid AND dl.[MovementType] = 2 AND d.[IsActive] = 1;
                            """;
                        chk.Parameters.AddWithValue("@Sid", serialId);
                        linkedElsewhere = Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0;
                    }
                    if (linkedElsewhere)
                        throw new InvalidOperationException(
                            $"'{itemCode}' için '{serialNo}' serisi zaten stokta — aynı seri ikinci kez giriş yapamaz.");
                }
                else // status == 3 Blocked
                {
                    throw new InvalidOperationException($"'{itemCode}' için '{serialNo}' serisi bloke — giriş yapılamaz.");
                }
            }
            else // Çıkış (1) / Transfer (3)
            {
                if (serialId == 0 || !isActive)
                    throw new InvalidOperationException(
                        $"'{itemCode}' için '{serialNo}' serisi bulunamadı — çıkış/transfer yalnız stoktaki serilerle yapılabilir.");
                if (status != 1)
                    throw new InvalidOperationException(
                        $"'{itemCode}' için '{serialNo}' serisi stokta değil ({(status == 2 ? "çıkmış" : "bloke")}).");
                if (movementType == 1)
                {
                    await using var iss = conn.CreateCommand();
                    iss.Transaction = tx;
                    iss.CommandText = $"UPDATE {T("ItemSerial")} SET [Status] = 2, [Updated] = SYSUTCDATETIME() WHERE [Id] = @Id;";
                    iss.Parameters.AddWithValue("@Id", serialId);
                    await iss.ExecuteNonQueryAsync(ct);
                }
            }

            await using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = $"INSERT INTO {T("DocumentLineSerial")} ([DocumentLineId],[SerialId]) VALUES (@LineId, @Sid);";
            link.Parameters.AddWithValue("@LineId", lineId);
            link.Parameters.AddWithValue("@Sid", serialId);
            await link.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Otomatik seri üretimi: {ItemCode}-{yyMMdd}-{NNN}. Sıra numarası aynı önekteki en
    /// yüksek değerin devamıdır; UPDLOCK+HOLDLOCK ile tx içinde okunur, UX_ItemSerial_Item_SerialNo
    /// unique index'i olası yarışta çakışmayı DB seviyesinde engeller (tx rollback).</summary>
    private async Task<List<string>> GenerateAutoSerialsAsync(
        SqlConnection conn, SqlTransaction tx, int itemId, string itemCode, int count, DateTime docDate, CancellationToken ct)
    {
        var prefix = $"{itemCode}-{docDate:yyMMdd}-";
        int next;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                SELECT ISNULL(MAX(TRY_CAST(SUBSTRING([SerialNo], LEN(@Prefix) + 1, 10) AS INT)), 0) + 1
                FROM {T("ItemSerial")} WITH (UPDLOCK, HOLDLOCK)
                WHERE [ItemId] = @ItemId AND [SerialNo] LIKE @Prefix + '%';
                """;
            cmd.Parameters.AddWithValue("@Prefix", prefix);
            cmd.Parameters.AddWithValue("@ItemId", itemId);
            next = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
        return Enumerable.Range(next, count).Select(n => $"{prefix}{n:D3}").ToList();
    }

    /// <summary>Items.Code → Items.Id çözümü (ID tabanlı eşleştirme kuralı — kod yalnızca display).</summary>
    private async Task<int?> ResolveItemIdByCodeAsync(SqlConnection conn, SqlTransaction tx, string code, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT TOP 1 [Id] FROM {T("Items")} WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code", code.Trim());
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int id ? id : null;
    }

    private static string? CombineNotesWithRef(string? notes, string? refNo)
    {
        if (string.IsNullOrWhiteSpace(refNo)) return notes;
        var prefix = $"[Ref: {refNo.Trim()}]";
        return string.IsNullOrWhiteSpace(notes) ? prefix : $"{prefix} {notes.Trim()}";
    }

    private static StockDocDto ReadDoc(SqlDataReader r) => new(
        Id: r.GetInt32(0),
        CompanyId: r.GetInt32(1),
        DocType: r.GetString(2),
        DocNo: r.GetString(3),
        DocDate: r.GetDateTime(4),
        FromLocationId: r.IsDBNull(5) ? null : r.GetInt32(5),
        FromLocationName: r.IsDBNull(6) ? null : r.GetString(6),
        ToLocationId: r.IsDBNull(7) ? null : r.GetInt32(7),
        ToLocationName: r.IsDBNull(8) ? null : r.GetString(8),
        RefNo: null,
        Notes: r.IsDBNull(9) ? null : r.GetString(9),
        CreatedById: r.IsDBNull(10) ? null : r.GetInt32(10),
        Created: r.GetDateTime(11),
        IsActive: r.GetBoolean(12),
        LineCount: r.GetInt32(13),
        ArgeProjectId: r.IsDBNull(14) ? null : r.GetInt32(14),
        ArgeProjectName: r.IsDBNull(15) ? null : r.GetString(15));

    private static async Task<IReadOnlyList<StockDocLineDto>> ReadLinesAsync(SqlCommand cmd, CancellationToken ct)
    {
        var result = new List<StockDocLineDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new StockDocLineDto(
                Id: r.GetInt32(0),
                DocId: r.GetInt32(1),
                LineNo: r.GetInt32(2),
                ItemId: r.GetInt32(3),
                MaterialCode: r.IsDBNull(4) ? null : r.GetString(4),
                MaterialName: r.IsDBNull(5) ? null : r.GetString(5),
                UnitId: r.IsDBNull(6) ? null : r.GetInt32(6),
                UnitCode: r.IsDBNull(7) ? null : r.GetString(7),
                Qty: r.GetDecimal(8),
                CombinationId: r.IsDBNull(9) ? null : r.GetInt32(9),
                CombinationCode: r.IsDBNull(10) ? null : r.GetString(10),
                Notes: r.IsDBNull(11) ? null : r.GetString(11),
                FromLocationId: r.IsDBNull(12) ? null : r.GetInt32(12),
                FromLocationName: r.IsDBNull(13) ? null : r.GetString(13),
                ToLocationId: r.IsDBNull(14) ? null : r.GetInt32(14),
                ToLocationName: r.IsDBNull(15) ? null : r.GetString(15),
                UnitCost: r.IsDBNull(16) ? null : r.GetDecimal(16),
                LotNo: r.IsDBNull(17) ? null : r.GetString(17),
                Serials: null, // AttachSerialsAsync doldurur (yalnız DocumentLine tabanlı belgelerde)
                TrackingType: r.IsDBNull(18) ? null : r.GetString(18),
                AutoSerial: !r.IsDBNull(19) && r.GetBoolean(19)));
        }
        return result;
    }

    /// <summary>
    /// Belge numarası çözümü: önce Tasarım Kuralları → Numara Kuralı motoru (belge tipine
    /// tanımlı aktif kural varsa), yoksa sabit "PREFIX-YIL-SIRA" fallback üreteci.
    /// </summary>
    private async Task<string> ResolveDocNoAsync(
        SqlConnection conn, SqlTransaction tx, string docType, int? createdById, DateTime docDate, CancellationToken ct)
    {
        await using (var typeCmd = conn.CreateCommand())
        {
            typeCmd.Transaction = tx;
            typeCmd.CommandText = $"SELECT [Id] FROM {T("DocumentType")} WHERE [Code] = @Code;";
            typeCmd.Parameters.AddWithValue("@Code", TypeCodeFor(docType));
            var typeIdObj = await typeCmd.ExecuteScalarAsync(ct);
            if (typeIdObj is int typeId)
            {
                var ruleNo = await _numberService.GenerateNextAsync(
                    new DocumentNumberContext(typeId, null, null, createdById, null, docDate), ct);
                if (!string.IsNullOrWhiteSpace(ruleNo)) return ruleNo;
            }
        }
        return await GenerateDocNoAsync(conn, tx, docType, ct);
    }

    private async Task<string> GenerateDocNoAsync(SqlConnection conn, SqlTransaction tx, string docType, CancellationToken ct)
    {
        var prefix = docType switch
        {
            "TRANSFER" => "TRF",
            "STOCK_IN" => "GRS",
            "STOCK_OUT" => "CKS",
            "INVENTORY_COUNT" => "SAY",
            _ => "WH",
        };
        var year = DateTime.Now.Year;

        // Sıra numarası "PREFIX-YYYY-" bloğundan sonra başlar → LEN(prefix) + 7. karakter
        // (önceki +6 off-by-one: "-000" okuyup hep 1 üretiyordu → duplicate key).
        // TRY_CAST: kural motorundan gelen farklı formatlı numaralar LIKE'a takılırsa NULL sayılır.
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

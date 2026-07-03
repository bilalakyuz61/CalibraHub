using CalibraHub.Application.Abstractions.Persistence;
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
    private readonly string _schema;

    public SqlStockDocRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
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
            SELECT d.[id], d.[CompanyId], dt.[code], d.[DocumentNumber], d.[DocumentDate],
                   NULL AS FromLocationId, NULL AS FromLocationName,
                   d.[LocationId], loc.[LocationName] AS ToLocationName,
                   d.[notes], d.[CreatedById], d.[Created], d.[IsActive],
                   (SELECT COUNT(*) FROM {T("DocumentLine")} dl WHERE dl.[DocumentId] = d.[id]) AS LineCount,
                   d.[ParentDocumentId] AS ArgeProjectId, ap.[Name] AS ArgeProjectName
            FROM {T("Document")} d
            INNER JOIN {T("document_types")} dt ON dt.[id] = d.[DocumentTypeId]
            LEFT JOIN {T("Location")} loc ON loc.[Id] = d.[LocationId]
            LEFT JOIN {T("ArgeProject")} ap ON ap.[DocumentId] = d.[ParentDocumentId]
            WHERE d.[CompanyId] = @CompanyId AND dt.[code] IN ({paramList}) AND d.[IsActive] = 1
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
            SELECT d.[id], d.[CompanyId], N'INVENTORY_COUNT' AS DocType, d.[DocumentNumber], d.[DocumentDate],
                   ic.[LocationId], loc.[LocationName] AS FromLocationName,
                   NULL AS ToLocationId, NULL AS ToLocationName,
                   d.[notes], d.[CreatedById], d.[Created], d.[IsActive],
                   (SELECT COUNT(*) FROM {T("InventoryCountLine")} l WHERE l.[InventoryCountId] = ic.[Id]) AS LineCount,
                   NULL AS ArgeProjectId, NULL AS ArgeProjectName
            FROM {T("InventoryCount")} ic
            INNER JOIN {T("Document")} d ON d.[id] = ic.[DocumentId]
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
                SELECT d.[id], d.[CompanyId], N'INVENTORY_COUNT' AS DocType, d.[DocumentNumber], d.[DocumentDate],
                       ic.[LocationId], loc.[LocationName] AS FromLocationName,
                       NULL AS ToLocationId, NULL AS ToLocationName,
                       d.[notes], d.[CreatedById], d.[Created], d.[IsActive],
                       (SELECT COUNT(*) FROM {T("InventoryCountLine")} l WHERE l.[InventoryCountId] = ic.[Id]) AS LineCount,
                       NULL AS ArgeProjectId, NULL AS ArgeProjectName
                FROM {T("InventoryCount")} ic
                INNER JOIN {T("Document")} d ON d.[id] = ic.[DocumentId]
                LEFT JOIN {T("Location")} loc ON loc.[Id] = ic.[LocationId]
                WHERE d.[id] = @Id AND d.[CompanyId] = @CompanyId;
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
                SELECT d.[id], d.[CompanyId], dt.[code], d.[DocumentNumber], d.[DocumentDate],
                       NULL AS FromLocationId, NULL AS FromLocationName,
                       d.[LocationId], loc.[LocationName] AS ToLocationName,
                       d.[notes], d.[CreatedById], d.[Created], d.[IsActive],
                       (SELECT COUNT(*) FROM {T("DocumentLine")} dl WHERE dl.[DocumentId] = d.[id]) AS LineCount,
                       d.[ParentDocumentId] AS ArgeProjectId, ap.[Name] AS ArgeProjectName
                FROM {T("Document")} d
                INNER JOIN {T("document_types")} dt ON dt.[id] = d.[DocumentTypeId]
                LEFT JOIN {T("Location")} loc ON loc.[Id] = d.[LocationId]
                LEFT JOIN {T("ArgeProject")} ap ON ap.[DocumentId] = d.[ParentDocumentId]
                WHERE d.[id] = @Id AND d.[CompanyId] = @CompanyId;
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
                SELECT l.[Id], ic.[DocumentId], CAST(ROW_NUMBER() OVER (ORDER BY l.[Id]) AS INT) AS LineNo, l.[ItemId],
                       i.[Code] AS material_code, i.[Name] AS material_name,
                       l.[UnitId], u.[Code] AS unit_code,
                       l.[CountedQty], l.[ConfigId],
                       cfg.[RecordCode] AS combination_code,
                       l.[Notes],
                       ic.[LocationId], loc.[LocationName] AS FromLocationName,
                       NULL AS ToLocationId, NULL AS ToLocationName,
                       NULL AS UnitCost, NULL AS LotNo
                FROM {T("InventoryCountLine")} l
                INNER JOIN {T("InventoryCount")} ic ON ic.[Id] = l.[InventoryCountId]
                LEFT JOIN {T("Items")} i ON i.[Id] = l.[ItemId]
                LEFT JOIN {T("Unit")} u ON u.[Id] = l.[UnitId]
                LEFT JOIN {T("ItemConfiguration")} cfg ON cfg.[Id] = l.[ConfigId]
                LEFT JOIN {T("Location")} loc ON loc.[Id] = ic.[LocationId]
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
                       l.[UnitCost], l.[LotNo]
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
            return await ReadLinesAsync(cmd, ct);
        }
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
                        [notes]        = @Notes,
                        [ParentDocumentId] = @ArgeProjectId
                    WHERE [id] = @Id AND [CompanyId] = @CompanyId;
                    SELECT [DocumentNumber] FROM {T("Document")} WHERE [id] = @Id;
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
                docNo = await GenerateDocNoAsync(conn, tx, request.DocType, ct);
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("Document")}
                        ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[LocationId],
                         [notes],[status],[CreatedById],[Created],[IsActive],[ParentDocumentId])
                    SELECT @CompanyId, @DocNo, dt.[id], @DocDate, @ToLoc,
                           @Notes, N'Draft', @CreatedById, SYSUTCDATETIME(), 1, @ArgeProjectId
                    FROM {T("document_types")} dt WHERE dt.[code] = @TypeCode;
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

            // Kalemler: sil + yeniden ekle (mevcut stock_doc_line davranışıyla aynı — bu belge
            // tipleri icin "draft" kavramı yok, MovementType hemen set edilir).
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {T("DocumentLine")} WHERE [DocumentId] = @DocId;";
                del.Parameters.AddWithValue("@DocId", docId);
                await del.ExecuteNonQueryAsync(ct);
            }

            var lineNo = 1;
            foreach (var line in request.Lines ?? [])
            {
                if (!line.ItemId.HasValue || line.ItemId.Value <= 0) continue;
                if (line.Qty <= 0) continue;

                await using var lineIns = conn.CreateCommand();
                lineIns.Transaction = tx;
                lineIns.CommandText = $"""
                    INSERT INTO {T("DocumentLine")}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[UnitPrice],[DiscountRate],[LineTotal],
                         [CombinationId],[FromLocationId],[LocationId],[MovementType],[UnitCost],[LotNo],[Notes])
                    VALUES
                        (@DocId,@LineNo,@ItemId,@UnitId,@Qty,0,0,0,
                         @CombId,@FromLoc,@ToLoc,@MovementType,
                         COALESCE(@UnitCost, (SELECT TOP 1 pl.[Price] FROM {T("PriceList")} pl
                                              WHERE pl.[ItemId] = @ItemId AND pl.[PriceType] = N'm' AND pl.[IsActive] = 1
                                                AND pl.[ValidFrom] <= @DocDate AND (pl.[ValidTo] IS NULL OR pl.[ValidTo] >= @DocDate)
                                              ORDER BY pl.[ValidFrom] DESC)),
                         @LotNo, @Notes);
                    """;
                lineIns.Parameters.AddWithValue("@DocId", docId);
                lineIns.Parameters.AddWithValue("@LineNo", lineNo++);
                lineIns.Parameters.AddWithValue("@ItemId", line.ItemId.Value);
                lineIns.Parameters.AddWithValue("@UnitId", (object?)line.UnitId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Qty", line.Qty);
                lineIns.Parameters.AddWithValue("@CombId", (object?)line.CombinationId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@FromLoc", (object?)(line.FromLocationId ?? request.FromLocationId) ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@ToLoc", (object?)(line.ToLocationId ?? request.ToLocationId) ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@MovementType", (object?)movementType ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@UnitCost", (object?)line.UnitCost ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@LotNo", (object?)line.LotNo ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Notes", (object?)line.Notes ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
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
                await using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = $"""
                        UPDATE {T("Document")} SET [DocumentDate] = @DocDate, [notes] = @Notes
                        WHERE [id] = @Id AND [CompanyId] = @CompanyId;
                        UPDATE {T("InventoryCount")} SET [LocationId] = @LocId
                        WHERE [DocumentId] = @Id AND [Status] = 0;
                        SELECT [DocumentNumber] FROM {T("Document")} WHERE [id] = @Id;
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
                docNo = await GenerateDocNoAsync(conn, tx, request.DocType, ct);
                await using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = $"""
                        INSERT INTO {T("Document")}
                            ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[notes],[status],[CreatedById],[Created],[IsActive])
                        SELECT @CompanyId, @DocNo, dt.[id], @DocDate, @Notes, N'Draft', @CreatedById, SYSUTCDATETIME(), 1
                        FROM {T("document_types")} dt WHERE dt.[code] = N'sayim';
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
                if (!line.ItemId.HasValue || line.ItemId.Value <= 0) continue;
                if (line.Qty <= 0) continue;

                await using var lineIns = conn.CreateCommand();
                lineIns.Transaction = tx;
                lineIns.CommandText = $"""
                    INSERT INTO {T("InventoryCountLine")}
                        ([InventoryCountId],[ItemId],[ConfigId],[UnitId],[CountedQty],[Notes])
                    VALUES
                        (@CountId,@ItemId,@ConfigId,@UnitId,@Qty,@Notes);
                    """;
                lineIns.Parameters.AddWithValue("@CountId", countId);
                lineIns.Parameters.AddWithValue("@ItemId", line.ItemId.Value);
                lineIns.Parameters.AddWithValue("@ConfigId", (object?)line.CombinationId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@UnitId", (object?)line.UnitId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Qty", line.Qty);
                lineIns.Parameters.AddWithValue("@Notes", (object?)line.Notes ?? DBNull.Value);
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

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {T("Document")} SET [IsActive]=0 WHERE [id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await cmd.ExecuteNonQueryAsync(ct);
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
                LotNo: r.IsDBNull(17) ? null : r.GetString(17)));
        }
        return result;
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

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(MAX(CAST(SUBSTRING([DocumentNumber], LEN(@Prefix)+6, 4) AS INT)), 0) + 1
            FROM {T("Document")}
            WHERE [DocumentNumber] LIKE @Prefix + '-' + CAST(@Year AS NVARCHAR(4)) + '-%';
            """;
        cmd.Parameters.AddWithValue("@Prefix", prefix);
        cmd.Parameters.AddWithValue("@Year", year);

        var seq = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return $"{prefix}-{year}-{seq:D4}";
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

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

    public async Task<IReadOnlyList<StockDocDto>> GetByTypeAsync(string docType, CancellationToken ct)
        => await GetByTypesAsync([docType], ct);

    public async Task<IReadOnlyList<StockDocDto>> GetByTypesAsync(IEnumerable<string> docTypes, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var types = docTypes.ToList();
        var paramList = string.Join(",", types.Select((_, i) => $"@t{i}"));

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT d.id, d.company_id, d.doc_type, d.doc_no, d.doc_date,
                   d.from_location_id, fl.LocationName AS from_location_name,
                   d.to_location_id,   tl.LocationName AS to_location_name,
                   d.ref_no, d.notes, d.created_by_id, d.created, d.is_active,
                   COUNT(l.id) AS line_count,
                   d.arge_project_id, ap.Name AS arge_project_name
            FROM {T("stock_doc")} d
            LEFT JOIN {T("Location")} fl ON fl.Id = d.from_location_id
            LEFT JOIN {T("Location")} tl ON tl.Id = d.to_location_id
            LEFT JOIN {T("stock_doc_line")} l ON l.doc_id = d.id
            LEFT JOIN {T("ArgeProject")} ap ON ap.DocumentId = d.arge_project_id
            WHERE d.company_id = @CompanyId AND d.doc_type IN ({paramList}) AND d.is_active = 1
            GROUP BY d.id, d.company_id, d.doc_type, d.doc_no, d.doc_date,
                     d.from_location_id, fl.LocationName,
                     d.to_location_id, tl.LocationName,
                     d.ref_no, d.notes, d.created_by_id, d.created, d.is_active,
                     d.arge_project_id, ap.Name
            ORDER BY d.created DESC;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        for (var i = 0; i < types.Count; i++)
            cmd.Parameters.AddWithValue($"@t{i}", types[i]);

        var result = new List<StockDocDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(ReadDoc(r));
        return result;
    }

    public async Task<StockDocDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT d.id, d.company_id, d.doc_type, d.doc_no, d.doc_date,
                   d.from_location_id, fl.LocationName AS from_location_name,
                   d.to_location_id,   tl.LocationName AS to_location_name,
                   d.ref_no, d.notes, d.created_by_id, d.created, d.is_active,
                   COUNT(l.id) AS line_count,
                   d.arge_project_id, ap.Name AS arge_project_name
            FROM {T("stock_doc")} d
            LEFT JOIN {T("Location")} fl ON fl.Id = d.from_location_id
            LEFT JOIN {T("Location")} tl ON tl.Id = d.to_location_id
            LEFT JOIN {T("stock_doc_line")} l ON l.doc_id = d.id
            LEFT JOIN {T("ArgeProject")} ap ON ap.DocumentId = d.arge_project_id
            WHERE d.id = @Id AND d.company_id = @CompanyId
            GROUP BY d.id, d.company_id, d.doc_type, d.doc_no, d.doc_date,
                     d.from_location_id, fl.LocationName,
                     d.to_location_id, tl.LocationName,
                     d.ref_no, d.notes, d.created_by_id, d.created, d.is_active,
                     d.arge_project_id, ap.Name;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadDoc(r) : null;
    }

    public async Task<IReadOnlyList<StockDocLineDto>> GetLinesAsync(int docId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT l.id, l.doc_id, l.line_no, l.item_id,
                   i.Code AS material_code, i.Name AS material_name,
                   l.unit_id, u.Code AS unit_code,
                   l.qty, l.combination_id,
                   cfg.RecordCode AS combination_code,
                   l.notes,
                   l.from_location_id, fl.LocationName AS from_location_name,
                   l.to_location_id,   tl.LocationName AS to_location_name,
                   l.unit_cost,
                   l.lot_no
            FROM {T("stock_doc_line")} l
            LEFT JOIN {T("Items")} i ON i.Id = l.item_id
            LEFT JOIN {T("Unit")} u ON u.Id = l.unit_id
            LEFT JOIN {T("ItemConfiguration")} cfg ON cfg.Id = l.combination_id
            LEFT JOIN {T("Location")} fl ON fl.Id = l.from_location_id
            LEFT JOIN {T("Location")} tl ON tl.Id = l.to_location_id
            WHERE l.doc_id = @DocId
            ORDER BY l.line_no;
            """;
        cmd.Parameters.AddWithValue("@DocId", docId);

        var result = new List<StockDocLineDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new StockDocLineDto(
                Id:               r.GetInt32(0),
                DocId:            r.GetInt32(1),
                LineNo:           r.GetInt32(2),
                ItemId:           r.GetInt32(3),
                MaterialCode:     r.IsDBNull(4)  ? null : r.GetString(4),
                MaterialName:     r.IsDBNull(5)  ? null : r.GetString(5),
                UnitId:           r.IsDBNull(6)  ? null : r.GetInt32(6),
                UnitCode:         r.IsDBNull(7)  ? null : r.GetString(7),
                Qty:              r.GetDecimal(8),
                CombinationId:    r.IsDBNull(9)  ? null : r.GetInt32(9),
                CombinationCode:  r.IsDBNull(10) ? null : r.GetString(10),
                Notes:            r.IsDBNull(11) ? null : r.GetString(11),
                FromLocationId:   r.IsDBNull(12) ? null : r.GetInt32(12),
                FromLocationName: r.IsDBNull(13) ? null : r.GetString(13),
                ToLocationId:     r.IsDBNull(14) ? null : r.GetInt32(14),
                ToLocationName:   r.IsDBNull(15) ? null : r.GetString(15),
                UnitCost:         r.IsDBNull(16) ? null : r.GetDecimal(16),
                LotNo:            r.IsDBNull(17) ? null : r.GetString(17)));
        }
        return result;
    }

    public async Task<(int Id, string DocNo)> SaveAsync(SaveStockDocRequest request, int? createdById, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            int docId;
            string docNo;

            if (request.Id.HasValue && request.Id.Value > 0)
            {
                // UPDATE
                docId = request.Id.Value;
                docNo = request.DocNo ?? "";
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE {T("stock_doc")} SET
                        doc_date         = @DocDate,
                        from_location_id = @FromLoc,
                        to_location_id   = @ToLoc,
                        ref_no           = @RefNo,
                        notes            = @Notes,
                        arge_project_id  = @ArgeProjectId
                    WHERE id = @Id AND company_id = @CompanyId;
                    SELECT doc_no FROM {T("stock_doc")} WHERE id = @Id;
                    """;
                upd.Parameters.AddWithValue("@Id", docId);
                upd.Parameters.AddWithValue("@CompanyId", companyId);
                upd.Parameters.AddWithValue("@DocDate", request.DocDate.Date);
                upd.Parameters.AddWithValue("@FromLoc", (object?)request.FromLocationId ?? DBNull.Value);
                upd.Parameters.AddWithValue("@ToLoc",   (object?)request.ToLocationId   ?? DBNull.Value);
                upd.Parameters.AddWithValue("@RefNo",   (object?)request.RefNo           ?? DBNull.Value);
                upd.Parameters.AddWithValue("@Notes",   (object?)request.Notes           ?? DBNull.Value);
                upd.Parameters.AddWithValue("@ArgeProjectId", (object?)request.ArgeProjectId ?? DBNull.Value);
                var res = await upd.ExecuteScalarAsync(ct);
                docNo = res?.ToString() ?? docNo;
            }
            else
            {
                // INSERT — doc_no üret
                docNo = await GenerateDocNoAsync(conn, tx, companyId, request.DocType, ct);
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("stock_doc")}
                        (company_id, doc_type, doc_no, doc_date, from_location_id, to_location_id, ref_no, notes, created_by_id, arge_project_id)
                    VALUES
                        (@CompanyId, @DocType, @DocNo, @DocDate, @FromLoc, @ToLoc, @RefNo, @Notes, @CreatedById, @ArgeProjectId);
                    SELECT SCOPE_IDENTITY();
                    """;
                ins.Parameters.AddWithValue("@CompanyId", companyId);
                ins.Parameters.AddWithValue("@DocType",   request.DocType);
                ins.Parameters.AddWithValue("@DocNo",     docNo);
                ins.Parameters.AddWithValue("@DocDate",   request.DocDate.Date);
                ins.Parameters.AddWithValue("@FromLoc",   (object?)request.FromLocationId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ToLoc",     (object?)request.ToLocationId   ?? DBNull.Value);
                ins.Parameters.AddWithValue("@RefNo",     (object?)request.RefNo           ?? DBNull.Value);
                ins.Parameters.AddWithValue("@Notes",     (object?)request.Notes           ?? DBNull.Value);
                ins.Parameters.AddWithValue("@CreatedById", (object?)createdById             ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ArgeProjectId", (object?)request.ArgeProjectId ?? DBNull.Value);
                docId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
            }

            // Kalemler: sil + yeniden ekle
            await using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {T("stock_doc_line")} WHERE doc_id = @DocId;";
            del.Parameters.AddWithValue("@DocId", docId);
            await del.ExecuteNonQueryAsync(ct);

            var lineNo = 1;
            foreach (var line in (request.Lines ?? []))
            {
                if (!line.ItemId.HasValue || line.ItemId.Value <= 0) continue;
                if (line.Qty <= 0) continue;

                await using var lineIns = conn.CreateCommand();
                lineIns.Transaction = tx;
                // unit_cost: UI'dan gelen manuel deger ELSE PriceList 'm' (maliyet) fiyatindan fis tarihinde snapshot.
                lineIns.CommandText = $"""
                    INSERT INTO {T("stock_doc_line")}
                        (doc_id, line_no, item_id, unit_id, qty, combination_id, notes,
                         from_location_id, to_location_id, unit_cost, lot_no)
                    VALUES
                        (@DocId, @LineNo, @ItemId, @UnitId, @Qty, @CombId, @Notes,
                         @FromLoc, @ToLoc,
                         COALESCE(@UnitCost, (SELECT TOP 1 pl.[Price] FROM {T("PriceList")} pl
                                              WHERE pl.[ItemId] = @ItemId AND pl.[PriceType] = N'm' AND pl.[IsActive] = 1
                                                AND pl.[ValidFrom] <= @DocDate AND (pl.[ValidTo] IS NULL OR pl.[ValidTo] >= @DocDate)
                                              ORDER BY pl.[ValidFrom] DESC)), @LotNo);
                    """;
                lineIns.Parameters.AddWithValue("@DocId",   docId);
                lineIns.Parameters.AddWithValue("@LineNo",  lineNo++);
                lineIns.Parameters.AddWithValue("@ItemId",  line.ItemId.Value);
                lineIns.Parameters.AddWithValue("@UnitId",  (object?)line.UnitId        ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Qty",     line.Qty);
                lineIns.Parameters.AddWithValue("@CombId",  (object?)line.CombinationId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@Notes",   (object?)line.Notes         ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@FromLoc", (object?)line.FromLocationId ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@ToLoc",   (object?)line.ToLocationId   ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@UnitCost", (object?)line.UnitCost      ?? DBNull.Value);
                lineIns.Parameters.AddWithValue("@LotNo",    (object?)line.LotNo         ?? DBNull.Value);
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

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {T("stock_doc")} SET is_active=0 WHERE id=@Id AND company_id=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────────
    private static StockDocDto ReadDoc(SqlDataReader r) => new(
        Id:               r.GetInt32(0),
        CompanyId:        r.GetInt32(1),
        DocType:          r.GetString(2),
        DocNo:            r.GetString(3),
        DocDate:          r.GetDateTime(4),
        FromLocationId:   r.IsDBNull(5)  ? null : r.GetInt32(5),
        FromLocationName: r.IsDBNull(6)  ? null : r.GetString(6),
        ToLocationId:     r.IsDBNull(7)  ? null : r.GetInt32(7),
        ToLocationName:   r.IsDBNull(8)  ? null : r.GetString(8),
        RefNo:            r.IsDBNull(9)  ? null : r.GetString(9),
        Notes:            r.IsDBNull(10) ? null : r.GetString(10),
        CreatedById:      r.IsDBNull(11) ? null : r.GetInt32(11),
        Created:          r.GetDateTime(12),
        IsActive:         r.GetBoolean(13),
        LineCount:        r.GetInt32(14),
        ArgeProjectId:    r.IsDBNull(15) ? null : r.GetInt32(15),
        ArgeProjectName:  r.IsDBNull(16) ? null : r.GetString(16));

    private async Task<string> GenerateDocNoAsync(
        SqlConnection conn, SqlTransaction tx, int companyId, string docType, CancellationToken ct)
    {
        var prefix = docType switch
        {
            "TRANSFER"        => "TRF",
            "STOCK_IN"        => "GRS",
            "STOCK_OUT"       => "CKS",
            "INVENTORY_COUNT" => "SAY",
            _                 => "WH",
        };
        var year = DateTime.Now.Year;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT ISNULL(MAX(CAST(SUBSTRING(doc_no, LEN(@Prefix)+6, 4) AS INT)), 0) + 1
            FROM {T("stock_doc")}
            WHERE company_id = @CompanyId
              AND doc_type = @DocType
              AND doc_no LIKE @Prefix + '-' + CAST(@Year AS NVARCHAR(4)) + '-%';
            """;
        cmd.Parameters.AddWithValue("@Prefix",    prefix);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@DocType",   docType);
        cmd.Parameters.AddWithValue("@Year",      year);

        var seq = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return $"{prefix}-{year}-{seq:D4}";
    }
}

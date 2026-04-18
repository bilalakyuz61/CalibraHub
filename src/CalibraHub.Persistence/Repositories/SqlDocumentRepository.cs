using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDocumentRepository : IDocumentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _quoteTable;
    private readonly string _lineTable;
    private readonly string _detailTable;
    private readonly string _schema;

    public SqlDocumentRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema = schema;
        _quoteTable = $"[{schema}].[Document]";
        _lineTable = $"[{schema}].[DocumentLine]";
        _detailTable = $"[{schema}].[sales_quote_line_details]";
    }

    public async Task<IReadOnlyCollection<Document>> GetAllAsync(string? search, string? status, CancellationToken ct)
    {
        var list = new List<Document>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = "WHERE [is_active] = 1";
        if (!string.IsNullOrWhiteSpace(status))
        {
            where += " AND [status] = @Status";
            cmd.Parameters.Add(new SqlParameter("@Status", status.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND ([document_number] LIKE @Search OR [contact_name] COLLATE Turkish_CI_AI LIKE @Search)";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }

        cmd.CommandText = $"""
            SELECT q.[id],q.[document_number],q.[document_date],q.[valid_until],q.[contact_id],q.[contact_name],q.[contact_address],
                   q.[sales_rep_id],q.[currency],q.[sub_total],q.[discount_rate],q.[discount_amount],q.[tax_rate],q.[tax_amount],q.[grand_total],
                   q.[payment_terms],q.[delivery_terms],q.[delivery_address],q.[status],q.[revision_no],q.[parent_document_id],
                   q.[notes],q.[created_by],q.[created_at],q.[updated_at],q.[is_active],q.[document_type_id],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[document_id] = q.[id] AND l.[is_active] = 1) AS [line_count]
            FROM {_quoteTable} q
            {where.Replace("[", "q.[")}
            ORDER BY q.[created_at] DESC;
            """;

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapQuote(r));
        return list;
    }

    public async Task<Document?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT q.[id],q.[document_number],q.[document_date],q.[valid_until],q.[contact_id],q.[contact_name],q.[contact_address],
                   q.[sales_rep_id],q.[currency],q.[sub_total],q.[discount_rate],q.[discount_amount],q.[tax_rate],q.[tax_amount],q.[grand_total],
                   q.[payment_terms],q.[delivery_terms],q.[delivery_address],q.[status],q.[revision_no],q.[parent_document_id],
                   q.[notes],q.[created_by],q.[created_at],q.[updated_at],q.[is_active],q.[document_type_id],
                   COALESCE(ca_id.[AccountCode], ca_name.[AccountCode]) AS customer_code
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca_id
                ON ca_id.[Id] = q.[contact_id]
            LEFT JOIN [{_schema}].[Contact] ca_name
                ON q.[contact_id] IS NULL
                AND ca_name.[AccountTitle] COLLATE Turkish_CI_AI = q.[contact_name] COLLATE Turkish_CI_AI
                AND ca_name.[IsActive] = 1
            WHERE q.[id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapQuote(r) : null;
    }

    public async Task<IReadOnlyCollection<DocumentLine>> GetLinesAsync(int documentId, CancellationToken ct)
    {
        var list = new List<DocumentLine>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[document_id],[line_no],[item_id],[material_code],[material_name],[unit_name],
                   [quantity],[unit_price],[discount_rate],[line_total],[combination_code],[notes],[is_active]
            FROM {_lineTable} WHERE [document_id] = @DocumentId AND [is_active] = 1 ORDER BY [line_no];
            """;
        cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapLine(r));
        return list;
    }

    /// <summary>
    /// INSERT (Id=0) veya UPDATE (Id&gt;0). Yeni kayit icin IDENTITY tarafindan atanan
    /// Id'yi Document.Id yazmak mumkun degil (init-only) — bunun yerine yeni Id
    /// caller'a return edilir.
    /// </summary>
    public async Task<int> UpsertAsync(Document q, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (q.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_quoteTable} SET
                    [document_type_id]=@DocumentTypeId,
                    [document_date]=@DocumentDate, [valid_until]=@ValidUntil,
                    [contact_id]=@ContactId, [contact_name]=@ContactName, [contact_address]=@ContactAddress,
                    [sales_rep_id]=@SalesRepId,
                    [currency]=@Currency, [sub_total]=@SubTotal, [discount_rate]=@DiscountRate,
                    [discount_amount]=@DiscountAmount, [tax_rate]=@TaxRate, [tax_amount]=@TaxAmount,
                    [grand_total]=@GrandTotal, [payment_terms]=@PaymentTerms, [delivery_terms]=@DeliveryTerms,
                    [delivery_address]=@DeliveryAddress, [status]=@Status, [revision_no]=@RevisionNo,
                    [parent_document_id]=@ParentDocumentId,
                    [notes]=@Notes, [updated_at]=@UpdatedAt
                WHERE [id] = @Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", q.Id));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_quoteTable}
                    ([document_number],[document_type_id],[document_date],[valid_until],[contact_id],[contact_name],[contact_address],
                     [sales_rep_id],[currency],[sub_total],[discount_rate],[discount_amount],[tax_rate],[tax_amount],[grand_total],
                     [payment_terms],[delivery_terms],[delivery_address],[status],[revision_no],[parent_document_id],
                     [notes],[created_by],[created_at],[updated_at],[is_active])
                VALUES
                    (@DocumentNumber,@DocumentTypeId,@DocumentDate,@ValidUntil,@ContactId,@ContactName,@ContactAddress,
                     @SalesRepId,@Currency,@SubTotal,@DiscountRate,@DiscountAmount,@TaxRate,@TaxAmount,@GrandTotal,
                     @PaymentTerms,@DeliveryTerms,@DeliveryAddress,@Status,@RevisionNo,@ParentDocumentId,
                     @Notes,@CreatedBy,@CreatedAt,@UpdatedAt,@IsActive);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        }

        cmd.Parameters.Add(new SqlParameter("@DocumentNumber", q.DocumentNumber));
        cmd.Parameters.Add(new SqlParameter("@DocumentTypeId", (object?)q.DocumentTypeId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DocumentDate", q.DocumentDate));
        cmd.Parameters.Add(new SqlParameter("@ValidUntil", (object?)q.ValidUntil ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactId", (object?)q.ContactId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactName", (object?)q.ContactName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactAddress", (object?)q.ContactAddress ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SalesRepId", (object?)q.SalesRepId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Currency", q.Currency));
        cmd.Parameters.Add(new SqlParameter("@SubTotal", q.SubTotal));
        cmd.Parameters.Add(new SqlParameter("@DiscountRate", q.DiscountRate));
        cmd.Parameters.Add(new SqlParameter("@DiscountAmount", q.DiscountAmount));
        cmd.Parameters.Add(new SqlParameter("@TaxRate", q.TaxRate));
        cmd.Parameters.Add(new SqlParameter("@TaxAmount", q.TaxAmount));
        cmd.Parameters.Add(new SqlParameter("@GrandTotal", q.GrandTotal));
        cmd.Parameters.Add(new SqlParameter("@PaymentTerms", (object?)q.PaymentTerms ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeliveryTerms", (object?)q.DeliveryTerms ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeliveryAddress", (object?)q.DeliveryAddress ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Status", q.Status.ToString()));
        cmd.Parameters.Add(new SqlParameter("@RevisionNo", q.RevisionNo));
        cmd.Parameters.Add(new SqlParameter("@ParentDocumentId", (object?)q.ParentDocumentId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Notes", (object?)q.Notes ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedBy", (object?)q.CreatedBy ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", q.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", q.UpdatedAt));
        cmd.Parameters.Add(new SqlParameter("@IsActive", q.IsActive));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task SaveLinesAsync(int documentId, IReadOnlyCollection<DocumentLine> lines, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        // Mevcut satirlari sil (CASCADE ile detail'lar da silinir)
        await using (var del = conn.CreateCommand())
        {
            del.CommandText = $"DELETE FROM {_lineTable} WHERE [document_id] = @DocumentId;";
            del.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            await del.ExecuteNonQueryAsync(ct);
        }
        // Yeni satirlari ekle — id IDENTITY tarafindan uretilir
        foreach (var ln in lines)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_lineTable}
                    ([document_id],[line_no],[item_id],[material_code],[material_name],[unit_name],
                     [quantity],[unit_price],[discount_rate],[line_total],[combination_code],[notes],[is_active])
                VALUES
                    (@DocumentId,@LineNo,@ItemId,@MaterialCode,@MaterialName,@UnitName,
                     @Quantity,@UnitPrice,@DiscountRate,@LineTotal,@CombinationCode,@Notes,@IsActive);
                """;
            cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            cmd.Parameters.Add(new SqlParameter("@LineNo", ln.LineNo));
            cmd.Parameters.Add(new SqlParameter("@ItemId", (object?)ln.ItemId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@MaterialCode", ln.MaterialCode));
            cmd.Parameters.Add(new SqlParameter("@MaterialName", ln.MaterialName));
            cmd.Parameters.Add(new SqlParameter("@UnitName", (object?)ln.UnitName ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Quantity", ln.Quantity));
            cmd.Parameters.Add(new SqlParameter("@UnitPrice", ln.UnitPrice));
            cmd.Parameters.Add(new SqlParameter("@DiscountRate", ln.DiscountRate));
            cmd.Parameters.Add(new SqlParameter("@LineTotal", ln.LineTotal));
            cmd.Parameters.Add(new SqlParameter("@CombinationCode", (object?)ln.CombinationCode ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Notes", (object?)ln.Notes ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@IsActive", ln.IsActive));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_quoteTable} SET [is_active] = 0, [updated_at] = @Now WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string> GetNextDocumentNumberAsync(CancellationToken ct)
    {
        // Format: TKL202600000001 (15 karakter) — TKL + yyyy + 8 haneli sira
        var prefix = "TKL" + DateTime.Now.ToString("yyyy");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [document_number] FROM {_quoteTable}
            WHERE [document_number] LIKE @Prefix + '%'
            ORDER BY [document_number] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Prefix", prefix));
        var last = await cmd.ExecuteScalarAsync(ct) as string;
        int nextSeq = 1;
        if (last != null && last.Length == 15 && last.StartsWith(prefix))
        {
            if (int.TryParse(last[prefix.Length..], out var seq))
                nextSeq = seq + 1;
        }
        return prefix + nextSeq.ToString("D8");
    }

    private static Document MapQuote(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("id")),
        DocumentNumber = r.GetString(r.GetOrdinal("document_number")),
        DocumentTypeId = TryGetOrdinal(r, "document_type_id") is int dtOrd && dtOrd >= 0 && !r.IsDBNull(dtOrd)
            ? r.GetInt32(dtOrd) : null,
        DocumentDate = r.GetDateTime(r.GetOrdinal("document_date")),
        ValidUntil = r.IsDBNull(r.GetOrdinal("valid_until")) ? null : r.GetDateTime(r.GetOrdinal("valid_until")),
        ContactId = r.IsDBNull(r.GetOrdinal("contact_id")) ? null : r.GetInt32(r.GetOrdinal("contact_id")),
        ContactName = r.IsDBNull(r.GetOrdinal("contact_name")) ? null : r.GetString(r.GetOrdinal("contact_name")),
        ContactAddress = r.IsDBNull(r.GetOrdinal("contact_address")) ? null : r.GetString(r.GetOrdinal("contact_address")),
        ContactCode = TryGetOrdinal(r, "customer_code") is int ccOrd && ccOrd >= 0 && !r.IsDBNull(ccOrd)
            ? r.GetString(ccOrd) : null,
        SalesRepId = r.IsDBNull(r.GetOrdinal("sales_rep_id")) ? null : r.GetInt32(r.GetOrdinal("sales_rep_id")),
        Currency = r.GetString(r.GetOrdinal("currency")),
        SubTotal = r.GetDecimal(r.GetOrdinal("sub_total")),
        DiscountRate = r.GetDecimal(r.GetOrdinal("discount_rate")),
        DiscountAmount = r.GetDecimal(r.GetOrdinal("discount_amount")),
        TaxRate = r.GetDecimal(r.GetOrdinal("tax_rate")),
        TaxAmount = r.GetDecimal(r.GetOrdinal("tax_amount")),
        GrandTotal = r.GetDecimal(r.GetOrdinal("grand_total")),
        PaymentTerms = r.IsDBNull(r.GetOrdinal("payment_terms")) ? null : r.GetString(r.GetOrdinal("payment_terms")),
        DeliveryTerms = r.IsDBNull(r.GetOrdinal("delivery_terms")) ? null : r.GetString(r.GetOrdinal("delivery_terms")),
        DeliveryAddress = r.IsDBNull(r.GetOrdinal("delivery_address")) ? null : r.GetString(r.GetOrdinal("delivery_address")),
        Status = Enum.TryParse<DocumentStatus>(r.GetString(r.GetOrdinal("status")), out var s) ? s : DocumentStatus.Draft,
        RevisionNo = r.GetInt32(r.GetOrdinal("revision_no")),
        ParentDocumentId = r.IsDBNull(r.GetOrdinal("parent_document_id")) ? null : r.GetInt32(r.GetOrdinal("parent_document_id")),
        Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes")),
        CreatedBy = r.IsDBNull(r.GetOrdinal("created_by")) ? null : r.GetString(r.GetOrdinal("created_by")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        LineCount = TryGetOrdinal(r, "line_count") is int lcOrd && lcOrd >= 0 ? r.GetInt32(lcOrd) : 0
    };

    private static DocumentLine MapLine(SqlDataReader r)
    {
        var combOrd = TryGetOrdinal(r, "combination_code");
        return new()
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            DocumentId = r.GetInt32(r.GetOrdinal("document_id")),
            LineNo = r.GetInt32(r.GetOrdinal("line_no")),
            ItemId = r.IsDBNull(r.GetOrdinal("item_id")) ? null : r.GetInt32(r.GetOrdinal("item_id")),
            MaterialCode = r.GetString(r.GetOrdinal("material_code")),
            MaterialName = r.GetString(r.GetOrdinal("material_name")),
            UnitName = r.IsDBNull(r.GetOrdinal("unit_name")) ? null : r.GetString(r.GetOrdinal("unit_name")),
            Quantity = r.GetDecimal(r.GetOrdinal("quantity")),
            UnitPrice = r.GetDecimal(r.GetOrdinal("unit_price")),
            DiscountRate = r.GetDecimal(r.GetOrdinal("discount_rate")),
            LineTotal = r.GetDecimal(r.GetOrdinal("line_total")),
            CombinationCode = combOrd >= 0 && !r.IsDBNull(combOrd) ? r.GetString(combOrd) : null,
            Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes")),
            IsActive = r.GetBoolean(r.GetOrdinal("is_active"))
        };
    }

    private static int TryGetOrdinal(SqlDataReader r, string name)
    {
        try { return r.GetOrdinal(name); } catch { return -1; }
    }

    // ── Line Details (ozellik-deger aciklamalari) ────────────────────────

    public async Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(int documentLineId, CancellationToken ct)
    {
        var list = new List<DocumentLineDetail>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[quote_line_id],[feature_name],[value_code],[value_name],[description],[line_order]
            FROM {_detailTable} WHERE [quote_line_id] = @LineId ORDER BY [line_order];
            """;
        cmd.Parameters.Add(new SqlParameter("@LineId", documentLineId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new DocumentLineDetail
            {
                Id = r.GetInt32(0),
                QuoteLineId = r.GetInt32(1),
                FeatureName = r.GetString(2),
                ValueCode = r.GetString(3),
                ValueName = r.GetString(4),
                Description = r.IsDBNull(5) ? null : r.GetString(5),
                LineOrder = r.GetInt32(6),
            });
        return list;
    }

    public async Task SaveLineDetailsAsync(int documentLineId, IReadOnlyCollection<DocumentLineDetail> details, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using (var del = conn.CreateCommand())
        {
            del.CommandText = $"DELETE FROM {_detailTable} WHERE [quote_line_id] = @LineId;";
            del.Parameters.Add(new SqlParameter("@LineId", documentLineId));
            await del.ExecuteNonQueryAsync(ct);
        }
        var order = 0;
        foreach (var d in details)
        {
            order++;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_detailTable} ([quote_line_id],[feature_name],[value_code],[value_name],[description],[line_order])
                VALUES (@LineId, @Feature, @ValCode, @ValName, @Desc, @Order);
                """;
            cmd.Parameters.Add(new SqlParameter("@LineId", documentLineId));
            cmd.Parameters.Add(new SqlParameter("@Feature", d.FeatureName));
            cmd.Parameters.Add(new SqlParameter("@ValCode", d.ValueCode));
            cmd.Parameters.Add(new SqlParameter("@ValName", d.ValueName));
            cmd.Parameters.Add(new SqlParameter("@Desc", (object?)d.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Order", order));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlSalesQuoteRepository : ISalesQuoteRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _quoteTable;
    private readonly string _lineTable;
    private readonly string _detailTable;
    private readonly string _schema;

    public SqlSalesQuoteRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema = schema;
        _quoteTable = $"[{schema}].[sales_quotes]";
        _lineTable = $"[{schema}].[sales_quote_lines]";
        _detailTable = $"[{schema}].[sales_quote_line_details]";
    }

    public async Task<IReadOnlyCollection<SalesQuote>> GetAllAsync(string? search, string? status, CancellationToken ct)
    {
        var list = new List<SalesQuote>();
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
            where += " AND ([quote_number] LIKE @Search OR [customer_name] LIKE @Search)";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }

        cmd.CommandText = $"""
            SELECT q.[id],q.[quote_number],q.[quote_date],q.[valid_until],q.[customer_id],q.[customer_name],q.[customer_address],
                   q.[sales_rep_id],q.[currency],q.[sub_total],q.[discount_rate],q.[discount_amount],q.[tax_rate],q.[tax_amount],q.[grand_total],
                   q.[payment_terms],q.[delivery_terms],q.[delivery_address],q.[status],q.[revision_no],q.[parent_quote_id],
                   q.[notes],q.[created_by],q.[created_at],q.[updated_at],q.[is_active],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[quote_id] = q.[id] AND l.[is_active] = 1) AS [line_count]
            FROM {_quoteTable} q
            {where.Replace("[", "q.[")}
            ORDER BY q.[created_at] DESC;
            """;

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapQuote(r));
        return list;
    }

    public async Task<SalesQuote?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[quote_number],[quote_date],[valid_until],[customer_id],[customer_name],[customer_address],
                   [sales_rep_id],[currency],[sub_total],[discount_rate],[discount_amount],[tax_rate],[tax_amount],[grand_total],
                   [payment_terms],[delivery_terms],[delivery_address],[status],[revision_no],[parent_quote_id],
                   [notes],[created_by],[created_at],[updated_at],[is_active]
            FROM {_quoteTable} WHERE [id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapQuote(r) : null;
    }

    public async Task<IReadOnlyCollection<SalesQuoteLine>> GetLinesAsync(Guid quoteId, CancellationToken ct)
    {
        var list = new List<SalesQuoteLine>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[quote_id],[line_no],[stock_card_id],[material_code],[material_name],[unit_name],
                   [quantity],[unit_price],[discount_rate],[line_total],[combination_code],[notes],[is_active]
            FROM {_lineTable} WHERE [quote_id] = @QuoteId AND [is_active] = 1 ORDER BY [line_no];
            """;
        cmd.Parameters.Add(new SqlParameter("@QuoteId", quoteId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapLine(r));
        return list;
    }

    public async Task UpsertAsync(SalesQuote q, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_quoteTable} WHERE [id] = @Id)
                UPDATE {_quoteTable} SET
                    [quote_date]=@QuoteDate, [valid_until]=@ValidUntil,
                    [customer_id]=@CustomerId, [customer_name]=@CustomerName, [customer_address]=@CustomerAddress,
                    [sales_rep_id]=@SalesRepId,
                    [currency]=@Currency, [sub_total]=@SubTotal, [discount_rate]=@DiscountRate,
                    [discount_amount]=@DiscountAmount, [tax_rate]=@TaxRate, [tax_amount]=@TaxAmount,
                    [grand_total]=@GrandTotal, [payment_terms]=@PaymentTerms, [delivery_terms]=@DeliveryTerms,
                    [delivery_address]=@DeliveryAddress, [status]=@Status, [revision_no]=@RevisionNo,
                    [notes]=@Notes, [updated_at]=@UpdatedAt
                WHERE [id] = @Id
            ELSE
                INSERT INTO {_quoteTable}
                    ([id],[quote_number],[quote_date],[valid_until],[customer_id],[customer_name],[customer_address],
                     [sales_rep_id],[currency],[sub_total],[discount_rate],[discount_amount],[tax_rate],[tax_amount],[grand_total],
                     [payment_terms],[delivery_terms],[delivery_address],[status],[revision_no],[parent_quote_id],
                     [notes],[created_by],[created_at],[updated_at],[is_active])
                VALUES
                    (@Id,@QuoteNumber,@QuoteDate,@ValidUntil,@CustomerId,@CustomerName,@CustomerAddress,
                     @SalesRepId,@Currency,@SubTotal,@DiscountRate,@DiscountAmount,@TaxRate,@TaxAmount,@GrandTotal,
                     @PaymentTerms,@DeliveryTerms,@DeliveryAddress,@Status,@RevisionNo,@ParentQuoteId,
                     @Notes,@CreatedBy,@CreatedAt,@UpdatedAt,@IsActive);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", q.Id));
        cmd.Parameters.Add(new SqlParameter("@QuoteNumber", q.QuoteNumber));
        cmd.Parameters.Add(new SqlParameter("@QuoteDate", q.QuoteDate));
        cmd.Parameters.Add(new SqlParameter("@ValidUntil", (object?)q.ValidUntil ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CustomerId", (object?)q.CustomerId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CustomerName", (object?)q.CustomerName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CustomerAddress", (object?)q.CustomerAddress ?? DBNull.Value));
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
        cmd.Parameters.Add(new SqlParameter("@ParentQuoteId", (object?)q.ParentQuoteId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Notes", (object?)q.Notes ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedBy", (object?)q.CreatedBy ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", q.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", q.UpdatedAt));
        cmd.Parameters.Add(new SqlParameter("@IsActive", q.IsActive));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveLinesAsync(Guid quoteId, IReadOnlyCollection<SalesQuoteLine> lines, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        // Mevcut satirlari sil
        await using (var del = conn.CreateCommand())
        {
            del.CommandText = $"DELETE FROM {_lineTable} WHERE [quote_id] = @QuoteId;";
            del.Parameters.Add(new SqlParameter("@QuoteId", quoteId));
            await del.ExecuteNonQueryAsync(ct);
        }
        // Yeni satirlari ekle
        foreach (var ln in lines)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_lineTable}
                    ([id],[quote_id],[line_no],[stock_card_id],[material_code],[material_name],[unit_name],
                     [quantity],[unit_price],[discount_rate],[line_total],[combination_code],[notes],[is_active])
                VALUES
                    (@Id,@QuoteId,@LineNo,@StockCardId,@MaterialCode,@MaterialName,@UnitName,
                     @Quantity,@UnitPrice,@DiscountRate,@LineTotal,@CombinationCode,@Notes,@IsActive);
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", ln.Id));
            cmd.Parameters.Add(new SqlParameter("@QuoteId", ln.QuoteId));
            cmd.Parameters.Add(new SqlParameter("@LineNo", ln.LineNo));
            cmd.Parameters.Add(new SqlParameter("@StockCardId", (object?)ln.StockCardId ?? DBNull.Value));
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

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_quoteTable} SET [is_active] = 0, [updated_at] = @Now WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string> GetNextQuoteNumberAsync(CancellationToken ct)
    {
        // Format: TKL202600000001 (15 karakter) — TKL + yyyy + 8 haneli sira
        var prefix = "TKL" + DateTime.Now.ToString("yyyy");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [quote_number] FROM {_quoteTable}
            WHERE [quote_number] LIKE @Prefix + '%'
            ORDER BY [quote_number] DESC;
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

    private static SalesQuote MapQuote(SqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        QuoteNumber = r.GetString(r.GetOrdinal("quote_number")),
        QuoteDate = r.GetDateTime(r.GetOrdinal("quote_date")),
        ValidUntil = r.IsDBNull(r.GetOrdinal("valid_until")) ? null : r.GetDateTime(r.GetOrdinal("valid_until")),
        CustomerId = r.IsDBNull(r.GetOrdinal("customer_id")) ? null : r.GetInt32(r.GetOrdinal("customer_id")),
        CustomerName = r.IsDBNull(r.GetOrdinal("customer_name")) ? null : r.GetString(r.GetOrdinal("customer_name")),
        CustomerAddress = r.IsDBNull(r.GetOrdinal("customer_address")) ? null : r.GetString(r.GetOrdinal("customer_address")),
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
        Status = Enum.TryParse<SalesQuoteStatus>(r.GetString(r.GetOrdinal("status")), out var s) ? s : SalesQuoteStatus.Draft,
        RevisionNo = r.GetInt32(r.GetOrdinal("revision_no")),
        ParentQuoteId = r.IsDBNull(r.GetOrdinal("parent_quote_id")) ? null : r.GetGuid(r.GetOrdinal("parent_quote_id")),
        Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes")),
        CreatedBy = r.IsDBNull(r.GetOrdinal("created_by")) ? null : r.GetString(r.GetOrdinal("created_by")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        LineCount = TryGetOrdinal(r, "line_count") is int lcOrd && lcOrd >= 0 ? r.GetInt32(lcOrd) : 0
    };

    private static SalesQuoteLine MapLine(SqlDataReader r)
    {
        var combOrd = TryGetOrdinal(r, "combination_code");
        return new()
        {
            Id = r.GetGuid(r.GetOrdinal("id")),
            QuoteId = r.GetGuid(r.GetOrdinal("quote_id")),
            LineNo = r.GetInt32(r.GetOrdinal("line_no")),
            StockCardId = r.IsDBNull(r.GetOrdinal("stock_card_id")) ? null : r.GetInt32(r.GetOrdinal("stock_card_id")),
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

    public async Task<IReadOnlyCollection<SalesQuoteLineDetail>> GetLineDetailsAsync(Guid quoteLineId, CancellationToken ct)
    {
        var list = new List<SalesQuoteLineDetail>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[quote_line_id],[feature_name],[value_code],[value_name],[description],[line_order]
            FROM {_detailTable} WHERE [quote_line_id] = @LineId ORDER BY [line_order];
            """;
        cmd.Parameters.Add(new SqlParameter("@LineId", quoteLineId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new SalesQuoteLineDetail
            {
                Id = r.GetInt32(0),
                QuoteLineId = r.GetGuid(1),
                FeatureName = r.GetString(2),
                ValueCode = r.GetString(3),
                ValueName = r.GetString(4),
                Description = r.IsDBNull(5) ? null : r.GetString(5),
                LineOrder = r.GetInt32(6),
            });
        return list;
    }

    public async Task SaveLineDetailsAsync(Guid quoteLineId, IReadOnlyCollection<SalesQuoteLineDetail> details, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using (var del = conn.CreateCommand())
        {
            del.CommandText = $"DELETE FROM {_detailTable} WHERE [quote_line_id] = @LineId;";
            del.Parameters.Add(new SqlParameter("@LineId", quoteLineId));
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
            cmd.Parameters.Add(new SqlParameter("@LineId", quoteLineId));
            cmd.Parameters.Add(new SqlParameter("@Feature", d.FeatureName));
            cmd.Parameters.Add(new SqlParameter("@ValCode", d.ValueCode));
            cmd.Parameters.Add(new SqlParameter("@ValName", d.ValueName));
            cmd.Parameters.Add(new SqlParameter("@Desc", (object?)d.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Order", order));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}

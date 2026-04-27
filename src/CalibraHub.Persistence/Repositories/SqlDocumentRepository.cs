using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDocumentRepository : IDocumentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _quoteTable;
    private readonly string _lineTable;
    private readonly string _detailTable;
    private readonly string _schema;

    public SqlDocumentRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema = schema;
        _quoteTable = $"[{schema}].[Document]";
        _lineTable = $"[{schema}].[DocumentLine]";
        _detailTable = $"[{schema}].[sales_quote_line_details]";
    }

    /// <summary>
    /// HTTP context'ten mevcut kullanicinin sirket kimligini ceker.
    /// Claim yoksa 0 doner; caller fallback olarak DB default'una (1) guvenir.
    /// </summary>
    private int GetCurrentCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var raw = httpContext.User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var id))
                return id;
        }
        return 0;
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
            where += " AND ([document_number] LIKE @Search)";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }

        // Cari ismi Contact.AccountTitle'dan cekilir — contact_name kolonu Faz 2'de drop edildi.
        cmd.CommandText = $"""
            SELECT q.[id],q.[company_id],q.[document_number],q.[document_date],q.[valid_until],q.[contact_id],
                   ca.[AccountTitle] AS [contact_name],
                   q.[contact_address],
                   q.[sales_rep_id],q.[currency],q.[sub_total],q.[discount_rate],q.[discount_amount],q.[tax_rate],q.[tax_amount],q.[grand_total],
                   q.[payment_terms],q.[delivery_terms],q.[delivery_address],q.[status],q.[revision_no],q.[parent_document_id],
                   q.[notes],q.[created_by],q.[created_at],q.[updated_at],q.[is_active],q.[document_type_id],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[document_id] = q.[id]) AS [line_count]
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[contact_id]
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
        // Cari ismi Contact.AccountTitle'dan gelir — contact_name kolonu Faz 2'de drop edildi.
        cmd.CommandText = $"""
            SELECT q.[id],q.[company_id],q.[document_number],q.[document_date],q.[valid_until],q.[contact_id],
                   ca.[AccountTitle] AS [contact_name],
                   q.[contact_address],
                   q.[sales_rep_id],q.[currency],q.[sub_total],q.[discount_rate],q.[discount_amount],q.[tax_rate],q.[tax_amount],q.[grand_total],
                   q.[payment_terms],q.[delivery_terms],q.[delivery_address],q.[status],q.[revision_no],q.[parent_document_id],
                   q.[notes],q.[created_by],q.[created_at],q.[updated_at],q.[is_active],q.[document_type_id],
                   ca.[AccountCode] AS customer_code
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[contact_id]
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
        // Material/Unit/Combination/Location display alanlari JOIN ile okunur — tabloda tutulmuyor.
        cmd.CommandText = $"""
            SELECT l.[id],l.[document_id],l.[line_no],l.[item_id],l.[unit_id],
                   l.[quantity],l.[unit_price],l.[discount_rate],l.[line_total],
                   l.[combination_id],l.[location_id],l.[notes],ISNULL(l.[notes_pinned], 0) AS [notes_pinned],
                   l.[revised_from_id],
                   i.[code] AS [material_code], i.[name] AS [material_name],
                   u.[UnitCode] AS [unit_code], u.[UnitName] AS [unit_name],
                   pc.[RecordCode] AS [combination_code],
                   loc.[LocationCode] AS [location_code], loc.[LocationName] AS [location_name]
            FROM {_lineTable} l
            LEFT JOIN [{_schema}].[Items] i ON i.[id] = l.[item_id]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = l.[unit_id]
            LEFT JOIN [{_schema}].[ProductConfiguration] pc ON pc.[Id] = l.[combination_id]
            LEFT JOIN [{_schema}].[Location] loc ON loc.[Id] = l.[location_id]
            WHERE l.[document_id] = @DocumentId
            ORDER BY l.[line_no];
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
                    [contact_id]=@ContactId, [contact_address]=@ContactAddress,
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
                    ([company_id],[document_number],[document_type_id],[document_date],[valid_until],[contact_id],[contact_address],
                     [sales_rep_id],[currency],[sub_total],[discount_rate],[discount_amount],[tax_rate],[tax_amount],[grand_total],
                     [payment_terms],[delivery_terms],[delivery_address],[status],[revision_no],[parent_document_id],
                     [notes],[created_by],[created_at],[updated_at],[is_active])
                VALUES
                    (@CompanyId,@DocumentNumber,@DocumentTypeId,@DocumentDate,@ValidUntil,@ContactId,@ContactAddress,
                     @SalesRepId,@Currency,@SubTotal,@DiscountRate,@DiscountAmount,@TaxRate,@TaxAmount,@GrandTotal,
                     @PaymentTerms,@DeliveryTerms,@DeliveryAddress,@Status,@RevisionNo,@ParentDocumentId,
                     @Notes,@CreatedBy,@CreatedAt,@UpdatedAt,@IsActive);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            // Kayit anindaki oturum kullanicisinin sirketinden cek; claim yoksa 1 (default).
            var effectiveCompanyId = q.CompanyId > 0 ? q.CompanyId
                                    : (GetCurrentCompanyId() is int cid && cid > 0 ? cid : 1);
            cmd.Parameters.Add(new SqlParameter("@CompanyId", effectiveCompanyId));
        }

        cmd.Parameters.Add(new SqlParameter("@DocumentNumber", q.DocumentNumber));
        cmd.Parameters.Add(new SqlParameter("@DocumentTypeId", (object?)q.DocumentTypeId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DocumentDate", q.DocumentDate));
        cmd.Parameters.Add(new SqlParameter("@ValidUntil", (object?)q.ValidUntil ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactId", (object?)q.ContactId ?? DBNull.Value));
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

    /// <summary>
    /// UPSERT (DELETE degil). Mevcut satirlarin Id'leri korunur ki:
    ///   1) Widget verileri (WidgetTra, RecordId = DocumentLine.Id) orphanlanmaz,
    ///   2) Kombinasyon detaylari (sales_quote_line_details) yeniden yazilmaz.
    /// Akis:
    ///   - DB'deki mevcut satir Id'lerini cek
    ///   - Request'teki her satir: Id>0 ve mevcut ise UPDATE, aksi halde INSERT
    ///   - Request'te olmayan mevcut Id'leri DELETE et (CASCADE detaylari temizler)
    /// </summary>
    public async Task SaveLinesAsync(int documentId, IReadOnlyCollection<DocumentLine> lines, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) Mevcut satir Id'lerini topla
        var existingIds = new HashSet<int>();
        await using (var getCmd = conn.CreateCommand())
        {
            getCmd.CommandText = $"SELECT [id] FROM {_lineTable} WHERE [document_id] = @DocumentId;";
            getCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            await using var r = await getCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) existingIds.Add(r.GetInt32(0));
        }

        var keptIds = new HashSet<int>();

        // 2) Request satirlari — UPSERT
        foreach (var ln in lines)
        {
            var isUpdate = ln.Id > 0 && existingIds.Contains(ln.Id);
            if (isUpdate) keptIds.Add(ln.Id);

            await using var cmd = conn.CreateCommand();
            if (isUpdate)
            {
                cmd.CommandText = $"""
                    UPDATE {_lineTable} SET
                        [line_no]         = @LineNo,
                        [item_id]         = @ItemId,
                        [unit_id]         = @UnitId,
                        [quantity]        = @Quantity,
                        [unit_price]      = @UnitPrice,
                        [discount_rate]   = @DiscountRate,
                        [line_total]      = @LineTotal,
                        [combination_id]  = @CombinationId,
                        [location_id]     = @LocationId,
                        [notes]           = @Notes,
                        [notes_pinned]    = @NotesPinned,
                        [revised_from_id] = @RevisedFromId
                    WHERE [id] = @Id AND [document_id] = @DocumentId;
                    """;
                cmd.Parameters.Add(new SqlParameter("@Id", ln.Id));
            }
            else
            {
                cmd.CommandText = $"""
                    INSERT INTO {_lineTable}
                        ([document_id],[line_no],[item_id],[unit_id],
                         [quantity],[unit_price],[discount_rate],[line_total],
                         [combination_id],[location_id],[notes],[notes_pinned],[revised_from_id])
                    VALUES
                        (@DocumentId,@LineNo,@ItemId,@UnitId,
                         @Quantity,@UnitPrice,@DiscountRate,@LineTotal,
                         @CombinationId,@LocationId,@Notes,@NotesPinned,@RevisedFromId);
                    """;
            }
            cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            cmd.Parameters.Add(new SqlParameter("@LineNo", ln.LineNo));
            cmd.Parameters.Add(new SqlParameter("@ItemId", ln.ItemId));
            cmd.Parameters.Add(new SqlParameter("@UnitId", (object?)ln.UnitId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Quantity", ln.Quantity));
            cmd.Parameters.Add(new SqlParameter("@UnitPrice", ln.UnitPrice));
            cmd.Parameters.Add(new SqlParameter("@DiscountRate", ln.DiscountRate));
            cmd.Parameters.Add(new SqlParameter("@LineTotal", ln.LineTotal));
            cmd.Parameters.Add(new SqlParameter("@CombinationId", (object?)ln.CombinationId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@LocationId", (object?)ln.LocationId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Notes", (object?)ln.Notes ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@NotesPinned", ln.NotesPinned));
            cmd.Parameters.Add(new SqlParameter("@RevisedFromId", (object?)ln.RevisedFromId ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3) Request'te olmayan mevcut Id'leri sil (CASCADE detaylari temizler)
        var toDelete = existingIds.Except(keptIds).ToArray();
        if (toDelete.Length > 0)
        {
            await using var delCmd = conn.CreateCommand();
            delCmd.CommandText = $"DELETE FROM {_lineTable} WHERE [document_id] = @DocumentId AND [id] IN ({string.Join(",", toDelete)});";
            delCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            await delCmd.ExecuteNonQueryAsync(ct);
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

    /// <summary>
    /// Satir revizyonu — atomic SQL batch:
    ///   1) Eski satirin notlari @Description ile UPDATE edilir (revize gerekcesi).
    ///   2) Yeni satir INSERT edilir: eski satirin kolonlari aynen kopyalanir,
    ///      revised_from_id = parentLineId, notes = eski satirin ORIJINAL notu.
    ///   3) Kombinasyon detaylari (sales_quote_line_details) yeni satira kopyalanir.
    /// Not: Siralama icin line_no = MAX(line_no)+1 atanir (belge genelinde).
    /// Widget degerleri (WidgetTra vb.) bu method'a dahil DEGIL — WidgetService
    /// tarafinda copy-forward yapilir (schema dinamik).
    /// </summary>
    public async Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Eski satirin dokumentId + orijinal notes'unu al (copy icin, sonra UPDATE)
            int documentId;
            string? originalNotes;
            await using (var selCmd = conn.CreateCommand())
            {
                selCmd.Transaction = tx;
                selCmd.CommandText = $"SELECT [document_id], [notes] FROM {_lineTable} WHERE [id] = @Id;";
                selCmd.Parameters.Add(new SqlParameter("@Id", parentLineId));
                await using var r = await selCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }
                documentId = r.GetInt32(0);
                originalNotes = r.IsDBNull(1) ? null : r.GetString(1);
            }

            // 2) Yeni line_no hesapla (belgenin max + 1)
            int newLineNo;
            await using (var maxCmd = conn.CreateCommand())
            {
                maxCmd.Transaction = tx;
                maxCmd.CommandText = $"SELECT ISNULL(MAX([line_no]), 0) + 1 FROM {_lineTable} WHERE [document_id] = @DocId;";
                maxCmd.Parameters.Add(new SqlParameter("@DocId", documentId));
                var obj = await maxCmd.ExecuteScalarAsync(ct);
                newLineNo = obj == null || obj == DBNull.Value ? 1 : Convert.ToInt32(obj);
            }

            // 3) Eski satirin notes'unu aciklama ile UPDATE et
            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = $"UPDATE {_lineTable} SET [notes] = @Desc WHERE [id] = @Id;";
                upd.Parameters.Add(new SqlParameter("@Id", parentLineId));
                upd.Parameters.Add(new SqlParameter("@Desc",
                    string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description));
                await upd.ExecuteNonQueryAsync(ct);
            }

            // 4) Yeni satir INSERT — SELECT ile eski satirin kolonlarini kopyala
            //    notes = orijinal notes (eski haline sahip olsun), revised_from_id = parentId
            int newLineId;
            await using (var insCmd = conn.CreateCommand())
            {
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_lineTable}
                        ([document_id],[line_no],[item_id],[unit_id],[quantity],[unit_price],
                         [discount_rate],[line_total],[combination_id],[location_id],[notes],
                         [notes_pinned],[revised_from_id])
                    SELECT
                        [document_id], @NewLineNo, [item_id], [unit_id], [quantity], [unit_price],
                        [discount_rate], [line_total], [combination_id], [location_id], @OrigNotes,
                        [notes_pinned], @ParentId
                    FROM {_lineTable}
                    WHERE [id] = @ParentId;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                insCmd.Parameters.Add(new SqlParameter("@NewLineNo", newLineNo));
                insCmd.Parameters.Add(new SqlParameter("@ParentId", parentLineId));
                insCmd.Parameters.Add(new SqlParameter("@OrigNotes", (object?)originalNotes ?? DBNull.Value));
                var idObj = await insCmd.ExecuteScalarAsync(ct);
                newLineId = Convert.ToInt32(idObj);
            }

            // 5) Kombinasyon detaylarini yeni satira kopyala
            await using (var copyDetails = conn.CreateCommand())
            {
                copyDetails.Transaction = tx;
                copyDetails.CommandText = $"""
                    INSERT INTO {_detailTable}
                        ([quote_line_id],[feature_name],[value_code],[value_name],[description],[line_order])
                    SELECT @NewId, [feature_name], [value_code], [value_name], [description], [line_order]
                    FROM {_detailTable}
                    WHERE [quote_line_id] = @ParentId;
                    """;
                copyDetails.Parameters.Add(new SqlParameter("@NewId", newLineId));
                copyDetails.Parameters.Add(new SqlParameter("@ParentId", parentLineId));
                await copyDetails.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return newLineId;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { }
            throw;
        }
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
        CompanyId = TryGetOrdinal(r, "company_id") is int cmpOrd && cmpOrd >= 0 && !r.IsDBNull(cmpOrd)
            ? r.GetInt32(cmpOrd) : 0,
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
        var unitIdOrd  = TryGetOrdinal(r, "unit_id");
        var unitCodeOrd = TryGetOrdinal(r, "unit_code");
        var unitNameOrd = TryGetOrdinal(r, "unit_name");
        var matCodeOrd = TryGetOrdinal(r, "material_code");
        var matNameOrd = TryGetOrdinal(r, "material_name");
        var combIdOrd  = TryGetOrdinal(r, "combination_id");
        var combCodeOrd = TryGetOrdinal(r, "combination_code");
        var locIdOrd   = TryGetOrdinal(r, "location_id");
        var locCodeOrd = TryGetOrdinal(r, "location_code");
        var locNameOrd = TryGetOrdinal(r, "location_name");
        var notesPinnedOrd = TryGetOrdinal(r, "notes_pinned");
        var revisedFromIdOrd = TryGetOrdinal(r, "revised_from_id");
        return new()
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            DocumentId = r.GetInt32(r.GetOrdinal("document_id")),
            LineNo = r.GetInt32(r.GetOrdinal("line_no")),
            ItemId = r.GetInt32(r.GetOrdinal("item_id")),
            UnitId = unitIdOrd >= 0 && !r.IsDBNull(unitIdOrd) ? r.GetInt32(unitIdOrd) : null,
            Quantity = r.GetDecimal(r.GetOrdinal("quantity")),
            UnitPrice = r.GetDecimal(r.GetOrdinal("unit_price")),
            DiscountRate = r.GetDecimal(r.GetOrdinal("discount_rate")),
            LineTotal = r.GetDecimal(r.GetOrdinal("line_total")),
            CombinationId = combIdOrd >= 0 && !r.IsDBNull(combIdOrd) ? r.GetInt32(combIdOrd) : null,
            LocationId = locIdOrd >= 0 && !r.IsDBNull(locIdOrd) ? r.GetInt32(locIdOrd) : null,
            Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes")),
            NotesPinned = notesPinnedOrd >= 0 && !r.IsDBNull(notesPinnedOrd) && r.GetBoolean(notesPinnedOrd),
            RevisedFromId = revisedFromIdOrd >= 0 && !r.IsDBNull(revisedFromIdOrd) ? r.GetInt32(revisedFromIdOrd) : null,
            MaterialCode = matCodeOrd >= 0 && !r.IsDBNull(matCodeOrd) ? r.GetString(matCodeOrd) : null,
            MaterialName = matNameOrd >= 0 && !r.IsDBNull(matNameOrd) ? r.GetString(matNameOrd) : null,
            UnitCode = unitCodeOrd >= 0 && !r.IsDBNull(unitCodeOrd) ? r.GetString(unitCodeOrd) : null,
            UnitName = unitNameOrd >= 0 && !r.IsDBNull(unitNameOrd) ? r.GetString(unitNameOrd) : null,
            CombinationCode = combCodeOrd >= 0 && !r.IsDBNull(combCodeOrd) ? r.GetString(combCodeOrd) : null,
            LocationCode = locCodeOrd >= 0 && !r.IsDBNull(locCodeOrd) ? r.GetString(locCodeOrd) : null,
            LocationName = locNameOrd >= 0 && !r.IsDBNull(locNameOrd) ? r.GetString(locNameOrd) : null,
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

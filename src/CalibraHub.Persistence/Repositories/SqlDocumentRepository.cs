using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDocumentRepository : IDocumentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly ILogger<SqlDocumentRepository>? _logger;
    private readonly string _quoteTable;
    private readonly string _lineTable;
    private readonly string _detailTable;
    private readonly string _schema;

    public SqlDocumentRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor,
        IDataVisibilityFilter dvFilter,
        ILogger<SqlDocumentRepository>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _dvFilter = dvFilter;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema = schema;
        _quoteTable = $"[{schema}].[Document]";
        _lineTable = $"[{schema}].[DocumentLine]";
        _detailTable = $"[{schema}].[SalesQuoteLineDetail]";
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

        var where = "WHERE [IsActive] = 1";
        if (!string.IsNullOrWhiteSpace(status))
        {
            where += " AND [Status] = @Status";
            cmd.Parameters.Add(new SqlParameter("@Status", status.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND ([DocumentNumber] LIKE @Search)";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }

        // Satır görünürlük kuralları (row-level security). dv.Sql alias 'q' ile üretilir ve
        // where.Replace("[", "q.[") DÖNÜŞÜMÜNDEN SONRA eklenir (aksi halde q.q.[ bozulur).
        var dv = await _dvFilter.BuildAsync(FormCodes.SalesQuote, "q", "id", ct);

        // Cari ismi Contact.AccountTitle'dan cekilir — contact_name kolonu Faz 2'de drop edildi.
        cmd.CommandText = $"""
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id]) AS [line_count]
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            {where.Replace("[", "q.[")}
            {dv.Sql}
            ORDER BY q.[Created] DESC;
            """;
        foreach (var prm in dv.Parameters) cmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapQuote(r));
        return list;
    }

    public async Task<IReadOnlyCollection<Document>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct)
    {
        var list = new List<Document>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = "WHERE q.[IsActive] = 1 AND dt.[Code] = @TypeCode";
        cmd.Parameters.Add(new SqlParameter("@TypeCode", typeCode));
        if (!string.IsNullOrWhiteSpace(status))
        {
            where += " AND q.[Status] = @Status";
            cmd.Parameters.Add(new SqlParameter("@Status", status.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND q.[DocumentNumber] LIKE @Search";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }
        // Satır görünürlük kuralları (row-level security) — alias 'q', where zaten q. prefix'li.
        var dv = await _dvFilter.BuildAsync(FormCodes.SalesQuote, "q", "id", ct);

        cmd.CommandText = $"""
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id]) AS [line_count],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id] AND ISNULL(l.[FulfillmentStatus],0) = 0) AS [fulfill_pending],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id] AND ISNULL(l.[FulfillmentStatus],0) = 1) AS [fulfill_partial],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id] AND ISNULL(l.[FulfillmentStatus],0) = 2) AS [fulfill_full]
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            INNER JOIN [{_schema}].[DocumentType] dt ON dt.[Id] = q.[DocumentTypeId]
            {where}
            {dv.Sql}
            ORDER BY q.[Created] DESC;
            """;
        foreach (var prm in dv.Parameters) cmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapQuote(r));
        return list;
    }

    public async Task<IReadOnlyCollection<Document>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct)
    {
        var list = new List<Document>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = "WHERE q.[IsActive] = 1 AND q.[Status] = N'Approved' AND dt.[Code] = N'satis_teklifi'";
        // NOT EXISTS koprusu — daha onceden siparise donusturulen teklifler hariç.
        // document_source tablosu IDocumentSourceRepository.EnsureSchemaAsync ile garantilenir;
        // burada OBJECT_ID guard ile tabloya bagimliligi gevsetmek zorunda kalmadan,
        // sadece NOT EXISTS sub-query'i tablo olusturulduktan sonra cagiriyoruz (DocumentService garanti eder).
        where += $" AND NOT EXISTS (SELECT 1 FROM [{_schema}].[DocumentSource] ds WHERE ds.[SourceDocumentId] = q.[Id])";

        if (fromDate.HasValue)
        {
            where += " AND q.[DocumentDate] >= @FromDate";
            cmd.Parameters.Add(new SqlParameter("@FromDate", fromDate.Value.Date));
        }
        if (toDate.HasValue)
        {
            where += " AND q.[DocumentDate] < @ToDateExclusive";
            cmd.Parameters.Add(new SqlParameter("@ToDateExclusive", toDate.Value.Date.AddDays(1)));
        }
        if (contactId.HasValue && contactId.Value > 0)
        {
            where += " AND q.[ContactId] = @ContactId";
            cmd.Parameters.Add(new SqlParameter("@ContactId", contactId.Value));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND q.[DocumentNumber] LIKE @Search";
            cmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
        }

        cmd.CommandText = $"""
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   (SELECT COUNT(*) FROM {_lineTable} l WHERE l.[DocumentId] = q.[Id]) AS [line_count]
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            INNER JOIN [{_schema}].[DocumentType] dt ON dt.[Id] = q.[DocumentTypeId]
            {where}
            ORDER BY q.[ContactId], q.[DocumentDate] DESC;
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
            SELECT q.[Id],q.[CompanyId],q.[DocumentNumber],q.[DocumentDate],q.[ValidUntil],q.[DeliveryDate],q.[DeliveryDays],q.[ContactId],
                   ca.[AccountTitle] AS [contact_name],
                   q.[ContactAddress],
                   q.[SalesRepId],q.[RequesterPersonnelId],p.[FullName] AS RequesterPersonnelName,
                   q.[LocationId],hloc.[LocationName] AS HeaderLocationName,
                   q.[CurrencyId],cur.[Code] AS CurrencyCode,cur.[Symbol] AS CurrencySymbol,q.[SubTotal],q.[DiscountRate],q.[DiscountAmount],q.[TaxRate],q.[TaxAmount],q.[GrandTotal],
                   q.[PaymentTerms],q.[DeliveryTerms],q.[DeliveryAddress],q.[Status],q.[RevisionNo],q.[ParentDocumentId],
                   q.[Notes],q.[CreatedById],q.[Created],q.[Updated],q.[IsActive],q.[DocumentTypeId],
                   ca.[AccountCode] AS customer_code
            FROM {_quoteTable} q
            LEFT JOIN [{_schema}].[Contact] ca ON ca.[Id] = q.[ContactId]
            LEFT JOIN [{_schema}].[Currency] cur ON cur.[Id] = q.[CurrencyId]
            LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = q.[RequesterPersonnelId]
            LEFT JOIN [{_schema}].[Location] hloc ON hloc.[Id] = q.[LocationId]
            WHERE q.[Id] = @Id;
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
            SELECT l.[Id],l.[DocumentId],l.[LineNo],l.[ItemId],l.[UnitId],
                   l.[Quantity],l.[UnitPrice],l.[DiscountRate],l.[LineTotal],
                   l.[CombinationId],l.[LocationId],l.[Notes],ISNULL(l.[NotesPinned], 0) AS [NotesPinned],
                   l.[RevisedFromId], l.[SourceLineId], l.[DeliveryDate], l.[DeliveryDays],
                   ISNULL(l.[FulfilledFromStock], 0) AS [FulfilledFromStock],
                   ISNULL(l.[FulfilledByPurchase], 0) AS [FulfilledByPurchase],
                   CAST(ISNULL(l.[FulfillmentStatus], 0) AS INT) AS [FulfillmentStatus],
                   i.[Code] AS [material_code], i.[Name] AS [material_name],
                   u.[Code] AS [unit_code], u.[Name] AS [unit_name],
                   pc.[RecordCode] AS [combination_code],
                   loc.[LocationCode] AS [location_code], loc.[LocationName] AS [location_name]
            FROM {_lineTable} l
            LEFT JOIN [{_schema}].[Items] i ON i.[Id] = l.[ItemId]
            LEFT JOIN [{_schema}].[Unit] u ON u.[Id] = l.[UnitId]
            LEFT JOIN [{_schema}].[ItemConfiguration] pc ON pc.[Id] = l.[CombinationId]
            LEFT JOIN [{_schema}].[Location] loc ON loc.[Id] = l.[LocationId]
            WHERE l.[DocumentId] = @DocumentId
            ORDER BY l.[LineNo];
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
                    [DocumentTypeId]=@DocumentTypeId,
                    [DocumentDate]=@DocumentDate, [ValidUntil]=@ValidUntil, [DeliveryDate]=@DeliveryDate, [DeliveryDays]=@DeliveryDays,
                    [ContactId]=@ContactId, [ContactAddress]=@ContactAddress,
                    [SalesRepId]=@SalesRepId, [RequesterPersonnelId]=@RequesterPersonnelId, [LocationId]=@LocationId,
                    [CurrencyId]=@CurrencyId, [SubTotal]=@SubTotal, [DiscountRate]=@DiscountRate,
                    [DiscountAmount]=@DiscountAmount, [TaxRate]=@TaxRate, [TaxAmount]=@TaxAmount,
                    [GrandTotal]=@GrandTotal, [PaymentTerms]=@PaymentTerms, [DeliveryTerms]=@DeliveryTerms,
                    [DeliveryAddress]=@DeliveryAddress, [Status]=@Status, [RevisionNo]=@RevisionNo,
                    [ParentDocumentId]=@ParentDocumentId,
                    [Notes]=@Notes, [Updated]=@UpdatedAt
                WHERE [Id] = @Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", q.Id));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_quoteTable}
                    ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[ValidUntil],[DeliveryDate],[DeliveryDays],[ContactId],[ContactAddress],
                     [SalesRepId],[RequesterPersonnelId],[LocationId],[CurrencyId],[SubTotal],[DiscountRate],[DiscountAmount],[TaxRate],[TaxAmount],[GrandTotal],
                     [PaymentTerms],[DeliveryTerms],[DeliveryAddress],[Status],[RevisionNo],[ParentDocumentId],
                     [Notes],[CreatedById],[Created],[Updated],[IsActive])
                VALUES
                    (@CompanyId,@DocumentNumber,@DocumentTypeId,@DocumentDate,@ValidUntil,@DeliveryDate,@DeliveryDays,@ContactId,@ContactAddress,
                     @SalesRepId,@RequesterPersonnelId,@LocationId,@CurrencyId,@SubTotal,@DiscountRate,@DiscountAmount,@TaxRate,@TaxAmount,@GrandTotal,
                     @PaymentTerms,@DeliveryTerms,@DeliveryAddress,@Status,@RevisionNo,@ParentDocumentId,
                     @Notes,@CreatedById,@CreatedAt,@UpdatedAt,@IsActive);
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
        cmd.Parameters.Add(new SqlParameter("@DeliveryDate", (object?)q.DeliveryDate ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DeliveryDays", (object?)q.DeliveryDays ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactId", (object?)q.ContactId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactAddress", (object?)q.ContactAddress ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SalesRepId", (object?)q.SalesRepId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RequesterPersonnelId", (object?)q.RequesterPersonnelId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LocationId", (object?)q.LocationId ?? DBNull.Value));
        // CurrencyId — entity'de int, default 1 (TRY). Display alanlari (CurrencyCode/Symbol)
        // SELECT'te currencies JOIN ile doldurulur, INSERT/UPDATE'te kullanilmaz.
        cmd.Parameters.Add(new SqlParameter("@CurrencyId", q.CurrencyId > 0 ? q.CurrencyId : 1));
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
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)q.CreatedById ?? DBNull.Value));
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

        // TANI LOG: hangi satir id'lerine hangi qty/price geliyor — revize sorunlarini
        // takip etmek icin (kullanici "qty parent'a yaziliyor" raporu).
        if (_logger != null)
        {
            foreach (var ln in lines)
            {
                _logger.LogInformation(
                    "[SaveLines] docId={DocId} lineId={LineId} lineNo={LineNo} itemId={ItemId} qty={Qty} unitPrice={Price} revisedFromId={Rev}",
                    documentId, ln.Id, ln.LineNo, ln.ItemId, ln.Quantity, ln.UnitPrice, ln.RevisedFromId);
            }
        }

        // 1) Mevcut satir Id'lerini topla
        var existingIds = new HashSet<int>();
        await using (var getCmd = conn.CreateCommand())
        {
            getCmd.CommandText = $"SELECT [Id] FROM {_lineTable} WHERE [DocumentId] = @DocumentId;";
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
                        [LineNo]         = @LineNo,
                        [ItemId]         = @ItemId,
                        [UnitId]         = @UnitId,
                        [Quantity]        = @Quantity,
                        [UnitPrice]      = @UnitPrice,
                        [DiscountRate]   = @DiscountRate,
                        [LineTotal]      = @LineTotal,
                        [CombinationId]  = @CombinationId,
                        [LocationId]     = @LocationId,
                        [Notes]           = @Notes,
                        [NotesPinned]    = @NotesPinned,
                        [RevisedFromId] = @RevisedFromId,
                        [SourceLineId]  = @SourceLineId,
                        [DeliveryDate]  = @DeliveryDate,
                        [DeliveryDays]  = @DeliveryDays
                    WHERE [Id] = @Id AND [DocumentId] = @DocumentId;
                    """;
                cmd.Parameters.Add(new SqlParameter("@Id", ln.Id));
            }
            else
            {
                cmd.CommandText = $"""
                    INSERT INTO {_lineTable}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],
                         [Quantity],[UnitPrice],[DiscountRate],[LineTotal],
                         [CombinationId],[LocationId],[Notes],[NotesPinned],[RevisedFromId],[SourceLineId],[DeliveryDate],[DeliveryDays])
                    VALUES
                        (@DocumentId,@LineNo,@ItemId,@UnitId,
                         @Quantity,@UnitPrice,@DiscountRate,@LineTotal,
                         @CombinationId,@LocationId,@Notes,@NotesPinned,@RevisedFromId,@SourceLineId,@DeliveryDate,@DeliveryDays);
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
            cmd.Parameters.Add(new SqlParameter("@SourceLineId", (object?)ln.SourceLineId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@DeliveryDate", (object?)ln.DeliveryDate ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@DeliveryDays", (object?)ln.DeliveryDays ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3) Request'te olmayan mevcut Id'leri sil (CASCADE detaylari temizler)
        var toDelete = existingIds.Except(keptIds).ToArray();
        if (toDelete.Length > 0)
        {
            await using var delCmd = conn.CreateCommand();
            delCmd.CommandText = $"DELETE FROM {_lineTable} WHERE [DocumentId] = @DocumentId AND [Id] IN ({string.Join(",", toDelete)});";
            delCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            await delCmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<int> AppendStockLineAsync(int documentId, DocumentLine line, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var newId = await AppendStockLineCoreAsync(conn, tx, documentId, line, ct);
            await tx.CommitAsync(ct);
            return newId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Append-only insert — SaveLinesAsync'in upsert-all/replace davranışının aksine mevcut
    /// satırlara dokunmaz. LineNo ataması UPDLOCK+HOLDLOCK ile concurrent-safe (SqlDocumentNumberService
    /// deseninin aynısı — bkz. SqlDocumentNumberService.IncrementCounterAsync).
    /// </summary>
    private async Task<int> AppendStockLineCoreAsync(SqlConnection conn, SqlTransaction tx, int documentId, DocumentLine line, CancellationToken ct)
    {
        int nextLineNo;
        await using (var selCmd = conn.CreateCommand())
        {
            selCmd.Transaction = tx;
            selCmd.CommandText = $"""
                SELECT ISNULL(MAX([LineNo]), 0) + 1 FROM {_lineTable} WITH (UPDLOCK, HOLDLOCK)
                WHERE [DocumentId] = @DocumentId;
                """;
            selCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            nextLineNo = Convert.ToInt32(await selCmd.ExecuteScalarAsync(ct));
        }

        await using var insCmd = conn.CreateCommand();
        insCmd.Transaction = tx;
        insCmd.CommandText = $"""
            INSERT INTO {_lineTable}
                ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[BaseQuantity],[UnitPrice],[DiscountRate],[LineTotal],
                 [CombinationId],[LocationId],[FromLocationId],[MovementType],[UnitCost],[LotNo],[Notes])
            VALUES
                (@DocumentId,@LineNo,@ItemId,@UnitId,@Quantity,{StockUnitSql.BaseQtyExpr($"[{_schema}].[Items]", $"[{_schema}].[ItemUnits]", "@Quantity", "@ItemId", "@UnitId")},0,0,0,
                 @CombinationId,@LocationId,@FromLocationId,@MovementType,@UnitCost,@LotNo,@Notes);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        insCmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
        insCmd.Parameters.Add(new SqlParameter("@LineNo", nextLineNo));
        insCmd.Parameters.Add(new SqlParameter("@ItemId", line.ItemId));
        insCmd.Parameters.Add(new SqlParameter("@UnitId", (object?)line.UnitId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@Quantity", line.Quantity));
        insCmd.Parameters.Add(new SqlParameter("@CombinationId", (object?)line.CombinationId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@LocationId", (object?)line.LocationId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@FromLocationId", (object?)line.FromLocationId ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@MovementType", (object?)line.MovementType ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@UnitCost", (object?)line.UnitCost ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@LotNo", (object?)line.LotNo ?? DBNull.Value));
        insCmd.Parameters.Add(new SqlParameter("@Notes", (object?)line.Notes ?? DBNull.Value));
        return Convert.ToInt32(await insCmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_quoteTable} SET [IsActive] = 0, [Updated] = @Now WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateStatusAsync(int id, string status, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_quoteTable} SET [Status] = @Status, [Updated] = @Now WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@Status", status));
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
                selCmd.CommandText = $"SELECT [DocumentId], [Notes] FROM {_lineTable} WHERE [Id] = @Id;";
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
                maxCmd.CommandText = $"SELECT ISNULL(MAX([LineNo]), 0) + 1 FROM {_lineTable} WHERE [DocumentId] = @DocId;";
                maxCmd.Parameters.Add(new SqlParameter("@DocId", documentId));
                var obj = await maxCmd.ExecuteScalarAsync(ct);
                newLineNo = obj == null || obj == DBNull.Value ? 1 : Convert.ToInt32(obj);
            }

            // 3) Eski satirin notes'unu aciklama ile UPDATE et
            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = $"UPDATE {_lineTable} SET [Notes] = @Desc WHERE [Id] = @Id;";
                upd.Parameters.Add(new SqlParameter("@Id", parentLineId));
                upd.Parameters.Add(new SqlParameter("@Desc",
                    string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description));
                await upd.ExecuteNonQueryAsync(ct);
            }

            // 4) Yeni satir INSERT — SELECT ile eski satirin kolonlarini kopyala
            //    notes = orijinal notes, revised_from_id = NULL (yeni satir her zaman aktif/son haldir)
            //    source_line_id = eski satirinki aynen tasinir (orijinal kaynak iz korunur)
            int newLineId;
            await using (var insCmd = conn.CreateCommand())
            {
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_lineTable}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[UnitPrice],
                         [DiscountRate],[LineTotal],[CombinationId],[LocationId],[Notes],
                         [NotesPinned],[RevisedFromId],[SourceLineId],[DeliveryDate],[DeliveryDays])
                    SELECT
                        [DocumentId], @NewLineNo, [ItemId], [UnitId], [Quantity], [UnitPrice],
                        [DiscountRate], [LineTotal], [CombinationId], [LocationId], @OrigNotes,
                        [NotesPinned], NULL, [SourceLineId], [DeliveryDate], [DeliveryDays]
                    FROM {_lineTable}
                    WHERE [Id] = @ParentId;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                insCmd.Parameters.Add(new SqlParameter("@NewLineNo", newLineNo));
                insCmd.Parameters.Add(new SqlParameter("@ParentId", parentLineId));
                insCmd.Parameters.Add(new SqlParameter("@OrigNotes", (object?)originalNotes ?? DBNull.Value));
                var idObj = await insCmd.ExecuteScalarAsync(ct);
                newLineId = Convert.ToInt32(idObj);
            }

            // 4b) Eski satirda revised_from_id = yeni satir id'si — eski satir artik superseded
            await using (var markOld = conn.CreateCommand())
            {
                markOld.Transaction = tx;
                markOld.CommandText = $"UPDATE {_lineTable} SET [RevisedFromId] = @NewLineId WHERE [Id] = @ParentId;";
                markOld.Parameters.Add(new SqlParameter("@NewLineId", newLineId));
                markOld.Parameters.Add(new SqlParameter("@ParentId", parentLineId));
                await markOld.ExecuteNonQueryAsync(ct);
            }

            // 5) Kombinasyon detaylarini yeni satira kopyala
            await using (var copyDetails = conn.CreateCommand())
            {
                copyDetails.Transaction = tx;
                copyDetails.CommandText = $"""
                    INSERT INTO {_detailTable}
                        ([QuoteLineId],[FeatureName],[ValueCode],[ValueName],[Description],[LineOrder])
                    SELECT @NewId, [FeatureName], [ValueCode], [ValueName], [Description], [LineOrder]
                    FROM {_detailTable}
                    WHERE [QuoteLineId] = @ParentId;
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
            SELECT TOP 1 [DocumentNumber] FROM {_quoteTable}
            WHERE [DocumentNumber] LIKE @Prefix + '%'
            ORDER BY [DocumentNumber] DESC;
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
        Id = r.GetInt32(r.GetOrdinal("Id")),
        CompanyId = TryGetOrdinal(r, "CompanyId") is int cmpOrd && cmpOrd >= 0 && !r.IsDBNull(cmpOrd)
            ? r.GetInt32(cmpOrd) : 0,
        DocumentNumber = r.GetString(r.GetOrdinal("DocumentNumber")),
        DocumentTypeId = TryGetOrdinal(r, "DocumentTypeId") is int dtOrd && dtOrd >= 0 && !r.IsDBNull(dtOrd)
            ? r.GetInt32(dtOrd) : null,
        DocumentDate = r.GetDateTime(r.GetOrdinal("DocumentDate")),
        ValidUntil = r.IsDBNull(r.GetOrdinal("ValidUntil")) ? null : r.GetDateTime(r.GetOrdinal("ValidUntil")),
        DeliveryDate = SafeOrdinalDate(r, "DeliveryDate"),
        DeliveryDays = SafeOrdinalInt(r, "DeliveryDays"),
        ContactId = r.IsDBNull(r.GetOrdinal("ContactId")) ? null : r.GetInt32(r.GetOrdinal("ContactId")),
        ContactName = r.IsDBNull(r.GetOrdinal("contact_name")) ? null : r.GetString(r.GetOrdinal("contact_name")),
        ContactAddress = r.IsDBNull(r.GetOrdinal("ContactAddress")) ? null : r.GetString(r.GetOrdinal("ContactAddress")),
        ContactCode = TryGetOrdinal(r, "customer_code") is int ccOrd && ccOrd >= 0 && !r.IsDBNull(ccOrd)
            ? r.GetString(ccOrd) : null,
        SalesRepId = r.IsDBNull(r.GetOrdinal("SalesRepId")) ? null : r.GetInt32(r.GetOrdinal("SalesRepId")),
        RequesterPersonnelId = TryGetOrdinal(r, "RequesterPersonnelId") is int reqOrd && reqOrd >= 0 && !r.IsDBNull(reqOrd)
            ? r.GetInt32(reqOrd) : null,
        RequesterPersonnelName = TryGetOrdinal(r, "RequesterPersonnelName") is int rnOrd && rnOrd >= 0 && !r.IsDBNull(rnOrd)
            ? r.GetString(rnOrd) : null,
        LocationId = TryGetOrdinal(r, "LocationId") is int hlocOrd && hlocOrd >= 0 && !r.IsDBNull(hlocOrd)
            ? r.GetInt32(hlocOrd) : null,
        LocationName = TryGetOrdinal(r, "HeaderLocationName") is int hlocNOrd && hlocNOrd >= 0 && !r.IsDBNull(hlocNOrd)
            ? r.GetString(hlocNOrd) : null,
        CurrencyId = TryGetOrdinal(r, "CurrencyId") is int curOrd && curOrd >= 0 && !r.IsDBNull(curOrd)
            ? r.GetInt32(curOrd) : 1,
        CurrencyCode = TryGetOrdinal(r, "CurrencyCode") is int curCodeOrd && curCodeOrd >= 0 && !r.IsDBNull(curCodeOrd)
            ? r.GetString(curCodeOrd) : null,
        CurrencySymbol = TryGetOrdinal(r, "CurrencySymbol") is int curSymOrd && curSymOrd >= 0 && !r.IsDBNull(curSymOrd)
            ? r.GetString(curSymOrd) : null,
        SubTotal = r.GetDecimal(r.GetOrdinal("SubTotal")),
        DiscountRate = r.GetDecimal(r.GetOrdinal("DiscountRate")),
        DiscountAmount = r.GetDecimal(r.GetOrdinal("DiscountAmount")),
        TaxRate = r.GetDecimal(r.GetOrdinal("TaxRate")),
        TaxAmount = r.GetDecimal(r.GetOrdinal("TaxAmount")),
        GrandTotal = r.GetDecimal(r.GetOrdinal("GrandTotal")),
        PaymentTerms = r.IsDBNull(r.GetOrdinal("PaymentTerms")) ? null : r.GetString(r.GetOrdinal("PaymentTerms")),
        DeliveryTerms = r.IsDBNull(r.GetOrdinal("DeliveryTerms")) ? null : r.GetString(r.GetOrdinal("DeliveryTerms")),
        DeliveryAddress = r.IsDBNull(r.GetOrdinal("DeliveryAddress")) ? null : r.GetString(r.GetOrdinal("DeliveryAddress")),
        Status = Enum.TryParse<DocumentStatus>(r.GetString(r.GetOrdinal("Status")), out var s) ? s : DocumentStatus.Draft,
        RevisionNo = r.GetInt32(r.GetOrdinal("RevisionNo")),
        ParentDocumentId = r.IsDBNull(r.GetOrdinal("ParentDocumentId")) ? null : r.GetInt32(r.GetOrdinal("ParentDocumentId")),
        Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
        CreatedById = TryGetOrdinal(r, "CreatedById") is int cbOrd && cbOrd >= 0 && !r.IsDBNull(cbOrd) ? r.GetInt32(cbOrd) : null,
        CreatedAt = r.GetDateTime(r.GetOrdinal("Created")),
        UpdatedAt = r.IsDBNull(r.GetOrdinal("Updated")) ? DateTime.MinValue : r.GetDateTime(r.GetOrdinal("Updated")),
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
        LineCount      = TryGetOrdinal(r, "line_count")      is int lcOrd  && lcOrd  >= 0 ? r.GetInt32(lcOrd)  : 0,
        FulfillPending = TryGetOrdinal(r, "fulfill_pending") is int fp0rd  && fp0rd  >= 0 ? r.GetInt32(fp0rd)  : 0,
        FulfillPartial = TryGetOrdinal(r, "fulfill_partial") is int fp1rd  && fp1rd  >= 0 ? r.GetInt32(fp1rd)  : 0,
        FulfillFull    = TryGetOrdinal(r, "fulfill_full")    is int fp2rd  && fp2rd  >= 0 ? r.GetInt32(fp2rd)  : 0,
    };

    private static DocumentLine MapLine(SqlDataReader r)
    {
        var unitIdOrd  = TryGetOrdinal(r, "UnitId");
        var unitCodeOrd = TryGetOrdinal(r, "unit_code");
        var unitNameOrd = TryGetOrdinal(r, "unit_name");
        var matCodeOrd = TryGetOrdinal(r, "material_code");
        var matNameOrd = TryGetOrdinal(r, "material_name");
        var combIdOrd  = TryGetOrdinal(r, "CombinationId");
        var combCodeOrd = TryGetOrdinal(r, "combination_code");
        var locIdOrd   = TryGetOrdinal(r, "LocationId");
        var locCodeOrd = TryGetOrdinal(r, "location_code");
        var locNameOrd = TryGetOrdinal(r, "location_name");
        var notesPinnedOrd = TryGetOrdinal(r, "NotesPinned");
        var revisedFromIdOrd = TryGetOrdinal(r, "RevisedFromId");
        var sourceLineIdOrd = TryGetOrdinal(r, "SourceLineId");
        return new()
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            DocumentId = r.GetInt32(r.GetOrdinal("DocumentId")),
            LineNo = r.GetInt32(r.GetOrdinal("LineNo")),
            ItemId = r.GetInt32(r.GetOrdinal("ItemId")),
            UnitId = unitIdOrd >= 0 && !r.IsDBNull(unitIdOrd) ? r.GetInt32(unitIdOrd) : null,
            Quantity = r.GetDecimal(r.GetOrdinal("Quantity")),
            UnitPrice = r.GetDecimal(r.GetOrdinal("UnitPrice")),
            DiscountRate = r.GetDecimal(r.GetOrdinal("DiscountRate")),
            LineTotal = r.GetDecimal(r.GetOrdinal("LineTotal")),
            CombinationId = combIdOrd >= 0 && !r.IsDBNull(combIdOrd) ? r.GetInt32(combIdOrd) : null,
            LocationId = locIdOrd >= 0 && !r.IsDBNull(locIdOrd) ? r.GetInt32(locIdOrd) : null,
            Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
            NotesPinned = notesPinnedOrd >= 0 && !r.IsDBNull(notesPinnedOrd) && r.GetBoolean(notesPinnedOrd),
            RevisedFromId = revisedFromIdOrd >= 0 && !r.IsDBNull(revisedFromIdOrd) ? r.GetInt32(revisedFromIdOrd) : null,
            SourceLineId = sourceLineIdOrd >= 0 && !r.IsDBNull(sourceLineIdOrd) ? r.GetInt32(sourceLineIdOrd) : null,
            DeliveryDate = SafeOrdinalDate(r, "DeliveryDate"),
            DeliveryDays = SafeOrdinalInt(r, "DeliveryDays"),
            MaterialCode = matCodeOrd >= 0 && !r.IsDBNull(matCodeOrd) ? r.GetString(matCodeOrd) : null,
            MaterialName = matNameOrd >= 0 && !r.IsDBNull(matNameOrd) ? r.GetString(matNameOrd) : null,
            UnitCode = unitCodeOrd >= 0 && !r.IsDBNull(unitCodeOrd) ? r.GetString(unitCodeOrd) : null,
            UnitName = unitNameOrd >= 0 && !r.IsDBNull(unitNameOrd) ? r.GetString(unitNameOrd) : null,
            CombinationCode = combCodeOrd >= 0 && !r.IsDBNull(combCodeOrd) ? r.GetString(combCodeOrd) : null,
            LocationCode       = locCodeOrd >= 0 && !r.IsDBNull(locCodeOrd) ? r.GetString(locCodeOrd) : null,
            LocationName       = locNameOrd >= 0 && !r.IsDBNull(locNameOrd) ? r.GetString(locNameOrd) : null,
            FulfilledFromStock = SafeOrdinalDecimal(r, "FulfilledFromStock"),
            FulfilledByPurchase = SafeOrdinalDecimal(r, "FulfilledByPurchase"),
            FulfillmentStatus  = TryGetOrdinal(r, "FulfillmentStatus") is int fsOrd && fsOrd >= 0 && !r.IsDBNull(fsOrd) ? r.GetInt32(fsOrd) : 0,
        };
    }

    private static int TryGetOrdinal(SqlDataReader r, string name)
    {
        try { return r.GetOrdinal(name); } catch { return -1; }
    }

    /// <summary>Kolon yoksa (eski schema) veya null ise null doner — SELECT'lerde guvenli.</summary>
    private static DateTime? SafeOrdinalDate(SqlDataReader r, string name)
    {
        var ord = TryGetOrdinal(r, name);
        if (ord < 0) return null;
        return r.IsDBNull(ord) ? null : r.GetDateTime(ord);
    }

    /// <summary>Int kolonunu guvenli oku — kolon yoksa veya null ise null doner.</summary>
    private static int? SafeOrdinalInt(SqlDataReader r, string name)
    {
        var ord = TryGetOrdinal(r, name);
        if (ord < 0) return null;
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }

    /// <summary>Decimal kolonunu guvenli oku — kolon yoksa veya null ise 0 doner.</summary>
    private static decimal SafeOrdinalDecimal(SqlDataReader r, string name)
    {
        var ord = TryGetOrdinal(r, name);
        if (ord < 0) return 0m;
        return r.IsDBNull(ord) ? 0m : r.GetDecimal(ord);
    }

    // ── Fulfillment update ───────────────────────────────────────────────

    public async Task UpdateLineFulfillmentAsync(int lineId, decimal fulfilledFromStock, decimal fulfilledByPurchase, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            DECLARE @qty DECIMAL(18,4), @newStatus INT;
            SELECT @qty = [Quantity] FROM {_lineTable} WHERE [Id] = @LineId;
            SET @newStatus = CASE
                WHEN @qty IS NULL THEN 0
                WHEN (@FulfilledFromStock + @FulfilledByPurchase) >= @qty THEN 2
                WHEN (@FulfilledFromStock + @FulfilledByPurchase) > 0     THEN 1
                ELSE 0
            END;
            UPDATE {_lineTable}
               SET [FulfilledFromStock]  = @FulfilledFromStock,
                   [FulfilledByPurchase] = @FulfilledByPurchase,
                   [FulfillmentStatus]   = @newStatus
             WHERE [Id] = @LineId;
            """;
        cmd.Parameters.Add(new SqlParameter("@LineId", lineId));
        cmd.Parameters.Add(new SqlParameter("@FulfilledFromStock",  fulfilledFromStock));
        cmd.Parameters.Add(new SqlParameter("@FulfilledByPurchase", fulfilledByPurchase));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Line Details (ozellik-deger aciklamalari) ────────────────────────

    public async Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(int documentLineId, CancellationToken ct)
    {
        var list = new List<DocumentLineDetail>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[QuoteLineId],[FeatureName],[ValueCode],[ValueName],[Description],[LineOrder]
            FROM {_detailTable} WHERE [QuoteLineId] = @LineId ORDER BY [LineOrder];
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
            del.CommandText = $"DELETE FROM {_detailTable} WHERE [QuoteLineId] = @LineId;";
            del.Parameters.Add(new SqlParameter("@LineId", documentLineId));
            await del.ExecuteNonQueryAsync(ct);
        }
        var order = 0;
        foreach (var d in details)
        {
            order++;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_detailTable} ([QuoteLineId],[FeatureName],[ValueCode],[ValueName],[Description],[LineOrder])
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

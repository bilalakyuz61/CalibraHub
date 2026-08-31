using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Repositories;

/// <inheritdoc />
public sealed class SqlPurchaseInvoiceRepository : IPurchaseInvoiceRepository
{
    private const string InvoiceTypeCode = "alis_faturasi";
    private const string InvoicePrefix = "AFT";

    /// <summary>Alış faturasında stok hareketi yönü: giriş.</summary>
    private const byte MovementReceipt = 2;

    /// <summary>DocumentLineLink.LinkType = 10 (dönüşüm/derivation).</summary>
    private const byte DerivationLinkType = 10;

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IDocumentNumberService _numberService;
    private readonly IDocumentLineLinkRepository? _lineLinks;
    private readonly ILogger<SqlPurchaseInvoiceRepository>? _logger;
    private readonly string _schema;

    public SqlPurchaseInvoiceRepository(
        SqlServerConnectionFactory connectionFactory,
        IDocumentNumberService numberService,
        CalibraDatabaseOptions options,
        IDocumentLineLinkRepository? lineLinks = null,
        ILogger<SqlPurchaseInvoiceRepository>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _numberService = numberService;
        _lineLinks = lineLinks;
        _logger = logger;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema.Replace("]", "]]")}].[{table}]";

    // ── Aday okuma ────────────────────────────────────────────────────────────

    public async Task<PurchaseInvoiceCandidatesDto?> GetCandidatesAsync(
        int incomingDocumentId, string mode, CancellationToken ct)
    {
        if (incomingDocumentId <= 0) return null;
        mode = NormalizeMode(mode);

        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) e-belge başlığı + bağlı cari + (varsa) daha önce üretilmiş fatura
        int? contactId = null;
        string? contactCode = null, contactTitle = null, senderTaxNumber = null, docNumber = null;
        DateTime issueDate = DateTime.Today;
        int? existingInvoiceId = null;
        string? existingInvoiceNumber = null;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT d.[DocumentNumber], d.[IssueDate], d.[SenderTaxNumber], d.[ContactId],
                       c.[AccountCode], c.[AccountTitle], d.[InvoiceDocumentId], inv.[DocumentNumber]
                  FROM {T("IncomingDocument")} d
                  LEFT JOIN {T("Contact")}  c   ON c.[Id]  = d.[ContactId]
                  LEFT JOIN {T("Document")} inv ON inv.[Id] = d.[InvoiceDocumentId] AND inv.[IsActive] = 1
                 WHERE d.[Id] = @Id AND d.[CompanyId] = @Cid;
                """;
            cmd.Parameters.AddWithValue("@Id", incomingDocumentId);
            cmd.Parameters.AddWithValue("@Cid", companyId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            docNumber = r.GetString(0);
            issueDate = r.GetDateTime(1);
            senderTaxNumber = r.IsDBNull(2) ? null : r.GetString(2);
            contactId = r.IsDBNull(3) ? null : r.GetInt32(3);
            contactCode = r.IsDBNull(4) ? null : r.GetString(4);
            contactTitle = r.IsDBNull(5) ? null : r.GetString(5);
            existingInvoiceId = r.IsDBNull(6) ? null : r.GetInt32(6);
            existingInvoiceNumber = r.IsDBNull(7) ? null : r.GetString(7);
        }

        // 2) e-belge kalemleri + stok önerisi
        var eLines = new List<EDocumentInvoiceLineDto>();
        await using (var cmd = conn.CreateCommand())
        {
            // Öneri sırası: (a) bu carinin kendi kodu (Cari×Stok / ContactItem.VendorCode),
            // (b) bizim stok kodumuzla birebir eşleşme. Öğrenilen eşleştirme (a) her zaman
            // öncelikli: tedarikçi kendi kodunu kullanır, bizim kod tesadüfen çakışabilir.
            cmd.CommandText = $"""
                SELECT l.[LineNumber], l.[ItemCode], l.[ItemName], l.[Quantity], l.[UnitCode],
                       l.[UnitPrice], l.[LineAmount], l.[VatRate],
                       (SELECT TOP 1 t.[TaxAmount] FROM {T("IncomingDocumentTax")} t
                         WHERE t.[IncomingDocumentLineId] = l.[Id]) AS VatAmount,
                       ci.[ItemId] AS VendorItemId, i2.[Id] AS CodeItemId,
                       ISNULL(i1.[Code], i2.[Code]) AS SugCode,
                       ISNULL(i1.[Name], i2.[Name]) AS SugName,
                       u.[Id] AS UnitId, u.[Code] AS UnitLocalCode, u.[Name] AS UnitLocalName
                  FROM {T("IncomingDocumentLine")} l
                  LEFT JOIN {T("ContactItem")} ci
                         ON ci.[ContactId] = @ContactId AND ci.[IsActive] = 1
                        AND NULLIF(LTRIM(RTRIM(l.[ItemCode])), N'') IS NOT NULL
                        AND LTRIM(RTRIM(ci.[VendorCode])) = LTRIM(RTRIM(l.[ItemCode]))
                  LEFT JOIN {T("Items")} i1 ON i1.[Id] = ci.[ItemId]
                  LEFT JOIN {T("Items")} i2
                         ON i2.[IsActive] = 1
                        AND NULLIF(LTRIM(RTRIM(l.[ItemCode])), N'') IS NOT NULL
                        AND LTRIM(RTRIM(i2.[Code])) = LTRIM(RTRIM(l.[ItemCode]))
                  -- Birim eşleşmesi: e-belge UBL'de ULUSLARARASI kodu taşır (C62 = adet,
                  -- KGM = kg). Bizdeki karşılığı Unit.IntlCode; o boşsa Unit.Code denenir.
                  LEFT JOIN {T("Unit")} u
                         ON u.[IsActive] = 1
                        AND NULLIF(LTRIM(RTRIM(l.[UnitCode])), N'') IS NOT NULL
                        AND (LTRIM(RTRIM(u.[IntlCode])) = LTRIM(RTRIM(l.[UnitCode]))
                          OR LTRIM(RTRIM(u.[Code]))     = LTRIM(RTRIM(l.[UnitCode])))
                 WHERE l.[IncomingDocumentId] = @Id AND l.[CompanyId] = @Cid
                 ORDER BY l.[LineNumber];
                """;
            cmd.Parameters.AddWithValue("@Id", incomingDocumentId);
            cmd.Parameters.AddWithValue("@Cid", companyId);
            cmd.Parameters.AddWithValue("@ContactId", (object?)contactId ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var vendorItemId = r.IsDBNull(9) ? (int?)null : r.GetInt32(9);
                var codeItemId = r.IsDBNull(10) ? (int?)null : r.GetInt32(10);
                eLines.Add(new EDocumentInvoiceLineDto(
                    LineNumber: r.GetInt32(0),
                    ItemCode: r.IsDBNull(1) ? null : r.GetString(1),
                    ItemName: r.IsDBNull(2) ? null : r.GetString(2),
                    Quantity: r.GetDecimal(3),
                    UnitCode: r.IsDBNull(4) ? null : r.GetString(4),
                    UnitPrice: r.GetDecimal(5),
                    LineAmount: r.IsDBNull(6) ? null : r.GetDecimal(6),
                    VatRate: r.IsDBNull(7) ? null : r.GetDecimal(7),
                    VatAmount: r.IsDBNull(8) ? null : r.GetDecimal(8),
                    SuggestedItemId: vendorItemId ?? codeItemId,
                    SuggestedItemCode: r.IsDBNull(11) ? null : r.GetString(11),
                    SuggestedItemName: r.IsDBNull(12) ? null : r.GetString(12),
                    UnitId: r.IsDBNull(13) ? null : r.GetInt32(13),
                    UnitLocalCode: r.IsDBNull(14) ? null : r.GetString(14),
                    UnitLocalName: r.IsDBNull(15) ? null : r.GetString(15)));
            }
        }

        // 3) Aday kaynak satırlar (yalnız sipariş/irsaliye modunda)
        var sourceLines = new List<PurchaseInvoiceSourceLineDto>();
        if (mode is "order" or "delivery" && contactId.HasValue)
        {
            var sourceType = mode == "order" ? "alis_siparisi" : "alis_irsaliyesi";
            // Sipariş satırı TİCARİ satırdır (MovementType NULL); irsaliye satırı stok hareketidir
            // (MovementType dolu). İkisini aynı filtreyle çekmek irsaliyede 0 satır dönderirdi.
            var movementFilter = mode == "order"
                ? "dl.[MovementType] IS NULL"
                : "dl.[MovementType] IS NOT NULL";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT dl.[Id], dl.[DocumentId], doc.[DocumentNumber], doc.[DocumentDate], dl.[LineNo],
                       dl.[ItemId], i.[Code], i.[Name], dl.[Quantity], dl.[UnitId], u.[Code] AS UnitCode,
                       dl.[UnitPrice], dl.[BaseQuantity], ISNULL(dl.[LocationId], doc.[LocationId]) AS LocId,
                       ISNULL(inv.[InvoicedQty], 0) AS InvoicedQty
                  FROM {T("DocumentLine")} dl
                  INNER JOIN {T("Document")}     doc ON doc.[Id] = dl.[DocumentId]
                  INNER JOIN {T("DocumentType")} dt  ON dt.[Id]  = doc.[DocumentTypeId]
                  LEFT  JOIN {T("Items")} i ON i.[Id] = dl.[ItemId]
                  LEFT  JOIN {T("Unit")}  u ON u.[Id] = dl.[UnitId]
                  OUTER APPLY (
                        SELECT SUM(ll.[Quantity]) AS InvoicedQty
                          FROM {T("DocumentLineLink")} ll
                          INNER JOIN {T("DocumentLine")} tl ON tl.[Id] = ll.[TargetLineId]
                          INNER JOIN {T("Document")}     td ON td.[Id] = tl.[DocumentId] AND td.[IsActive] = 1
                          INNER JOIN {T("DocumentType")} tt ON tt.[Id] = td.[DocumentTypeId]
                                                          AND tt.[Code] = N'{InvoiceTypeCode}'
                         WHERE ll.[SourceLineId] = dl.[Id]
                           AND ll.[IsActive] = 1 AND ll.[LinkType] = {DerivationLinkType}) inv
                 WHERE doc.[CompanyId] = @Cid AND doc.[IsActive] = 1
                   AND doc.[ContactId] = @ContactId
                   AND dt.[Code] = @SourceType
                   AND dl.[ItemId] IS NOT NULL
                   AND {movementFilter}
                 ORDER BY doc.[DocumentDate] DESC, doc.[DocumentNumber], dl.[LineNo];
                """;
            cmd.Parameters.AddWithValue("@Cid", companyId);
            cmd.Parameters.AddWithValue("@ContactId", contactId.Value);
            cmd.Parameters.AddWithValue("@SourceType", sourceType);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var qty = r.GetDecimal(8);
                var invoiced = r.GetDecimal(14);
                var remaining = qty - invoiced;
                if (remaining <= 0) continue;   // tamamı faturalanmış satır listeye girmez

                sourceLines.Add(new PurchaseInvoiceSourceLineDto(
                    LineId: r.GetInt32(0),
                    DocumentId: r.GetInt32(1),
                    DocumentNumber: r.GetString(2),
                    DocumentDate: r.GetDateTime(3),
                    LineNo: r.GetInt32(4),
                    ItemId: r.GetInt32(5),
                    ItemCode: r.IsDBNull(6) ? null : r.GetString(6),
                    ItemName: r.IsDBNull(7) ? null : r.GetString(7),
                    Quantity: qty,
                    InvoicedQuantity: invoiced,
                    RemainingQuantity: remaining,
                    UnitId: r.IsDBNull(9) ? null : r.GetInt32(9),
                    UnitCode: r.IsDBNull(10) ? null : r.GetString(10),
                    UnitPrice: r.GetDecimal(11),
                    BaseQuantity: r.GetDecimal(12),
                    LocationId: r.IsDBNull(13) ? null : r.GetInt32(13)));
            }
        }

        return new PurchaseInvoiceCandidatesDto(
            IncomingDocumentId: incomingDocumentId,
            DocumentNumber: docNumber ?? string.Empty,
            IssueDate: issueDate,
            Mode: mode,
            ContactId: contactId,
            ContactCode: contactCode,
            ContactTitle: contactTitle,
            SenderTaxNumber: senderTaxNumber,
            EDocumentLines: eLines,
            SourceLines: sourceLines,
            ExistingInvoiceId: existingInvoiceId,
            ExistingInvoiceNumber: existingInvoiceNumber);
    }

    // ── Fatura oluşturma ──────────────────────────────────────────────────────

    public async Task<CreatePurchaseInvoiceResultDto> CreateAsync(
        CreatePurchaseInvoiceRequest request, int? userId, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Lines is null || request.Lines.Count == 0)
            throw new InvalidOperationException("Faturaya yazılacak kalem yok.");
        if (request.ContactId <= 0)
            throw new InvalidOperationException("Fatura için cari seçilmelidir.");

        var mode = NormalizeMode(request.Mode);
        // Stok etkisi: yalnız irsaliye bağlantılı faturada YOK — mal girişini irsaliye yaptı,
        // fatura mükerrer saymaz. Doğrudan/sipariş bağlantılıda giriş hareketi üretilir.
        var affectsStock = mode != "delivery";
        var companyId = _connectionFactory.ResolveCurrentCompanyId();

        foreach (var line in request.Lines)
        {
            if (line.ItemId <= 0)
                throw new InvalidOperationException("Eşleştirilmemiş kalem var: her satır bir stok kartına bağlanmalı.");
            if (line.Quantity <= 0)
                throw new InvalidOperationException("Miktar sıfır veya negatif olan satır faturaya yazılamaz.");
        }
        if (affectsStock && request.LocationId is null or <= 0)
            throw new InvalidOperationException("Stok girişi için depo seçilmelidir.");

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        int docId;
        string docNo;
        try
        {
            // 0) Aynı e-belge ikinci kez faturalanmasın (idempotens + kullanıcı hatası koruması).
            await using (var guard = conn.CreateCommand())
            {
                guard.Transaction = tx;
                guard.CommandText = $"""
                    SELECT inv.[DocumentNumber]
                      FROM {T("IncomingDocument")} d
                      INNER JOIN {T("Document")} inv ON inv.[Id] = d.[InvoiceDocumentId] AND inv.[IsActive] = 1
                     WHERE d.[Id] = @Id AND d.[CompanyId] = @Cid;
                    """;
                guard.Parameters.AddWithValue("@Id", request.IncomingDocumentId);
                guard.Parameters.AddWithValue("@Cid", companyId);
                if (await guard.ExecuteScalarAsync(ct) is string existing)
                    throw new InvalidOperationException($"Bu e-belge zaten faturalanmış: {existing}");
            }

            // 1) Toplamlar — satırlardan hesaplanır (KDV satır bazında, belgede toplanır:
            //    DocumentLine'da KDV kolonu YOKTUR, vergi başlıkta tutulur).
            decimal subTotal = 0, taxAmount = 0;
            foreach (var l in request.Lines)
            {
                var lineTotal = decimal.Round(l.Quantity * l.UnitPrice, 4);
                subTotal += lineTotal;
                taxAmount += decimal.Round(lineTotal * (l.VatRate / 100m), 4);
            }
            subTotal = decimal.Round(subTotal, 2);
            taxAmount = decimal.Round(taxAmount, 2);
            var grandTotal = subTotal + taxAmount;
            // Karma KDV oranlı faturada tek bir "oran" yoktur; ağırlıklı oran yazılır
            // (yuvarlama farkı doğurmaması için tutar ayrıca ve tam yazılır).
            var taxRate = subTotal == 0 ? 0 : decimal.Round(taxAmount / subTotal * 100m, 2);

            // 2) Belge numarası + başlık
            docNo = await DocumentNumberResolver.ResolveAsync(
                conn, tx, _schema, _numberService, InvoiceTypeCode, InvoicePrefix, userId, request.InvoiceDate, ct);

            var parentDocId = request.Lines
                .Where(l => l.SourceLineId.HasValue)
                .Select(l => l.SourceLineId!.Value)
                .Any()
                ? await ResolveSingleSourceDocumentAsync(conn, tx, request.Lines, companyId, ct)
                : null;

            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("Document")}
                        ([CompanyId],[DocumentNumber],[DocumentTypeId],[DocumentDate],[LocationId],
                         [ContactId],[CurrencyId],[SubTotal],[DiscountRate],[DiscountAmount],
                         [TaxRate],[TaxAmount],[GrandTotal],[Notes],[Status],[CreatedById],[Created],
                         [IsActive],[ParentDocumentId],[ExternalRefNumber])
                    SELECT @CompanyId, @DocNo, dt.[Id], @DocDate, @LocationId,
                           @ContactId, 1, @SubTotal, 0, 0,
                           @TaxRate, @TaxAmount, @GrandTotal, @Notes, N'Draft', @CreatedById, SYSUTCDATETIME(),
                           1, @ParentDocumentId, @ExternalRef
                    FROM {T("DocumentType")} dt WHERE dt.[Code] = @TypeCode;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                ins.Parameters.AddWithValue("@CompanyId", companyId);
                ins.Parameters.AddWithValue("@DocNo", docNo);
                ins.Parameters.AddWithValue("@DocDate", request.InvoiceDate.Date);
                ins.Parameters.AddWithValue("@LocationId", (object?)(affectsStock ? request.LocationId : null) ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ContactId", request.ContactId);
                ins.Parameters.AddWithValue("@SubTotal", subTotal);
                ins.Parameters.AddWithValue("@TaxRate", taxRate);
                ins.Parameters.AddWithValue("@TaxAmount", taxAmount);
                ins.Parameters.AddWithValue("@GrandTotal", grandTotal);
                ins.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                ins.Parameters.AddWithValue("@CreatedById", (object?)userId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ParentDocumentId", (object?)parentDocId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ExternalRef", (object?)request.ExternalNumber ?? DBNull.Value);
                ins.Parameters.AddWithValue("@TypeCode", InvoiceTypeCode);

                var scalar = await ins.ExecuteScalarAsync(ct);
                docId = Convert.ToInt32(scalar);
                if (docId <= 0) throw new InvalidOperationException("Fatura başlığı oluşturulamadı.");
            }

            // 3) Satırlar — kaynak satır başına BİR satır (satır kırılımı korunur).
            var lineNo = 0;
            foreach (var l in request.Lines)
            {
                lineNo++;
                var lineTotal = decimal.Round(l.Quantity * l.UnitPrice, 4);

                // Baz miktar: kaynak satır varsa onun birim çevrim oranı kullanılır
                // (Quantity ≠ BaseQuantity olabilir); yoksa 1:1.
                decimal baseQty = l.Quantity;
                int? locationId = affectsStock ? request.LocationId : null;
                if (l.SourceLineId.HasValue)
                {
                    var (factor, srcLoc) = await ReadSourceLineFactorAsync(conn, tx, l.SourceLineId.Value, companyId, ct);
                    baseQty = decimal.Round(l.Quantity * factor, 4);
                    if (affectsStock) locationId ??= srcLoc;
                }

                await using var li = conn.CreateCommand();
                li.Transaction = tx;
                li.CommandText = $"""
                    INSERT INTO {T("DocumentLine")}
                        ([DocumentId],[LineNo],[ItemId],[UnitId],[Quantity],[UnitPrice],[DiscountRate],
                         [LineTotal],[BaseQuantity],[DeliveredQuantity],[LocationId],[MovementType],
                         [SourceLineId],[Notes],[CompanyId])
                    VALUES
                        (@DocId, @LineNo, @ItemId, @UnitId, @Qty, @Price, 0,
                         @LineTotal, @BaseQty, 0, @LocationId, @MovementType,
                         @SourceLineId, @Notes,
                         (SELECT p.[CompanyId] FROM {T("Document")} p WHERE p.[Id] = @DocId));
                    """;
                li.Parameters.AddWithValue("@DocId", docId);
                li.Parameters.AddWithValue("@LineNo", lineNo);
                li.Parameters.AddWithValue("@ItemId", l.ItemId);
                li.Parameters.AddWithValue("@UnitId", (object?)l.UnitId ?? DBNull.Value);
                li.Parameters.AddWithValue("@Qty", l.Quantity);
                li.Parameters.AddWithValue("@Price", l.UnitPrice);
                li.Parameters.AddWithValue("@LineTotal", lineTotal);
                li.Parameters.AddWithValue("@BaseQty", baseQty);
                li.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
                li.Parameters.AddWithValue("@MovementType", affectsStock ? MovementReceipt : (object)DBNull.Value);
                li.Parameters.AddWithValue("@SourceLineId", (object?)l.SourceLineId ?? DBNull.Value);
                li.Parameters.AddWithValue("@Notes", (object?)l.Notes ?? DBNull.Value);
                await li.ExecuteNonQueryAsync(ct);
            }

            // 4) Belge-seviye köken kaydı (sipariş/irsaliye → fatura)
            if (parentDocId.HasValue)
            {
                await using var src = conn.CreateCommand();
                src.Transaction = tx;
                src.CommandText = $"""
                    INSERT INTO {T("DocumentSource")} ([DocumentId],[SourceDocumentId],[CreatedAt],[CompanyId])
                    VALUES (@DocId, @SrcId, SYSUTCDATETIME(),
                            (SELECT p.[CompanyId] FROM {T("Document")} p WHERE p.[Id] = @DocId));
                    """;
                src.Parameters.AddWithValue("@DocId", docId);
                src.Parameters.AddWithValue("@SrcId", parentDocId.Value);
                await src.ExecuteNonQueryAsync(ct);
            }

            // 5) e-belge → fatura bağlantısı + işlendi işareti
            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE {T("IncomingDocument")}
                       SET [InvoiceDocumentId] = @DocId, [IsProcessed] = 1
                     WHERE [Id] = @Id AND [CompanyId] = @Cid;
                    """;
                upd.Parameters.AddWithValue("@DocId", docId);
                upd.Parameters.AddWithValue("@Id", request.IncomingDocumentId);
                upd.Parameters.AddWithValue("@Cid", companyId);
                await upd.ExecuteNonQueryAsync(ct);
            }

            // 6) Öğrenme: tedarikçi stok kodu → bizim malzeme kartı (Cari×Stok).
            //    Sonraki faturalarda kalem otomatik önerilir (kullanıcı kararı 2026-08-31).
            await LearnVendorCodesAsync(conn, tx, request, companyId, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* bağlantı zaten kapanmış olabilir */ }
            throw;
        }

        // 7) Kalem eşleşme kayıtları (DocumentLineLink) — ana kaydın DIŞINDA, best-effort.
        //    Link hatası faturayı ASLA bozmaz (mevcut dual-write ilkesi).
        try
        {
            await using var linkConn = await _connectionFactory.OpenConnectionAsync(ct);
            await DerivationLinkHelper.TryLinkDerivedLinesAsync(
                linkConn, null!, _schema, docId, userId, _lineLinks, _logger, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AlışFatura] Kalem eşleşme kayıtları yazılamadı (DocId={DocId}).", docId);
        }

        return new CreatePurchaseInvoiceResultDto(docId, docNo, request.Lines.Count, affectsStock);
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────────

    private static string NormalizeMode(string? mode) =>
        (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "order" or "siparis" => "order",
            "delivery" or "irsaliye" => "delivery",
            _ => "direct",
        };

    /// <summary>
    /// Satırların bağlı olduğu TEK kaynak belge (hepsi aynı belgedense). Birden fazla belgeden
    /// satır seçilmişse başlıkta tek bir ebeveyn gösterilemez → null (köken yine satır bazında
    /// SourceLineId + DocumentLineLink ile korunur).
    /// </summary>
    private async Task<int?> ResolveSingleSourceDocumentAsync(
        SqlConnection conn, SqlTransaction tx, IReadOnlyList<PurchaseInvoiceLineInput> lines, int companyId, CancellationToken ct)
    {
        var ids = lines.Where(l => l.SourceLineId.HasValue).Select(l => l.SourceLineId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return null;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var names = ids.Select((_, i) => "@s" + i).ToArray();
        cmd.CommandText = $"""
            SELECT DISTINCT dl.[DocumentId]
              FROM {T("DocumentLine")} dl
              INNER JOIN {T("Document")} d ON d.[Id] = dl.[DocumentId] AND d.[CompanyId] = @Cid
             WHERE dl.[Id] IN ({string.Join(",", names)});
            """;
        for (var i = 0; i < ids.Length; i++) cmd.Parameters.AddWithValue(names[i], ids[i]);
        cmd.Parameters.AddWithValue("@Cid", companyId);

        var docIds = new List<int>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) docIds.Add(r.GetInt32(0));
        }
        return docIds.Count == 1 ? docIds[0] : null;
    }

    /// <summary>Kaynak satırın birim çevrim oranı (BaseQuantity / Quantity) ve deposu.</summary>
    private async Task<(decimal Factor, int? LocationId)> ReadSourceLineFactorAsync(
        SqlConnection conn, SqlTransaction tx, int sourceLineId, int companyId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT dl.[Quantity], dl.[BaseQuantity], ISNULL(dl.[LocationId], d.[LocationId])
              FROM {T("DocumentLine")} dl
              INNER JOIN {T("Document")} d ON d.[Id] = dl.[DocumentId] AND d.[CompanyId] = @Cid
             WHERE dl.[Id] = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id", sourceLineId);
        cmd.Parameters.AddWithValue("@Cid", companyId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return (1m, null);
        var qty = r.GetDecimal(0);
        var baseQty = r.GetDecimal(1);
        var loc = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
        var factor = qty == 0 ? 1m : baseQty / qty;
        return (factor <= 0 ? 1m : factor, loc);
    }

    /// <summary>
    /// Tedarikçi stok kodunu (e-belge kalemindeki kod) seçilen malzeme kartına bağlar.
    /// Zaten varsa dokunulmaz; kod boşsa atlanır.
    /// </summary>
    private async Task LearnVendorCodesAsync(
        SqlConnection conn, SqlTransaction tx, CreatePurchaseInvoiceRequest request, int companyId, CancellationToken ct)
    {
        try
        {
            // e-belge kalem kodları (satır no → kod)
            var codes = new Dictionary<int, string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    SELECT [LineNumber], [ItemCode] FROM {T("IncomingDocumentLine")}
                     WHERE [IncomingDocumentId] = @Id AND [CompanyId] = @Cid
                       AND NULLIF(LTRIM(RTRIM([ItemCode])), N'') IS NOT NULL;
                    """;
                cmd.Parameters.AddWithValue("@Id", request.IncomingDocumentId);
                cmd.Parameters.AddWithValue("@Cid", companyId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) codes[r.GetInt32(0)] = r.GetString(1).Trim();
            }
            if (codes.Count == 0) return;

            foreach (var pair in request.Lines
                         .Where(l => codes.ContainsKey(l.EDocumentLineNumber))
                         .Select(l => (Code: codes[l.EDocumentLineNumber], l.ItemId))
                         .Distinct())
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    IF NOT EXISTS (SELECT 1 FROM {T("ContactItem")}
                                    WHERE [ContactId] = @ContactId AND [ItemId] = @ItemId)
                        INSERT INTO {T("ContactItem")} ([ContactId],[ItemId],[VendorCode],[IsActive],[Created],[CompanyId])
                        VALUES (@ContactId, @ItemId, @VendorCode, 1, SYSUTCDATETIME(),
                                (SELECT c.[CompanyId] FROM {T("Contact")} c WHERE c.[Id] = @ContactId));
                    """;
                ins.Parameters.AddWithValue("@ContactId", request.ContactId);
                ins.Parameters.AddWithValue("@ItemId", pair.ItemId);
                ins.Parameters.AddWithValue("@VendorCode", pair.Code);
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Öğrenme yan işlevdir: başarısız olursa fatura yine kaydedilir.
            _logger?.LogWarning(ex, "[AlışFatura] Tedarikçi kodu öğrenilemedi (IncomingDocumentId={Id}).",
                request.IncomingDocumentId);
        }
    }
}

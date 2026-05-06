using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repo;
    private readonly IFinanceService _financeService;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly IDocumentSourceRepository _docSourceRepo;
    private const string DefaultSalesQuoteTypeCode = "satis_teklifi";
    private const string DefaultSalesOrderTypeCode = "satis_siparisi";

    public DocumentService(
        IDocumentRepository repo,
        IFinanceService financeService,
        IDocumentTypeRepository documentTypeRepo,
        IDocumentSourceRepository docSourceRepo)
    {
        _repo = repo;
        _financeService = financeService;
        _documentTypeRepo = documentTypeRepo;
        _docSourceRepo = docSourceRepo;
    }

    private async Task<int?> ResolveDefaultQuoteTypeIdAsync(CancellationToken ct)
    {
        var type = await _documentTypeRepo.GetByCodeAsync(DefaultSalesQuoteTypeCode, ct);
        return type?.Id;
    }

    private async Task<int?> ResolveDefaultOrderTypeIdAsync(CancellationToken ct)
    {
        var type = await _documentTypeRepo.GetByCodeAsync(DefaultSalesOrderTypeCode, ct);
        return type?.Id;
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct)
    {
        var quotes = await _repo.GetAllAsync(search, status, ct);
        return quotes.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.Currency, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId
        )).ToArray();
    }

    /// <summary>Bir cariye ait tum tekliflerin ozet listesi (cari karti "Verilen Teklifler" sekmesi icin).</summary>
    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesByContactAsync(int contactId, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(null, null, ct);
        return all.Where(q => q.ContactId == contactId)
            .Select(q => new DocumentListItemDto(
                q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
                q.ContactName, q.Currency, q.GrandTotal,
                q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
                q.ContactId,
                q.DocumentTypeId
            )).ToArray();
    }

    /// <summary>Bir cariye ait tum hareketler (belge tipi + tarih aralgi filtresi).</summary>
    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetMovementsByContactAsync(
        int contactId, int? documentTypeId, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(null, null, ct);
        var q = all.Where(d => d.ContactId == contactId);
        if (documentTypeId is > 0) q = q.Where(d => d.DocumentTypeId == documentTypeId);
        if (fromDate.HasValue)     q = q.Where(d => d.DocumentDate >= fromDate.Value.Date);
        if (toDate.HasValue)       q = q.Where(d => d.DocumentDate <= toDate.Value.Date);
        return q.OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Id)
            .Select(d => new DocumentListItemDto(
                d.Id, d.DocumentNumber, d.DocumentDate, d.ValidUntil,
                d.ContactName, d.Currency, d.GrandTotal,
                d.Status.ToString(), d.RevisionNo, d.IsActive, d.LineCount,
                d.ContactId,
                d.DocumentTypeId
            )).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct)
    {
        var docs = await _repo.GetByTypeAsync(typeCode, search, status, ct);
        return docs.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.Currency, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            q.DocumentTypeId
        )).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct)
    {
        // document_source tablosu yoksa olustur — modal/repo NOT EXISTS subquery'sinin
        // calismasi icin garanti gerekir. Idempotent.
        await _docSourceRepo.EnsureSchemaAsync(ct);
        var docs = await _repo.GetConvertibleQuotesAsync(fromDate, toDate, contactId, search, ct);
        return docs.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.Currency, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount,
            q.ContactId,
            q.DocumentTypeId
        )).ToArray();
    }

    public async Task<DocumentDto?> GetQuoteByIdAsync(int id, CancellationToken ct)
    {
        var q = await _repo.GetByIdAsync(id, ct);
        return q == null ? null : MapDto(q);
    }

    public async Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(int documentId, CancellationToken ct)
    {
        var lines = await _repo.GetLinesAsync(documentId, ct);
        var result = new List<DocumentLineDto>(lines.Count);
        foreach (var ln in lines)
        {
            var details = await _repo.GetLineDetailsAsync(ln.Id, ct);
            var detailDtos = details
                .Select(d => new DocumentLineDetailDto(
                    d.Id, d.QuoteLineId, d.FeatureName, d.ValueCode, d.ValueName, d.Description, d.LineOrder))
                .ToList();
            result.Add(MapLineDto(ln, detailDtos));
        }
        return result;
    }

    public async Task<(bool Success, string? Error, DocumentDto? Quote)> SaveQuoteAsync(
        SaveDocumentRequest request, string? createdBy, CancellationToken ct)
    {
        // ── Cari cozumleme ─────────────────────────────────────
        // contact_id otorite kaynaktir; client ContactName gondermiyor (label),
        // ID cozumlendikten sonra Contact'tan live AccountTitle alinir (display/rapor icin).
        int? resolvedContactId = request.ContactId;
        string? resolvedContactName = request.ContactName;

        if (!resolvedContactId.HasValue && !string.IsNullOrWhiteSpace(request.ContactCode))
        {
            // Koda gore tek satir cek (search param'i ile filtreli — tum cari listesini yuklemez)
            var code = request.ContactCode.Trim();
            var matches = await _financeService.GetContactsAsync(null, code, ct);
            var exact = matches.FirstOrDefault(a =>
                a.IsActive && string.Equals(a.AccountCode, code, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                resolvedContactId = exact.Id;
                resolvedContactName = exact.AccountTitle;
            }
        }
        else if (resolvedContactId.HasValue)
        {
            // ID set edilmisse tek satirlik PK sorgusu — full list yuklemekten cok daha hizli.
            var live = await _financeService.GetContactByIdAsync(resolvedContactId.Value, ct);
            if (live != null && live.IsActive)
                resolvedContactName = live.AccountTitle;
        }

        if (!resolvedContactId.HasValue && string.IsNullOrWhiteSpace(resolvedContactName))
            return (false, "Cari (musteri) zorunludur. Kalem eklemeden once cari seciniz.", null);
        if (request.Lines.Count == 0)
            return (false, "En az bir satir eklenmeli.", null);

        // Kombinasyon takibi acik olan stok icin kombinasyon ID zorunlu
        foreach (var ln in request.Lines)
        {
            if (ln.TrackCombinations && (!ln.CombinationId.HasValue || ln.CombinationId.Value <= 0))
            {
                return (false, $"Secili satir (Item #{ln.ItemId}) stokunda kombinasyon takibi acik; kombinasyon secilmelidir.", null);
            }
        }

        request = request with { ContactId = resolvedContactId, ContactName = resolvedContactName };

        var isNew = !request.Id.HasValue || request.Id.Value == 0;
        var quoteNumber = isNew ? await _repo.GetNextDocumentNumberAsync(ct) : "";

        // Satirlari hesapla
        var lineRequests = request.Lines.ToArray();
        decimal subTotal = 0;
        var lineEntities = new List<DocumentLine>(lineRequests.Length);
        int lineNo = 1;
        foreach (var ln in lineRequests)
        {
            var lineDiscountMultiplier = 1m - (ln.DiscountRate / 100m);
            var lineTotal = ln.Quantity * ln.UnitPrice * lineDiscountMultiplier;
            subTotal += lineTotal;
            lineEntities.Add(new DocumentLine
            {
                // DocumentId 0 — UpsertAsync sonrasi quote.Id ile set edecegiz.
                // Id dolu gelirse repository UPDATE uygular; yoksa INSERT ederek yeni IDENTITY alir.
                Id = ln.Id ?? 0,
                LineNo = lineNo++,
                ItemId = ln.ItemId,
                UnitId = ln.UnitId,
                Quantity = ln.Quantity,
                UnitPrice = ln.UnitPrice,
                DiscountRate = ln.DiscountRate,
                LineTotal = Math.Round(lineTotal, 4),
                CombinationId = ln.CombinationId,
                LocationId = ln.LocationId,
                Notes = ln.Notes,
                NotesPinned = ln.NotesPinned,
                RevisedFromId = ln.RevisedFromId,
            });
        }

        var discountAmount = Math.Round(subTotal * (request.DiscountRate / 100m), 4);
        var afterDiscount = subTotal - discountAmount;
        var taxAmount = Math.Round(afterDiscount * (request.TaxRate / 100m), 4);
        var grandTotal = afterDiscount + taxAmount;

        // DocumentTypeId null ise varsayilan 'satis_teklifi' turune bagla
        var effectiveDocumentTypeId = request.DocumentTypeId ?? await ResolveDefaultQuoteTypeIdAsync(ct);

        Document quote;
        if (isNew)
        {
            quote = new Document
            {
                DocumentNumber = quoteNumber,
                DocumentTypeId = effectiveDocumentTypeId,
                DocumentDate = request.DocumentDate,
                ValidUntil = request.ValidUntil,
                ContactId = request.ContactId,
                ContactName = request.ContactName,
                ContactAddress = request.ContactAddress,
                SalesRepId = request.SalesRepId,
                Currency = request.Currency ?? "TRY",
                SubTotal = Math.Round(subTotal, 4),
                DiscountRate = request.DiscountRate,
                DiscountAmount = discountAmount,
                TaxRate = request.TaxRate,
                TaxAmount = taxAmount,
                GrandTotal = Math.Round(grandTotal, 4),
                PaymentTerms = request.PaymentTerms,
                DeliveryTerms = request.DeliveryTerms,
                DeliveryAddress = request.DeliveryAddress,
                Notes = request.Notes,
                CreatedBy = createdBy,
                Status = DocumentStatus.Draft
            };
        }
        else
        {
            var existing = await _repo.GetByIdAsync(request.Id!.Value, ct)
                    ?? throw new InvalidOperationException("Teklif bulunamadi.");

            // GetByIdAsync zaten line_count'u tek sorguda getiriyor — tekrar lines cekmeye gerek yok.
            if (existing.LineCount > 0 && existing.ContactId != request.ContactId)
                return (false, "Kalem girilmis belgenin cari kodu degistirilemez.", null);

            existing.DocumentTypeId = request.DocumentTypeId ?? existing.DocumentTypeId ?? effectiveDocumentTypeId;
            existing.DocumentDate = request.DocumentDate;
            existing.ValidUntil = request.ValidUntil;
            existing.ContactId = request.ContactId;
            existing.ContactName = request.ContactName;
            existing.ContactAddress = request.ContactAddress;
            existing.SalesRepId = request.SalesRepId;
            existing.Currency = request.Currency ?? "TRY";
            existing.SubTotal = Math.Round(subTotal, 4);
            existing.DiscountRate = request.DiscountRate;
            existing.DiscountAmount = discountAmount;
            existing.TaxRate = request.TaxRate;
            existing.TaxAmount = taxAmount;
            existing.GrandTotal = Math.Round(grandTotal, 4);
            existing.PaymentTerms = request.PaymentTerms;
            existing.DeliveryTerms = request.DeliveryTerms;
            existing.DeliveryAddress = request.DeliveryAddress;
            existing.Notes = request.Notes;
            existing.UpdatedAt = DateTime.Now;
            quote = existing;
        }

        var savedId = await _repo.UpsertAsync(quote, ct);

        // Upsert sonrasi yeni Id'yi entity'ye ata (init-only oldugu icin yeni instance)
        if (isNew)
        {
            quote = new Document
            {
                Id = savedId,
                DocumentNumber = quote.DocumentNumber,
                DocumentTypeId = quote.DocumentTypeId,
                DocumentDate = quote.DocumentDate,
                ValidUntil = quote.ValidUntil,
                ContactId = quote.ContactId,
                ContactName = quote.ContactName,
                ContactAddress = quote.ContactAddress,
                ContactCode = quote.ContactCode,
                SalesRepId = quote.SalesRepId,
                Currency = quote.Currency,
                SubTotal = quote.SubTotal,
                DiscountRate = quote.DiscountRate,
                DiscountAmount = quote.DiscountAmount,
                TaxRate = quote.TaxRate,
                TaxAmount = quote.TaxAmount,
                GrandTotal = quote.GrandTotal,
                PaymentTerms = quote.PaymentTerms,
                DeliveryTerms = quote.DeliveryTerms,
                DeliveryAddress = quote.DeliveryAddress,
                Status = quote.Status,
                RevisionNo = quote.RevisionNo,
                ParentDocumentId = quote.ParentDocumentId,
                Notes = quote.Notes,
                CreatedBy = quote.CreatedBy,
                CreatedAt = quote.CreatedAt,
                UpdatedAt = quote.UpdatedAt,
                IsActive = quote.IsActive,
                LineCount = quote.LineCount,
            };
        }

        // Satirlari kaydet — DocumentId'yi yeni Id ile set et.
        // ÖNEMLI: Id'yi de kopyala — aksi halde UPSERT'te tum satirlar INSERT olarak
        // algilanir, eski Id'ler silinir ve WidgetTra kayitlari orphan kalir.
        var finalLines = lineEntities.Select(ln => new DocumentLine
        {
            Id = ln.Id,
            DocumentId = quote.Id,
            LineNo = ln.LineNo,
            ItemId = ln.ItemId,
            UnitId = ln.UnitId,
            Quantity = ln.Quantity,
            UnitPrice = ln.UnitPrice,
            DiscountRate = ln.DiscountRate,
            LineTotal = ln.LineTotal,
            CombinationId = ln.CombinationId,
            LocationId = ln.LocationId,
            Notes = ln.Notes,
            NotesPinned = ln.NotesPinned,
            RevisedFromId = ln.RevisedFromId,
        }).ToArray();

        await _repo.SaveLinesAsync(quote.Id, finalLines, ct);

        // Satir detaylarini (ozellik-deger-aciklama) kaydet — herhangi bir satirda
        // detay varsa line ID'leri icin ek sorgu at. Cogu teklif detaysiz (no-op save)
        // bu yol hic calismaz ve 1 RT + N RT kazanilir.
        var hasAnyDetails = lineRequests.Any(r => r.CombinationDetails != null && r.CombinationDetails.Count > 0);
        if (hasAnyDetails)
        {
            var savedLines = await _repo.GetLinesAsync(quote.Id, ct);
            var byLineNo = savedLines.ToDictionary(l => l.LineNo);
            for (int i = 0; i < lineRequests.Length; i++)
            {
                var reqLine = lineRequests[i];
                var lineNoForThis = i + 1;
                if (!byLineNo.TryGetValue(lineNoForThis, out var savedLine)) continue;

                var details = (reqLine.CombinationDetails ?? new List<SaveQuoteLineDetailItem>())
                    .Select((d, idx) => new DocumentLineDetail
                    {
                        QuoteLineId = savedLine.Id,
                        FeatureName = d.FeatureName,
                        ValueCode = d.ValueCode,
                        ValueName = d.ValueName,
                        Description = d.Description,
                        LineOrder = d.LineOrder > 0 ? d.LineOrder : (idx + 1),
                    })
                    .ToList();

                await _repo.SaveLineDetailsAsync(savedLine.Id, details, ct);
            }
        }

        return (true, null, MapDto(quote));
    }

    public async Task DeleteQuoteAsync(int id, CancellationToken ct)
    {
        await _repo.DeleteAsync(id, ct);
    }

    public async Task<(bool Success, string? Error)> ChangeStatusAsync(int id, string newStatus, CancellationToken ct)
    {
        var quote = await _repo.GetByIdAsync(id, ct);
        if (quote == null) return (false, "Teklif bulunamadi.");

        if (!Enum.TryParse<DocumentStatus>(newStatus, out var status))
            return (false, "Gecersiz durum.");

        quote.Status = status;
        quote.UpdatedAt = DateTime.Now;
        await _repo.UpsertAsync(quote, ct);
        return (true, null);
    }

    public Task<string> GetNextDocumentNumberAsync(CancellationToken ct) => _repo.GetNextDocumentNumberAsync(ct);

    private static DocumentDto MapDto(Document q) => new(
        q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
        q.ContactId, q.ContactName, q.ContactAddress,
        q.SalesRepId,
        q.Currency, q.SubTotal, q.DiscountRate, q.DiscountAmount,
        q.TaxRate, q.TaxAmount, q.GrandTotal,
        q.PaymentTerms, q.DeliveryTerms, q.DeliveryAddress,
        q.Status.ToString(), q.RevisionNo, q.ParentDocumentId, q.Notes,
        q.CreatedBy, q.CreatedAt, q.UpdatedAt, q.IsActive,
        q.ContactCode,
        q.DocumentTypeId);

    /// <summary>
    /// Satir revizyonu — repository katmanina delege eder. Widget degerlerinin
    /// kopyalanmasi bu service sinifinda DEGIL (WidgetService farkli), controller
    /// akisinda yapilir.
    /// </summary>
    public Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct)
        => _repo.ReviseLineAsync(parentLineId, description, ct);

    /// <summary>
    /// Tekliflerden cari bazli siparis(ler) uretir.
    ///   1) Tum kaynak teklifleri yukle + dogrula (Approved + henuz consume edilmemis)
    ///   2) ContactId bazinda grupla — her grup icin tek bir Document (type=satis_siparisi) olustur
    ///   3) Tum gruptaki teklifin satirlarini siparise clone et (LineNo resequence + Notes'a kaynak ekle)
    ///   4) Kombinasyon detaylarini ayrica kopyala
    ///   5) document_source koprusu kayit ekle, kaynak teklif statusunu Converted yap
    /// Service-level transaction yok; document_source UNIQUE INDEX cift insert riskini engeller.
    /// </summary>
    public async Task<CreateOrdersFromQuotesResult> CreateOrdersFromQuotesAsync(
        CreateOrdersFromQuotesRequest req, string? createdBy, CancellationToken ct)
    {
        if (req.QuoteIds == null || req.QuoteIds.Count == 0)
            return new CreateOrdersFromQuotesResult(false, "En az bir teklif secilmelidir.", 0, Array.Empty<int>());

        await _docSourceRepo.EnsureSchemaAsync(ct);

        var orderTypeId = await ResolveDefaultOrderTypeIdAsync(ct);
        if (!orderTypeId.HasValue)
            return new CreateOrdersFromQuotesResult(false, "'satis_siparisi' belge tipi bulunamadi.", 0, Array.Empty<int>());

        var quoteTypeId = await ResolveDefaultQuoteTypeIdAsync(ct);

        // ── 1) Yukle + dogrula ──
        var quotes = new List<(Document Quote, IReadOnlyCollection<DocumentLine> Lines)>(req.QuoteIds.Count);
        foreach (var qid in req.QuoteIds.Distinct())
        {
            var doc = await _repo.GetByIdAsync(qid, ct);
            if (doc == null)
                return new CreateOrdersFromQuotesResult(false, $"Teklif #{qid} bulunamadi.", 0, Array.Empty<int>());
            if (!doc.IsActive)
                return new CreateOrdersFromQuotesResult(false, $"Teklif {doc.DocumentNumber} pasif.", 0, Array.Empty<int>());
            if (doc.Status != DocumentStatus.Approved)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} onaylanmamis (durum: {doc.Status}). Sadece Approved teklifler siparise donusturulebilir.", 0, Array.Empty<int>());
            if (quoteTypeId.HasValue && doc.DocumentTypeId.HasValue && doc.DocumentTypeId != quoteTypeId)
                return new CreateOrdersFromQuotesResult(false,
                    $"Belge {doc.DocumentNumber} satis teklifi degil.", 0, Array.Empty<int>());
            if (!doc.ContactId.HasValue)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} icin cari bos. Cari secilmeden siparise donusturulemez.", 0, Array.Empty<int>());
            if (await _docSourceRepo.IsSourceConsumedAsync(qid, ct))
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} zaten siparise donusturulmus.", 0, Array.Empty<int>());

            var lines = await _repo.GetLinesAsync(qid, ct);
            if (lines.Count == 0)
                return new CreateOrdersFromQuotesResult(false,
                    $"Teklif {doc.DocumentNumber} icin satir yok — bos teklif siparise donusturulemez.", 0, Array.Empty<int>());
            quotes.Add((doc, lines));
        }

        // ── 2) Cari bazli grupla ──
        var groups = quotes.GroupBy(t => t.Quote.ContactId!.Value).ToArray();
        var orderIds = new List<int>(groups.Length);

        foreach (var grp in groups)
        {
            var first = grp.First().Quote;

            // Yeni siparis numarasi
            var orderNumber = await _repo.GetNextDocumentNumberAsync(ct);

            // Tutar hesabi: subtotal = tum gruptaki line'larin line_total toplami
            var allLines = grp.SelectMany(t => t.Lines).ToArray();
            var subTotal = allLines.Sum(l => l.LineTotal);
            var discountRate = first.DiscountRate;
            var taxRate = first.TaxRate;
            var discountAmount = Math.Round(subTotal * (discountRate / 100m), 4);
            var afterDiscount = subTotal - discountAmount;
            var taxAmount = Math.Round(afterDiscount * (taxRate / 100m), 4);
            var grandTotal = afterDiscount + taxAmount;

            var order = new Document
            {
                DocumentNumber = orderNumber,
                DocumentTypeId = orderTypeId,
                DocumentDate = req.OrderDate,
                ValidUntil = null,
                ContactId = first.ContactId,
                ContactName = first.ContactName,
                ContactAddress = first.ContactAddress,
                SalesRepId = first.SalesRepId,
                Currency = first.Currency,
                SubTotal = Math.Round(subTotal, 4),
                DiscountRate = discountRate,
                DiscountAmount = discountAmount,
                TaxRate = taxRate,
                TaxAmount = taxAmount,
                GrandTotal = Math.Round(grandTotal, 4),
                PaymentTerms = first.PaymentTerms,
                DeliveryTerms = first.DeliveryTerms,
                DeliveryAddress = first.DeliveryAddress,
                Notes = grp.Count() > 1
                    ? $"Kaynak teklifler: {string.Join(", ", grp.Select(t => t.Quote.DocumentNumber))}"
                    : $"Kaynak teklif: {first.DocumentNumber}",
                CreatedBy = createdBy,
                Status = DocumentStatus.Draft,
            };

            var newOrderId = await _repo.UpsertAsync(order, ct);

            // ── 3) Satirlari clone et (LineNo resequence + Notes'a kaynak ekle) ──
            var clonedLines = new List<DocumentLine>();
            // Her clone'un kaynak QuoteLine.Id'sini hatirla — detail kopyasinda kullanilacak
            var lineSourceMap = new List<int>();
            int lineNo = 1;
            foreach (var (quote, lines) in grp)
            {
                foreach (var src in lines)
                {
                    var sourceTag = $"[Kaynak: {quote.DocumentNumber}]";
                    var combinedNotes = string.IsNullOrWhiteSpace(src.Notes)
                        ? sourceTag
                        : $"{src.Notes}\n{sourceTag}";

                    clonedLines.Add(new DocumentLine
                    {
                        Id = 0, // INSERT
                        DocumentId = newOrderId,
                        LineNo = lineNo++,
                        ItemId = src.ItemId,
                        UnitId = src.UnitId,
                        Quantity = src.Quantity,
                        UnitPrice = src.UnitPrice,
                        DiscountRate = src.DiscountRate,
                        LineTotal = src.LineTotal,
                        CombinationId = src.CombinationId,
                        LocationId = src.LocationId,
                        Notes = combinedNotes,
                        NotesPinned = src.NotesPinned,
                        // RevisedFromId YOK — siparis revizyonu farkli bir senaryo
                        // SourceLineId — kalem bazli kaynak iz (sipariş satiri ↔ teklif satiri)
                        SourceLineId = src.Id,
                    });
                    lineSourceMap.Add(src.Id);
                }
            }

            await _repo.SaveLinesAsync(newOrderId, clonedLines, ct);

            // ── 4) Kombinasyon detaylarini kopyala ──
            // SaveLinesAsync sonrasi yeni Id'leri yukleyip line_no -> new id eslestirmesi yap
            var savedLines = await _repo.GetLinesAsync(newOrderId, ct);
            // savedLines line_no'ya gore sirali; lineSourceMap da line_no sirasiyla insert edilmisti.
            var savedByLineNo = savedLines.OrderBy(l => l.LineNo).ToArray();
            for (int i = 0; i < savedByLineNo.Length && i < lineSourceMap.Count; i++)
            {
                var sourceLineId = lineSourceMap[i];
                var destLineId = savedByLineNo[i].Id;
                var details = await _repo.GetLineDetailsAsync(sourceLineId, ct);
                if (details.Count == 0) continue;
                var clonedDetails = details.Select((d, idx) => new DocumentLineDetail
                {
                    QuoteLineId = destLineId,
                    FeatureName = d.FeatureName,
                    ValueCode = d.ValueCode,
                    ValueName = d.ValueName,
                    Description = d.Description,
                    LineOrder = d.LineOrder > 0 ? d.LineOrder : (idx + 1),
                }).ToList();
                await _repo.SaveLineDetailsAsync(destLineId, clonedDetails, ct);
            }

            // ── 5) document_source koprusu + status update ──
            foreach (var (quote, _) in grp)
            {
                await _docSourceRepo.AddAsync(newOrderId, quote.Id, ct);
                quote.Status = DocumentStatus.Converted;
                quote.UpdatedAt = DateTime.Now;
                await _repo.UpsertAsync(quote, ct);
            }

            orderIds.Add(newOrderId);
        }

        return new CreateOrdersFromQuotesResult(true, null, orderIds.Count, orderIds);
    }

    private static DocumentLineDto MapLineDto(DocumentLine ln, IReadOnlyList<DocumentLineDetailDto>? details = null) => new(
        ln.Id, ln.DocumentId, ln.LineNo,
        ln.ItemId, ln.MaterialCode, ln.MaterialName,
        ln.UnitId, ln.UnitCode, ln.UnitName,
        ln.Quantity, ln.UnitPrice, ln.DiscountRate, ln.LineTotal,
        ln.CombinationId, ln.CombinationCode,
        ln.LocationId, ln.LocationCode, ln.LocationName,
        ln.Notes, details,
        NotesPinned: ln.NotesPinned,
        RevisedFromId: ln.RevisedFromId,
        SourceLineId: ln.SourceLineId);
}

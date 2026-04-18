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

    public DocumentService(IDocumentRepository repo, IFinanceService financeService)
    {
        _repo = repo;
        _financeService = financeService;
    }

    public async Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct)
    {
        var quotes = await _repo.GetAllAsync(search, status, ct);
        return quotes.Select(q => new DocumentListItemDto(
            q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
            q.ContactName, q.Currency, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount
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
        int? resolvedContactId = request.ContactId;
        string? resolvedContactName = request.ContactName;

        if (!resolvedContactId.HasValue && !string.IsNullOrWhiteSpace(request.ContactCode))
        {
            var code = request.ContactCode.Trim();
            var matches = await _financeService.GetContactsAsync(null, code, ct);
            var exact = matches.FirstOrDefault(a =>
                a.IsActive && string.Equals(a.AccountCode, code, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                resolvedContactId = exact.Id;
                if (string.IsNullOrWhiteSpace(resolvedContactName))
                    resolvedContactName = exact.AccountTitle;
            }
        }

        if (!resolvedContactId.HasValue && string.IsNullOrWhiteSpace(resolvedContactName))
            return (false, "Cari (musteri) zorunludur. Kalem eklemeden once cari seciniz.", null);
        if (request.Lines.Count == 0)
            return (false, "En az bir satir eklenmeli.", null);

        // Kombinasyon takibi acik olan stok icin kombinasyon kodu zorunlu
        foreach (var ln in request.Lines)
        {
            if (ln.TrackCombinations && string.IsNullOrWhiteSpace(ln.CombinationCode))
            {
                var label = string.IsNullOrWhiteSpace(ln.MaterialCode) ? "Secili satir" : $"'{ln.MaterialCode}'";
                return (false, $"{label} stokunda kombinasyon takibi acik; kombinasyon secilmelidir.", null);
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
                // DocumentId 0 — UpsertAsync sonrasi quote.Id ile set edecegiz
                LineNo = lineNo++,
                ItemId = ln.ItemId,
                MaterialCode = ln.MaterialCode,
                MaterialName = ln.MaterialName,
                UnitName = ln.UnitName,
                Quantity = ln.Quantity,
                UnitPrice = ln.UnitPrice,
                DiscountRate = ln.DiscountRate,
                LineTotal = Math.Round(lineTotal, 4),
                CombinationCode = ln.CombinationCode
            });
        }

        var discountAmount = Math.Round(subTotal * (request.DiscountRate / 100m), 4);
        var afterDiscount = subTotal - discountAmount;
        var taxAmount = Math.Round(afterDiscount * (request.TaxRate / 100m), 4);
        var grandTotal = afterDiscount + taxAmount;

        Document quote;
        if (isNew)
        {
            quote = new Document
            {
                DocumentNumber = quoteNumber,
                DocumentTypeId = request.DocumentTypeId,
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

            var existingLines = await _repo.GetLinesAsync(request.Id.Value, ct);
            if (existingLines.Count > 0 && existing.ContactId != request.ContactId)
                return (false, "Kalem girilmis belgenin cari kodu degistirilemez.", null);

            existing.DocumentTypeId = request.DocumentTypeId ?? existing.DocumentTypeId;
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

        // Satirlari kaydet — DocumentId'yi yeni Id ile set et
        var finalLines = lineEntities.Select(ln => new DocumentLine
        {
            DocumentId = quote.Id,
            LineNo = ln.LineNo,
            ItemId = ln.ItemId,
            MaterialCode = ln.MaterialCode,
            MaterialName = ln.MaterialName,
            UnitName = ln.UnitName,
            Quantity = ln.Quantity,
            UnitPrice = ln.UnitPrice,
            DiscountRate = ln.DiscountRate,
            LineTotal = ln.LineTotal,
            CombinationCode = ln.CombinationCode,
            Notes = ln.Notes
        }).ToArray();

        await _repo.SaveLinesAsync(quote.Id, finalLines, ct);

        // Satir detaylarini (ozellik-deger-aciklama) kaydet
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

    private static DocumentLineDto MapLineDto(DocumentLine ln, IReadOnlyList<DocumentLineDetailDto>? details = null) => new(
        ln.Id, ln.DocumentId, ln.LineNo,
        ln.ItemId, ln.MaterialCode, ln.MaterialName, ln.UnitName,
        ln.Quantity, ln.UnitPrice, ln.DiscountRate, ln.LineTotal,
        ln.CombinationCode, ln.Notes, ln.IsActive, details);
}

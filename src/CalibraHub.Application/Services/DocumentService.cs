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

    public async Task<DocumentDto?> GetQuoteByIdAsync(Guid id, CancellationToken ct)
    {
        var q = await _repo.GetByIdAsync(id, ct);
        return q == null ? null : MapDto(q);
    }

    public async Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(Guid quoteId, CancellationToken ct)
    {
        var lines = await _repo.GetLinesAsync(quoteId, ct);
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
        // Id yoksa ContactCode (AccountCode) uzerinden aktif cari bulup Id'yi cozeriz.
        int? resolvedCustomerId = request.ContactId;
        string? resolvedCustomerName = request.ContactName;

        if (!resolvedCustomerId.HasValue && !string.IsNullOrWhiteSpace(request.ContactCode))
        {
            var code = request.ContactCode.Trim();
            var matches = await _financeService.GetContactsAsync(null, code, ct);
            var exact = matches.FirstOrDefault(a =>
                a.IsActive && string.Equals(a.AccountCode, code, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                resolvedCustomerId = exact.Id;
                if (string.IsNullOrWhiteSpace(resolvedCustomerName))
                    resolvedCustomerName = exact.AccountTitle;
            }
        }

        if (!resolvedCustomerId.HasValue && string.IsNullOrWhiteSpace(resolvedCustomerName))
            return (false, "Cari (musteri) zorunludur. Kalem eklemeden once cari seciniz.", null);
        if (request.Lines.Count == 0)
            return (false, "En az bir satir eklenmeli.", null);

        // Request artik resolve edilmis Id'yi kullanir
        request = request with { ContactId = resolvedCustomerId, ContactName = resolvedCustomerName };

        var isNew = !request.Id.HasValue || request.Id.Value == Guid.Empty;
        var quoteNumber = isNew ? await _repo.GetNextQuoteNumberAsync(ct) : "";

        // Satirlari hesapla
        var lineEntities = new List<DocumentLine>();
        decimal subTotal = 0;
        int lineNo = 1;
        foreach (var ln in request.Lines)
        {
            var lineDiscountMultiplier = 1m - (ln.DiscountRate / 100m);
            var lineTotal = ln.Quantity * ln.UnitPrice * lineDiscountMultiplier;
            subTotal += lineTotal;
            lineEntities.Add(new DocumentLine
            {
                DocumentId = request.Id ?? Guid.Empty,
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
            quote = await _repo.GetByIdAsync(request.Id!.Value, ct)
                    ?? throw new InvalidOperationException("Teklif bulunamadi.");

            // Kayitli kalem varsa cari degistirilemez
            var existingLines = await _repo.GetLinesAsync(request.Id.Value, ct);
            if (existingLines.Count > 0 && quote.ContactId != request.ContactId)
                return (false, "Kalem girilmis belgenin cari kodu degistirilemez.", null);

            quote.DocumentDate = request.DocumentDate;
            quote.ValidUntil = request.ValidUntil;
            quote.ContactId = request.ContactId;
            quote.ContactName = request.ContactName;
            quote.ContactAddress = request.ContactAddress;
            quote.SalesRepId = request.SalesRepId;
            quote.Currency = request.Currency ?? "TRY";
            quote.SubTotal = Math.Round(subTotal, 4);
            quote.DiscountRate = request.DiscountRate;
            quote.DiscountAmount = discountAmount;
            quote.TaxRate = request.TaxRate;
            quote.TaxAmount = taxAmount;
            quote.GrandTotal = Math.Round(grandTotal, 4);
            quote.PaymentTerms = request.PaymentTerms;
            quote.DeliveryTerms = request.DeliveryTerms;
            quote.DeliveryAddress = request.DeliveryAddress;
            quote.Notes = request.Notes;
            quote.UpdatedAt = DateTime.Now;
        }

        await _repo.UpsertAsync(quote, ct);

        // Satirlari kaydet
        foreach (var ln in lineEntities)
        {
            // DocumentId'yi set et (yeni kayitlarda)
            var field = typeof(DocumentLine).GetProperty("DocumentId");
            // init-only oldugundan reflection ile set edemeyiz — yeni entity olusturalim
        }
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
        // SaveLinesAsync muhtemelen satirlari replace ettigi icin, yeni Id'lerini yeniden okuyoruz.
        var savedLines = await _repo.GetLinesAsync(quote.Id, ct);
        var byLineNo = savedLines.ToDictionary(l => l.LineNo);
        var reqLineArr = request.Lines.ToArray();
        for (int i = 0; i < reqLineArr.Length; i++)
        {
            var reqLine = reqLineArr[i];
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

    public async Task DeleteQuoteAsync(Guid id, CancellationToken ct)
    {
        await _repo.DeleteAsync(id, ct);
    }

    public async Task<(bool Success, string? Error)> ChangeStatusAsync(Guid id, string newStatus, CancellationToken ct)
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

    public Task<string> GetNextQuoteNumberAsync(CancellationToken ct) => _repo.GetNextQuoteNumberAsync(ct);

    private static DocumentDto MapDto(Document q) => new(
        q.Id, q.DocumentNumber, q.DocumentDate, q.ValidUntil,
        q.ContactId, q.ContactName, q.ContactAddress,
        q.SalesRepId,
        q.Currency, q.SubTotal, q.DiscountRate, q.DiscountAmount,
        q.TaxRate, q.TaxAmount, q.GrandTotal,
        q.PaymentTerms, q.DeliveryTerms, q.DeliveryAddress,
        q.Status.ToString(), q.RevisionNo, q.ParentDocumentId, q.Notes,
        q.CreatedBy, q.CreatedAt, q.UpdatedAt, q.IsActive,
        q.ContactCode);

    private static DocumentLineDto MapLineDto(DocumentLine ln, IReadOnlyList<DocumentLineDetailDto>? details = null) => new(
        ln.Id, ln.DocumentId, ln.LineNo,
        ln.ItemId, ln.MaterialCode, ln.MaterialName, ln.UnitName,
        ln.Quantity, ln.UnitPrice, ln.DiscountRate, ln.LineTotal,
        ln.CombinationCode, ln.Notes, ln.IsActive, details);
}

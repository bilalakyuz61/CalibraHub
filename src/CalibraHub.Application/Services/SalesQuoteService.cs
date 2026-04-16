using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class SalesQuoteService : ISalesQuoteService
{
    private readonly ISalesQuoteRepository _repo;

    public SalesQuoteService(ISalesQuoteRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<SalesQuoteListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct)
    {
        var quotes = await _repo.GetAllAsync(search, status, ct);
        return quotes.Select(q => new SalesQuoteListItemDto(
            q.Id, q.QuoteNumber, q.QuoteDate, q.ValidUntil,
            q.CustomerName, q.Currency, q.GrandTotal,
            q.Status.ToString(), q.RevisionNo, q.IsActive, q.LineCount
        )).ToArray();
    }

    public async Task<SalesQuoteDto?> GetQuoteByIdAsync(Guid id, CancellationToken ct)
    {
        var q = await _repo.GetByIdAsync(id, ct);
        return q == null ? null : MapDto(q);
    }

    public async Task<IReadOnlyCollection<SalesQuoteLineDto>> GetQuoteLinesAsync(Guid quoteId, CancellationToken ct)
    {
        var lines = await _repo.GetLinesAsync(quoteId, ct);
        return lines.Select(MapLineDto).ToArray();
    }

    public async Task<(bool Success, string? Error, SalesQuoteDto? Quote)> SaveQuoteAsync(
        SaveSalesQuoteRequest request, string? createdBy, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
            return (false, "En az bir satir eklenmeli.", null);

        var isNew = !request.Id.HasValue || request.Id.Value == Guid.Empty;
        var quoteNumber = isNew ? await _repo.GetNextQuoteNumberAsync(ct) : "";

        // Satirlari hesapla
        var lineEntities = new List<SalesQuoteLine>();
        decimal subTotal = 0;
        int lineNo = 1;
        foreach (var ln in request.Lines)
        {
            var lineDiscountMultiplier = 1m - (ln.DiscountRate / 100m);
            var lineTotal = ln.Quantity * ln.UnitPrice * lineDiscountMultiplier;
            subTotal += lineTotal;
            lineEntities.Add(new SalesQuoteLine
            {
                QuoteId = request.Id ?? Guid.Empty,
                LineNo = lineNo++,
                StockCardId = ln.StockCardId,
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

        SalesQuote quote;
        if (isNew)
        {
            quote = new SalesQuote
            {
                QuoteNumber = quoteNumber,
                QuoteDate = request.QuoteDate,
                ValidUntil = request.ValidUntil,
                CustomerId = request.CustomerId,
                CustomerName = request.CustomerName,
                CustomerAddress = request.CustomerAddress,
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
                Status = SalesQuoteStatus.Draft
            };
        }
        else
        {
            quote = await _repo.GetByIdAsync(request.Id!.Value, ct)
                    ?? throw new InvalidOperationException("Teklif bulunamadi.");
            quote.QuoteDate = request.QuoteDate;
            quote.ValidUntil = request.ValidUntil;
            quote.CustomerId = request.CustomerId;
            quote.CustomerName = request.CustomerName;
            quote.CustomerAddress = request.CustomerAddress;
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
            // QuoteId'yi set et (yeni kayitlarda)
            var field = typeof(SalesQuoteLine).GetProperty("QuoteId");
            // init-only oldugundan reflection ile set edemeyiz — yeni entity olusturalim
        }
        var finalLines = lineEntities.Select(ln => new SalesQuoteLine
        {
            QuoteId = quote.Id,
            LineNo = ln.LineNo,
            StockCardId = ln.StockCardId,
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

        if (!Enum.TryParse<SalesQuoteStatus>(newStatus, out var status))
            return (false, "Gecersiz durum.");

        quote.Status = status;
        quote.UpdatedAt = DateTime.Now;
        await _repo.UpsertAsync(quote, ct);
        return (true, null);
    }

    public Task<string> GetNextQuoteNumberAsync(CancellationToken ct) => _repo.GetNextQuoteNumberAsync(ct);

    private static SalesQuoteDto MapDto(SalesQuote q) => new(
        q.Id, q.QuoteNumber, q.QuoteDate, q.ValidUntil,
        q.CustomerId, q.CustomerName, q.CustomerAddress,
        q.SalesRepId,
        q.Currency, q.SubTotal, q.DiscountRate, q.DiscountAmount,
        q.TaxRate, q.TaxAmount, q.GrandTotal,
        q.PaymentTerms, q.DeliveryTerms, q.DeliveryAddress,
        q.Status.ToString(), q.RevisionNo, q.ParentQuoteId, q.Notes,
        q.CreatedBy, q.CreatedAt, q.UpdatedAt, q.IsActive);

    private static SalesQuoteLineDto MapLineDto(SalesQuoteLine ln) => new(
        ln.Id, ln.QuoteId, ln.LineNo,
        ln.StockCardId, ln.MaterialCode, ln.MaterialName, ln.UnitName,
        ln.Quantity, ln.UnitPrice, ln.DiscountRate, ln.LineTotal,
        ln.CombinationCode, ln.Notes, ln.IsActive);
}

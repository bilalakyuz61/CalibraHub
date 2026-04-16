namespace CalibraHub.Domain.Entities;

public sealed class ExchangeRate
{
    public int Id { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public DateTime RateDate { get; init; }
    public decimal BuyingRate { get; init; }
    public decimal SellingRate { get; init; }
    public decimal EffectiveBuyingRate { get; init; }
    public decimal EffectiveSellingRate { get; init; }
    public string Source { get; init; } = "TCMB";
    public string? CurrencyName { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

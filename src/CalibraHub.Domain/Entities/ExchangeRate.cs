namespace CalibraHub.Domain.Entities;

public sealed class ExchangeRate
{
    public int Id { get; init; }
    /// <summary>FK currencies(id). Save sirasinda set edilmek zorunda — CurrencyCode'dan lookup yapilabilir.</summary>
    public int CurrencyId { get; init; }
    /// <summary>currencies tablosundan JOIN ile cozulen 3-harfli kod (USD/EUR/...). SaveRatesAsync icin de
    /// kullanilir: CurrencyId=0 ise repository CurrencyCode'dan id lookup eder (TCMB akisi icin).</summary>
    public string CurrencyCode { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public decimal BuyingRate { get; init; }
    public decimal SellingRate { get; init; }
    public decimal EffectiveBuyingRate { get; init; }
    public decimal EffectiveSellingRate { get; init; }
    public string Source { get; init; } = "TCMB";
    public string? CurrencyName { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

namespace CalibraHub.Application.Contracts;

public sealed record CurrencyDto(int Id, string Code, string Name, string? Symbol, bool IsActive, decimal? LatestBuyingRate, decimal? LatestSellingRate, decimal? EffectiveBuyingRate, decimal? EffectiveSellingRate, DateTime? LatestRateDate);
public sealed record ExchangeRateDto(string CurrencyCode, DateTime RateDate, decimal BuyingRate, decimal SellingRate, decimal EffectiveBuyingRate, decimal EffectiveSellingRate, string Source);
public sealed record CreateCurrencyRequest(string Code, string Name, string? Symbol, bool IsActive = true);
public sealed record UpdateCurrencyRequest(int Id, string Code, string Name, string? Symbol, bool IsActive);

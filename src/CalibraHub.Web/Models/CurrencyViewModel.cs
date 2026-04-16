using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Web.Models;

public sealed class CurrencyViewModel
{
    public IReadOnlyCollection<CurrencyDto> Items { get; init; } = [];
    public IReadOnlyCollection<ExchangeRateDto> RateItems { get; init; } = [];
    public CurrencyInput Input { get; init; } = new();
    public string? FilterCode { get; init; }
    public DateTime FromDate { get; init; } = DateTime.Today;
    public DateTime ToDate { get; init; } = DateTime.Today;
}

public sealed class CurrencyInput
{
    public int? Id { get; set; }
    [Required, MaxLength(5)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(5)] public string? Symbol { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? BuyingRate { get; set; }
    public decimal? SellingRate { get; set; }
    public decimal? EffectiveBuyingRate { get; set; }
    public decimal? EffectiveSellingRate { get; set; }
}

namespace CalibraHub.Application.Contracts;

/// <summary>
/// Ondalık ayarlar ekranı satırı — Forms tablosundaki her form + varsa özel ayarı.
/// HasCustom=false ise form şirket geneli varsayılanı ('*') kullanıyor demektir.
/// </summary>
public sealed record DecimalSettingRowDto(
    string FormCode,
    string FormName,
    string? Module,
    bool HasCustom,
    int QuantityDecimals,
    int UnitPriceDecimals,
    int FxUnitPriceDecimals,
    int AmountDecimals,
    int RateDecimals,
    int ExchangeRateDecimals);

/// <summary>Ayar kaydetme isteği. FormCode '*' = şirket geneli varsayılan.</summary>
public sealed record SaveDecimalSettingRequest(
    string FormCode,
    int QuantityDecimals,
    int UnitPriceDecimals,
    int FxUnitPriceDecimals,
    int AmountDecimals,
    int RateDecimals,
    int ExchangeRateDecimals);

/// <summary>
/// Runtime etkili ondalık ayarı — frontend hesap/format ve backend rounding
/// aynı değerleri kullanır. Source: 'form' | 'default' | 'fallback'.
/// </summary>
public sealed record EffectiveDecimalsDto(
    string FormCode,
    int Quantity,
    int UnitPrice,
    int FxUnitPrice,
    int Amount,
    int Rate,
    int ExchangeRate,
    string Source)
{
    /// <summary>Hiç kayıt yokken kullanılan sabit varsayılan.</summary>
    public static EffectiveDecimalsDto Fallback(string formCode) =>
        new(formCode, 2, 2, 4, 2, 2, 4, "fallback");

    public decimal RoundQuantity(decimal value)     => Math.Round(value, Quantity, MidpointRounding.AwayFromZero);
    public decimal RoundUnitPrice(decimal value)    => Math.Round(value, UnitPrice, MidpointRounding.AwayFromZero);
    public decimal RoundFxUnitPrice(decimal value)  => Math.Round(value, FxUnitPrice, MidpointRounding.AwayFromZero);
    public decimal RoundAmount(decimal value)       => Math.Round(value, Amount, MidpointRounding.AwayFromZero);
    public decimal RoundRate(decimal value)         => Math.Round(value, Rate, MidpointRounding.AwayFromZero);
    public decimal RoundExchangeRate(decimal value) => Math.Round(value, ExchangeRate, MidpointRounding.AwayFromZero);
}

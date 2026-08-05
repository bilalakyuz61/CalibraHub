namespace CalibraHub.Application.Constants;

/// <summary>
/// Satış Teklifi (ve genel dövizli belge) şirket parametreleri.
/// FormCode "SALES_QUOTE" (FormCodes.SalesQuote ile aynı string; diğer param
/// sınıflarındaki gibi literal tutulur — bkz. StockParameters/ApprovalParameters).
/// </summary>
public static class SalesQuoteParameters
{
    public const string FormCode = "SALES_QUOTE";

    /// <summary>
    /// Belge dövizi için kullanılacak TCMB kur tipi — GetCurrencyRate hangi rate
    /// alanını (ExchangeRate.*) döndürecek. Değer sabitleri aşağıda. Tanımsız → Satış.
    /// (2026-08-06 kullanıcı kararı: tüm dövizli belgelerde geçerli, tek şirket tercihi.)
    /// </summary>
    public const string RateTypeKey = "RATE_TYPE";

    // Değer sabitleri — ExchangeRate entity alanlarına eşlenir.
    public const string RateTypeSelling          = "Selling";          // SellingRate          (Satış) — varsayılan
    public const string RateTypeBuying           = "Buying";           // BuyingRate           (Alış)
    public const string RateTypeEffectiveSelling = "EffectiveSelling"; // EffectiveSellingRate (Efektif Satış)
    public const string RateTypeEffectiveBuying  = "EffectiveBuying";  // EffectiveBuyingRate  (Efektif Alış)

    public const string RateTypeDefault = RateTypeSelling;
}

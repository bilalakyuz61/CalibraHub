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

    // ── TL fiyata mudahale (2026-08-22, kullanici istegi) ────────────────────
    // Dovizli belgede kalem gridindeki TL aynasi normalde SALT OKUNUR bir
    // gostergedir (birim fiyat x kur). Bu parametre acikken TL fiyat alani
    // duzenlenebilir olur; girilen TL degeri KURA BOLUNEREK doviz birim fiyati
    // geri hesaplanir.
    // BELGE TURU BAZINDA: anahtar, belge turu koduyla uretilir (StockParameters
    // .EffectKey ile ayni desen) → her belge tipi ayri ayarlanabilir.
    // Tanimsiz/false → bugunku davranis (TL alani salt okunur) — fail-open.
    public const string TlPriceEditKeyPrefix = "TL_PRICE_EDIT_";

    public static string TlPriceEditKey(string docTypeCode) => TlPriceEditKeyPrefix + docTypeCode;

    /// <summary>TL fiyata mudahale ayarinin sunuldugu belge turleri — fiyatli ve
    /// dovizli olabilen ticari belgeler. (Ihtiyac Kaydi fiyatsizdir, listede yok.)</summary>
    public static readonly (string Code, string Label)[] TlPriceEditCapableTypes =
    [
        ("satis_teklifi",    "Satış Teklifi"),
        ("satis_siparisi",   "Satış Siparişi"),
        ("satis_irsaliyesi", "Satış İrsaliyesi"),
        ("alis_teklifi",     "Satın Alma Teklifi"),
        ("alis_siparisi",    "Satın Alma Siparişi"),
        ("alis_irsaliyesi",  "Alış İrsaliyesi"),
    ];
}

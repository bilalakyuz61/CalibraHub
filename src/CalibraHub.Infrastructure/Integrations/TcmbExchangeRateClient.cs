using System.Globalization;
using System.Xml.Linq;
using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Infrastructure.Integrations;

public sealed class TcmbExchangeRateClient : ITcmbExchangeRateClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string TcmbTodayUrl = "https://www.tcmb.gov.tr/kurlar/today.xml";
    // Gecmis tarih formati: https://www.tcmb.gov.tr/kurlar/202604/07042026.xml (yyyyMM/ddMMyyyy.xml)

    public async Task<IReadOnlyCollection<ExchangeRate>> GetDailyRatesAsync(CancellationToken ct)
    {
        return await GetRatesForDateAsync(DateTime.Today, ct);
    }

    public async Task<IReadOnlyCollection<ExchangeRate>> GetRatesForDateAsync(DateTime date, CancellationToken ct)
    {
        var url = date.Date == DateTime.Today
            ? TcmbTodayUrl
            : $"https://www.tcmb.gov.tr/kurlar/{date:yyyyMM}/{date:ddMMyyyy}.xml";

        var response = await Http.GetStringAsync(url, ct);
        var doc = XDocument.Parse(response);
        var rates = new List<ExchangeRate>();
        var today = DateTime.Today;

        // TCMB XML: <Tarih_Date> root, <Currency CurrencyCode="USD"> children
        var dateAttr = doc.Root?.Attribute("Date")?.Value;
        if (!string.IsNullOrEmpty(dateAttr))
        {
            if (DateTime.TryParseExact(dateAttr, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                today = parsed;
        }

        foreach (var el in doc.Descendants("Currency"))
        {
            var code = el.Attribute("CurrencyCode")?.Value;
            if (string.IsNullOrEmpty(code)) continue;

            var buyStr = el.Element("ForexBuying")?.Value;
            var sellStr = el.Element("ForexSelling")?.Value;
            var effBuyStr = el.Element("BanknoteBuying")?.Value ?? el.Element("ForexBuying")?.Value;
            var effSellStr = el.Element("BanknoteSelling")?.Value ?? el.Element("ForexSelling")?.Value;

            var nameStr = el.Element("Isim")?.Value ?? el.Element("CurrencyName")?.Value;

            if (TryParseDecimal(buyStr, out var buy) && TryParseDecimal(sellStr, out var sell))
            {
                TryParseDecimal(effBuyStr, out var effBuy);
                TryParseDecimal(effSellStr, out var effSell);
                rates.Add(new ExchangeRate
                {
                    CurrencyCode = code.ToUpperInvariant(),
                    RateDate = today,
                    BuyingRate = buy,
                    SellingRate = sell,
                    EffectiveBuyingRate = effBuy,
                    EffectiveSellingRate = effSell,
                    Source = "TCMB",
                    CurrencyName = nameStr?.Trim()
                });
            }
        }

        return rates;
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        // TCMB uses dot as decimal separator
        return decimal.TryParse(value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }
}

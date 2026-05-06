using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class CurrencyService : ICurrencyService
{
    private readonly ICurrencyRepository _currencyRepo;
    private readonly IExchangeRateRepository _rateRepo;
    private readonly ITcmbExchangeRateClient _tcmbClient;

    public CurrencyService(ICurrencyRepository currencyRepo, IExchangeRateRepository rateRepo, ITcmbExchangeRateClient tcmbClient)
    {
        _currencyRepo = currencyRepo;
        _rateRepo = rateRepo;
        _tcmbClient = tcmbClient;
    }

    public async Task<IReadOnlyCollection<CurrencyDto>> GetAllAsync(CancellationToken ct)
    {
        var currencies = await _currencyRepo.GetAllAsync(ct);
        var latestRates = await _rateRepo.GetLatestRatesAsync(ct);
        var rateLookup = latestRates.ToDictionary(r => r.CurrencyCode, StringComparer.OrdinalIgnoreCase);

        return currencies.Select(c =>
        {
            rateLookup.TryGetValue(c.Code, out var rate);
            return new CurrencyDto(c.Id, c.Code, c.Name, c.Symbol, c.IsActive,
                rate?.BuyingRate, rate?.SellingRate, rate?.EffectiveBuyingRate, rate?.EffectiveSellingRate, rate?.Date);
        }).ToArray();
    }

    public async Task<IReadOnlyCollection<CurrencyDto>> GetAllForDateAsync(DateTime date, CancellationToken ct)
    {
        var currencies = await _currencyRepo.GetAllAsync(ct);
        var dateRates = await _rateRepo.GetRatesForDateAsync(date, ct);
        var rateLookup = dateRates.ToDictionary(r => r.CurrencyCode, StringComparer.OrdinalIgnoreCase);

        return currencies.Select(c =>
        {
            rateLookup.TryGetValue(c.Code, out var rate);
            return new CurrencyDto(c.Id, c.Code, c.Name, c.Symbol, c.IsActive,
                rate?.BuyingRate, rate?.SellingRate, rate?.EffectiveBuyingRate, rate?.EffectiveSellingRate, rate?.Date);
        }).ToArray();
    }

    public async Task<CurrencyDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var c = await _currencyRepo.GetByIdAsync(id, ct);
        if (c is null) return null;
        var rate = await _rateRepo.GetRateAsync(c.Code, DateTime.Today, ct);
        return new CurrencyDto(c.Id, c.Code, c.Name, c.Symbol, c.IsActive,
            rate?.BuyingRate, rate?.SellingRate, rate?.EffectiveBuyingRate, rate?.EffectiveSellingRate, rate?.Date);
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateCurrencyRequest req, CancellationToken ct)
    {
        var code = req.Code?.Trim().ToUpperInvariant() ?? "";
        var name = req.Name?.Trim() ?? "";
        if (code.Length == 0) return (false, "Doviz kodu bos olamaz.", null);
        if (code.Length > 5) return (false, "Doviz kodu en fazla 5 karakter olabilir.", null);
        if (name.Length == 0) return (false, "Doviz adi bos olamaz.", null);

        var all = await _currencyRepo.GetAllAsync(ct);
        if (all.Any(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return (false, $"'{code}' kodlu doviz zaten mevcut.", null);

        var id = await _currencyRepo.AddAsync(new Currency { Code = code, Name = name, Symbol = req.Symbol?.Trim(), IsActive = req.IsActive }, ct);
        return (true, null, id);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(UpdateCurrencyRequest req, CancellationToken ct)
    {
        var code = req.Code?.Trim().ToUpperInvariant() ?? "";
        var name = req.Name?.Trim() ?? "";
        if (code.Length == 0) return (false, "Doviz kodu bos olamaz.");
        if (name.Length == 0) return (false, "Doviz adi bos olamaz.");

        var existing = await _currencyRepo.GetByIdAsync(req.Id, ct);
        if (existing is null) return (false, "Doviz bulunamadi.");

        var all = await _currencyRepo.GetAllAsync(ct);
        if (all.Any(x => x.Id != req.Id && x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return (false, $"'{code}' kodlu baska bir doviz zaten mevcut.");

        existing.Code = code;
        existing.Name = name;
        existing.Symbol = req.Symbol?.Trim();
        existing.IsActive = req.IsActive;
        await _currencyRepo.UpdateAsync(existing, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken ct)
    {
        var existing = await _currencyRepo.GetByIdAsync(id, ct);
        if (existing is null) return (false, "Doviz bulunamadi.");
        // TRY kontrolu kaldirildi — kullanici isterse silebilir
        await _currencyRepo.DeleteAsync(id, ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int Count)> UpdateRatesFromTcmbAsync(CancellationToken ct)
    {
        return await UpdateRatesFromTcmbAsync(DateTime.Today, ct);
    }

    public async Task<(bool Success, string? Error, int Count)> UpdateRatesFromTcmbAsync(DateTime date, CancellationToken ct)
    {
        // Kurlari her zaman istenen tarih altinda kaydet. Iki sebep:
        //   1) Istenen tarih hafta sonu/tatil ise en yakin is gununun kurunu o tarih altina yazariz.
        //   2) TCMB today.xml ~15:30 oncesi genellikle onceki is gununun kurlarini doner; bu
        //      durumda da istenen tarih (bugun) altinda kaydederiz, tablo bos kalmasin.
        var tryDate = date.Date;
        for (var attempt = 0; attempt < 7; attempt++)
        {
            if (tryDate.DayOfWeek != DayOfWeek.Saturday && tryDate.DayOfWeek != DayOfWeek.Sunday)
            {
                try
                {
                    var rates = await _tcmbClient.GetRatesForDateAsync(tryDate, ct);
                    if (rates.Count > 0)
                    {
                        var toSave = rates.Select(r => CloneWithDate(r, date.Date)).ToList();
                        await _rateRepo.SaveRatesAsync(toSave, ct);
                        await UpdateCurrencyNamesFromRatesAsync(rates, ct);
                        return (true, null, toSave.Count);
                    }
                }
                catch { /* sonraki gunu dene */ }
            }
            tryDate = tryDate.AddDays(-1);
        }
        return (false, $"TCMB'den {date:dd.MM.yyyy} ve oncesi icin kur bilgisi alinamadi.", 0);
    }

    private async Task<IReadOnlyCollection<ExchangeRate>?> FetchPreviousBusinessDayRatesAsync(DateTime weekendDate, CancellationToken ct)
    {
        var probe = weekendDate.Date.AddDays(-1);
        for (var attempt = 0; attempt < 7; attempt++)
        {
            if (probe.DayOfWeek != DayOfWeek.Saturday && probe.DayOfWeek != DayOfWeek.Sunday)
            {
                try
                {
                    var r = await _tcmbClient.GetRatesForDateAsync(probe, ct);
                    if (r.Count > 0) return r;
                }
                catch { /* geri git */ }
            }
            probe = probe.AddDays(-1);
        }
        return null;
    }

    private static ExchangeRate CloneWithDate(ExchangeRate r, DateTime newDate) => new()
    {
        CurrencyId = r.CurrencyId,
        CurrencyCode = r.CurrencyCode,
        Date = newDate,
        BuyingRate = r.BuyingRate,
        SellingRate = r.SellingRate,
        EffectiveBuyingRate = r.EffectiveBuyingRate,
        EffectiveSellingRate = r.EffectiveSellingRate,
        Source = r.Source,
        CurrencyName = r.CurrencyName
    };

    public async Task<(bool Success, string? Error, int TotalCount)> UpdateRatesFromTcmbBulkAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        if (from > to) return (false, "Baslangic tarihi bitis tarihinden buyuk olamaz.", 0);
        if ((to - from).TotalDays > 365) return (false, "En fazla 1 yillik aralik secilebilir.", 0);

        var totalCount = 0;
        var fetchedDays = 0;
        var skippedDays = new List<string>();
        IReadOnlyCollection<ExchangeRate>? lastBusinessRates = null;
        var current = from.Date;
        var endDate = to.Date;
        while (current <= endDate)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                try
                {
                    var rates = await _tcmbClient.GetRatesForDateAsync(current, ct);
                    if (rates.Count > 0)
                    {
                        // Kurlari istenen gun altinda kaydet — TCMB today.xml ~15:30 oncesi
                        // onceki gunun verisini donebilir; stamping ile bu gunun sutunu bos kalmaz.
                        var stamped = rates.Select(r => CloneWithDate(r, current)).ToList();
                        await _rateRepo.SaveRatesAsync(stamped, ct);
                        totalCount += stamped.Count;
                        fetchedDays++;
                        lastBusinessRates = stamped;
                    }
                    else
                    {
                        skippedDays.Add(current.ToString("dd.MM.yyyy"));
                    }
                }
                catch
                {
                    skippedDays.Add(current.ToString("dd.MM.yyyy"));
                }
            }
            else
            {
                // Hafta sonu: son is gununun kurlarini bu tarihe kopyala
                var source = lastBusinessRates ?? await FetchPreviousBusinessDayRatesAsync(current, ct);
                if (source is not null && source.Count > 0)
                {
                    var cloned = source.Select(r => CloneWithDate(r, current)).ToList();
                    await _rateRepo.SaveRatesAsync(cloned, ct);
                    totalCount += cloned.Count;
                    fetchedDays++;
                    lastBusinessRates ??= source;
                }
                else
                {
                    skippedDays.Add(current.ToString("dd.MM.yyyy"));
                }
            }
            current = current.AddDays(1);
        }

        if (totalCount > 0)
        {
            // Ilk basta cekilen kurlardan doviz isimlerini guncelle
            await UpdateCurrencyNamesFromRatesAsync(
                (await _tcmbClient.GetRatesForDateAsync(from > DateTime.Today ? DateTime.Today : from, ct)), ct);

            var msg = $"{totalCount} kur guncellendi ({fetchedDays} gun).";
            if (skippedDays.Count > 0)
                msg += $" {skippedDays.Count} gun icin veri bulunamadi (tatil/resmi tatil olabilir).";
            return (true, msg, totalCount);
        }

        return (false,
            $"{from:dd.MM.yyyy} - {to:dd.MM.yyyy} araliginda kur bilgisi alinamadi. " +
            "TCMB henuz bu tarihler icin veri yayinlamamis olabilir (kurlar genellikle 15:30'dan sonra guncellenir).", 0);
    }

    /// <summary>TCMB'den gelen kur verilerindeki doviz isimlerini, ismi bos olan currencies kayitlarina yazar.</summary>
    private async Task UpdateCurrencyNamesFromRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken ct)
    {
        try
        {
            var currencies = await _currencyRepo.GetAllAsync(ct);
            var nameMap = rates
                .Where(r => !string.IsNullOrWhiteSpace(r.CurrencyName))
                .GroupBy(r => r.CurrencyCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().CurrencyName!, StringComparer.OrdinalIgnoreCase);

            foreach (var c in currencies)
            {
                if (!string.IsNullOrWhiteSpace(c.Name)) continue;
                if (!nameMap.TryGetValue(c.Code, out var tcmbName)) continue;
                c.Name = tcmbName;
                c.UpdatedAt = DateTime.Now;
                await _currencyRepo.UpdateAsync(c, ct);
            }
        }
        catch { /* isim guncelleme basarisiz olursa kur kaydi etkilenmesin */ }
    }
}

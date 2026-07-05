using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace CalibraHub.Application.Services;

/// <summary>
/// Ondalık ayar çözümleme + cache. Etkili ayar sırası:
///   1) (CompanyId, FormCode)  2) (CompanyId, '*')  3) fallback (2,2,2,2,4)
/// Cache: (companyId, formCode) → EffectiveDecimalsDto, 5 dk TTL. Save/Reset
/// şirket generation sayacını artırır → o şirketin tüm girdileri anında bayatlar
/// ('*' değişimi tüm formları etkilediği için tek tek silmek yerine generation).
/// </summary>
public sealed class DecimalSettingService : IDecimalSettingService
{
    public const string DefaultFormCode = "*";

    private readonly IDecimalSettingRepository _repository;
    private readonly IFormRepository _formRepository;
    private readonly ICurrentCompanyProvider _currentCompany;
    private readonly IMemoryCache _cache;

    public DecimalSettingService(
        IDecimalSettingRepository repository,
        IFormRepository formRepository,
        ICurrentCompanyProvider currentCompany,
        IMemoryCache cache)
    {
        _repository = repository;
        _formRepository = formRepository;
        _currentCompany = currentCompany;
        _cache = cache;
    }

    public async Task<EffectiveDecimalsDto> GetEffectiveAsync(string? formCode, CancellationToken ct)
    {
        var companyId = _currentCompany.GetCurrentCompanyId();
        var code = string.IsNullOrWhiteSpace(formCode) ? DefaultFormCode : formCode.Trim();
        if (companyId <= 0) return EffectiveDecimalsDto.Fallback(code);

        var gen = GetGeneration(companyId);
        var cacheKey = $"decset:{companyId}:{gen}:{code}";
        if (_cache.TryGetValue(cacheKey, out EffectiveDecimalsDto? cached) && cached is not null)
            return cached;

        EffectiveDecimalsDto result;
        var own = code == DefaultFormCode ? null : await _repository.GetAsync(companyId, code, ct);
        if (own is not null)
        {
            result = ToEffective(code, own, "form");
        }
        else
        {
            var def = await _repository.GetAsync(companyId, DefaultFormCode, ct);
            result = def is not null
                ? ToEffective(code, def, "default")
                : EffectiveDecimalsDto.Fallback(code);
        }

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    public async Task<IReadOnlyList<DecimalSettingRowDto>> GetPageRowsAsync(CancellationToken ct)
    {
        var companyId = _currentCompany.GetCurrentCompanyId();
        var forms = await _formRepository.GetAllAsync(ct);
        var settings = companyId > 0
            ? await _repository.GetAllAsync(companyId, ct)
            : Array.Empty<DecimalSetting>() as IReadOnlyList<DecimalSetting>;
        var byCode = settings.ToDictionary(s => s.FormCode, StringComparer.OrdinalIgnoreCase);

        var defaults = byCode.TryGetValue(DefaultFormCode, out var d)
            ? d
            : new DecimalSetting { FormCode = DefaultFormCode }; // ctor varsayılanları (2,2,2,2,4)

        var rows = new List<DecimalSettingRowDto>
        {
            new(DefaultFormCode, "Genel Varsayılan", null,
                byCode.ContainsKey(DefaultFormCode),
                defaults.QuantityDecimals, defaults.UnitPriceDecimals, defaults.FxUnitPriceDecimals,
                defaults.AmountDecimals, defaults.RateDecimals, defaults.ExchangeRateDecimals),
        };

        foreach (var form in forms.Where(f => f.IsActive).OrderBy(f => f.Module).ThenBy(f => f.SortOrder).ThenBy(f => f.FormCode))
        {
            var has = byCode.TryGetValue(form.FormCode, out var s);
            var src = has ? s! : defaults;
            rows.Add(new DecimalSettingRowDto(
                form.FormCode, form.FormName, form.Module, has,
                src.QuantityDecimals, src.UnitPriceDecimals, src.FxUnitPriceDecimals,
                src.AmountDecimals, src.RateDecimals, src.ExchangeRateDecimals));
        }
        return rows;
    }

    public async Task SaveAsync(SaveDecimalSettingRequest request, int? userId, CancellationToken ct)
    {
        var companyId = _currentCompany.GetCurrentCompanyId();
        if (companyId <= 0) throw new InvalidOperationException("Şirket kimliği çözümlenemedi.");
        var code = string.IsNullOrWhiteSpace(request.FormCode) ? DefaultFormCode : request.FormCode.Trim();

        await _repository.UpsertAsync(new DecimalSetting
        {
            CompanyId            = companyId,
            FormCode             = code,
            QuantityDecimals     = Clamp(request.QuantityDecimals),
            UnitPriceDecimals    = Clamp(request.UnitPriceDecimals),
            FxUnitPriceDecimals  = Clamp(request.FxUnitPriceDecimals),
            AmountDecimals       = Clamp(request.AmountDecimals),
            RateDecimals         = Clamp(request.RateDecimals),
            ExchangeRateDecimals = Clamp(request.ExchangeRateDecimals),
            CreatedById          = userId,
            UpdatedById          = userId,
        }, ct);
        BumpGeneration(companyId);
    }

    public async Task ResetAsync(string formCode, CancellationToken ct)
    {
        var companyId = _currentCompany.GetCurrentCompanyId();
        if (companyId <= 0) throw new InvalidOperationException("Şirket kimliği çözümlenemedi.");
        var code = (formCode ?? string.Empty).Trim();
        if (code.Length == 0 || code == DefaultFormCode)
            throw new InvalidOperationException("Genel varsayılan silinemez; değerlerini güncelleyin.");
        await _repository.DeleteAsync(companyId, code, ct);
        BumpGeneration(companyId);
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 6);

    private static EffectiveDecimalsDto ToEffective(string code, DecimalSetting s, string source) =>
        new(code, s.QuantityDecimals, s.UnitPriceDecimals, s.FxUnitPriceDecimals,
            s.AmountDecimals, s.RateDecimals, s.ExchangeRateDecimals, source);

    // Şirket başına cache jenerasyonu — Save/Reset ile artar, eski girdiler doğal düşer.
    private int GetGeneration(int companyId) =>
        _cache.GetOrCreate($"decset:gen:{companyId}", e => { e.Priority = CacheItemPriority.NeverRemove; return 0; });

    private void BumpGeneration(int companyId)
    {
        var key = $"decset:gen:{companyId}";
        var current = _cache.GetOrCreate(key, e => { e.Priority = CacheItemPriority.NeverRemove; return 0; });
        _cache.Set(key, current + 1, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
    }
}

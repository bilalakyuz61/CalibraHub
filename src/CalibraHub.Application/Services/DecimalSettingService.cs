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

    /// <summary>
    /// Belge ailesi normalizasyonu — üst/kalem/yeni/düzenleme ekran kodları tek belge
    /// koduna iner (SALES_QUOTE_LINES → SALES_QUOTE, STOCK_IN_LINES → STOCK_IN).
    /// Kullanıcı belge başına TEK ondalık tanımı yapar; grid'lerin lineFormCode'u ile
    /// backend'in kök kodu aynı ayara düşer. Kayıt/silme/çözümleme/liste hepsi bu
    /// kökle çalışır — alias kod ile kayıt oluşmaz.
    /// </summary>
    public static string NormalizeFormCode(string code)
    {
        if (code.EndsWith("_LINES", StringComparison.OrdinalIgnoreCase)) return code[..^6];
        if (code.EndsWith("_EDIT",  StringComparison.OrdinalIgnoreCase)) return code[..^5];
        if (code.EndsWith("_NEW",   StringComparison.OrdinalIgnoreCase)) return code[..^4];
        return code;
    }

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
        var code = string.IsNullOrWhiteSpace(formCode) ? DefaultFormCode : NormalizeFormCode(formCode.Trim());
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

        // Belge ailesi konsolidasyonu: SALES_QUOTE + _NEW/_EDIT/_LINES → tek "Satış Teklifi"
        // satırı. Üst/kalem için ayrı tanım yok — belge bazında tek tanım (2026-07-07 talebi).
        // Aile etiketi: çok üyeli ailede belge adı SubModule'de taşınır ("Ambar Giriş",
        // "İhtiyaç Kaydı"); üye FormName'leri "Üst Bilgi"/"Kalem Bilgisi" olduğundan kullanılmaz.
        var families = forms
            .Where(f => f.IsActive)
            .GroupBy(f => NormalizeFormCode(f.FormCode), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var primary = g.OrderBy(f => f.SortOrder).ThenBy(f => f.FormCode, StringComparer.OrdinalIgnoreCase).First();
                var label = g.Count() > 1
                    ? g.Select(f => f.SubModule).FirstOrDefault(sm => !string.IsNullOrWhiteSpace(sm)) ?? primary.FormName
                    : primary.FormName;
                return (RootCode: g.Key, Label: label, primary.Module, primary.SortOrder);
            })
            .OrderBy(x => x.Module)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.RootCode, StringComparer.OrdinalIgnoreCase);

        foreach (var family in families)
        {
            var has = byCode.TryGetValue(family.RootCode, out var s);
            var src = has ? s! : defaults;
            rows.Add(new DecimalSettingRowDto(
                family.RootCode, family.Label, family.Module, has,
                src.QuantityDecimals, src.UnitPriceDecimals, src.FxUnitPriceDecimals,
                src.AmountDecimals, src.RateDecimals, src.ExchangeRateDecimals));
        }
        return rows;
    }

    public async Task SaveAsync(SaveDecimalSettingRequest request, int? userId, CancellationToken ct)
    {
        var companyId = _currentCompany.GetCurrentCompanyId();
        if (companyId <= 0) throw new InvalidOperationException("Şirket kimliği çözümlenemedi.");
        var code = string.IsNullOrWhiteSpace(request.FormCode) ? DefaultFormCode : NormalizeFormCode(request.FormCode.Trim());

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
        code = NormalizeFormCode(code);
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

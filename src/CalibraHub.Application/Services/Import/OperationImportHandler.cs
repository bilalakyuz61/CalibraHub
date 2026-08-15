using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Operasyon içe-aktarım handler'ı. Yazma <see cref="IOperationService.SaveAsync"/>'e
/// delege edilir. Kod kullanıcıdan istenmez; boşsa addan türetilir
/// (kullanıcı kod girmez kuralı — uniqueness ad üzerinden).
/// </summary>
public sealed class OperationImportHandler : RowImportHandlerBase
{
    private readonly IOperationService _operations;
    private List<OperationDto>? _cache;

    public OperationImportHandler(IOperationService operations) => _operations = operations;

    public override string Entity => "OPERATION";
    public override string Label => "Operasyon";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new List<ImportTargetFieldDto>
    {
        new("Name", "Operasyon Adı", "string", true, true, "Zorunlu", MaxLength: 200),
        new("Code", "Operasyon Kodu", "string", false, true, "Boşsa addan otomatik üretilir", MaxLength: 50),
        new("Description", "Açıklama", "string", false, false),
        new("StandardDuration", "Standart Süre", "decimal", false, false),
        new("DurationUnit", "Süre Birimi", "string", false, false, "Dakika / Saat",
            new[] { "Dakika", "Saat" }),
        new("HourlyRate", "Saatlik Maliyet", "decimal", false, false),
        new("SortOrder", "Sıra", "int", false, false),
        new("IsActive", "Aktif", "bool", false, false, "Evet / Hayır", new[] { "Evet", "Hayır" }),
    };

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "Name"))) errs.Add("Operasyon adı boş.");

        var dur = Get(d, "StandardDuration");
        if (!string.IsNullOrWhiteSpace(dur) && ParseDecimal(dur) is null)
            errs.Add($"Standart süre sayı değil: '{dur}'.");

        var rate = Get(d, "HourlyRate");
        if (!string.IsNullOrWhiteSpace(rate) && ParseDecimal(rate) is null)
            errs.Add($"Saatlik maliyet sayı değil: '{rate}'.");

        var unit = Get(d, "DurationUnit");
        if (!string.IsNullOrWhiteSpace(unit) && ResolveUnit(unit) is null)
            errs.Add($"Süre birimi tanınmadı: '{unit}' (Dakika / Saat).");

        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys.Count == 0) return ("insert", null);

        var list = await EnsureAsync(ct);
        var hit = list.FirstOrDefault(o => KeysMatch(d, matchKeys, k => OperationValue(o, k)));
        return hit is not null ? ("update", hit.Id) : ("insert", null);
    }

    /// <summary>Mevcut operasyonun hedef alan karşılığı (bileşik anahtar karşılaştırması için).</summary>
    private static string? OperationValue(OperationDto o, string key) => key.ToLowerInvariant() switch
    {
        "code" => o.Code,
        "name" => o.Name,
        _      => null,
    };

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        OperationDto? existing = null;
        if (action == "update" && existingId is > 0)
        {
            var list = await EnsureAsync(ct);
            existing = list.FirstOrDefault(o => o.Id == existingId.Value);
        }

        var name = Get(d, "Name")?.Trim() ?? existing?.Name ?? string.Empty;
        var code = existing?.Code ?? await DeriveUniqueCodeAsync(Get(d, "Code"), name, usedCodes, ct);

        var req = new SaveOperationRequest(
            Id: existing?.Id ?? 0,
            Code: code,
            Name: name,
            Description: Pick(d, "Description", existing?.Description),
            StandardDuration: PickDecimal(d, "StandardDuration", existing?.StandardDuration),
            DurationUnit: ResolveUnit(Get(d, "DurationUnit")) ?? existing?.DurationUnit ?? DurationUnit.Minute,
            HourlyRate: PickDecimal(d, "HourlyRate", existing?.HourlyRate),
            SortOrder: PickInt(d, "SortOrder", existing?.SortOrder ?? 0),
            IsActive: PickBool(d, "IsActive", existing?.IsActive ?? true));

        var id = await _operations.SaveAsync(req, ct);
        _cache = null;
        return (true, null, id);
    }

    private async Task<List<OperationDto>> EnsureAsync(CancellationToken ct)
        => _cache ??= (await _operations.ListAsync(true, ct)).ToList();

    private async Task<string> DeriveUniqueCodeAsync(
        string? rawCode, string name, HashSet<string> used, CancellationToken ct)
    {
        var list = await EnsureAsync(ct);
        bool Exists(string c) => used.Contains(c) || list.Any(o => Same(o.Code, c));

        if (!string.IsNullOrWhiteSpace(rawCode))
        {
            var c = rawCode.Trim().ToUpperInvariant();
            if (!Exists(c)) { used.Add(c); return c; }
        }

        var baseCode = new string((name ?? "OPR").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (baseCode.Length == 0) baseCode = "OPR";
        if (baseCode.Length > 16) baseCode = baseCode[..16];
        var candidate = baseCode; var n = 1;
        while (Exists(candidate)) { n++; candidate = baseCode + n; }
        used.Add(candidate);
        return candidate;
    }

    /// <summary>Serbest metni süre birimine eşler. Tanınmazsa null (doğrulama hatası verir).</summary>
    private static DurationUnit? ResolveUnit(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return null;
        if (v is "dakika" or "dk" or "minute" or "minutes" or "min") return DurationUnit.Minute;
        if (v is "saat" or "sa" or "hour" or "hours" or "hr") return DurationUnit.Hour;
        return null;
    }

    private static bool Same(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Pick(IReadOnlyDictionary<string, string?> d, string key, string? existing)
    {
        var v = Get(d, key);
        return string.IsNullOrWhiteSpace(v) ? existing : v.Trim();
    }

    private static decimal? PickDecimal(IReadOnlyDictionary<string, string?> d, string key, decimal? existing)
    {
        var v = Get(d, key);
        return string.IsNullOrWhiteSpace(v) ? existing : ParseDecimal(v);
    }

    private static int PickInt(IReadOnlyDictionary<string, string?> d, string key, int existing)
    {
        var v = Get(d, key);
        if (string.IsNullOrWhiteSpace(v)) return existing;
        return (int?)ParseDecimal(v) ?? existing;
    }

    private static bool PickBool(IReadOnlyDictionary<string, string?> d, string key, bool existing)
    {
        var v = Get(d, key);
        return string.IsNullOrWhiteSpace(v) ? existing : ParseBool(v);
    }
}

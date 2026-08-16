using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Makine içe-aktarım handler'ı. Yazma <see cref="ILogisticsConfigurationService"/>
/// Create/UpdateMachineAsync'e delege edilir (ad benzersizliği + kod auto-türetme orada).
///
/// Makine bir lokasyona bağlıdır (LocationId zorunlu FK). Kaynak veride lokasyon kodu
/// ya da adı taşınır; burada koda/ada göre ÇÖZÜLÜR — bulunamazsa satır reddedilir
/// (sessizce rastgele bir lokasyona bağlamak yerine).
/// </summary>
public sealed class MachineImportHandler : RowImportHandlerBase
{
    private readonly ILogisticsConfigurationService _logistics;
    private readonly ImportWidgetSupport _widgetSupport;
    private List<MachineDto>? _machines;
    private List<LocationDto>? _locations;

    public MachineImportHandler(
        ILogisticsConfigurationService logistics,
        IWidgetRepository widgetRepo,
        IWidgetService widgetService)
    {
        _logistics = logistics;
        // MACHINES — MachineEdit ekranının DynamicWidgetRenderer formCode'u;
        // RecordId = Machine.Id (ekrandaki data-record-id ile aynı konvansiyon).
        _widgetSupport = new ImportWidgetSupport(widgetRepo, widgetService, "MACHINES");
    }

    public override Task PreloadAsync(CancellationToken ct) => _widgetSupport.PreloadAsync(ct);

    public override string Entity => "MACHINE";
    public override string Label => "Makine";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields()
    {
        var fields = new List<ImportTargetFieldDto>
        {
        new("Name", "Makine Adı", "string", true, true, "Zorunlu", MaxLength: 200),
        new("Code", "Makine Kodu", "string", false, true, "Boşsa addan otomatik üretilir", MaxLength: 50),
        new("LocationCode", "Lokasyon Kodu", "string", true, false, "Makinenin bağlı olduğu lokasyon (kod veya ad)"),
        new("HourlyCapacity", "Saatlik Kapasite", "decimal", false, false),
        new("SortOrder", "Sıra", "int", false, false),
        new("IsActive", "Aktif", "bool", false, false, "Evet / Hayır", new[] { "Evet", "Hayır" }),
        };
        // Makine kartına admin'in eklediği özel (widget) alanlar — PreloadAsync ile yüklenir.
        fields.AddRange(_widgetSupport.GetFields());
        return fields;
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "Name"))) errs.Add("Makine adı boş.");
        if (string.IsNullOrWhiteSpace(Get(d, "LocationCode"))) errs.Add("Lokasyon boş.");

        var cap = Get(d, "HourlyCapacity");
        if (!string.IsNullOrWhiteSpace(cap) && ParseDecimal(cap) is null)
            errs.Add($"Saatlik kapasite sayı değil: '{cap}'.");

        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys.Count == 0) return ("insert", null);

        var list = await EnsureMachinesAsync(ct);
        var hit = list.FirstOrDefault(m => KeysMatch(d, matchKeys, k => MachineValue(m, k)));
        return hit is not null ? ("update", hit.Id) : ("insert", null);
    }

    public override bool SupportsDeactivate => true;

    /// <summary>Kaynakta olmayan aktif makineleri pasife alır.</summary>
    public override async Task<int> DeactivateAbsentAsync(ImportRowSet set, bool previewOnly, CancellationToken ct)
    {
        var sourceKeys = BuildSourceKeySet(set);
        if (sourceKeys.Count == 0) return 0;

        var all = await EnsureMachinesAsync(ct);
        var absent = all
            .Where(m => m.IsActive)
            .Where(m => KeySignature(k => MachineValue(m, k), set.MatchKeyFields) is string sig
                        && !sourceKeys.Contains(sig))
            .ToList();

        if (previewOnly) return absent.Count;

        foreach (var m in absent)
        {
            await _logistics.UpdateMachineAsync(new UpdateMachineRequest(
                m.Id, m.LocationId, m.Code, m.Name, m.HourlyCapacity, m.SortOrder, false), ct);
        }
        if (absent.Count > 0) _machines = null;
        return absent.Count;
    }

    /// <summary>Mevcut makinenin hedef alan karşılığı (bileşik anahtar karşılaştırması için).</summary>
    private static string? MachineValue(MachineDto m, string key) => key.ToLowerInvariant() switch
    {
        "code" => m.Code,
        "name" => m.Name,
        _      => null,
    };

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        MachineDto? existing = null;
        if (action == "update" && existingId is > 0)
        {
            var list = await EnsureMachinesAsync(ct);
            existing = list.FirstOrDefault(m => m.Id == existingId.Value);
        }

        var locationRaw = Get(d, "LocationCode");
        var locationId = await ResolveLocationIdAsync(locationRaw, ct);
        if (locationId is null)
        {
            // Bulunamayan lokasyonu sessizce atlamak veya varsayılana bağlamak yanlış
            // veri üretir — satır açıkça reddedilir.
            if (existing is null)
                return (false, $"Lokasyon bulunamadı: '{locationRaw}'.", null);
            locationId = existing.LocationId;
        }

        var name = Get(d, "Name")?.Trim() ?? existing?.Name ?? string.Empty;
        var capacity = PickDecimal(d, "HourlyCapacity", existing?.HourlyCapacity);
        var sortOrder = PickInt(d, "SortOrder", existing?.SortOrder ?? 0);
        var isActive = PickBool(d, "IsActive", existing?.IsActive ?? true);

        if (existing is not null)
        {
            await _logistics.UpdateMachineAsync(
                new UpdateMachineRequest(existing.Id, locationId.Value, existing.Code, name, capacity, sortOrder, isActive), ct);
            _machines = null;

            var wErrU = await _widgetSupport.SaveRowValuesAsync(existing.Id.ToString(), d, ct);
            if (wErrU != null) return (false, $"Makine güncellendi, {wErrU}", existing.Id);

            return (true, null, existing.Id);
        }

        var code = await DeriveUniqueCodeAsync(Get(d, "Code"), name, usedCodes, ct);
        var id = await _logistics.CreateMachineAsync(
            new CreateMachineRequest(locationId.Value, code, name, capacity, sortOrder, isActive), ct);
        _machines = null;

        // Özel (widget) alanlar — RecordId = Machine.Id. CreateMachineAsync Id döndürdüğü
        // için stok kartındaki gibi ek sorguya gerek yok.
        var wErr = await _widgetSupport.SaveRowValuesAsync(id.ToString(), d, ct);
        if (wErr != null) return (false, $"Makine oluşturuldu, {wErr}", id);

        return (true, null, id);
    }

    private async Task<List<MachineDto>> EnsureMachinesAsync(CancellationToken ct)
        => _machines ??= (await _logistics.GetMachinesAsync(ct)).ToList();

    private async Task<List<LocationDto>> EnsureLocationsAsync(CancellationToken ct)
        => _locations ??= (await _logistics.GetLocationsAsync(ct)).ToList();

    /// <summary>Önce lokasyon koduna, bulunamazsa adına bakar.</summary>
    private async Task<int?> ResolveLocationIdAsync(string? raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var locations = await EnsureLocationsAsync(ct);

        var hit = locations.FirstOrDefault(l => Same(l.LocationCode, raw))
               ?? locations.FirstOrDefault(l => Same(l.LocationName, raw));
        return hit?.Id;
    }

    private async Task<string> DeriveUniqueCodeAsync(
        string? rawCode, string name, HashSet<string> used, CancellationToken ct)
    {
        var list = await EnsureMachinesAsync(ct);
        bool Exists(string c) => used.Contains(c) || list.Any(m => Same(m.Code, c));

        if (!string.IsNullOrWhiteSpace(rawCode))
        {
            var c = rawCode.Trim().ToUpperInvariant();
            if (!Exists(c)) { used.Add(c); return c; }
        }

        var baseCode = new string((name ?? "MAK").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (baseCode.Length == 0) baseCode = "MAK";
        if (baseCode.Length > 16) baseCode = baseCode[..16];
        var candidate = baseCode; var n = 1;
        while (Exists(candidate)) { n++; candidate = baseCode + n; }
        used.Add(candidate);
        return candidate;
    }

    private static bool Same(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

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

using System.Globalization;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Özellik Değeri içe-aktarım handler'ı. Özellik aktarımından SONRA çalıştırılır —
/// değer, adına göre çözülen bir özelliğe bağlanır.
///
/// Şablonda KOD KOLONU YOKTUR: değer kodu sunucuda otomatik üretilir
/// (<c>CreateProductConfigurationValueAsync</c> (Id, Code) döner). Kullanıcı bir Excel
/// hazırlarken bu kodu bilemez; bu yüzden hem burada hem Stok–Özellik şablonunda değere
/// KENDİ METNİYLE referans verilir.
///
/// Değerin hangi kolona yazılacağı özelliğin veri tipine göre belirlenir
/// (Metin → TextValue, Sayı → NumericValue, Tarih → DateValue). Tip uymayan satır
/// REDDEDİLİR — yanlış kolona yazmak, ekranda boş görünen ama "aktarıldı" sayılan
/// değerler üretirdi.
/// </summary>
public sealed class FeatureValueImportHandler : RowImportHandlerBase
{
    private readonly ILogisticsConfigurationService _logistics;
    private ProductConfigurationSnapshotDto? _snapshot;

    public FeatureValueImportHandler(ILogisticsConfigurationService logistics) => _logistics = logistics;

    public override string Entity => "FEATURE_VALUE";
    public override string Label => "Özellik Değeri";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new List<ImportTargetFieldDto>
    {
        new("FeatureName", "Özellik Adı", "string", true, true, "Önce Özellik şablonuyla tanımlanmış olmalı", MaxLength: 200),
        new("Value", "Değer", "string", true, true, "Özelliğin veri tipine uygun olmalı", MaxLength: 200),
        new("Description", "Açıklama", "string", false, false, "Listede gösterilen etiket", MaxLength: 500),
        new("Note", "Not", "string", false, false, MaxLength: 500),
    };

    public override async Task PreloadAsync(CancellationToken ct)
        => _snapshot = await _logistics.GetProductConfigurationSnapshotAsync(ct);

    private async Task<ProductConfigurationSnapshotDto> EnsureAsync(CancellationToken ct)
    {
        if (_snapshot is null) await PreloadAsync(ct);
        return _snapshot!;
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "FeatureName"))) errs.Add("Özellik adı boş.");
        if (string.IsNullOrWhiteSpace(Get(d, "Value"))) errs.Add("Değer boş.");
        return errs;
    }

    /// <summary>
    /// Özelliği ADINA göre çözer. Bulunamazsa null — çağıran satırı reddeder.
    /// Rastgele/ilk özelliğe bağlamak sessiz veri bozulması olurdu.
    /// </summary>
    private static ProductConfigurationFeatureDto? FindFeature(
        ProductConfigurationSnapshotDto snap, string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return null;
        return snap.Features.FirstOrDefault(f =>
            f.IsActive && string.Equals(f.Name?.Trim(), n, StringComparison.OrdinalIgnoreCase));
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys.Count == 0) return ("insert", null);
        var snap = await EnsureAsync(ct);
        var hit = snap.Values.FirstOrDefault(v => KeysMatch(d, matchKeys, k => ValueFieldOf(v, k)));
        return hit is not null ? ("update", hit.Id) : ("insert", null);
    }

    private static string? ValueFieldOf(ProductConfigurationValueDto v, string key) => key switch
    {
        "FeatureName" => v.FeatureName,
        "Value" => v.Value,
        "Description" => v.Description,
        _ => null,
    };

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var snap = await EnsureAsync(ct);
        var featureName = Get(d, "FeatureName");
        var feature = FindFeature(snap, featureName);
        if (feature is null)
            return (false, $"Özellik bulunamadı: '{featureName?.Trim()}'. Önce Özellik şablonunu aktarın.", null);

        var raw = (Get(d, "Value") ?? "").Trim();
        var description = Get(d, "Description")?.Trim();
        var note = Get(d, "Note")?.Trim();

        if (action == "update" && existingId is int id)
        {
            // Güncellemede yalnız açıklama/not değişebilir — değerin kendisi kombinasyonlarda
            // referanslıdır, sessizce değiştirmek mevcut kombinasyonların anlamını bozar.
            await _logistics.UpdateProductConfigurationValueAsync(id, description, note, ct);
            _snapshot = null;
            return (true, null, id);
        }

        // Değer, özelliğin veri tipine göre doğru kolona yazılır.
        string? textValue = null;
        decimal? numericValue = null;
        DateTime? dateValue = null;

        switch ((feature.DataType ?? "").Trim().ToLowerInvariant())
        {
            case "numeric":
            case "sayı":
            case "sayi":
                numericValue = ParseDecimal(raw);
                if (numericValue is null)
                    return (false, $"'{feature.Name}' sayısal bir özellik; '{raw}' sayı olarak okunamadı.", null);
                break;

            case "date":
            case "tarih":
                if (!DateTime.TryParse(raw, new CultureInfo("tr-TR"), DateTimeStyles.None, out var parsed) &&
                    !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    return (false, $"'{feature.Name}' tarih tipli bir özellik; '{raw}' tarih olarak okunamadı.", null);
                dateValue = parsed;
                break;

            default:
                textValue = raw;
                break;
        }

        var (newId, _) = await _logistics.CreateProductConfigurationValueAsync(
            new CreateProductConfigurationValueRequest(
                feature.Id, description, textValue, numericValue, dateValue, true, note), ct);
        _snapshot = null;
        return (true, null, newId);
    }
}

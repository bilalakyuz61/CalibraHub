using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Özellik (ürün konfigürasyon özelliği) içe-aktarım handler'ı.
///
/// DİKKAT — iki paralel API var: <c>CreatePropertyAsync</c> ailesi ile
/// <c>CreateProductConfigurationFeatureAsync</c> ailesi aynı tabloları farklı yollardan
/// yazar. Ekranın (ProductFeatureController → /Logistics/SaveProductFeatureJson) KULLANDIĞI
/// ikincisidir; bu handler da onu çağırır. Birincisi seçilseydi aktarım "başarılı" der,
/// kayıtlar ekranda görünmezdi.
///
/// Benzersizlik ADA göre — özellikte kullanıcıya kod sordurulmaz (kod otomatik türetilir),
/// bu projenin "kullanıcı kod girmez" kuralının gereği.
/// </summary>
public sealed class FeatureImportHandler : RowImportHandlerBase
{
    private readonly ILogisticsConfigurationService _logistics;
    private IReadOnlyCollection<ProductConfigurationFeatureDto>? _features;

    public FeatureImportHandler(ILogisticsConfigurationService logistics) => _logistics = logistics;

    public override string Entity => "FEATURE";
    public override string Label => "Özellik";

    /// <summary>Excel'de yazılabilecek veri tipi karşılıkları (hem Türkçe hem enum adı).</summary>
    private static readonly Dictionary<string, ConfigurationFieldDataType> DataTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Metin"] = ConfigurationFieldDataType.Text,
            ["Text"] = ConfigurationFieldDataType.Text,
            ["Sayı"] = ConfigurationFieldDataType.Numeric,
            ["Sayi"] = ConfigurationFieldDataType.Numeric,
            ["Numeric"] = ConfigurationFieldDataType.Numeric,
            ["Tarih"] = ConfigurationFieldDataType.Date,
            ["Date"] = ConfigurationFieldDataType.Date,
        };

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new List<ImportTargetFieldDto>
    {
        new("Name", "Özellik Adı", "string", true, true, "Zorunlu — benzersizlik bu alana göredir", MaxLength: 200),
        new("DataType", "Veri Tipi", "string", false, false, "Boşsa Metin kabul edilir",
            new[] { "Metin", "Sayı", "Tarih" }),
        new("UnitOfMeasure", "Birim", "string", false, false, "Örn. mm, kg — yalnız gösterim", MaxLength: 50),
        new("VisibleInDesign", "Tasarımda Görünür", "bool", false, false, "Evet / Hayır", new[] { "Evet", "Hayır" }),
        // Aktiflik YALNIZ yeni kayıtta uygulanır: güncelleme sözleşmesi (UpdateProductConfiguration-
        // FeatureRequest) bu alanı taşımıyor. Var olmayan bir davranışı varmış gibi göstermemek için
        // ipucunda açıkça yazılı.
        new("IsActive", "Aktif", "bool", false, false, "Evet / Hayır — yalnız YENİ kayıtta uygulanır",
            new[] { "Evet", "Hayır" }),
    };

    public override async Task PreloadAsync(CancellationToken ct)
    {
        var snapshot = await _logistics.GetProductConfigurationSnapshotAsync(ct);
        _features = snapshot.Features;
    }

    private async Task<IReadOnlyCollection<ProductConfigurationFeatureDto>> EnsureFeaturesAsync(CancellationToken ct)
    {
        if (_features is null) await PreloadAsync(ct);
        return _features ?? Array.Empty<ProductConfigurationFeatureDto>();
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "Name"))) errs.Add("Özellik adı boş.");

        var dt = Get(d, "DataType");
        if (!string.IsNullOrWhiteSpace(dt) && !DataTypes.ContainsKey(dt.Trim()))
            errs.Add($"Veri tipi tanınmadı: '{dt}'. Geçerli: Metin, Sayı, Tarih.");

        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys.Count == 0) return ("insert", null);
        var list = await EnsureFeaturesAsync(ct);
        var hit = list.FirstOrDefault(f => KeysMatch(d, matchKeys, k => FeatureValueOf(f, k)));
        return hit is not null ? ("update", hit.Id) : ("insert", null);
    }

    private static string? FeatureValueOf(ProductConfigurationFeatureDto f, string key) => key switch
    {
        "Name" => f.Name,
        "UnitOfMeasure" => f.UnitOfMeasure,
        "DataType" => f.DataType,
        _ => null,
    };

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var name = (Get(d, "Name") ?? "").Trim();
        var dtRaw = (Get(d, "DataType") ?? "").Trim();
        var dataType = string.IsNullOrWhiteSpace(dtRaw)
            ? ConfigurationFieldDataType.Text
            : DataTypes[dtRaw];
        var unit = Get(d, "UnitOfMeasure")?.Trim();
        var visible = string.IsNullOrWhiteSpace(Get(d, "VisibleInDesign")) || ParseBool(Get(d, "VisibleInDesign"));

        if (action == "update" && existingId is int id)
        {
            await _logistics.UpdateProductConfigurationFeatureAsync(
                new UpdateProductConfigurationFeatureRequest(id, name, dataType, unit, visible), ct);
            _features = null;   // liste değişti — sonraki satır tazesini görsün
            return (true, null, id);
        }

        var isActive = string.IsNullOrWhiteSpace(Get(d, "IsActive")) || ParseBool(Get(d, "IsActive"));
        var newId = await _logistics.CreateProductConfigurationFeatureAsync(
            new CreateProductConfigurationFeatureRequest(name, dataType, isActive, unit, visible), ct);
        _features = null;
        return (true, null, newId);
    }
}

using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Stok–Özellik(–Değer) içe-aktarım handler'ı. TEK şablon iki ihtiyacı da karşılar, çünkü
/// ikisi de aynı tablodur (<c>ItemFeatureMapping</c>: ItemId · FeatureId · FeatureValueId
/// NULL olabilir):
///   · Değer kolonu BOŞ  → "bu stokta bu özellik var, değeri serbest"
///   · Değer kolonu DOLU → "bu stokta bu özellik yalnız bu değer(ler)e izinli"
/// Aynı stok+özellik için birden çok satır = birden çok izinli değer.
///
/// Tekrar aktarımda MEVCUT EŞLEŞTİRMELER KORUNUR (kullanıcı kararı 2026-08-25): dosyada
/// olmayan bir eşleştirme silinmez. Yazma yolu ekranınkiyle aynı
/// (<c>SetFeaturesForItemAsync</c>) ama o metod bir stoğun listesini TAMAMEN yeniden yazar;
/// bu yüzden mevcut durum okunur, satır üzerine EKLENİR, birleşmiş liste yazılır.
///
/// Birleşme durumu stok başına bellekte tutulur: aynı stok dosyanın farklı satırlarında
/// geçtiğinde her satır bir öncekini silmesin diye. (Doğrudan
/// <c>CreateStockPropertyMappingAsync</c> kullanılamaz — o metod FeatureValueId zorunlu
/// tutuyor, yani "değeri serbest" satırını yazamaz.)
/// </summary>
public sealed class ItemFeatureImportHandler : RowImportHandlerBase
{
    private readonly ILogisticsConfigurationService _logistics;

    private ProductConfigurationSnapshotDto? _snapshot;
    private IReadOnlyCollection<ItemDto>? _items;

    /// <summary>itemId → (featureId → (print, izinli değer Id'leri)). Dosya boyunca birikir.</summary>
    private readonly Dictionary<int, Dictionary<int, (bool Print, HashSet<int> ValueIds)>> _pending = new();

    public ItemFeatureImportHandler(ILogisticsConfigurationService logistics) => _logistics = logistics;

    public override string Entity => "ITEM_FEATURE";
    public override string Label => "Stok Özelliği";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new List<ImportTargetFieldDto>
    {
        new("MaterialCode", "Malzeme Kodu", "string", true, true, "Aktif bir stok kartı olmalı", MaxLength: 50),
        new("FeatureName", "Özellik Adı", "string", true, true, "Önce Özellik şablonuyla tanımlanmış olmalı", MaxLength: 200),
        new("Value", "Değer", "string", false, true, "Boş bırakılırsa değer kısıtı olmaz", MaxLength: 200),
        new("PrintDescriptionInDesign", "Tasarımda Yazdır", "bool", false, false, "Evet / Hayır", new[] { "Evet", "Hayır" }),
    };

    public override async Task PreloadAsync(CancellationToken ct)
    {
        _snapshot = await _logistics.GetProductConfigurationSnapshotAsync(ct);
        _items = await _logistics.GetItemsForLookupAsync(ct);
        _pending.Clear();
    }

    private async Task EnsureAsync(CancellationToken ct)
    {
        if (_snapshot is null || _items is null) await PreloadAsync(ct);
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "MaterialCode"))) errs.Add("Malzeme kodu boş.");
        if (string.IsNullOrWhiteSpace(Get(d, "FeatureName"))) errs.Add("Özellik adı boş.");
        return errs;
    }

    private ItemDto? FindItem(string? code)
    {
        var c = (code ?? "").Trim();
        if (c.Length == 0 || _items is null) return null;
        return _items.FirstOrDefault(i =>
            i.IsActive && string.Equals(i.Code?.Trim(), c, StringComparison.OrdinalIgnoreCase));
    }

    private ProductConfigurationFeatureDto? FindFeature(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0 || _snapshot is null) return null;
        return _snapshot.Features.FirstOrDefault(f =>
            f.IsActive && string.Equals(f.Name?.Trim(), n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Değeri, KENDİ METNİYLE ve ilgili özellik içinde arar (kod otomatik üretildiği için).</summary>
    private ProductConfigurationValueDto? FindValue(int featureId, string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0 || _snapshot is null) return null;
        return _snapshot.Values.FirstOrDefault(x =>
            x.FeatureId == featureId && x.IsActive &&
            (string.Equals(x.Value?.Trim(), v, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.Code?.Trim(), v, StringComparison.OrdinalIgnoreCase)));
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        await EnsureAsync(ct);

        var item = FindItem(Get(d, "MaterialCode"));
        var feature = FindFeature(Get(d, "FeatureName"));
        if (item is null || feature is null) return ("insert", null);

        var current = await LoadCurrentAsync(item.Id, ct);
        if (!current.TryGetValue(feature.Id, out var entry)) return ("insert", null);

        var valueText = Get(d, "Value");
        if (string.IsNullOrWhiteSpace(valueText))
            return ("update", item.Id);                       // özellik zaten bağlı

        var value = FindValue(feature.Id, valueText);
        if (value is null) return ("insert", null);           // değer çözülemedi — commit reddedecek
        return entry.ValueIds.Contains(value.Id) ? ("update", item.Id) : ("insert", null);
    }

    /// <summary>Stoğun mevcut özellik eşleştirmesi — bir kez okunur, sonra bellekte birikir.</summary>
    private async Task<Dictionary<int, (bool Print, HashSet<int> ValueIds)>> LoadCurrentAsync(
        int itemId, CancellationToken ct)
    {
        if (_pending.TryGetValue(itemId, out var cached)) return cached;

        var map = new Dictionary<int, (bool Print, HashSet<int> ValueIds)>();
        var batch = await _logistics.GetItemFeatureMappingsBatchAsync(new[] { itemId }, ct);
        if (batch.TryGetValue(itemId, out var rows))
        {
            foreach (var r in rows.Where(r => r.IsActive))
            {
                if (!map.TryGetValue(r.FeatureId, out var e))
                    e = (r.PrintDescriptionInDesign, new HashSet<int>());
                if (r.FeatureValueId is int vid) e.ValueIds.Add(vid);
                map[r.FeatureId] = e;
            }
        }
        _pending[itemId] = map;
        return map;
    }

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        await EnsureAsync(ct);

        var code = Get(d, "MaterialCode")?.Trim();
        var item = FindItem(code);
        if (item is null)
            return (false, $"Stok kartı bulunamadı veya aktif değil: '{code}'.", null);

        var featureName = Get(d, "FeatureName")?.Trim();
        var feature = FindFeature(featureName);
        if (feature is null)
            return (false, $"Özellik bulunamadı: '{featureName}'. Önce Özellik şablonunu aktarın.", null);

        var valueText = Get(d, "Value")?.Trim();
        int? valueId = null;
        if (!string.IsNullOrWhiteSpace(valueText))
        {
            var value = FindValue(feature.Id, valueText);
            if (value is null)
                return (false, $"'{feature.Name}' özelliğinde '{valueText}' değeri tanımlı değil. Önce Özellik Değeri şablonunu aktarın.", null);
            valueId = value.Id;
        }

        var print = string.IsNullOrWhiteSpace(Get(d, "PrintDescriptionInDesign"))
            || ParseBool(Get(d, "PrintDescriptionInDesign"));

        var current = await LoadCurrentAsync(item.Id, ct);
        if (!current.TryGetValue(feature.Id, out var entry))
            entry = (print, new HashSet<int>());
        else
            entry = (print, entry.ValueIds);
        if (valueId is int vid) entry.ValueIds.Add(vid);
        current[feature.Id] = entry;

        // Birleşmiş listeyi yaz. Ekranın kullandığı yol budur; doğrulamaları (stok aktif mi,
        // özellik geçerli mi) o metod yapar.
        var payload = current
            .Select(kv => (FeatureId: kv.Key, PrintDescriptionInDesign: kv.Value.Print, AllowedValueIds: kv.Value.ValueIds.ToArray()))
            .ToList();

        await _logistics.SetFeaturesForItemAsync(item.Code, payload, ct);
        return (true, null, item.Id);
    }
}

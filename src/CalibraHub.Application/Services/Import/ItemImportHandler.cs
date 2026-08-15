using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Stok Kartı / Malzeme (Item) içe-aktarım handler'ı. Yazma
/// <see cref="ILogisticsConfigurationService.CreateItemAsync"/>/<c>UpdateItemAsync</c>'e delege edilir.
/// Kod yoksa addan benzersiz türetilir (kullanıcı kod girmez kuralı).
/// Özel (widget) alanlar ImportWidgetSupport ile desteklenir — ITEMS formuna
/// admin'in eklediği alanlar şablonda kolon olarak görünür ve WidgetTra'ya yazılır.
/// </summary>
public sealed class ItemImportHandler : RowImportHandlerBase
{
    private readonly ILogisticsConfigurationService _logistics;
    private readonly ILogisticsConfigurationRepository _logisticsRepo;
    private readonly ImportWidgetSupport _widgetSupport;
    private List<Item>? _items;   // run-cache (scoped, satırlar sıralı işlenir)
    private List<Unit>? _units;   // birim çözümü — PreloadAsync ile doldurulur

    public ItemImportHandler(
        ILogisticsConfigurationService logistics,
        ILogisticsConfigurationRepository logisticsRepo,
        IWidgetRepository widgetRepo,
        IWidgetService widgetService)
    {
        _logistics = logistics;
        _logisticsRepo = logisticsRepo;
        // "MATERIAL_CARD_EDIT" — MaterialCardEdit'in DynamicWidgetRenderer formCode'u
        // (RecordId = Items.Id konvansiyonu ile ayni). ITEMS formu 2026-07-06'da
        // kaldirildi — Forms seed'inde yok, taze DB'lerde satiri hic olusmaz.
        _widgetSupport = new ImportWidgetSupport(widgetRepo, widgetService, "MATERIAL_CARD_EDIT");
    }

    public override string Entity => "ITEM";
    public override string Label => "Stok Kartı";

    /// <summary>Malzeme tipi kodları — MaterialCardEdit ekranındaki sabit liste ile birebir.</summary>
    private static readonly (int Id, string Label)[] MaterialTypes =
    {
        (1, "Hammadde"), (2, "Yarı Mamul"), (3, "Mamul"),
        (4, "Ticari Mal"), (5, "Sarf Malzemesi"), (10, "Kit"),
    };

    public override IReadOnlyList<ImportTargetFieldDto> GetFields()
    {
        var fields = new List<ImportTargetFieldDto>
        {
            // MaxLength'ler Items tablosu NVARCHAR uzunluklarıyla birebir (Name 200, Code 50).
            // Ad da eşleşme anahtarı olabilir — tek başına ya da bileşik (örn. Kod + Ad).
            new("Name",    "Stok Adı",  "string",  true,  true,  "Malzeme/ürün adı (zorunlu)", MaxLength: 200),
            new("Code",    "Stok Kodu", "string",  false, true,  "Boşsa addan otomatik üretilir; eşleştirme anahtarı olabilir", MaxLength: 50),
            new("Barcode", "Barkod",    "string",  false, true,  MaxLength: 100),
            new("MaterialType", "Malzeme Tipi", "string", false, false,
                "Hammadde / Yarı Mamul / Mamul / Ticari Mal / Sarf Malzemesi / Kit",
                MaterialTypes.Select(t => t.Label).ToArray()),
            new("Unit", "Ana Birim", "string", false, false, "Birim kodu veya adı (örn. ADET, KG)"),
            new("TaxRate",      "KDV Oranı",  "decimal", false, false, "Yüzde (boşsa %20)"),
            new("TrackingType", "Takip Tipi", "string",  false, false, "Yok / Lot / Seri (boşsa Yok)", new[] { "Yok", "Lot", "Seri" }),
            new("MinStock",     "Minimum Stok", "decimal", false, false),
            new("AutoSerial",   "Otomatik Seri", "bool", false, false, "Evet / Hayır", new[] { "Evet", "Hayır" }),
            new("Combinations", "Kombinasyonlu", "bool", false, false, "Evet / Hayır", new[] { "Evet", "Hayır" }),
        };
        // Stok kartına admin'in eklediği özel (widget) alanlar — PreloadAsync ile yüklenir.
        fields.AddRange(_widgetSupport.GetFields());
        return fields;
    }

    public override async Task PreloadAsync(CancellationToken ct)
    {
        await _widgetSupport.PreloadAsync(ct);
        // Birim doğrulaması ValidateRow'da yapılır — liste önceden yüklenmeli.
        _units ??= (await _logisticsRepo.GetUnitsAsync(ct)).ToList();
    }

    /// <summary>Serbest metni malzeme tipine eşler (etiket veya sayısal kod). Tanınmazsa null.</summary>
    private static int? ResolveMaterialTypeId(string? raw)
    {
        var v = (raw ?? string.Empty).Trim();
        if (v.Length == 0) return null;

        if (int.TryParse(v, out var numeric) && MaterialTypes.Any(t => t.Id == numeric)) return numeric;

        // Türkçe karakter/boşluk farklarına dayanıklı karşılaştırma.
        static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var target = Norm(v);
        foreach (var (id, label) in MaterialTypes)
            if (Norm(label) == target) return id;

        return null;
    }

    /// <summary>
    /// Dinamik izinli değerler — birimler DB'den gelir, statik listeye yazılamaz.
    /// Eşleme ekranında gösterilir ki uyarlamacı kaynak view'ı buna göre şekillendirsin.
    /// </summary>
    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetDynamicAllowedValuesAsync(
        CancellationToken ct)
    {
        _units ??= (await _logisticsRepo.GetUnitsAsync(ct)).ToList();
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Unit"] = _units.Where(u => u.IsActive)
                             .Select(u => u.Code?.Trim() ?? string.Empty)
                             .Where(c => c.Length > 0)
                             .ToList(),
        };
    }

    /// <summary>Birim kodu veya adına göre UnitId. Bulunamazsa null.</summary>
    private int? ResolveUnitId(string? raw)
    {
        var v = (raw ?? string.Empty).Trim();
        if (v.Length == 0 || _units is null) return null;
        var hit = _units.FirstOrDefault(u => string.Equals(u.Code?.Trim(), v, StringComparison.OrdinalIgnoreCase))
               ?? _units.FirstOrDefault(u => string.Equals(u.Name?.Trim(), v, StringComparison.OrdinalIgnoreCase));
        return hit?.Id;
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "Name"))) errs.Add("Stok adı boş.");

        var tax = Get(d, "TaxRate");
        if (!string.IsNullOrWhiteSpace(tax) && ParseDecimal(tax) is null) errs.Add($"KDV oranı sayı değil: '{tax}'.");

        var min = Get(d, "MinStock");
        if (!string.IsNullOrWhiteSpace(min) && ParseDecimal(min) is null) errs.Add($"Minimum stok sayı değil: '{min}'.");

        // Tanınmayan tip/birim SESSİZCE null'a düşerse kart eksik kurulur — açıkça reddedilir.
        var type = Get(d, "MaterialType");
        if (!string.IsNullOrWhiteSpace(type) && ResolveMaterialTypeId(type) is null)
            errs.Add($"Malzeme tipi tanınmadı: '{type}'.");

        var unit = Get(d, "Unit");
        if (!string.IsNullOrWhiteSpace(unit) && _units is not null && ResolveUnitId(unit) is null)
            errs.Add($"Birim bulunamadı: '{unit}'.");

        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys.Count == 0) return ("insert", null);

        var items = await EnsureItemsAsync(ct);
        var hit = items.FirstOrDefault(i => KeysMatch(d, matchKeys, k => ItemValue(i, k)));
        return hit is not null ? ("update", hit.Id) : ("insert", null);
    }

    /// <summary>Mevcut stok kartının hedef alan karşılığı (bileşik anahtar karşılaştırması için).</summary>
    private static string? ItemValue(Item i, string key) => key.ToLowerInvariant() switch
    {
        "code"    => i.Code,
        "name"    => i.Name,
        "barcode" => i.Barcode,
        _         => null,
    };

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var name = Get(d, "Name")!.Trim();
        var taxRate = ParseDecimal(Get(d, "TaxRate")) ?? 20m;
        var tracking = ResolveTracking(Get(d, "TrackingType"));
        var typeId = ResolveMaterialTypeId(Get(d, "MaterialType"));
        var unitId = ResolveUnitId(Get(d, "Unit"));
        var barcode = Get(d, "Barcode")?.Trim();
        var minStock = ParseDecimal(Get(d, "MinStock"));
        var autoSerialRaw = Get(d, "AutoSerial");
        var combinationsRaw = Get(d, "Combinations");

        if (action == "update" && existingId is > 0)
        {
            var items = await EnsureItemsAsync(ct);
            var ex = items.FirstOrDefault(i => i.Id == existingId.Value);
            // Eşlenmemiş (veya boş) alan mevcut değerini KORUR — aktarım eşlenmeyen
            // alanları sessizce sıfırlamamalı.
            var req = new UpdateItemRequest(
                existingId.Value,
                ex?.Code ?? (Get(d, "Code") ?? name),
                name,
                typeId ?? ex?.TypeId,
                unitId ?? ex?.UnitId,
                string.IsNullOrWhiteSpace(combinationsRaw) ? (ex?.Combinations ?? false) : ParseBool(combinationsRaw),
                taxRate,
                tracking ?? ex?.TrackingType ?? "None",
                minStock ?? ex?.MinStock ?? 0m,
                string.IsNullOrWhiteSpace(autoSerialRaw) ? (ex?.AutoSerial ?? false) : ParseBool(autoSerialRaw),
                string.IsNullOrWhiteSpace(barcode) ? ex?.Barcode : barcode);
            await _logistics.UpdateItemAsync(req, ct);

            // Özel (widget) alanlar — RecordId = Items.Id (MaterialCardEdit DWR konvansiyonu)
            var wErrU = await _widgetSupport.SaveRowValuesAsync(existingId.Value.ToString(), d, ct);
            if (wErrU != null) return (false, $"Stok güncellendi, {wErrU}", existingId);
            return (true, null, existingId);
        }

        var code = await DeriveUniqueCodeAsync(Get(d, "Code"), name, usedCodes, ct);
        await _logistics.CreateItemAsync(new CreateItemRequest(
            code, name, typeId, unitId,
            ParseBool(combinationsRaw), taxRate, tracking ?? "None",
            minStock ?? 0m, ParseBool(autoSerialRaw),
            string.IsNullOrWhiteSpace(barcode) ? null : barcode), ct);

        // Yeni kaydın Id'si CreateItemAsync'ten dönmüyor — yalnizca satirda widget
        // degeri VARSA kod uzerinden geri bulunur (ek sorgu maliyetine o zaman girilir).
        if (_widgetSupport.HasRowValues(d))
        {
            var (paged, _) = await _logisticsRepo.GetItemsPagedAsync(code, 0, 2, ct);
            var createdItem = paged.FirstOrDefault(i =>
                string.Equals(i.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase));
            if (createdItem is not null)
            {
                var wErr = await _widgetSupport.SaveRowValuesAsync(createdItem.Id.ToString(), d, ct);
                if (wErr != null) return (false, $"Stok oluşturuldu, {wErr}", createdItem.Id);
                return (true, null, createdItem.Id);
            }
        }
        return (true, null, null);
    }

    private async Task<List<Item>> EnsureItemsAsync(CancellationToken ct)
        => _items ??= (await _logisticsRepo.GetItemsAsync(ct)).ToList();

    private async Task<string> DeriveUniqueCodeAsync(string? rawCode, string name, HashSet<string> used, CancellationToken ct)
    {
        var items = await EnsureItemsAsync(ct);
        bool Exists(string c) => used.Contains(c) || items.Any(i => string.Equals(i.Code?.Trim(), c, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(rawCode))
        {
            var c = rawCode.Trim().ToUpperInvariant();
            if (!Exists(c)) { used.Add(c); return c; }
        }
        var baseCode = new string((name ?? "STOK").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (baseCode.Length == 0) baseCode = "STOK";
        if (baseCode.Length > 16) baseCode = baseCode[..16];
        var candidate = baseCode; int n = 1;
        while (Exists(candidate)) { n++; candidate = baseCode + n; }
        used.Add(candidate);
        return candidate;
    }

    /// <summary>Serbest metni takip tipine eşle: Lot / Seri / Yok. Boş → null (servis "None" yapar).</summary>
    private static string? ResolveTracking(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return null;
        if (v is "lot" or "lot takibi" or "lot takip") return "Lot";
        if (v is "seri" or "serial" or "seri takibi" or "seri takip") return "Serial";
        return "None";
    }
}

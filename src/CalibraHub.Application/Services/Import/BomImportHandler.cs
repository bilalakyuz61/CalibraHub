using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Reçete (BOM / Ürün Ağacı) içe-aktarım handler'ı. Yapı başlık+kalem olduğu için
/// satır-bazlı tabanı KULLANMAZ: her Excel satırı bir bileşendir, satırlar "Ana Ürün Kodu"na
/// göre gruplanıp her ana ürün için tek <see cref="SaveBOMRequest"/> oluşturulur.
/// Stok kodları Id'ye çözülür (eksikse satır hata verir). Yazma <c>SaveBOMAsync</c>.
/// </summary>
public sealed class BomImportHandler : IImportTargetHandler
{
    private const int PreviewDetailLimit = 500;
    private readonly ILogisticsConfigurationService _logistics;
    private readonly ILogisticsConfigurationRepository _itemRepo;

    public BomImportHandler(ILogisticsConfigurationService logistics, ILogisticsConfigurationRepository itemRepo)
    { _logistics = logistics; _itemRepo = itemRepo; }

    public string Entity => "BOM";
    public string Label => "Reçete (Ürün Ağacı)";

    public IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("ParentCode",    "Ana Ürün Kodu", "string",  true,  false, "Reçetenin ait olduğu mamul/yarı-mamul stok kodu (zorunlu)"),
        new ImportTargetFieldDto("ComponentCode", "Bileşen Kodu",  "string",  true,  false, "Bu satırdaki bileşen (hammadde/yarı-mamul) stok kodu (zorunlu)"),
        new ImportTargetFieldDto("Quantity",      "Miktar",        "decimal", true,  false, "Bir ana ürün için bileşen miktarı (zorunlu)"),
        new ImportTargetFieldDto("ScrapRatio",          "Fire Oranı",           "decimal", false, false, "Yüzde (boşsa 0)"),
        new ImportTargetFieldDto("ParentCombination",   "Ana Ürün Kombinasyon", "string",  false, false, "Ana ürünün kombinasyon kodu (opsiyonel)"),
        new ImportTargetFieldDto("ComponentCombination","Bileşen Kombinasyon",  "string",  false, false, "Bileşenin kombinasyon kodu (opsiyonel)"),
        new ImportTargetFieldDto("Description",          "Açıklama",            "string",  false, false, "Reçete açıklaması (ana üründen alınır)"),
    };

    public async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        var items = (await _itemRepo.GetItemsAsync(ct)).ToList();
        bool HasItem(string code) => items.Any(i => string.Equals(i.Code?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));
        var (keys, labels) = DisplayCols(set.MappedKeys);

        int total = 0, valid = 0, error = 0;
        var detail = new List<ImportPreviewRowDto>();
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            total++;
            var errs = ValidateRow(row, HasItem, out var parent);
            if (errs.Count > 0) error++;
            else { valid++; if (!string.IsNullOrWhiteSpace(parent)) parents.Add(parent!); }
            if (detail.Count < PreviewDetailLimit)
            {
                var cells = keys.Select(k => new ImportPreviewCellDto(k, row.TryGetValue(k, out var v) ? v : null)).ToList();
                detail.Add(new ImportPreviewRowDto(total, errs.Count > 0 ? "error" : "insert", cells, errs));
            }
        }
        // insertCount = farklı reçete (ana ürün) sayısı; updateCount kullanılmıyor.
        return new ImportPreviewResultDto(true, null, total, valid, error, parents.Count, 0, keys, labels, detail);
    }

    public async Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct)
    {
        var items = (await _itemRepo.GetItemsAsync(ct)).ToList();
        Item? FindItem(string code) => items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));

        var results = new List<ImportCommitRowDto>();
        var groups = new Dictionary<string, BomGroup>(StringComparer.OrdinalIgnoreCase);
        int failed = 0, rowNo = 0;

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;
            var parent = ImportParse.Get(row, "ParentCode")?.Trim();
            var comp = ImportParse.Get(row, "ComponentCode")?.Trim();
            var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"));
            var scrap = ImportParse.ParseDecimal(ImportParse.Get(row, "ScrapRatio")) ?? 0m;

            if (string.IsNullOrWhiteSpace(parent)) { results.Add(Fail(rowNo, "Ana ürün kodu boş.")); failed++; continue; }
            if (string.IsNullOrWhiteSpace(comp)) { results.Add(Fail(rowNo, "Bileşen kodu boş.")); failed++; continue; }
            if (qty is null || qty <= 0) { results.Add(Fail(rowNo, "Miktar geçersiz (>0 olmalı).")); failed++; continue; }
            var parentItem = FindItem(parent); if (parentItem is null) { results.Add(Fail(rowNo, $"Ana ürün bulunamadı: '{parent}'")); failed++; continue; }
            var compItem = FindItem(comp); if (compItem is null) { results.Add(Fail(rowNo, $"Bileşen bulunamadı: '{comp}'")); failed++; continue; }

            int? parentConfigId = null;
            var parentCombo = ImportParse.Get(row, "ParentCombination")?.Trim();
            if (!string.IsNullOrWhiteSpace(parentCombo))
            {
                parentConfigId = await ResolveConfigIdAsync(parent, parentCombo, ct);
                if (parentConfigId is null) { results.Add(Fail(rowNo, $"Ana ürün kombinasyonu bulunamadı: '{parentCombo}' ({parent})")); failed++; continue; }
            }
            int? compConfigId = null;
            var compCombo = ImportParse.Get(row, "ComponentCombination")?.Trim();
            if (!string.IsNullOrWhiteSpace(compCombo))
            {
                compConfigId = await ResolveConfigIdAsync(comp, compCombo, ct);
                if (compConfigId is null) { results.Add(Fail(rowNo, $"Bileşen kombinasyonu bulunamadı: '{compCombo}' ({comp})")); failed++; continue; }
            }

            var key = parentItem.Id + "|" + (parentConfigId?.ToString() ?? "");
            if (!groups.TryGetValue(key, out var g))
            {
                g = new BomGroup(rowNo, parentItem.Id, parentConfigId, parent, ImportParse.Get(row, "Description"));
                groups[key] = g;
            }
            g.Lines.Add(new SaveBOMLineRequest(compItem.Id, compConfigId, comp, compCombo, qty.Value, scrap));
        }

        int inserted = 0;
        foreach (var g in groups.Values)
        {
            try
            {
                var req = new SaveBOMRequest(null, g.ParentId, g.ParentConfigId, g.ParentCode, null, g.Description,
                    null, null, null, 0, g.Lines, null, null);
                var bomId = await _logistics.SaveBOMAsync(req, userId, ct);
                inserted++;
                results.Add(new ImportCommitRowDto(g.FirstRow, true, "insert", null, bomId));
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new ImportCommitRowDto(g.FirstRow, false, "insert", $"Reçete '{g.ParentCode}': {ex.Message}", null));
            }
        }

        results = results.OrderBy(r => r.RowNumber).ToList();
        return new ImportCommitResultDto(true, null, inserted, 0, failed, results);
    }

    private static List<string> ValidateRow(IReadOnlyDictionary<string, string?> row, Func<string, bool> hasItem, out string? parent)
    {
        var errs = new List<string>();
        parent = ImportParse.Get(row, "ParentCode")?.Trim();
        var comp = ImportParse.Get(row, "ComponentCode")?.Trim();
        var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"));
        if (string.IsNullOrWhiteSpace(parent)) errs.Add("Ana ürün kodu boş.");
        else if (!hasItem(parent)) errs.Add($"Ana ürün bulunamadı: '{parent}'");
        if (string.IsNullOrWhiteSpace(comp)) errs.Add("Bileşen kodu boş.");
        else if (!hasItem(comp)) errs.Add($"Bileşen bulunamadı: '{comp}'");
        if (qty is null || qty <= 0) errs.Add("Miktar geçersiz (>0 olmalı).");
        return errs;
    }

    private (IReadOnlyList<string> Keys, IReadOnlyList<string> Labels) DisplayCols(IReadOnlyList<string> mapped)
    {
        var keys = new List<string>(); var labels = new List<string>();
        foreach (var f in GetFields())
            if (mapped.Any(k => string.Equals(k, f.Key, StringComparison.OrdinalIgnoreCase))) { keys.Add(f.Key); labels.Add(f.Label); }
        return (keys, labels);
    }

    private static ImportCommitRowDto Fail(int rowNo, string err) => new(rowNo, false, "error", err, null);

    // Kombinasyon kodu → ConfigId çözümü (stok kodu bazında cache). Bulunamazsa null.
    private readonly Dictionary<string, int?> _configCache = new(StringComparer.OrdinalIgnoreCase);
    private async Task<int?> ResolveConfigIdAsync(string itemCode, string comboCode, CancellationToken ct)
    {
        var k = itemCode + "||" + comboCode;
        if (_configCache.TryGetValue(k, out var cached)) return cached;
        var combos = await _itemRepo.GetCombinationsByMaterialCodeAsync(itemCode, ct);
        var match = combos.FirstOrDefault(c => string.Equals(c.Code?.Trim(), comboCode.Trim(), StringComparison.OrdinalIgnoreCase));
        var id = match?.ConfigId;
        _configCache[k] = id;
        return id;
    }

    private sealed class BomGroup
    {
        public BomGroup(int firstRow, int parentId, int? parentConfigId, string parentCode, string? description)
        { FirstRow = firstRow; ParentId = parentId; ParentConfigId = parentConfigId; ParentCode = parentCode; Description = description; }
        public int FirstRow { get; }
        public int ParentId { get; }
        public int? ParentConfigId { get; }
        public string ParentCode { get; }
        public string? Description { get; }
        public List<SaveBOMLineRequest> Lines { get; } = new();
    }
}

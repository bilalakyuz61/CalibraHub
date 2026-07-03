using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Sayım (envanter sayımı) içe-aktarım handler'ı. Başlık+kalem yapısı: her satır bir
/// "sayılan stok"tur; satırlar (Lokasyon + Tarih)'e göre gruplanıp her grup için tek
/// INVENTORY_COUNT belgesi (<see cref="SaveStockDocRequest"/>) oluşturulur. Stok/Lokasyon
/// kodları Id'ye çözülür; birim stok kartının varsayılan birimidir. Yazma <c>IStockDocRepository.SaveAsync</c>.
/// </summary>
public sealed class InventoryCountImportHandler : IImportTargetHandler
{
    private const int PreviewDetailLimit = 500;
    private readonly IStockDocRepository _stockDoc;
    private readonly ILogisticsConfigurationRepository _configRepo;

    public InventoryCountImportHandler(IStockDocRepository stockDoc, ILogisticsConfigurationRepository configRepo)
    { _stockDoc = stockDoc; _configRepo = configRepo; }

    public string Entity => "INVENTORY_COUNT";
    public string Label => "Sayım (Envanter)";

    public IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("LocationCode", "Lokasyon Kodu",  "string",  true,  false, "Sayımın yapıldığı depo/raf kodu (zorunlu) — her lokasyon ayrı sayım belgesi"),
        new ImportTargetFieldDto("ItemCode",     "Stok Kodu",      "string",  true,  false, "Sayılan stok kartının kodu (zorunlu)"),
        new ImportTargetFieldDto("Quantity",     "Sayılan Miktar", "decimal", true,  false, "Fiziksel sayım miktarı (zorunlu, >0)"),
        new ImportTargetFieldDto("Combination",  "Kombinasyon",    "string",  false, false, "Stok kombinasyon kodu (opsiyonel)"),
        new ImportTargetFieldDto("LotNo",        "Lot No",         "string",  false, false, "Lot numarası — lot-takipli stoklarda ZORUNLU (tek değer)"),
        new ImportTargetFieldDto("SerialNo",     "Seri No",        "string",  false, false, "Seri no(lar) — seri-takipli stoklarda ZORUNLU; miktar kadar, virgül/noktalı virgül ile ayır"),
        new ImportTargetFieldDto("CountDate",    "Sayım Tarihi",   "date",    false, false, "gg.aa.yyyy (boşsa bugün) — belge tarihi"),
        new ImportTargetFieldDto("Notes",        "Açıklama",       "string",  false, false, "Satır notu (opsiyonel)"),
    };

    public async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        var items = (await _configRepo.GetItemsAsync(ct)).ToList();
        var locations = (await _configRepo.GetLocationsAsync(ct)).ToList();
        Item? FindItem(string c) => items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), c.Trim(), StringComparison.OrdinalIgnoreCase));
        bool HasLoc(string c) => locations.Any(l =>
            string.Equals(l.LocationCode?.Trim(), c.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.LocationName?.Trim(), c.Trim(), StringComparison.OrdinalIgnoreCase));
        var (keys, labels) = DisplayCols(set.MappedKeys);

        int total = 0, valid = 0, error = 0;
        var detail = new List<ImportPreviewRowDto>();
        var docs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            total++;
            var errs = Validate(row, FindItem, HasLoc, out var loc, out var dateKey);
            if (errs.Count > 0) error++;
            else { valid++; docs.Add((loc ?? "") + "|" + dateKey); }
            if (detail.Count < PreviewDetailLimit)
            {
                var cells = keys.Select(k => new ImportPreviewCellDto(k, row.TryGetValue(k, out var v) ? v : null)).ToList();
                detail.Add(new ImportPreviewRowDto(total, errs.Count > 0 ? "error" : "insert", cells, errs));
            }
        }
        // insertCount = oluşturulacak sayım belgesi (lokasyon+tarih) sayısı
        return new ImportPreviewResultDto(true, null, total, valid, error, docs.Count, 0, keys, labels, detail);
    }

    public async Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct)
    {
        var items = (await _configRepo.GetItemsAsync(ct)).ToList();
        var locations = (await _configRepo.GetLocationsAsync(ct)).ToList();
        Item? FindItem(string c) => items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), c.Trim(), StringComparison.OrdinalIgnoreCase));
        Location? FindLoc(string c) =>
            locations.FirstOrDefault(l => string.Equals(l.LocationCode?.Trim(), c.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? locations.FirstOrDefault(l => string.Equals(l.LocationName?.Trim(), c.Trim(), StringComparison.OrdinalIgnoreCase));

        var results = new List<ImportCommitRowDto>();
        var groups = new Dictionary<string, CountGroup>(StringComparer.OrdinalIgnoreCase);
        int failed = 0, rowNo = 0;

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;
            var locCode = ImportParse.Get(row, "LocationCode")?.Trim();
            var itemCode = ImportParse.Get(row, "ItemCode")?.Trim();
            var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"));
            var date = ImportParse.ParseDate(ImportParse.Get(row, "CountDate")) ?? DateTime.Today;
            var notes = ImportParse.Get(row, "Notes");

            if (string.IsNullOrWhiteSpace(locCode)) { results.Add(Fail(rowNo, "Lokasyon kodu boş.")); failed++; continue; }
            if (string.IsNullOrWhiteSpace(itemCode)) { results.Add(Fail(rowNo, "Stok kodu boş.")); failed++; continue; }
            if (qty is null || qty <= 0) { results.Add(Fail(rowNo, "Sayılan miktar geçersiz (>0 olmalı).")); failed++; continue; }
            var loc = FindLoc(locCode); if (loc is null) { results.Add(Fail(rowNo, $"Lokasyon bulunamadı: '{locCode}'")); failed++; continue; }
            var item = FindItem(itemCode); if (item is null) { results.Add(Fail(rowNo, $"Stok bulunamadı: '{itemCode}'")); failed++; continue; }

            var lotNo = ImportParse.Get(row, "LotNo")?.Trim();
            var serialRaw = ImportParse.Get(row, "SerialNo");
            var combo = ImportParse.Get(row, "Combination")?.Trim();

            // İzlenebilirlik (Lot/Seri) + kombinasyon zorunlulukları (preview ile birebir aynı kurallar)
            var tErrs = TrackingErrors(item, qty.Value, lotNo, serialRaw, combo);
            if (tErrs.Count > 0) { results.Add(Fail(rowNo, string.Join(" ", tErrs))); failed++; continue; }

            int? configId = null;
            if (!string.IsNullOrWhiteSpace(combo))
            {
                configId = await ResolveConfigIdAsync(itemCode, combo, ct);
                if (configId is null) { results.Add(Fail(rowNo, $"Kombinasyon bulunamadı: '{combo}' ({itemCode})")); failed++; continue; }
            }

            var key = loc.Id + "|" + date.ToString("yyyyMMdd");
            if (!groups.TryGetValue(key, out var g))
            {
                g = new CountGroup(rowNo, loc.Id, loc.LocationCode ?? locCode, date);
                groups[key] = g;
            }

            // InventoryCountLine modeli lot/seri kolonu taşımaz (manuel sayımla birebir: ItemId + ConfigId +
            // CountedQty + Notes). İzlenebilir stoklarda lot/seri yukarıda VALIDATE edilir; commit'te kaybolmaması
            // için satır notuna işlenir. Seri-takiplide de TEK satır — CountedQty = sayılan miktar (seri adedine eşit).
            g.Lines.Add(new SaveStockDocLineRequest(
                Id: null, ItemId: item.Id, MaterialCode: item.Code, MaterialName: item.Name,
                UnitId: item.UnitId, Qty: qty.Value, CombinationId: configId,
                Notes: ComposeCountNotes(item, notes, lotNo, serialRaw),
                FromLocationId: null, ToLocationId: null, UnitCost: null, LotNo: null));
        }

        int inserted = 0;
        foreach (var g in groups.Values)
        {
            try
            {
                var req = new SaveStockDocRequest(
                    Id: null, DocType: "INVENTORY_COUNT", DocNo: null, DocDate: g.Date,
                    FromLocationId: g.LocationId, ToLocationId: null, RefNo: null, Notes: null,
                    Lines: g.Lines, ArgeProjectId: null);
                var (docId, _) = await _stockDoc.SaveAsync(req, userId, ct);
                inserted++;
                results.Add(new ImportCommitRowDto(g.FirstRow, true, "insert", null, docId));
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new ImportCommitRowDto(g.FirstRow, false, "insert", $"Sayım '{g.LocationCode}': {ex.Message}", null));
            }
        }

        results = results.OrderBy(r => r.RowNumber).ToList();
        return new ImportCommitResultDto(true, null, inserted, 0, failed, results);
    }

    private static List<string> Validate(IReadOnlyDictionary<string, string?> row, Func<string, Item?> findItem, Func<string, bool> hasLoc, out string? loc, out string dateKey)
    {
        var errs = new List<string>();
        loc = ImportParse.Get(row, "LocationCode")?.Trim();
        var itemCode = ImportParse.Get(row, "ItemCode")?.Trim();
        var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"));
        var date = ImportParse.ParseDate(ImportParse.Get(row, "CountDate")) ?? DateTime.Today;
        dateKey = date.ToString("yyyyMMdd");
        if (string.IsNullOrWhiteSpace(loc)) errs.Add("Lokasyon kodu boş.");
        else if (!hasLoc(loc)) errs.Add($"Lokasyon bulunamadı: '{loc}'");
        Item? item = null;
        if (string.IsNullOrWhiteSpace(itemCode)) errs.Add("Stok kodu boş.");
        else { item = findItem(itemCode); if (item is null) errs.Add($"Stok bulunamadı: '{itemCode}'"); }
        if (qty is null || qty <= 0) errs.Add("Sayılan miktar geçersiz (>0 olmalı).");
        if (item is not null && qty is > 0m)
            errs.AddRange(TrackingErrors(item, qty.Value,
                ImportParse.Get(row, "LotNo"), ImportParse.Get(row, "SerialNo"), ImportParse.Get(row, "Combination")));
        return errs;
    }

    /// <summary>İzlenebilirlik (Lot/Seri) + kombinasyon zorunluluk kuralları — preview+commit ortak.
    /// Kombinasyon BURADA Id'ye çözülmez; yalnız "zorunlu ama boş" denetimi yapılır.</summary>
    private static List<string> TrackingErrors(Item item, decimal qty, string? lotNo, string? serialNo, string? combo)
    {
        var errs = new List<string>();
        var tt = item.TrackingType ?? "None";
        if (string.Equals(tt, "Lot", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(lotNo))
            errs.Add($"Lot No zorunlu (stok lot-takipli): '{item.Code}'");
        if (string.Equals(tt, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            var serials = SplitSerials(serialNo);
            if (serials.Length == 0)
                errs.Add($"Seri No zorunlu (stok seri-takipli): '{item.Code}'");
            else
            {
                if (qty != Math.Truncate(qty) || serials.Length != (int)qty)
                    errs.Add($"Seri adedi miktara eşit olmalı — {serials.Length} seri / {qty:0.##} adet ('{item.Code}')");
                if (serials.Distinct(StringComparer.OrdinalIgnoreCase).Count() != serials.Length)
                    errs.Add($"Tekrarlanan seri no var: '{item.Code}'");
            }
        }
        if (item.Combinations && string.IsNullOrWhiteSpace(combo))
            errs.Add($"Kombinasyon zorunlu (stok kombinasyon-takipli): '{item.Code}'");
        return errs;
    }

    private static string[] SplitSerials(string? cell) =>
        string.IsNullOrWhiteSpace(cell)
            ? Array.Empty<string>()
            : cell.Split(new[] { ',', ';', '\n', '\r', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

    /// <summary>Lot/seri bilgisini satır notuna işler — InventoryCountLine lot/seri kolonu tutmadığı için
    /// (manuel sayım UI'ı da tutmaz) bilgi kaybolmasın diye Not'a yazılır. Lot-takipli → "Lot: X",
    /// seri-takipli → "Seri: a, b, c"; kullanıcı notu varsa " | " ile birleştirilir.</summary>
    private static string? ComposeCountNotes(Item item, string? notes, string? lotNo, string? serialRaw)
    {
        var tt = item.TrackingType ?? "None";
        string? trace = null;
        if (string.Equals(tt, "Lot", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(lotNo))
            trace = "Lot: " + lotNo.Trim();
        else if (string.Equals(tt, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            var serials = SplitSerials(serialRaw);
            if (serials.Length > 0) trace = "Seri: " + string.Join(", ", serials);
        }
        var baseNote = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (string.IsNullOrWhiteSpace(trace)) return baseNote;
        return baseNote is null ? trace : baseNote + " | " + trace;
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
        var combos = await _configRepo.GetCombinationsByMaterialCodeAsync(itemCode, ct);
        var match = combos.FirstOrDefault(c => string.Equals(c.Code?.Trim(), comboCode.Trim(), StringComparison.OrdinalIgnoreCase));
        var id = match?.ConfigId;
        _configCache[k] = id;
        return id;
    }

    private sealed class CountGroup
    {
        public CountGroup(int firstRow, int locationId, string locationCode, DateTime date)
        { FirstRow = firstRow; LocationId = locationId; LocationCode = locationCode; Date = date; }
        public int FirstRow { get; }
        public int LocationId { get; }
        public string LocationCode { get; }
        public DateTime Date { get; }
        public List<SaveStockDocLineRequest> Lines { get; } = new();
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Stok bakiyesi aktarımı (dış sistem = asıl kaynak, periyodik ayna).
///
/// Hareketleri DEĞİL bakiyeyi alır: her satır "bu stok şu lokasyonda şu kadar olmalı"
/// der. Fark hesabını CalibraHub'ın kendi sayım (Yansıt) mekanizması yapar —
/// lokasyon başına bir taslak sayım fişi yazılır, sonra:
///   1. <see cref="IInventoryCountRepository.ZeroUncountedAsync"/> — kaynakta gelmeyen
///      stoklar sıfırlanır (kaynak listesinde olmamak "bakiye yok" demektir).
///   2. <see cref="IInventoryCountRepository.ApplyAsync"/> — farklar MovementType=Adjust
///      olarak atomik yazılır.
///
/// Bu yaklaşım hareket aktarımının aksine MUTLAK ("şu kadar olsun") semantiktir:
/// aynı iş kaç kez çalışırsa çalışsın sonuç aynıdır, mükerrer hareket üretmez.
/// Bu nedenle iş tanımındaki anahtar alan burada teknik olarak gereksizdir
/// (idempotanlık zaten yapıdan gelir) — yine de zorunlu olduğu için Stok Kodu
/// anahtar seçilebilir.
///
/// DİKKAT — sıfırlama: kaynak sorgusu filtreliyse (örn. yalnız belirli bir stok grubu)
/// listede olmayan GERÇEK bakiyeler de sıfırlanır. Kaynak sorgu, aktarılan lokasyonun
/// TÜM stoklarını döndürmelidir.
/// </summary>
public sealed class StockBalanceImportHandler : IImportTargetHandler
{
    private const int PreviewDetailLimit = 500;

    private readonly IStockDocRepository _stockDoc;
    private readonly ILogisticsConfigurationRepository _configRepo;
    private readonly IInventoryCountRepository _counts;
    private readonly Dictionary<string, int?> _configCache = new(StringComparer.OrdinalIgnoreCase);

    public StockBalanceImportHandler(
        IStockDocRepository stockDoc,
        ILogisticsConfigurationRepository configRepo,
        IInventoryCountRepository counts)
    {
        _stockDoc = stockDoc;
        _configRepo = configRepo;
        _counts = counts;
    }

    public string Entity => "STOCK_BALANCE";
    public string Label => "Stok Bakiyesi";

    public IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("LocationCode", "Lokasyon Kodu", "string", true, true,
            "Bakiyenin ait olduğu depo/raf (zorunlu) — her lokasyon ayrı sayım fişi"),
        new ImportTargetFieldDto("ItemCode", "Stok Kodu", "string", true, true, "Zorunlu"),
        new ImportTargetFieldDto("Quantity", "Bakiye", "decimal", true, false,
            "Olması gereken miktar (0 olabilir)"),
        new ImportTargetFieldDto("Combination", "Kombinasyon", "string", false, false),
        new ImportTargetFieldDto("LotNo", "Lot No", "string", false, false, "Lot-takipli stoklarda zorunlu"),
        new ImportTargetFieldDto("SerialNo", "Seri No", "string", false, false,
            "Seri-takipli stoklarda zorunlu; virgül veya noktalı virgül ile ayır"),
        new ImportTargetFieldDto("ExpiryDate", "Son Kullanma Tarihi", "date", false, false),
        new ImportTargetFieldDto("BalanceDate", "Bakiye Tarihi", "date", false, false, "Boşsa bugün"),
        new ImportTargetFieldDto("Notes", "Açıklama", "string", false, false),
    };

    // ── Önizleme ─────────────────────────────────────────────────────────

    public async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        var ctx = await LoadContextAsync(ct);
        var (keys, labels) = DisplayCols(set.MappedKeys);

        int total = 0, valid = 0, error = 0;
        var detail = new List<ImportPreviewRowDto>();

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            total++;

            var errs = ValidateRow(row, ctx);
            if (errs.Count > 0) error++; else valid++;

            if (detail.Count < PreviewDetailLimit)
            {
                var cells = keys.Select(k => new ImportPreviewCellDto(k, row.TryGetValue(k, out var v) ? v : null)).ToList();
                detail.Add(new ImportPreviewRowDto(total, errs.Count > 0 ? "error" : "update", cells, errs));
            }
        }

        // Önizleme yalnız DOĞRULAR — fark hesabı Yansıt adımında sunucuda yapılır.
        return new ImportPreviewResultDto(true, null, total, valid, error, 0, valid, keys, labels, detail);
    }

    // ── Aktarım ──────────────────────────────────────────────────────────

    public async Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct)
    {
        var ctx = await LoadContextAsync(ct);
        var results = new List<ImportCommitRowDto>();
        var groups = new Dictionary<string, BalanceGroup>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;
        var rowNo = 0;

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;

            var errs = ValidateRow(row, ctx);
            if (errs.Count > 0) { results.Add(Fail(rowNo, string.Join(" ", errs))); failed++; continue; }

            var locId = ctx.FindLocationId(ImportParse.Get(row, "LocationCode"))!.Value;
            var itemCode = ImportParse.Get(row, "ItemCode")!.Trim();
            var item = ctx.FindItem(itemCode)!;
            var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"))!.Value;
            var date = ImportParse.ParseDate(ImportParse.Get(row, "BalanceDate")) ?? DateTime.Today;

            int? configId = null;
            var combo = ImportParse.Get(row, "Combination")?.Trim();
            if (!string.IsNullOrWhiteSpace(combo))
            {
                configId = await ResolveConfigIdAsync(itemCode, combo, ct);
                if (configId is null) { results.Add(Fail(rowNo, $"Kombinasyon bulunamadı: '{combo}' ({itemCode})")); failed++; continue; }
            }

            var groupKey = $"{locId}|{date:yyyyMMdd}";
            if (!groups.TryGetValue(groupKey, out var g))
            {
                g = new BalanceGroup(rowNo, locId, date);
                groups[groupKey] = g;
            }
            g.RowNumbers.Add(rowNo);

            // Aynı (stok + kombinasyon) için birden fazla kaynak satırı gelebilir
            // (lot/seri kırılımı) — tek sayım kalemine toplanır.
            var lineKey = $"{item.Id}|{configId}";
            if (!g.Lines.TryGetValue(lineKey, out var line))
            {
                line = new BalanceLine(item, configId);
                g.Lines[lineKey] = line;
            }

            line.TotalQty += qty;

            var lotNo = ImportParse.Get(row, "LotNo")?.Trim();
            var expiry = ImportParse.ParseDate(ImportParse.Get(row, "ExpiryDate"));
            var notes = ImportParse.Get(row, "Notes")?.Trim();

            if (IsSerial(item))
            {
                var serials = SplitSerials(ImportParse.Get(row, "SerialNo"));
                if (serials.Length == 1)
                    line.Serials.Add(new CountSerialBreakdownItem(serials[0], expiry, notes, qty));
                else
                    foreach (var s in serials)
                        line.Serials.Add(new CountSerialBreakdownItem(s, expiry, notes, 1m));
            }
            else if (IsLot(item) && !string.IsNullOrWhiteSpace(lotNo))
            {
                line.Lots.Add(new StockLotBreakdownItem(lotNo, qty, expiry, notes));
            }
            else if (line.Notes is null && !string.IsNullOrWhiteSpace(notes))
            {
                line.Notes = notes;
            }
        }

        var applied = 0;
        foreach (var g in groups.Values)
        {
            try
            {
                var lines = g.Lines.Values.Select(l => new SaveStockDocLineRequest(
                    Id: null,
                    ItemId: l.Item.Id,
                    MaterialCode: l.Item.Code,
                    MaterialName: l.Item.Name,
                    UnitId: l.Item.UnitId,
                    Qty: l.TotalQty,
                    CombinationId: l.ConfigId,
                    Notes: l.Notes,
                    FromLocationId: null,
                    ToLocationId: null,
                    UnitCost: null,
                    LotNo: null,
                    Serials: null,
                    LotBreakdown: l.Lots.Count > 0 ? l.Lots : null,
                    SerialBreakdown: l.Serials.Count > 0 ? l.Serials : null)).ToList();

                var req = new SaveStockDocRequest(
                    Id: null, DocType: "INVENTORY_COUNT", DocNo: null, DocDate: g.Date,
                    FromLocationId: g.LocationId, ToLocationId: null,
                    RefNo: null, Notes: "Bakiye aktarımı (otomatik)", Lines: lines, ArgeProjectId: null);

                var (docId, _) = await _stockDoc.SaveAsync(req, userId, ct);

                // Kaynak listesinde gelmeyen stoklar "bakiye yok" demektir → sıfırlanır.
                // Yansıt'tan ÖNCE çalışmalı: Applied sayım immutable olur.
                await _counts.ZeroUncountedAsync(docId, ct);

                // Farkları yaz (MovementType=Adjust).
                await _counts.ApplyAsync(docId, ct);

                applied++;
                foreach (var r in g.RowNumbers)
                    results.Add(new ImportCommitRowDto(r, true, "update", null, docId));
            }
            catch (Exception ex)
            {
                foreach (var r in g.RowNumbers)
                {
                    failed++;
                    results.Add(new ImportCommitRowDto(r, false, "error", $"Bakiye dengeleme başarısız: {ex.Message}", null));
                }
            }
        }

        results = results.OrderBy(r => r.RowNumber).ToList();
        // Bakiye dengeleme "güncelleme"dir — yeni kayıt üretmez.
        return new ImportCommitResultDto(true, null, 0, applied, failed, results);
    }

    // ── Doğrulama ────────────────────────────────────────────────────────

    private static List<string> ValidateRow(IReadOnlyDictionary<string, string?> row, Context ctx)
    {
        var errs = new List<string>();

        var locRaw = ImportParse.Get(row, "LocationCode");
        if (string.IsNullOrWhiteSpace(locRaw)) errs.Add("Lokasyon kodu boş.");
        else if (ctx.FindLocationId(locRaw) is null) errs.Add($"Lokasyon bulunamadı: '{locRaw}'");

        var itemCode = ImportParse.Get(row, "ItemCode")?.Trim();
        Item? item = null;
        if (string.IsNullOrWhiteSpace(itemCode)) errs.Add("Stok kodu boş.");
        else
        {
            item = ctx.FindItem(itemCode);
            if (item is null) errs.Add($"Stok bulunamadı: '{itemCode}'");
        }

        var qtyRaw = ImportParse.Get(row, "Quantity");
        var qty = ImportParse.ParseDecimal(qtyRaw);
        if (string.IsNullOrWhiteSpace(qtyRaw)) errs.Add("Bakiye boş.");
        else if (qty is null) errs.Add($"Bakiye sayı değil: '{qtyRaw}'.");
        else if (qty < 0) errs.Add("Bakiye negatif olamaz.");

        var dateRaw = ImportParse.Get(row, "BalanceDate");
        if (!string.IsNullOrWhiteSpace(dateRaw) && ImportParse.ParseDate(dateRaw) is null)
            errs.Add($"Bakiye tarihi okunamadı: '{dateRaw}'.");

        if (item is not null && qty is not null && qty >= 0)
        {
            var lotNo = ImportParse.Get(row, "LotNo");
            var serialRaw = ImportParse.Get(row, "SerialNo");
            var combo = ImportParse.Get(row, "Combination");

            // Bakiye 0 ise izlenebilirlik kırılımı aranmaz — "bu stok yok" demektir.
            if (qty > 0)
            {
                if (IsLot(item) && string.IsNullOrWhiteSpace(lotNo))
                    errs.Add($"Lot No zorunlu (stok lot-takipli): '{item.Code}'");

                if (IsSerial(item))
                {
                    var serials = SplitSerials(serialRaw);
                    if (serials.Length == 0)
                        errs.Add($"Seri No zorunlu (stok seri-takipli): '{item.Code}'");
                    else if (serials.Length > 1)
                    {
                        if (qty != Math.Truncate(qty.Value) || serials.Length != (int)qty.Value)
                            errs.Add($"Seri adedi bakiyeye eşit olmalı — {serials.Length} seri / {qty:0.##} ('{item.Code}')");
                        if (serials.Distinct(StringComparer.OrdinalIgnoreCase).Count() != serials.Length)
                            errs.Add($"Tekrarlanan seri no var: '{item.Code}'");
                    }
                }
            }

            if (item.Combinations && string.IsNullOrWhiteSpace(combo))
                errs.Add($"Kombinasyon zorunlu (stok kombinasyon-takipli): '{item.Code}'");
        }

        return errs;
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────

    private static bool IsLot(Item i) => string.Equals(i.TrackingType, "Lot", StringComparison.OrdinalIgnoreCase);
    private static bool IsSerial(Item i) => string.Equals(i.TrackingType, "Serial", StringComparison.OrdinalIgnoreCase);

    private static string[] SplitSerials(string? cell) =>
        string.IsNullOrWhiteSpace(cell)
            ? Array.Empty<string>()
            : cell.Split(new[] { ',', ';', '\n', '\r', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

    private async Task<Context> LoadContextAsync(CancellationToken ct)
    {
        var items = (await _configRepo.GetItemsAsync(ct)).ToList();
        var locations = (await _configRepo.GetLocationsAsync(ct)).ToList();
        return new Context(items, locations);
    }

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

    private (IReadOnlyList<string> Keys, IReadOnlyList<string> Labels) DisplayCols(IReadOnlyList<string> mapped)
    {
        var keys = new List<string>();
        var labels = new List<string>();
        foreach (var f in GetFields())
            if (mapped.Any(k => string.Equals(k, f.Key, StringComparison.OrdinalIgnoreCase)))
            { keys.Add(f.Key); labels.Add(f.Label); }
        return (keys, labels);
    }

    private static ImportCommitRowDto Fail(int rowNo, string err) => new(rowNo, false, "error", err, null);

    private sealed class Context
    {
        private readonly List<Item> _items;
        private readonly List<Location> _locations;

        public Context(List<Item> items, List<Location> locations)
        {
            _items = items;
            _locations = locations;
        }

        public Item? FindItem(string? code)
            => string.IsNullOrWhiteSpace(code)
                ? null
                : _items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));

        public int? FindLocationId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var v = raw.Trim();
            var hit = _locations.FirstOrDefault(l => string.Equals(l.LocationCode?.Trim(), v, StringComparison.OrdinalIgnoreCase))
                   ?? _locations.FirstOrDefault(l => string.Equals(l.LocationName?.Trim(), v, StringComparison.OrdinalIgnoreCase));
            return hit?.Id;
        }
    }

    private sealed class BalanceLine
    {
        public BalanceLine(Item item, int? configId) { Item = item; ConfigId = configId; }
        public Item Item { get; }
        public int? ConfigId { get; }
        public decimal TotalQty { get; set; }
        public string? Notes { get; set; }
        public List<StockLotBreakdownItem> Lots { get; } = new();
        public List<CountSerialBreakdownItem> Serials { get; } = new();
    }

    private sealed class BalanceGroup
    {
        public BalanceGroup(int firstRow, int locationId, DateTime date)
        { FirstRow = firstRow; LocationId = locationId; Date = date; }
        public int FirstRow { get; }
        public int LocationId { get; }
        public DateTime Date { get; }
        public List<int> RowNumbers { get; } = new();
        public Dictionary<string, BalanceLine> Lines { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

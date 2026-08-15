using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Stok hareketi (ambar giriş / çıkış / transfer) içe-aktarım handler'ı.
/// Başlık+kalem yapısı: her satır bir harekettir; satırlar
/// (Hareket Tipi + Referans No + Lokasyonlar + Tarih) anahtarına göre gruplanıp her grup
/// için tek ambar belgesi (<see cref="SaveStockDocRequest"/>) yazılır.
///
/// MÜKERRER KORUMASI: bu aktarım gerçek stok bakiyesini değiştirir ve cron ile tekrar
/// çalışabilir. Aynı Referans No ile daha önce belge yazılmışsa grup ATLANIR ve satır
/// hatası olarak raporlanır — sessizce ikinci kez stok hareketi üretmek yerine.
/// Bu nedenle Referans No zorunludur.
/// </summary>
public sealed class StockMovementImportHandler : IImportTargetHandler
{
    private const int PreviewDetailLimit = 500;

    private const string DocTypeIn = "depo_giris";
    private const string DocTypeOut = "depo_cikis";
    private const string DocTypeTransfer = "depo_transfer";

    private static readonly string[] AllDocTypes = { DocTypeIn, DocTypeOut, DocTypeTransfer };

    private readonly IStockDocRepository _stockDoc;
    private readonly ILogisticsConfigurationRepository _configRepo;
    private readonly Dictionary<string, int?> _configCache = new(StringComparer.OrdinalIgnoreCase);

    public StockMovementImportHandler(IStockDocRepository stockDoc, ILogisticsConfigurationRepository configRepo)
    {
        _stockDoc = stockDoc;
        _configRepo = configRepo;
    }

    public string Entity => "STOCK_MOVEMENT";
    public string Label => "Stok Hareketi";

    public IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("RefNo", "Referans No", "string", true, true,
            "Kaynak belge referansı (zorunlu) — aynı referans ikinci kez aktarılmaz"),
        new ImportTargetFieldDto("MovementType", "Hareket Tipi", "string", true, false,
            "Giriş / Çıkış / Transfer", new[] { "Giriş", "Çıkış", "Transfer" }),
        new ImportTargetFieldDto("ItemCode", "Stok Kodu", "string", true, false, "Stok kartının kodu (zorunlu)"),
        new ImportTargetFieldDto("Quantity", "Miktar", "decimal", true, false, "Zorunlu, sıfırdan büyük"),
        new ImportTargetFieldDto("FromLocationCode", "Çıkış Lokasyonu", "string", false, false,
            "Çıkış ve Transfer için zorunlu"),
        new ImportTargetFieldDto("ToLocationCode", "Giriş Lokasyonu", "string", false, false,
            "Giriş ve Transfer için zorunlu"),
        new ImportTargetFieldDto("DocDate", "Belge Tarihi", "date", false, false, "Boşsa bugün"),
        new ImportTargetFieldDto("Combination", "Kombinasyon", "string", false, false),
        new ImportTargetFieldDto("LotNo", "Lot No", "string", false, false, "Lot-takipli stoklarda zorunlu"),
        new ImportTargetFieldDto("SerialNo", "Seri No", "string", false, false,
            "Seri-takipli stoklarda miktar kadar; virgül veya noktalı virgül ile ayır"),
        new ImportTargetFieldDto("UnitCost", "Birim Maliyet", "decimal", false, false),
        new ImportTargetFieldDto("Notes", "Açıklama", "string", false, false),
    };

    // ── Önizleme ─────────────────────────────────────────────────────────

    public async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        var ctx = await LoadContextAsync(ct);
        var (keys, labels) = DisplayCols(set.MappedKeys);

        int total = 0, valid = 0, error = 0;
        var detail = new List<ImportPreviewRowDto>();
        var seenRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            total++;

            var errs = ValidateRow(row, ctx, out var refNo);

            // Önizlemede de mükerrer referansı göster — kullanıcı aktarmadan önce bilsin.
            if (errs.Count == 0 && !string.IsNullOrWhiteSpace(refNo)
                && ctx.ExistingRefs.Contains(refNo!) && seenRefs.Add(refNo!))
            {
                errs = new List<string> { $"Bu referans zaten aktarılmış: '{refNo}'." };
            }

            if (errs.Count > 0) error++; else valid++;

            if (detail.Count < PreviewDetailLimit)
            {
                var cells = keys.Select(k => new ImportPreviewCellDto(k, row.TryGetValue(k, out var v) ? v : null)).ToList();
                detail.Add(new ImportPreviewRowDto(total, errs.Count > 0 ? "error" : "insert", cells, errs));
            }
        }

        // Stok hareketi her zaman yeni belge yazar — güncelleme yoktur.
        return new ImportPreviewResultDto(true, null, total, valid, error, valid, 0, keys, labels, detail);
    }

    // ── Aktarım ──────────────────────────────────────────────────────────

    public async Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct)
    {
        var ctx = await LoadContextAsync(ct);
        var results = new List<ImportCommitRowDto>();
        var groups = new Dictionary<string, MovementGroup>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;
        var rowNo = 0;

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;

            var errs = ValidateRow(row, ctx, out var refNo);
            if (errs.Count > 0) { results.Add(Fail(rowNo, string.Join(" ", errs))); failed++; continue; }

            if (ctx.ExistingRefs.Contains(refNo!))
            {
                results.Add(Fail(rowNo, $"Bu referans zaten aktarılmış: '{refNo}' — mükerrer stok hareketi yazılmadı."));
                failed++;
                continue;
            }

            var movement = ResolveMovementType(ImportParse.Get(row, "MovementType"))!.Value;
            var itemCode = ImportParse.Get(row, "ItemCode")!.Trim();
            var item = ctx.FindItem(itemCode)!;
            var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"))!.Value;
            var date = ImportParse.ParseDate(ImportParse.Get(row, "DocDate")) ?? DateTime.Today;

            var fromId = ctx.FindLocationId(ImportParse.Get(row, "FromLocationCode"));
            var toId = ctx.FindLocationId(ImportParse.Get(row, "ToLocationCode"));

            int? configId = null;
            var combo = ImportParse.Get(row, "Combination")?.Trim();
            if (!string.IsNullOrWhiteSpace(combo))
            {
                configId = await ResolveConfigIdAsync(itemCode, combo, ct);
                if (configId is null) { results.Add(Fail(rowNo, $"Kombinasyon bulunamadı: '{combo}' ({itemCode})")); failed++; continue; }
            }

            var key = $"{movement.DocType}|{refNo}|{fromId}|{toId}|{date:yyyyMMdd}";
            if (!groups.TryGetValue(key, out var g))
            {
                g = new MovementGroup(rowNo, movement.DocType, refNo!, fromId, toId, date);
                groups[key] = g;
            }
            g.RowNumbers.Add(rowNo);

            var serials = SplitSerials(ImportParse.Get(row, "SerialNo"));
            g.Lines.Add(new SaveStockDocLineRequest(
                Id: null,
                ItemId: item.Id,
                MaterialCode: item.Code,
                MaterialName: item.Name,
                UnitId: item.UnitId,
                Qty: qty,
                CombinationId: configId,
                Notes: ImportParse.Get(row, "Notes")?.Trim(),
                FromLocationId: null,
                ToLocationId: null,
                UnitCost: ImportParse.ParseDecimal(ImportParse.Get(row, "UnitCost")),
                LotNo: string.IsNullOrWhiteSpace(ImportParse.Get(row, "LotNo")) ? null : ImportParse.Get(row, "LotNo")!.Trim(),
                Serials: serials.Length > 0 ? serials : null));
        }

        var inserted = 0;
        foreach (var g in groups.Values)
        {
            try
            {
                var req = new SaveStockDocRequest(
                    Id: null, DocType: g.DocType, DocNo: null, DocDate: g.Date,
                    FromLocationId: g.FromLocationId, ToLocationId: g.ToLocationId,
                    RefNo: g.RefNo, Notes: null, Lines: g.Lines, ArgeProjectId: null);

                var (docId, docNo) = await _stockDoc.SaveAsync(req, userId, ct);
                inserted++;
                ctx.ExistingRefs.Add(g.RefNo);   // aynı çalıştırmada tekrar yazılmasın
                results.Add(new ImportCommitRowDto(g.FirstRow, true, "insert", null, docId));

                // Gruptaki diğer satırlar da başarıyla yazıldı — kullanıcı hangi satırın
                // ne olduğunu görebilsin diye tek tek raporlanır.
                foreach (var r in g.RowNumbers.Where(r => r != g.FirstRow))
                    results.Add(new ImportCommitRowDto(r, true, "insert", null, docId));
            }
            catch (Exception ex)
            {
                // Belge yazılamadı → gruptaki TÜM satırlar başarısızdır (yarım belge yok).
                foreach (var r in g.RowNumbers)
                {
                    failed++;
                    results.Add(new ImportCommitRowDto(r, false, "error", $"Belge '{g.RefNo}': {ex.Message}", null));
                }
            }
        }

        results = results.OrderBy(r => r.RowNumber).ToList();
        return new ImportCommitResultDto(true, null, inserted, 0, failed, results);
    }

    // ── Doğrulama ────────────────────────────────────────────────────────

    private static List<string> ValidateRow(IReadOnlyDictionary<string, string?> row, Context ctx, out string? refNo)
    {
        var errs = new List<string>();

        refNo = ImportParse.Get(row, "RefNo")?.Trim();
        if (string.IsNullOrWhiteSpace(refNo)) errs.Add("Referans no boş.");

        var movement = ResolveMovementType(ImportParse.Get(row, "MovementType"));
        if (movement is null) errs.Add($"Hareket tipi tanınmadı: '{ImportParse.Get(row, "MovementType")}' (Giriş / Çıkış / Transfer).");

        var itemCode = ImportParse.Get(row, "ItemCode")?.Trim();
        Item? item = null;
        if (string.IsNullOrWhiteSpace(itemCode)) errs.Add("Stok kodu boş.");
        else
        {
            item = ctx.FindItem(itemCode);
            if (item is null) errs.Add($"Stok bulunamadı: '{itemCode}'");
        }

        var qty = ImportParse.ParseDecimal(ImportParse.Get(row, "Quantity"));
        if (qty is null || qty <= 0) errs.Add("Miktar geçersiz (>0 olmalı).");

        var date = ImportParse.Get(row, "DocDate");
        if (!string.IsNullOrWhiteSpace(date) && ImportParse.ParseDate(date) is null)
            errs.Add($"Belge tarihi okunamadı: '{date}'.");

        // Lokasyon zorunlulukları harekete göre değişir.
        if (movement is not null)
        {
            var fromRaw = ImportParse.Get(row, "FromLocationCode");
            var toRaw = ImportParse.Get(row, "ToLocationCode");

            if (movement.Value.NeedsFrom)
            {
                if (string.IsNullOrWhiteSpace(fromRaw)) errs.Add("Çıkış lokasyonu boş.");
                else if (ctx.FindLocationId(fromRaw) is null) errs.Add($"Çıkış lokasyonu bulunamadı: '{fromRaw}'");
            }
            if (movement.Value.NeedsTo)
            {
                if (string.IsNullOrWhiteSpace(toRaw)) errs.Add("Giriş lokasyonu boş.");
                else if (ctx.FindLocationId(toRaw) is null) errs.Add($"Giriş lokasyonu bulunamadı: '{toRaw}'");
            }
        }

        if (item is not null && qty is > 0m && movement is not null)
            errs.AddRange(TrackingErrors(item, qty.Value,
                ImportParse.Get(row, "LotNo"), ImportParse.Get(row, "SerialNo"),
                ImportParse.Get(row, "Combination"), movement.Value.DocType));

        return errs;
    }

    /// <summary>
    /// İzlenebilirlik kuralları. Girişte seri boş bırakılabilir (AutoSerial sunucuda üretebilir);
    /// çıkış ve transferde hangi serinin hareket ettiği belirsiz kalamaz — zorunludur.
    /// </summary>
    private static List<string> TrackingErrors(
        Item item, decimal qty, string? lotNo, string? serialNo, string? combo, string docType)
    {
        var errs = new List<string>();
        var tt = item.TrackingType ?? "None";

        if (string.Equals(tt, "Lot", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(lotNo))
            errs.Add($"Lot No zorunlu (stok lot-takipli): '{item.Code}'");

        if (string.Equals(tt, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            var serials = SplitSerials(serialNo);
            if (serials.Length == 0)
            {
                if (docType != DocTypeIn)
                    errs.Add($"Seri No zorunlu (stok seri-takipli): '{item.Code}'");
            }
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

    // ── Yardımcılar ──────────────────────────────────────────────────────

    private static (string DocType, bool NeedsFrom, bool NeedsTo)? ResolveMovementType(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return null;
        if (v is "giriş" or "giris" or "in" or "input" or "gir") return (DocTypeIn, false, true);
        if (v is "çıkış" or "cikis" or "out" or "output" or "cik") return (DocTypeOut, true, false);
        if (v is "transfer" or "aktarım" or "aktarim" or "move") return (DocTypeTransfer, true, true);
        return null;
    }

    private static string[] SplitSerials(string? cell) =>
        string.IsNullOrWhiteSpace(cell)
            ? Array.Empty<string>()
            : cell.Split(new[] { ',', ';', '\n', '\r', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

    private async Task<Context> LoadContextAsync(CancellationToken ct)
    {
        var items = (await _configRepo.GetItemsAsync(ct)).ToList();
        var locations = (await _configRepo.GetLocationsAsync(ct)).ToList();
        var docs = await _stockDoc.GetByTypesAsync(AllDocTypes, ct);

        var refs = new HashSet<string>(
            docs.Where(d => !string.IsNullOrWhiteSpace(d.RefNo)).Select(d => d.RefNo!.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return new Context(items, locations, refs);
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

        public Context(List<Item> items, List<Location> locations, HashSet<string> existingRefs)
        {
            _items = items;
            _locations = locations;
            ExistingRefs = existingRefs;
        }

        public HashSet<string> ExistingRefs { get; }

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

    private sealed class MovementGroup
    {
        public MovementGroup(int firstRow, string docType, string refNo, int? fromId, int? toId, DateTime date)
        {
            FirstRow = firstRow;
            DocType = docType;
            RefNo = refNo;
            FromLocationId = fromId;
            ToLocationId = toId;
            Date = date;
        }

        public int FirstRow { get; }
        public string DocType { get; }
        public string RefNo { get; }
        public int? FromLocationId { get; }
        public int? ToLocationId { get; }
        public DateTime Date { get; }
        public List<int> RowNumbers { get; } = new();
        public List<SaveStockDocLineRequest> Lines { get; } = new();
    }
}

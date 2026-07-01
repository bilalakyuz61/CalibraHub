using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Rota (Routing) içe-aktarım handler'ı. Her Excel satırı bir operasyon adımıdır;
/// satırlar "Rota Adı + Stok Kodu" ikilisine göre gruplanır ve her grup için
/// tek <see cref="IRoutingService.SaveAsync"/> çağrısı yapılır.
/// Operasyon kod → Id çözümü <see cref="IOperationRepository"/> üzerinden yapılır.
/// Makine kod → Id çözümü <see cref="ILogisticsConfigurationRepository"/> üzerinden yapılır.
/// </summary>
public sealed class RoutingImportHandler : IImportTargetHandler
{
    private const int PreviewDetailLimit = 500;
    private readonly IRoutingService _routing;
    private readonly IOperationRepository _opRepo;
    private readonly ILogisticsConfigurationRepository _itemRepo;
    private List<Operation>? _operations;
    private List<Machine>? _machines;
    private List<Item>? _items;

    public RoutingImportHandler(IRoutingService routing, IOperationRepository opRepo,
        ILogisticsConfigurationRepository itemRepo)
    { _routing = routing; _opRepo = opRepo; _itemRepo = itemRepo; }

    public string Entity => "ROUTING";
    public string Label => "Rota (Routing)";
    public Task PreloadAsync(CancellationToken ct) => Task.CompletedTask;

    public IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("RoutingName",   "Rota Adı",         "string",  true,  false, "Rotanın adı — aynı adlı satırlar tek rotada gruplanır (zorunlu)"),
        new ImportTargetFieldDto("ItemCode",      "Stok Kodu",        "string",  false, false, "Bu rotanın bağlı olduğu stok kodu (opsiyonel)"),
        new ImportTargetFieldDto("Sequence",      "Sıra No",          "decimal", true,  false, "Adımın sırası — aynı rota içinde benzersiz tamsayı (zorunlu)"),
        new ImportTargetFieldDto("OperationCode", "Operasyon Kodu",   "string",  true,  false, "Mevcut operasyon kodu (zorunlu)"),
        new ImportTargetFieldDto("MachineCode",   "Makine Kodu",      "string",  false, false, "Mevcut makine kodu (opsiyonel; verilirse eşleşmeli)"),
        new ImportTargetFieldDto("Duration",      "Süre",             "decimal", false, false, "Operasyon süresi (opsiyonel; operasyon varsayılanını ezer)"),
        new ImportTargetFieldDto("DurationUnit",  "Süre Birimi",      "string",  false, false, "Dk / Saat (boşsa Dk)", new[] { "Dk", "Saat" }),
        new ImportTargetFieldDto("Notes",         "Notlar",           "string",  false, false, "Operasyon adımına ait notlar (opsiyonel)"),
    };

    public async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        _operations ??= (await _opRepo.ListAsync(false, ct)).ToList();
        _machines   ??= (await _itemRepo.GetMachinesAsync(ct)).ToList();
        _items      ??= (await _itemRepo.GetItemsAsync(ct)).ToList();
        var (keys, labels) = DisplayCols(set.MappedKeys);

        int total = 0, valid = 0, error = 0;
        var detail = new List<ImportPreviewRowDto>();
        var routingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            total++;
            var errs = ValidateRow(row);
            if (errs.Count > 0) error++;
            else
            {
                valid++;
                var name = ImportParse.Get(row, "RoutingName")?.Trim() ?? "";
                var item = ImportParse.Get(row, "ItemCode")?.Trim() ?? "";
                routingKeys.Add(name + "||" + item);
            }
            if (detail.Count < PreviewDetailLimit)
            {
                var cells = keys.Select(k => new ImportPreviewCellDto(k, row.TryGetValue(k, out var v) ? v : null)).ToList();
                detail.Add(new ImportPreviewRowDto(total, errs.Count > 0 ? "error" : "insert", cells, errs));
            }
        }
        return new ImportPreviewResultDto(true, null, total, valid, error, routingKeys.Count, 0, keys, labels, detail);
    }

    public async Task<ImportCommitResultDto> CommitAsync(ImportRowSet set, int? userId, CancellationToken ct)
    {
        _operations ??= (await _opRepo.ListAsync(false, ct)).ToList();
        _machines   ??= (await _itemRepo.GetMachinesAsync(ct)).ToList();
        _items      ??= (await _itemRepo.GetItemsAsync(ct)).ToList();

        var results = new List<ImportCommitRowDto>();
        var groups  = new Dictionary<string, RoutingGroup>(StringComparer.OrdinalIgnoreCase);
        int failed = 0, rowNo = 0;

        foreach (var row in set.Rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNo++;
            var name     = ImportParse.Get(row, "RoutingName")?.Trim();
            var itemCode = ImportParse.Get(row, "ItemCode")?.Trim();
            var seqRaw   = ImportParse.ParseDecimal(ImportParse.Get(row, "Sequence"));
            var opCode   = ImportParse.Get(row, "OperationCode")?.Trim();
            var machCode = ImportParse.Get(row, "MachineCode")?.Trim();
            var dur      = ImportParse.ParseDecimal(ImportParse.Get(row, "Duration"));
            var durUnit  = ResolveDurationUnit(ImportParse.Get(row, "DurationUnit"));
            var notes    = ImportParse.Get(row, "Notes");

            if (string.IsNullOrWhiteSpace(name)) { results.Add(Fail(rowNo, "Rota adı boş.")); failed++; continue; }
            if (seqRaw is null or <= 0) { results.Add(Fail(rowNo, "Sıra No geçersiz (>0 tam sayı olmalı).")); failed++; continue; }
            if (string.IsNullOrWhiteSpace(opCode)) { results.Add(Fail(rowNo, "Operasyon kodu boş.")); failed++; continue; }

            // Operasyon kodu → Id
            var op = _operations.FirstOrDefault(o => string.Equals(o.Code?.Trim(), opCode, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(o.Name?.Trim(), opCode, StringComparison.OrdinalIgnoreCase));
            if (op is null) { results.Add(Fail(rowNo, $"Operasyon bulunamadı: '{opCode}'")); failed++; continue; }

            // Makine kodu → Id (opsiyonel)
            int? machineId = null;
            if (!string.IsNullOrWhiteSpace(machCode))
            {
                var mach = _machines.FirstOrDefault(m => string.Equals(m.Code?.Trim(), machCode, StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(m.Name?.Trim(), machCode, StringComparison.OrdinalIgnoreCase));
                if (mach is null) { results.Add(Fail(rowNo, $"Makine bulunamadı: '{machCode}'")); failed++; continue; }
                machineId = mach.Id;
            }

            // Stok kodu → Id (opsiyonel)
            int? itemId = null;
            if (!string.IsNullOrWhiteSpace(itemCode))
            {
                var item = _items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), itemCode, StringComparison.OrdinalIgnoreCase));
                if (item is null) { results.Add(Fail(rowNo, $"Stok bulunamadı: '{itemCode}'")); failed++; continue; }
                itemId = item.Id;
            }

            var groupKey = name + "||" + (itemCode ?? "");
            if (!groups.TryGetValue(groupKey, out var g))
            {
                g = new RoutingGroup(rowNo, name, itemId, itemCode);
                groups[groupKey] = g;
            }
            g.Lines.Add(new RoutingLine((int)seqRaw.Value, op.Id, machineId, dur, durUnit, notes));
        }

        int inserted = 0;
        foreach (var g in groups.Values)
        {
            try
            {
                var ops = g.Lines
                    .OrderBy(l => l.Sequence)
                    .Select(l => new SaveRoutingOperationLine(l.Sequence, l.OperationId, l.MachineId, l.Duration, l.DurationUnit, l.Notes))
                    .ToList();
                var code = g.Name.Length > 50 ? g.Name[..50] : g.Name;
                var req = new SaveRoutingRequest(0, code, g.Name, g.ItemId, null, null, true, ops);
                await _routing.SaveAsync(req, ct);
                inserted++;
                results.Add(new ImportCommitRowDto(g.FirstRow, true, "insert", null, null));
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new ImportCommitRowDto(g.FirstRow, false, "insert", $"Rota '{g.Name}': {ex.Message}", null));
            }
        }

        results = results.OrderBy(r => r.RowNumber).ToList();
        return new ImportCommitResultDto(true, null, inserted, 0, failed, results);
    }

    private IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> row)
    {
        var errs = new List<string>();
        var name = ImportParse.Get(row, "RoutingName")?.Trim();
        var seq  = ImportParse.ParseDecimal(ImportParse.Get(row, "Sequence"));
        var op   = ImportParse.Get(row, "OperationCode")?.Trim();
        var mach = ImportParse.Get(row, "MachineCode")?.Trim();
        var item = ImportParse.Get(row, "ItemCode")?.Trim();

        if (string.IsNullOrWhiteSpace(name)) errs.Add("Rota adı boş.");
        if (seq is null or <= 0) errs.Add("Sıra No geçersiz (>0 tam sayı olmalı).");
        if (string.IsNullOrWhiteSpace(op)) errs.Add("Operasyon kodu boş.");
        else if (_operations is not null && !_operations.Any(o => string.Equals(o.Code?.Trim(), op, StringComparison.OrdinalIgnoreCase)
                                                                || string.Equals(o.Name?.Trim(), op, StringComparison.OrdinalIgnoreCase)))
            errs.Add($"Operasyon bulunamadı: '{op}'");
        if (!string.IsNullOrWhiteSpace(mach) && _machines is not null
            && !_machines.Any(m => string.Equals(m.Code?.Trim(), mach, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(m.Name?.Trim(), mach, StringComparison.OrdinalIgnoreCase)))
            errs.Add($"Makine bulunamadı: '{mach}'");
        if (!string.IsNullOrWhiteSpace(item) && _items is not null
            && !_items.Any(i => string.Equals(i.Code?.Trim(), item, StringComparison.OrdinalIgnoreCase)))
            errs.Add($"Stok bulunamadı: '{item}'");
        return errs;
    }

    private (IReadOnlyList<string> Keys, IReadOnlyList<string> Labels) DisplayCols(IReadOnlyList<string> mapped)
    {
        var keys = new List<string>(); var labels = new List<string>();
        foreach (var f in GetFields())
            if (mapped.Any(k => string.Equals(k, f.Key, StringComparison.OrdinalIgnoreCase))) { keys.Add(f.Key); labels.Add(f.Label); }
        return (keys, labels);
    }

    private static DurationUnit ResolveDurationUnit(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v is "h" or "saat" or "hour" or "hours") return DurationUnit.Hour;
        return DurationUnit.Minute;
    }

    private static ImportCommitRowDto Fail(int rowNo, string err) => new(rowNo, false, "error", err, null);

    private sealed class RoutingGroup(int firstRow, string name, int? itemId, string? itemCode)
    {
        public int FirstRow { get; } = firstRow;
        public string Name { get; } = name;
        public int? ItemId { get; } = itemId;
        public string? ItemCode { get; } = itemCode;
        public List<RoutingLine> Lines { get; } = new();
    }

    private sealed record RoutingLine(int Sequence, int OperationId, int? MachineId, decimal? Duration, DurationUnit DurationUnit, string? Notes);
}

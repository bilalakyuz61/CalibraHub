using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Stok Kartı / Malzeme (Item) içe-aktarım handler'ı. Yazma
/// <see cref="ILogisticsConfigurationService.CreateItemAsync"/>/<c>UpdateItemAsync</c>'e delege edilir.
/// Kod yoksa addan benzersiz türetilir (kullanıcı kod girmez kuralı).
/// </summary>
public sealed class ItemImportHandler : RowImportHandlerBase
{
    private readonly ILogisticsConfigurationService _logistics;
    private readonly ILogisticsConfigurationRepository _logisticsRepo;
    private List<Item>? _items;   // run-cache (scoped, satırlar sıralı işlenir)

    public ItemImportHandler(ILogisticsConfigurationService logistics, ILogisticsConfigurationRepository logisticsRepo)
    { _logistics = logistics; _logisticsRepo = logisticsRepo; }

    public override string Entity => "ITEM";
    public override string Label => "Stok Kartı";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("Name",    "Stok Adı",  "string",  true,  false, "Malzeme/ürün adı (zorunlu)"),
        new ImportTargetFieldDto("Code",    "Stok Kodu", "string",  false, true,  "Boşsa addan otomatik üretilir; eşleştirme anahtarı olabilir"),
        new ImportTargetFieldDto("TaxRate", "KDV Oranı", "decimal", false, false, "Yüzde (boşsa %20)"),
    };

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(d, "Name"))) errs.Add("Stok adı boş.");
        var tax = Get(d, "TaxRate");
        if (!string.IsNullOrWhiteSpace(tax) && ParseDecimal(tax) is null) errs.Add($"KDV oranı sayı değil: '{tax}'.");
        return errs;
    }

    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, string? matchKeyField, CancellationToken ct)
    {
        if (string.Equals(matchKeyField, "Code", StringComparison.OrdinalIgnoreCase))
        {
            var code = Get(d, "Code");
            if (!string.IsNullOrWhiteSpace(code))
            {
                var items = await EnsureItemsAsync(ct);
                var hit = items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return ("update", hit.Id);
            }
        }
        return ("insert", null);
    }

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var name = Get(d, "Name")!.Trim();
        var taxRate = ParseDecimal(Get(d, "TaxRate")) ?? 20m;

        if (action == "update" && existingId is > 0)
        {
            var items = await EnsureItemsAsync(ct);
            var ex = items.FirstOrDefault(i => i.Id == existingId.Value);
            var req = new UpdateItemRequest(existingId.Value, ex?.Code ?? (Get(d, "Code") ?? name), name,
                ex?.TypeId, ex?.UnitId, ex?.Combinations ?? false, taxRate);
            await _logistics.UpdateItemAsync(req, ct);
            return (true, null, existingId);
        }

        var code = await DeriveUniqueCodeAsync(Get(d, "Code"), name, usedCodes, ct);
        await _logistics.CreateItemAsync(new CreateItemRequest(code, name, null, null, false, taxRate), ct);
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
}

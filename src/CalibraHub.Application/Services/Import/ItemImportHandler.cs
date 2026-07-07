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

    public override IReadOnlyList<ImportTargetFieldDto> GetFields()
    {
        var fields = new List<ImportTargetFieldDto>
        {
            // MaxLength'ler Items tablosu NVARCHAR uzunluklarıyla birebir (Name 200, Code 50).
            new("Name",    "Stok Adı",  "string",  true,  false, "Malzeme/ürün adı (zorunlu)", MaxLength: 200),
            new("Code",    "Stok Kodu", "string",  false, true,  "Boşsa addan otomatik üretilir; eşleştirme anahtarı olabilir", MaxLength: 50),
            new("TaxRate",      "KDV Oranı",  "decimal", false, false, "Yüzde (boşsa %20)"),
            new("TrackingType", "Takip Tipi", "string",  false, false, "Yok / Lot / Seri (boşsa Yok)", new[] { "Yok", "Lot", "Seri" }),
        };
        // Stok kartına admin'in eklediği özel (widget) alanlar — PreloadAsync ile yüklenir.
        fields.AddRange(_widgetSupport.GetFields());
        return fields;
    }

    public override async Task PreloadAsync(CancellationToken ct)
        => await _widgetSupport.PreloadAsync(ct);

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
        var tracking = ResolveTracking(Get(d, "TrackingType"));

        if (action == "update" && existingId is > 0)
        {
            var items = await EnsureItemsAsync(ct);
            var ex = items.FirstOrDefault(i => i.Id == existingId.Value);
            var req = new UpdateItemRequest(existingId.Value, ex?.Code ?? (Get(d, "Code") ?? name), name,
                ex?.TypeId, ex?.UnitId, ex?.Combinations ?? false, taxRate, tracking ?? ex?.TrackingType ?? "None");
            await _logistics.UpdateItemAsync(req, ct);

            // Özel (widget) alanlar — RecordId = Items.Id (MaterialCardEdit DWR konvansiyonu)
            var wErrU = await _widgetSupport.SaveRowValuesAsync(existingId.Value.ToString(), d, ct);
            if (wErrU != null) return (false, $"Stok güncellendi, {wErrU}", existingId);
            return (true, null, existingId);
        }

        var code = await DeriveUniqueCodeAsync(Get(d, "Code"), name, usedCodes, ct);
        await _logistics.CreateItemAsync(new CreateItemRequest(code, name, null, null, false, taxRate, tracking ?? "None"), ct);

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

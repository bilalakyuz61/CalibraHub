using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services.Import;

/// <summary>
/// Fiyat Listesi içe-aktarım handler'ı. Her satır bir fiyat kaydı. Stok/Grup/Döviz
/// koddan Id'ye çözülür (FK'ler INT). Yazma <see cref="IPriceListService.SaveEntryAsync"/>.
/// v1: her zaman yeni kayıt ekler (fiyat geçmişi temporaldir; upsert yok).
/// </summary>
public sealed class PriceListImportHandler : RowImportHandlerBase
{
    private readonly IPriceListService _priceService;
    private readonly IPriceListRepository _priceRepo;
    private readonly ILogisticsConfigurationRepository _itemRepo;
    private readonly ICurrencyRepository _currencyRepo;
    private List<Item>? _items;
    private List<PriceGroup>? _groups;
    private List<Currency>? _currencies;

    public PriceListImportHandler(
        IPriceListService priceService, IPriceListRepository priceRepo,
        ILogisticsConfigurationRepository itemRepo, ICurrencyRepository currencyRepo)
    { _priceService = priceService; _priceRepo = priceRepo; _itemRepo = itemRepo; _currencyRepo = currencyRepo; }

    public override string Entity => "PRICELIST";
    public override string Label => "Fiyat Listesi";

    public override IReadOnlyList<ImportTargetFieldDto> GetFields() => new[]
    {
        new ImportTargetFieldDto("ItemCode",  "Stok Kodu",            "string",  true,  false, "Fiyatı girilecek stok kartının kodu (zorunlu)"),
        new ImportTargetFieldDto("Combination","Kombinasyon",         "string",  false, false, "Stok kombinasyon kodu — kombinasyon-takipli stoklarda ZORUNLU"),
        new ImportTargetFieldDto("GroupCode", "Fiyat Grubu",          "string",  true,  false, "Fiyat grubu kodu veya adı (zorunlu)"),
        new ImportTargetFieldDto("Currency",  "Döviz",                "string",  true,  false, "TRY / USD / EUR (zorunlu)"),
        new ImportTargetFieldDto("PriceType", "Fiyat Tipi",           "string",  false, false, "Alış / Satış / Maliyet (boşsa Satış)", new[] { "Alış", "Satış", "Maliyet" }),
        new ImportTargetFieldDto("Price",     "Fiyat",                "decimal", true,  false, "Birim fiyat (zorunlu)"),
        new ImportTargetFieldDto("ValidFrom", "Geçerlilik Başlangıç", "date",    false, false, "gg.aa.yyyy (boşsa bugün)"),
        new ImportTargetFieldDto("ValidTo",   "Geçerlilik Bitiş",     "date",    false, false, "gg.aa.yyyy (opsiyonel)"),
    };

    public override async Task<ImportPreviewResultDto> PreviewAsync(ImportRowSet set, CancellationToken ct)
    {
        await EnsureItemsAsync(ct);   // ValidateRow'daki kombinasyon-zorunlu kontrolü için items cache'le
        return await base.PreviewAsync(set, ct);
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        var itemCode = Get(d, "ItemCode");
        if (string.IsNullOrWhiteSpace(itemCode)) errs.Add("Stok Kodu boş.");
        if (string.IsNullOrWhiteSpace(Get(d, "GroupCode"))) errs.Add("Fiyat Grubu boş.");
        if (string.IsNullOrWhiteSpace(Get(d, "Currency"))) errs.Add("Döviz boş.");
        if (ImportParse.ParseDecimal(Get(d, "Price")) is null) errs.Add("Fiyat sayı değil veya boş.");
        // Kombinasyon-takipli stokta kombinasyon zorunlu. _items preview'da preload edilir (commit'te de doğrulanır).
        if (_items is not null && !string.IsNullOrWhiteSpace(itemCode) && string.IsNullOrWhiteSpace(Get(d, "Combination")))
        {
            var it = _items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), itemCode!.Trim(), StringComparison.OrdinalIgnoreCase));
            if (it is { Combinations: true }) errs.Add($"Kombinasyon zorunlu (stok kombinasyon-takipli): '{itemCode}'");
        }
        return errs;
    }

    protected override Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, string? matchKeyField, CancellationToken ct)
        => Task.FromResult(("insert", (int?)null));

    protected override async Task<(bool Ok, string? Error, int? RecordId)> CommitRowAsync(
        IReadOnlyDictionary<string, string?> d, string action, int? existingId,
        int? userId, HashSet<string> usedCodes, CancellationToken ct)
    {
        var items = await EnsureItemsAsync(ct);
        var itemCode = Get(d, "ItemCode")!.Trim();
        var item = items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), itemCode, StringComparison.OrdinalIgnoreCase));
        if (item is null) return (false, $"Stok bulunamadı: '{itemCode}'", null);

        // Kombinasyon: takipli stokta zorunlu; verilmişse Id'ye çözülür (FK = ConfigId).
        int? configId = null;
        var combo = Get(d, "Combination")?.Trim();
        if (item.Combinations && string.IsNullOrWhiteSpace(combo))
            return (false, $"Kombinasyon zorunlu (stok kombinasyon-takipli): '{itemCode}'", null);
        if (!string.IsNullOrWhiteSpace(combo))
        {
            configId = await ResolveConfigIdAsync(itemCode, combo, ct);
            if (configId is null) return (false, $"Kombinasyon bulunamadı: '{combo}' ({itemCode})", null);
        }

        var groups = await EnsureGroupsAsync(ct);
        var gKey = Get(d, "GroupCode")!.Trim();
        var group = groups.FirstOrDefault(g => string.Equals(g.Code?.Trim(), gKey, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(g.Name?.Trim(), gKey, StringComparison.OrdinalIgnoreCase));
        if (group is null) return (false, $"Fiyat grubu bulunamadı: '{gKey}'", null);

        var currencies = await EnsureCurrenciesAsync(ct);
        var cKey = Get(d, "Currency")!.Trim();
        var currency = currencies.FirstOrDefault(c => string.Equals(c.Code?.Trim(), cKey, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(c.Name?.Trim(), cKey, StringComparison.OrdinalIgnoreCase));
        if (currency is null) return (false, $"Döviz bulunamadı: '{cKey}'", null);

        var priceType = ResolvePriceType(Get(d, "PriceType"));
        var price = ImportParse.ParseDecimal(Get(d, "Price")) ?? 0m;
        var validFrom = ImportParse.ParseDate(Get(d, "ValidFrom")) ?? DateTime.Today;
        var validTo = ImportParse.ParseDate(Get(d, "ValidTo"));

        var (ok, err, id) = await _priceService.SaveEntryAsync(
            new SavePriceListRequest(null, group.Id, item.Id, configId, currency.Id, priceType, price, validFrom, validTo, true), ct);
        return (ok, err, id);
    }

    private async Task<List<Item>> EnsureItemsAsync(CancellationToken ct) => _items ??= (await _itemRepo.GetItemsAsync(ct)).ToList();
    private async Task<List<PriceGroup>> EnsureGroupsAsync(CancellationToken ct) => _groups ??= (await _priceRepo.GetAllGroupsAsync(ct)).ToList();
    private async Task<List<Currency>> EnsureCurrenciesAsync(CancellationToken ct) => _currencies ??= (await _currencyRepo.GetAllAsync(ct)).ToList();

    private static string ResolvePriceType(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v is "b" or "alış" or "alis" or "alım" or "alim" or "buying" or "buy") return "b";
        if (v is "m" or "maliyet" or "cost") return "m";
        return "s"; // satış default
    }

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
}

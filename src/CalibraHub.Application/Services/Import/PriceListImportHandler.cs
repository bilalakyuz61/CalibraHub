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
        // ValidateRow'da FK soft-check için tüm referans listelerini cache'le.
        await EnsureItemsAsync(ct);
        await EnsureGroupsAsync(ct);
        await EnsureCurrenciesAsync(ct);
        return await base.PreviewAsync(set, ct);
    }

    protected override IReadOnlyList<string> ValidateRow(IReadOnlyDictionary<string, string?> d)
    {
        var errs = new List<string>();
        var itemCode = Get(d, "ItemCode");
        var groupCode = Get(d, "GroupCode");
        var currency = Get(d, "Currency");
        if (string.IsNullOrWhiteSpace(itemCode)) errs.Add("Stok Kodu boş.");
        if (string.IsNullOrWhiteSpace(groupCode)) errs.Add("Fiyat Grubu boş.");
        if (string.IsNullOrWhiteSpace(currency)) errs.Add("Döviz boş.");
        if (ImportParse.ParseDecimal(Get(d, "Price")) is null) errs.Add("Fiyat sayı değil veya boş.");

        if (_items is not null && !string.IsNullOrWhiteSpace(itemCode))
        {
            var it = _items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), itemCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (it is null) errs.Add($"Stok bulunamadı: '{itemCode}'");
            else if (it.Combinations && string.IsNullOrWhiteSpace(Get(d, "Combination")))
                errs.Add($"Kombinasyon zorunlu (stok kombinasyon-takipli): '{itemCode}'");
        }
        if (_groups is not null && !string.IsNullOrWhiteSpace(groupCode))
        {
            var g = _groups.FirstOrDefault(gr => string.Equals(gr.Code?.Trim(), groupCode.Trim(), StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(gr.Name?.Trim(), groupCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (g is null) errs.Add($"Fiyat grubu bulunamadı: '{groupCode}'");
        }
        if (_currencies is not null && !string.IsNullOrWhiteSpace(currency))
        {
            var c = _currencies.FirstOrDefault(cu => string.Equals(cu.Code?.Trim(), currency.Trim(), StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(cu.Name?.Trim(), currency.Trim(), StringComparison.OrdinalIgnoreCase));
            if (c is null) errs.Add($"Döviz bulunamadı: '{currency}'");
        }
        return errs;
    }

    /// <summary>
    /// Fiyat kaydının doğal anahtarı DOMAIN'de tanımlıdır:
    /// (Grup + Stok + Kombinasyon + Döviz + Fiyat Tipi + Geçerlilik Başlangıcı).
    /// Kullanıcının seçtiği anahtar alanlar burada kullanılmaz — mevcut
    /// <c>FindActiveDuplicateAsync</c> bu bileşimi zaten uygular; ayrı bir anahtar
    /// tanımlamak iki farklı "aynı kayıt" kuralı doğururdu.
    ///
    /// Tarih anahtara DAHİL olduğu için yeni geçerlilik tarihi yeni kayıt açar
    /// (fiyat geçmişi korunur), aynı tarihli kayıt güncellenir.
    /// </summary>
    protected override async Task<(string Action, int? ExistingId)> ResolveActionAsync(
        IReadOnlyDictionary<string, string?> d, IReadOnlyList<string> matchKeys, CancellationToken ct)
    {
        var resolved = await ResolveKeyPartsAsync(d, ct);
        if (resolved is null) return ("insert", null);   // çözülemeyen satırı CommitRow reddeder

        var (groupId, itemId, configId, currencyId, priceType, validFrom) = resolved.Value;
        var dup = await _priceRepo.FindActiveDuplicateAsync(
            groupId, itemId, configId, currencyId, priceType, validFrom, 0, ct);

        return dup is not null ? ("update", dup.Id) : ("insert", null);
    }

    /// <summary>
    /// Satırdaki kod/ad değerlerini doğal anahtarın Id bileşenlerine çözer.
    /// Herhangi biri çözülemezse null (CommitRow net hata mesajı verir).
    /// </summary>
    private async Task<(int GroupId, int ItemId, int? ConfigId, int CurrencyId, string PriceType, DateTime ValidFrom)?>
        ResolveKeyPartsAsync(IReadOnlyDictionary<string, string?> d, CancellationToken ct)
    {
        var items = await EnsureItemsAsync(ct);
        var itemCode = Get(d, "ItemCode")?.Trim();
        if (string.IsNullOrWhiteSpace(itemCode)) return null;
        var item = items.FirstOrDefault(i => string.Equals(i.Code?.Trim(), itemCode, StringComparison.OrdinalIgnoreCase));
        if (item is null) return null;

        int? configId = null;
        var combo = Get(d, "Combination")?.Trim();
        if (!string.IsNullOrWhiteSpace(combo))
        {
            configId = await ResolveConfigIdAsync(itemCode, combo, ct);
            if (configId is null) return null;
        }

        var groups = await EnsureGroupsAsync(ct);
        var gKey = Get(d, "GroupCode")?.Trim();
        if (string.IsNullOrWhiteSpace(gKey)) return null;
        var group = groups.FirstOrDefault(g => string.Equals(g.Code?.Trim(), gKey, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(g.Name?.Trim(), gKey, StringComparison.OrdinalIgnoreCase));
        if (group is null) return null;

        var currencies = await EnsureCurrenciesAsync(ct);
        var cKey = Get(d, "Currency")?.Trim();
        if (string.IsNullOrWhiteSpace(cKey)) return null;
        var currency = currencies.FirstOrDefault(c => string.Equals(c.Code?.Trim(), cKey, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(c.Name?.Trim(), cKey, StringComparison.OrdinalIgnoreCase));
        if (currency is null) return null;

        var priceType = ResolvePriceType(Get(d, "PriceType"));
        var validFrom = ImportParse.ParseDate(Get(d, "ValidFrom")) ?? DateTime.Today;
        return (group.Id, item.Id, configId, currency.Id, priceType, validFrom);
    }

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
        var validFrom = ImportParse.ParseDate(Get(d, "ValidFrom")) ?? DateTime.Today;
        // Eşlenmemiş = null: aşağıda mevcut kaydın değeriyle harmanlanır.
        // NOT: priceType/validFrom doğal anahtarın parçasıdır (FindActiveDuplicateAsync
        // ikisini de TAM eşleştirir) → update dalında zaten mevcut değere eşittirler,
        // geri yazmak no-op'tur. price/validTo ise anahtar DIŞINDADIR: eşlenmemişken
        // eskiden fiyat 0'a, bitiş tarihi null'a çekiliyordu. (2026-08-18)
        var price = ImportParse.ParseDecimal(Get(d, "Price"));
        var validTo = ImportParse.ParseDate(Get(d, "ValidTo"));

        // action=update ise mevcut kaydın Id'si geçirilir → servis günceller (mükerrer açmaz).
        var entryId = action == "update" && existingId is > 0 ? existingId : null;
        PriceList? existing = entryId is > 0 ? await _priceRepo.GetEntryByIdAsync(entryId.Value, ct) : null;

        var (ok, err, id) = await _priceService.SaveEntryAsync(
            new SavePriceListRequest(entryId, group.Id, item.Id, configId, currency.Id, priceType,
                price ?? existing?.Price ?? 0m, validFrom, validTo ?? existing?.ValidTo, true), ct);
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

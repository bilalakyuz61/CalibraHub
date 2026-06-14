using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ProductFeatureController — Urun Konfigurasyonu (Feature/Value/Stock) JSON CRUD
/// endpoint'leri (rapor §2.3 split). Modern ProductFeatureEdit ekranindan cagirilir.
///
/// Tasinmis endpoint'ler:
///   - GET  /Logistics/ProductFeatureEdit         → edit view
///   - GET  /Logistics/GetProductFeature          → feature detay (values + linkedStocks)
///   - POST /Logistics/SaveProductFeatureJson     → feature insert/update
///   - POST /Logistics/DeleteProductFeatureJson   → feature soft-delete
///   - POST /Logistics/SaveProductValueJson       → value ekle
///   - POST /Logistics/DeleteProductValueJson     → value soft-delete
///   - POST /Logistics/UpdateProductValueJson     → value description/aciklama guncelle
///   - POST /Logistics/SaveProductFeatureStocksJson → feature-stok baglama (full replace)
///   - POST /Logistics/UpdateFeatureVisibilityJson → dizaynda gorunsun toggle
///   - POST /Logistics/UpdateValueAciklamaJson    → value aciklama hizli edit
///
/// LogisticsController'da kalan (BuildProductConfigurationViewModelAsync helper'a bagli):
///   - ProductConfiguration view + Build helpers
///   - Legacy form-post: SaveProductFeature, SaveProductValue, SaveProductConfig,
///     UpdateProductFeature, DeleteProductFeature, SaveProductFeatureStocks,
///     DeleteProductValue, DeleteProductConfig
///   - Combinations + ProductCombinations + CombinationsDataJson (ayri split icin)
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
public sealed class ProductFeatureController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;

    public ProductFeatureController(ILogisticsConfigurationService logisticsConfigurationService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
    }

    // ── ProductFeatureEdit (vanilla JS sayfasi) ─────────────────────────────
    [HttpGet]
    public IActionResult ProductFeatureEdit(int? id)
    {
        ViewData["ProductFeatureEditId"] = id ?? 0;
        return View("~/Views/Logistics/ProductFeatureEdit.cshtml", new ProductFeatureEditViewModel { FeatureId = id });
    }

    /// <summary>Feature detay fetch - edit sayfasi load'da cagirir.</summary>
    [HttpGet]
    public async Task<IActionResult> GetProductFeature(int id, CancellationToken ct)
    {
        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
        var feature = snapshot.Features.FirstOrDefault(f => f.Id == id);
        if (feature is null) return NotFound();

        var values = snapshot.Values
            .Where(v => v.FeatureId == id && v.IsActive)
            .OrderBy(v => v.Description)
            .Select(v => new
            {
                id = v.Id,
                code = v.Code,
                description = v.Description,
                value = v.Value,
                aciklama = v.Aciklama,
            })
            .ToArray();

        // Bu feature icin tum stok-link'leri (her link'in AllowedValueIds[] var)
        var featureLinks = snapshot.FeatureStockLinks
            .Where(l => l.FeatureId == id && !string.IsNullOrWhiteSpace(l.StockCode))
            .GroupBy(l => l.StockCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()) // her stockCode tekil olmali (header satir)
            .ToArray();

        var stockCodes = featureLinks
            .Select(l => l.StockCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Stok adlarini cek (chip etiketi icin)
        var stockCards = await _logisticsConfigurationService.GetItemsForLookupAsync(ct);
        var stockByCode = stockCards
            .GroupBy(s => (s.Code ?? string.Empty).ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().Name ?? g.First().Code);

        var linkedStocks = featureLinks
            .Where(l => stockByCode.ContainsKey((l.StockCode ?? string.Empty).ToUpperInvariant()))
            .Select(l => new
            {
                code = (l.StockCode ?? string.Empty).Trim(),
                name = stockByCode.TryGetValue((l.StockCode ?? string.Empty).ToUpperInvariant(), out var n) ? n : l.StockCode,
                printDescriptionInDesign = l.PrintDescriptionInDesign,
                allowedValueIds = (l.AllowedValueIds ?? Array.Empty<int>()).ToArray(),
            })
            .ToArray();

        return Json(new
        {
            id = feature.Id,
            code = feature.Code,
            name = feature.Name,
            dataType = feature.DataType,
            unitOfMeasure = feature.UnitOfMeasure,
            isActive = feature.IsActive,
            visibleInDesign = feature.VisibleInDesign,
            values,
            stockCodes,
            linkedStocks,
        });
    }

    // ── Feature JSON CRUD ────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> SaveProductFeatureJson(
        [FromBody] SaveProductFeatureJsonInput input,
        CancellationToken ct)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Ozellik adi bos olamaz." });

        if (!Enum.TryParse<ConfigurationFieldDataType>(input.DataType, true, out var dataType))
            return Json(new { success = false, message = "Gecerli bir veri tipi seciniz." });

        try
        {
            int savedId;
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                await _logisticsConfigurationService.UpdateProductConfigurationFeatureAsync(
                    new UpdateProductConfigurationFeatureRequest(
                        input.Id.Value, input.Name.Trim(), dataType, input.UnitOfMeasure, input.VisibleInDesign),
                    ct);
                savedId = input.Id.Value;
            }
            else
            {
                savedId = await _logisticsConfigurationService.CreateProductConfigurationFeatureAsync(
                    new CreateProductConfigurationFeatureRequest(
                        input.Name.Trim(), dataType, input.IsActive, input.UnitOfMeasure, input.VisibleInDesign),
                    ct);
            }
            return Json(new { success = true, id = savedId });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteProductFeatureJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationFeatureAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ── Value JSON CRUD ─────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> SaveProductValueJson(
        [FromBody] SaveProductValueJsonInput input,
        CancellationToken ct)
    {
        if (input is null || input.FeatureId <= 0)
            return Json(new { success = false, message = "Gecersiz istek." });

        try
        {
            var (id, code) = await _logisticsConfigurationService.CreateProductConfigurationValueAsync(
                new CreateProductConfigurationValueRequest(
                    input.FeatureId, input.Description, input.TextValue,
                    input.NumericValue, input.DateValue, true, input.Aciklama),
                ct);
            return Json(new { success = true, id, code });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteProductValueJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationValueAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProductValueJson(
        [FromBody] UpdateProductValueJsonInput input, CancellationToken ct)
    {
        if (input is null || input.Id <= 0)
            return Json(new { success = false, message = "Gecersiz istek." });
        try
        {
            await _logisticsConfigurationService.UpdateProductConfigurationValueAsync(input.Id, input.Description, input.Aciklama, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ── Feature-Stok baglama ────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> SaveProductFeatureStocksJson(
        [FromBody] SaveProductFeatureStocksJsonInput input,
        CancellationToken ct)
    {
        if (input is null || input.FeatureId <= 0)
            return Json(new { success = false, message = "Gecersiz istek." });

        try
        {
            var stocks = (input.Stocks ?? Array.Empty<SaveProductFeatureStockInput>())
                .Where(x => !string.IsNullOrWhiteSpace(x.StockCode))
                .Select(x => new SaveProductConfigurationFeatureStockItem(
                    x.StockCode!.Trim(),
                    x.PrintDescriptionInDesign ?? true,
                    x.AllowedValueIds ?? Array.Empty<int>()))
                .ToList();

            if (stocks.Count == 0 && input.StockCodes is not null && input.StockCodes.Count > 0)
            {
                stocks.AddRange(input.StockCodes
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => new SaveProductConfigurationFeatureStockItem(c.Trim(), true, Array.Empty<int>())));
            }

            await _logisticsConfigurationService.SaveProductConfigurationFeatureStocksAsync(
                new SaveProductConfigurationFeatureStocksRequest(input.FeatureId, stocks),
                ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ── Kombinasyon popup inline edit ──────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UpdateFeatureVisibilityJson(
        [FromBody] UpdateFeatureVisibilityInput input,
        CancellationToken ct)
    {
        try
        {
            var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
            var feature = snapshot.Features.FirstOrDefault(f => f.Id == input.FeatureId);
            if (feature is null) return Json(new { success = false, message = "Ozellik bulunamadi." });

            // feature.DataType servis icinde "TEXT"/"NUMBER"/"DATE" formatinda saklanir;
            // Enum.TryParse bunlari tanimaz — manuel map.
            var normalized = (feature.DataType ?? string.Empty).Trim().ToUpperInvariant();
            var dataType = normalized switch
            {
                "TEXT"    => ConfigurationFieldDataType.Text,
                "NUMBER"  => ConfigurationFieldDataType.Numeric,
                "NUMERIC" => ConfigurationFieldDataType.Numeric,
                "DATE"    => ConfigurationFieldDataType.Date,
                _         => ConfigurationFieldDataType.Text
            };

            await _logisticsConfigurationService.UpdateProductConfigurationFeatureAsync(
                new UpdateProductConfigurationFeatureRequest(
                    feature.Id, feature.Name, dataType, feature.UnitOfMeasure, input.VisibleInDesign),
                ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex) { return Json(new { success = false, message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateValueAciklamaJson(
        [FromBody] UpdateValueAciklamaInput input,
        CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.UpdateProductConfigurationValueAsync(
                input.ValueId, null, input.Aciklama, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ── DTO Records ─────────────────────────────────────────────────────────
    public sealed record SaveProductFeatureJsonInput(
        int? Id,
        string Name,
        string DataType,
        string? UnitOfMeasure,
        bool IsActive = true,
        bool VisibleInDesign = true);

    public sealed record UpdateProductValueJsonInput(int Id, string? Description, string? Aciklama);

    public sealed record SaveProductValueJsonInput(
        int FeatureId,
        string? Description,
        string? TextValue,
        decimal? NumericValue,
        DateTime? DateValue,
        string? Aciklama = null);

    public sealed record SaveProductFeatureStocksJsonInput(
        int FeatureId,
        IReadOnlyCollection<string>? StockCodes,
        IReadOnlyCollection<SaveProductFeatureStockInput>? Stocks);

    public sealed record SaveProductFeatureStockInput(
        string? StockCode,
        bool? PrintDescriptionInDesign,
        IReadOnlyCollection<int>? AllowedValueIds);

    public sealed class UpdateFeatureVisibilityInput
    {
        public int FeatureId { get; set; }
        public bool VisibleInDesign { get; set; }
    }

    public sealed class UpdateValueAciklamaInput
    {
        public int ValueId { get; set; }
        public string? Aciklama { get; set; }
    }

    // ── Stok Karti <-> Ozellik Eslestirmesi (Kombinasyon Takibi acik iken) ──

    /// <summary>
    /// Bir stok karti icin mevcut tum FEATURE listesini + bu karta bagli olanlari doner.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStockFeatures(int stockCardId, CancellationToken ct)
    {
        var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
        var card = snapshot.Items.FirstOrDefault(s => s.Id == stockCardId);
        if (card is null) return NotFound(new { success = false, message = "Stok karti bulunamadi." });

        var productCfg = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
        var usedFeatureIdsInCombinations = await _logisticsConfigurationService
            .GetUsedFeatureIdsInCombinationsAsync(stockCardId, ct);
        var usedFeatureValuePairs = await _logisticsConfigurationService
            .GetUsedFeatureValueIdsInCombinationsAsync(stockCardId, ct);
        var combinationCount = await _logisticsConfigurationService
            .GetCombinationCountForItemAsync(stockCardId, ct);
        var usedValueIdsByFeature = usedFeatureValuePairs
            .GroupBy(x => x.FeatureId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ValueId).Distinct().ToArray());

        var stockLinks = productCfg.FeatureStockLinks
            .Where(l => string.Equals(l.StockCode, card.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => l.FeatureId)
            .ToDictionary(g => g.Key, g => g.First());

        var valuesByFeature = productCfg.Values
            .Where(v => v.IsActive)
            .GroupBy(v => v.FeatureId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(v => v.Description).Select(v => new
                {
                    id = v.Id,
                    code = v.Code,
                    description = v.Description,
                    value = v.Value,
                }).ToArray());

        var allFeatures = productCfg.Features
            .Where(f => f.IsActive)
            .OrderBy(f => f.Name)
            .Select(f =>
            {
                var hasLink = stockLinks.TryGetValue(f.Id, out var link);
                var usedValueIds = usedValueIdsByFeature.TryGetValue(f.Id, out var uv) ? uv : Array.Empty<int>();
                return new
                {
                    id = f.Id,
                    code = f.Code,
                    name = f.Name,
                    dataType = f.DataType,
                    unitOfMeasure = f.UnitOfMeasure,
                    linked = hasLink,
                    printDescription = hasLink ? link!.PrintDescriptionInDesign : true,
                    allowedValueIds = hasLink ? (link!.AllowedValueIds ?? Array.Empty<int>()).ToArray() : Array.Empty<int>(),
                    availableValues = valuesByFeature.TryGetValue(f.Id, out var vs) ? vs : Array.Empty<object>().Select(_ => new { id = 0, code = string.Empty, description = string.Empty, value = string.Empty }).ToArray(),
                    inUseInCombination = usedFeatureIdsInCombinations.Contains(f.Id),
                    usedValueIds = usedValueIds,
                };
            })
            .ToArray();

        return Json(new
        {
            materialCode = card.Code,
            combinationCount = combinationCount,
            features = allFeatures,
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveStockFeatures([FromBody] SaveStockFeaturesInput input, CancellationToken ct)
    {
        if (input is null || input.ItemId <= 0)
            return Json(new { success = false, message = "Stok karti ID gerekli." });

        try
        {
            var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
            var card = snapshot.Items.FirstOrDefault(s => s.Id == input.ItemId);
            if (card is null) return Json(new { success = false, message = "Stok karti bulunamadi." });

            (int FeatureId, bool PrintDescriptionInDesign, int[] AllowedValueIds)[] tuples;
            if (input.Items != null && input.Items.Length > 0)
            {
                tuples = input.Items
                    .Select(x => (
                        x.FeatureId,
                        x.PrintDescriptionInDesign,
                        AllowedValueIds: (x.AllowedValueIds ?? Array.Empty<int>()).Where(v => v > 0).Distinct().ToArray()))
                    .ToArray();
            }
            else
            {
                tuples = (input.FeatureIds ?? Array.Empty<int>())
                    .Select(id => (FeatureId: id, PrintDescriptionInDesign: true, AllowedValueIds: Array.Empty<int>()))
                    .ToArray();
            }

            await _logisticsConfigurationService.SetFeaturesForItemAsync(
                card.Code,
                tuples,
                ct);

            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatasi: " + ex.Message });
        }
    }
}

using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// CombinationController — Kombinasyon JSON CRUD endpoint'leri (rapor §2.3 split).
/// Modern React/vanilla JS kombinasyon matrisi ekranindan cagirilir.
///
/// Tasinmis endpoint'ler:
///   - POST /Logistics/DeleteCombinationJson         → kombinasyon kart sil
///   - GET  /Logistics/StockCodesJson                → kombinasyon ekrani stok kodu rehberi
///   - GET  /Logistics/CombinationsDataJson          → matris icin feature + value + mevcut kombolar
///   - POST /Logistics/SaveProductCombinationsJson   → kombinasyon liste replace (selectedValueIds permute)
///   - POST /Logistics/UpdateCombinationDescriptionJson → kombinasyon aciklama hizli edit
///   - POST /Logistics/AddSingleCombinationJson      → tek kombinasyon ekle (duplicate check)
///
/// LogisticsController'da kalan (form-post / view dependencies):
///   - Combinations view + CombinationsBoardConfig + BuildCombinationsBoardConfigAsync helper
///   - ProductCombinations view + SaveProductCombinations (form-post) + DeleteProductCombination (form-post)
///   - BuildCombinations helper (CombinationRowVm uretici)
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
public sealed class CombinationController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;

    public CombinationController(ILogisticsConfigurationService logisticsConfigurationService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
    }

    // ── Kombinasyon kart silme (SmartBoard kart icin) ───────────────────────
    [HttpPost]
    public async Task<IActionResult> DeleteCombinationJson(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationItemAsync(id, cancellationToken);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Kombinasyon Matrisi: stok kodu rehberi ──────────────────────────────
    [HttpGet]
    public async Task<IActionResult> StockCodesJson(CancellationToken cancellationToken)
    {
        var snapshot      = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
        var stockSnapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);

        var cardIndex = stockSnapshot.Items
            .GroupBy(s => s.Code.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var codes = snapshot.FeatureStockLinks
            .Select(x => x.StockCode.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => cardIndex.ContainsKey(code))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(code =>
            {
                var card = cardIndex[code];
                return new
                {
                    value = code,
                    label = $"{code} — {card.Name}"
                };
            })
            .ToArray();

        return Json(codes);
    }

    /// <summary>
    /// Stoka ait ozellikler + degerler + mevcut kombinasyonlar - React Kombinasyon Matrisi ekrani icin.
    /// Cross-product hesaplanmaz; mevcut DB kayitlari dogrudan donulur. Client tarafi cross-product uretir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CombinationsDataJson(
        string stockCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
            return Json(new { features = Array.Empty<object>(), combos = Array.Empty<object>() });

        stockCode = stockCode.Trim().ToUpperInvariant();

        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);

        // Bu stok'a bagli her feature icin AllowedValueIds[] (kisitlamali deger seti).
        var stockLinks = snapshot.FeatureStockLinks
            .Where(x => string.Equals(x.StockCode, stockCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var linkedFeatureIds = stockLinks.Select(x => x.FeatureId).ToHashSet();
        var allowedByFeature = stockLinks
            .GroupBy(x => x.FeatureId)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(x => x.AllowedValueIds ?? Array.Empty<int>()).ToHashSet());

        var featureVms = snapshot.Features
            .Where(f => linkedFeatureIds.Contains(f.Id) && f.IsActive)
            .OrderBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .Select(f => new CombinationFeatureVm
            {
                Id       = f.Id,
                Code     = f.Code,
                Name     = f.Name,
                DataType = f.DataType,
                Values   = snapshot.Values
                    .Where(v => v.FeatureId == f.Id && v.IsActive)
                    .OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new CombinationValueVm { Id = v.Id, Code = v.Code, Description = v.Description })
                    .ToArray()
            })
            .ToList();

        var featuresResult = featureVms.Select(f =>
        {
            var allowedSet = allowedByFeature.TryGetValue(f.Id, out var s) ? s : new HashSet<int>();
            var hasRestriction = allowedSet.Count > 0;
            return new
            {
                id     = f.Id,
                code   = f.Code,
                name   = f.Name,
                values = f.Values.Select(v => new
                {
                    id          = v.Id,
                    code        = v.Code,
                    description = v.Description,
                    allowed     = !hasRestriction || allowedSet.Contains(v.Id),
                }).ToArray()
            };
        }).ToArray();

        var existingConfigs = snapshot.Configurations
            .Where(c => string.Equals(c.RelatedMaterialCode, stockCode, StringComparison.OrdinalIgnoreCase)
                     && c.ValueIds != null && c.ValueIds.Count > 0)
            .ToList();

        var valueIndex = featureVms
            .SelectMany(f => f.Values.Select(v => new { f.Id, f.Name, v }))
            .ToDictionary(x => x.v.Id, x => new { featureId = x.Id, featureName = x.Name, x.v.Code, x.v.Description });

        var combosResult = existingConfigs
            .Select(config =>
            {
                var valueIdSet = config.ValueIds.ToHashSet();
                var cells = featureVms
                    .Select(f =>
                    {
                        var matchedValue = f.Values.FirstOrDefault(v => valueIdSet.Contains(v.Id));
                        if (matchedValue == null) return (object?)null;
                        return (object)new
                        {
                            featureId        = f.Id,
                            featureName      = f.Name,
                            valueId          = matchedValue.Id,
                            valueCode        = matchedValue.Code,
                            valueDescription = matchedValue.Description
                        };
                    })
                    .Where(c => c != null)
                    .ToArray();
                return new
                {
                    id          = config.Id,
                    code        = config.ConfigCode,
                    description = config.ConfigName,
                    date        = config.CreatedDate.ToString("dd.MM.yyyy"),
                    cells
                };
            })
            .Where(c => c.cells.Length > 0)
            .ToArray();

        return Json(new { features = featuresResult, combos = combosResult });
    }

    // ── Kombinasyon liste replace (selectedValueIds permute) ────────────────
    [HttpPost]
    public async Task<IActionResult> SaveProductCombinationsJson(
        [FromBody] SaveProductCombinationsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StockCode))
            return Json(new { success = false, message = "Stok kodu bos olamaz." });

        var resolvedStockCode = request.StockCode.Contains(',')
            ? request.StockCode.Split(',')[0].Trim()
            : request.StockCode.Trim();

        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);

        var requiredFeatureIds = snapshot.FeatureStockLinks
            .Where(x => string.Equals(x.StockCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.FeatureId)
            .ToHashSet();

        var activeRequiredFeatureIds = snapshot.Features
            .Where(f => requiredFeatureIds.Contains(f.Id) && f.IsActive)
            .Select(f => f.Id)
            .ToHashSet();

        var selectedValueIds = (request.SelectedCombinations ?? Array.Empty<string>())
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(x => x > 0)
            .ToHashSet();

        var selectedFeatureIds = snapshot.Values
            .Where(v => selectedValueIds.Contains(v.Id))
            .Select(v => v.FeatureId)
            .ToHashSet();

        if (activeRequiredFeatureIds.Count > 0 && selectedFeatureIds.Count < activeRequiredFeatureIds.Count)
            return Json(new { success = false, message = "Stoga bagli ozelliklerin tamamindan en az birer deger secmelisiniz." });

        var existingConfigs = snapshot.Configurations
            .Where(x => string.Equals(x.RelatedMaterialCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var exist in existingConfigs)
            await _logisticsConfigurationService.DeleteProductConfigurationItemAsync(exist.Id, cancellationToken);

        if (selectedValueIds.Count > 0)
        {
            var selectedValuesByFeature = snapshot.Values
                .Where(v => selectedValueIds.Contains(v.Id))
                .GroupBy(v => v.FeatureId)
                .ToList();

            if (selectedValuesByFeature.Count > 0)
            {
                var permutations = new List<List<int>> { new List<int>() };
                foreach (var featureGroup in selectedValuesByFeature)
                {
                    var newPermutations = new List<List<int>>();
                    foreach (var existing in permutations)
                        foreach (var val in featureGroup)
                            newPermutations.Add(new List<int>(existing) { val.Id });
                    permutations = newPermutations;
                }

                foreach (var comboIds in permutations)
                    if (comboIds.Count > 0)
                        await _logisticsConfigurationService.CreateProductConfigurationCombinationAsync(
                            new CreateProductConfigurationCombinationRequest(resolvedStockCode, comboIds.ToArray(), true),
                            cancellationToken);
            }
        }

        return Json(new { success = true, message = "Kombinasyon secimleri basariyla kaydedildi." });
    }

    // ── Kombinasyon aciklama hizli edit ─────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UpdateCombinationDescriptionJson(
        [FromBody] UpdateCombinationDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.UpdateProductCombinationDescriptionAsync(
                request.Id, request.Description, cancellationToken);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Tek kombinasyon ekle (duplicate check) ──────────────────────────────
    [HttpPost]
    public async Task<IActionResult> AddSingleCombinationJson(
        [FromBody] AddSingleCombinationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.StockCode))
                return Json(new { success = false, message = "Stok kodu bos olamaz." });
            if (request.ValueIds == null || request.ValueIds.Length == 0)
                return Json(new { success = false, message = "En az bir deger secilmelidir." });

            var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
            var stockCode = request.StockCode.Trim().ToUpperInvariant();

            var requiredFeatureIds = snapshot.FeatureStockLinks
                .Where(x => string.Equals(x.StockCode, stockCode, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.FeatureId)
                .ToHashSet();

            var activeFeatureIds = snapshot.Features
                .Where(f => requiredFeatureIds.Contains(f.Id) && f.IsActive)
                .Select(f => f.Id)
                .ToHashSet();

            var selectedValueIds = request.ValueIds.Where(v => v > 0).ToHashSet();

            var coveredFeatureIds = snapshot.Values
                .Where(v => selectedValueIds.Contains(v.Id))
                .Select(v => v.FeatureId)
                .ToHashSet();

            if (activeFeatureIds.Count > 0 && !activeFeatureIds.All(f => coveredFeatureIds.Contains(f)))
                return Json(new { success = false, message = "Her ozellik icin bir deger secilmelidir." });

            // Ayni kombinasyon zaten var mi
            var existing = snapshot.Configurations
                .Where(c => string.Equals(c.RelatedMaterialCode, stockCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selectedSorted = selectedValueIds.OrderBy(x => x).ToArray();
            var isDuplicate = existing.Any(c =>
            {
                var ids = (c.ValueIds ?? Array.Empty<int>()).OrderBy(x => x).ToArray();
                return ids.SequenceEqual(selectedSorted);
            });

            if (isDuplicate)
                return Json(new { success = false, message = "Bu kombinasyon zaten mevcut." });

            var (id, code) = await _logisticsConfigurationService.CreateProductConfigurationCombinationAsync(
                new CreateProductConfigurationCombinationRequest(stockCode, selectedValueIds.ToArray(), true),
                cancellationToken);

            var cells = snapshot.Values
                .Where(v => selectedValueIds.Contains(v.Id))
                .Select(v => new { featureName = v.FeatureName, valueDesc = v.Description, valueCode = v.Code })
                .ToArray();

            return Json(new { success = true, id, code, cells });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── DTO Records ─────────────────────────────────────────────────────────
    public sealed record UpdateCombinationDescriptionRequest(int Id, string? Description);
    public sealed record AddSingleCombinationRequest(string StockCode, int[] ValueIds);
}

using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// MaterialController — Malzeme Karti (Item) JSON CRUD endpoint'leri (rapor §2.3 split).
///
/// Tasinmis endpoint'ler (3 bagimsiz JSON):
///   - GET  /Logistics/GetMaterialCard      → tekil JSON (id veya code ile lookup)
///   - POST /Logistics/SaveMaterialCardJson → JSON upsert
///   - POST /Logistics/DeleteMaterialCardJson → JSON soft delete
///
/// LogisticsController'da kalan (helper bagimliliklari):
///   - MaterialCards (view + BuildMaterialCardsViewModelAsync ~500 satir)
///   - MaterialCardEdit, GetMaterialCardsPage, SaveMaterialCard (form-post),
///     DeleteMaterialCard (form-post), ConfigureMaterialCard
///   - GetMaterialCards (4 helper'a bagli: Normalize/Apply/Resolve/Visible)
///   - SaveMaterialCardGridColumns (uiConfigService)
///   - ToMaterialMessage helper (TR yerel mesaj)
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
public sealed class MaterialController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;

    public MaterialController(ILogisticsConfigurationService logisticsConfigurationService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialCard(int id, string? code, CancellationToken ct)
    {
        var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
        var card = id > 0
            ? snapshot.Items.FirstOrDefault(x => x.Id == id)
            : string.IsNullOrWhiteSpace(code)
                ? null
                : snapshot.Items.FirstOrDefault(x => string.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        if (card is null) return NotFound();

        // Kombinasyonlar
        var combinations = new List<object>();
        if (card.Combinations)
        {
            var productSnapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
            var stockCombinations = productSnapshot.Configurations
                .Where(x => string.Equals(x.RelatedMaterialCode, card.Code, StringComparison.OrdinalIgnoreCase)
                          && x.ValueIds != null && x.ValueIds.Any())
                .ToList();
            foreach (var c in stockCombinations)
            {
                var valObjs = productSnapshot.Values
                    .Where(v => c.ValueIds.Contains(v.Id))
                    .OrderBy(v => v.FeatureId)
                    .ToList();
                var valNames = valObjs
                    .Select(v => $"{v.FeatureName}: {v.Description ?? v.Code}")
                    .ToList();
                var features = valObjs.Select(v =>
                {
                    var feat = productSnapshot.Features.FirstOrDefault(f => f.Id == v.FeatureId);
                    return new
                    {
                        featureId       = v.FeatureId,
                        featureName     = v.FeatureName,
                        visibleInDesign = feat?.VisibleInDesign ?? true,
                        valueId         = v.Id,
                        valueName       = v.Description ?? v.Code,
                        aciklama        = v.Aciklama ?? string.Empty
                    };
                }).ToList();
                combinations.Add(new
                {
                    id              = c.Id,
                    combinationCode = c.ConfigCode ?? string.Empty,
                    combinationName = valNames.Count > 0 ? string.Join(" | ", valNames) : string.Empty,
                    description     = c.ConfigName ?? string.Empty,
                    features
                });
            }
        }

        return Json(new
        {
            stockCardId       = card.Id,
            materialCode      = card.Code,
            materialName      = card.Name,
            materialTypeId    = card.TypeId,
            unitId            = card.UnitId,
            trackCombinations = card.Combinations,
            taxRate           = card.TaxRate,
            meta              = new { createdDate = card.Created, modifiedDate = card.Updated },
            combinations
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveMaterialCardJson([FromBody] SaveMaterialCardJsonInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Malzeme kodu ve adi bos olamaz." });

        try
        {
            // Ayni kodla mevcut kart varsa guncellemeye yonlendir
            if (!input.ItemId.HasValue || input.ItemId.Value == 0)
            {
                var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
                var existing = snapshot.Items.FirstOrDefault(x =>
                    string.Equals(x.Code, input.Code.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    input.ItemId = existing.Id;
            }

            var isUpdate = input.ItemId.HasValue && input.ItemId.Value != 0;
            if (isUpdate)
            {
                await _logisticsConfigurationService.UpdateItemAsync(
                    new UpdateItemRequest(input.ItemId!.Value, input.Code, input.Name,
                        input.TypeId, input.UnitId, input.Combinations, input.TaxRate), ct);
            }
            else
            {
                await _logisticsConfigurationService.CreateItemAsync(
                    new CreateItemRequest(input.Code, input.Name,
                        input.TypeId, input.UnitId, input.Combinations, input.TaxRate), ct);
            }

            // Yeni kart icin id'yi turet
            var savedCardId = input.ItemId;
            if (!isUpdate && (savedCardId == null || savedCardId == 0))
            {
                var refreshed = await _logisticsConfigurationService.GetSnapshotAsync(ct);
                var created = refreshed.Items
                    .FirstOrDefault(x => string.Equals(x.Code, input.Code, StringComparison.OrdinalIgnoreCase));
                if (created != null) savedCardId = created.Id;
            }

            return Json(new
            {
                success = true,
                message = isUpdate ? "Malzeme karti guncellendi." : "Malzeme karti kaydedildi.",
                id      = savedCardId,
            });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ToMaterialMessage(ex.Message) });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMaterialCardJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeactivateItemAsync(id, ct);
            return Json(new { success = true, message = "Malzeme karti silindi." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ToMaterialMessage(ex.Message) });
        }
    }

    /// <summary>Service'ten gelen "stok" mesajlarini "malzeme" olarak kullaniciya gosterir.</summary>
    private static string ToMaterialMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Malzeme karti islemi tamamlanamadi.";
        return message
            .Replace("Stok karti", "Malzeme karti")
            .Replace("Stok", "Malzeme")
            .Replace("stok", "malzeme");
    }
}

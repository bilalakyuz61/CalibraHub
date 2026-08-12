using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.MaterialCardEdit)]
public sealed class MaterialController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;
    private readonly ICompanyParameterService _companyParameters;
    private readonly ILogger<MaterialController> _logger;

    public MaterialController(
        ILogisticsConfigurationService logisticsConfigurationService,
        ICompanyParameterService companyParameters,
        ILogger<MaterialController> logger)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
        _companyParameters = companyParameters;
        _logger = logger;
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
            barcode           = card.Barcode,
            materialTypeId    = card.TypeId,
            unitId            = card.UnitId,
            trackCombinations = card.Combinations,
            taxRate           = card.TaxRate,
            trackingType      = card.TrackingType ?? "None",
            autoSerial        = card.AutoSerial,
            minStock          = card.MinStock,
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
            var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);

            // Ayni kodla mevcut kart varsa guncellemeye yonlendir
            if (!input.ItemId.HasValue || input.ItemId.Value == 0)
            {
                var existingByCode = snapshot.Items.FirstOrDefault(x =>
                    string.Equals(x.Code, input.Code.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existingByCode is not null)
                    input.ItemId = existingByCode.Id;
            }

            var isUpdate = input.ItemId.HasValue && input.ItemId.Value != 0;

            // PageComment Seq 1099 (2026-08-12): izlenebilirlik sirket parametresi
            // kapaliyken Lot/Seri secimi (trackingType) DEGISTIRILEMEZ — sunucu tarafi
            // kapi (istemci tarafinda switch zaten disabled, ama eski sekme/otomasyon/
            // manuel istek icin server-side de reddedilir). Mevcut deger aynen
            // gonderiliyorsa (fiili degisiklik yok) engellenmez — parametre kapaliyken
            // dahi canli sirketlerin rutin kaydi kirilmaz.
            var traceabilityEnabled = await _companyParameters.GetBoolAsync(
                CalibraHub.Application.Constants.TraceabilityParameters.FormCode,
                CalibraHub.Application.Constants.TraceabilityParameters.EnabledKey, ct) ?? true;
            if (!traceabilityEnabled)
            {
                var requestedTracking = string.IsNullOrWhiteSpace(input.TrackingType) ? "None" : input.TrackingType;
                var currentTracking = "None";
                if (isUpdate)
                {
                    var existingItem = snapshot.Items.FirstOrDefault(x => x.Id == input.ItemId!.Value);
                    currentTracking = string.IsNullOrWhiteSpace(existingItem?.TrackingType) ? "None" : existingItem.TrackingType;
                }
                if (!string.Equals(requestedTracking, currentTracking, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "MaterialCard izlenebilirlik degisikligi reddedildi (sirket parametresi kapali): ItemId={ItemId}, Kod={Code}, Mevcut={Current}, Istenen={Requested}",
                        input.ItemId, input.Code, currentTracking, requestedTracking);
                    return Json(new
                    {
                        success = false,
                        message = "İzlenebilirlik özelliği şirket genelinde kapalı. Şirket Parametreleri > Stok altındaki İzlenebilirlik ayarını açmadan bu seçim değiştirilemez."
                    });
                }
            }

            if (isUpdate)
            {
                await _logisticsConfigurationService.UpdateItemAsync(
                    new UpdateItemRequest(input.ItemId!.Value, input.Code, input.Name,
                        input.TypeId, input.UnitId, input.Combinations, input.TaxRate, input.TrackingType,
                        input.MinStock ?? 0m, input.AutoSerial, input.Barcode), ct);
            }
            else
            {
                await _logisticsConfigurationService.CreateItemAsync(
                    new CreateItemRequest(input.Code, input.Name,
                        input.TypeId, input.UnitId, input.Combinations, input.TaxRate, input.TrackingType,
                        input.MinStock ?? 0m, input.AutoSerial, input.Barcode), ct);
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

    // ── Planlama: belge bazında malzeme kilidi ───────────────────────

    [HttpGet]
    public async Task<IActionResult> GetItemDocumentLocks(int itemId, CancellationToken ct)
    {
        var docTypes = itemId > 0
            ? await _logisticsConfigurationService.GetItemDocumentLocksAsync(itemId, ct)
            : [];
        return Json(new { docTypes });
    }

    [HttpPost]
    public async Task<IActionResult> SaveItemDocumentLocks([FromBody] SaveItemDocumentLocksInput input, CancellationToken ct)
    {
        if (input.ItemId <= 0)
            return Json(new { success = false, message = "Malzeme karti ID gerekli." });

        await _logisticsConfigurationService.SaveItemDocumentLocksAsync(
            input.ItemId, input.DocTypes ?? [], ct);
        return Json(new { success = true });
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

/// <summary>Planlama: belge bazında malzeme kilidi kaydetme girdisi (DocType kod listesi).</summary>
public sealed record SaveItemDocumentLocksInput(int ItemId, string[]? DocTypes);

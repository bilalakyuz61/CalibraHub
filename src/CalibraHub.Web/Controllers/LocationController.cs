using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// LocationController — Lokasyon JSON endpoint'leri (rapor §2.3 split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Logistics/GetAllLocations      → JSON liste (filtreli)
///   - GET  /Logistics/GetLocation/{id}     → JSON tekil
///   - POST /Logistics/SaveLocationJson     → JSON (insert/update)
///   - POST /Logistics/DeleteLocationJson   → JSON soft delete (FK uyarisi)
///
/// LogisticsController'da kalan (sonraki split icin):
///   - Locations(), LocationsTree() — view + tree config (200+ satir)
///   - LocationType + ItemLocation endpoint'leri (master-detail eslestirme)
///   - SaveLocation/DeleteLocation form-post (helper'a bagli)
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
public sealed class LocationController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;

    public LocationController(ILogisticsConfigurationService logisticsConfigurationService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllLocations(string? search, CancellationToken ct)
    {
        var all   = await _logisticsConfigurationService.GetLocationsAsync(ct);
        var types = await _logisticsConfigurationService.GetLocationTypesAsync(ct);
        var typeMap = types.ToDictionary(t => t.Code, t => t.Name, StringComparer.OrdinalIgnoreCase);

        string Resolve(string? code)
        {
            if (!string.IsNullOrEmpty(code) && typeMap.TryGetValue(code, out var name)) return name;
            return LocationTypeDisplayName(code);
        }

        var lookup = all.ToDictionary(x => x.Id);
        var filtered = all
            .OrderBy(x => x.SortOrder).ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(search) ||
                ContainsInsensitive(Resolve(x.LocationTypeCode), search) ||
                ContainsInsensitive(x.LocationCode, search) ||
                ContainsInsensitive(x.LocationName ?? string.Empty, search) ||
                ContainsInsensitive(
                    x.ParentId.HasValue && lookup.TryGetValue(x.ParentId.Value, out var p)
                        ? string.IsNullOrWhiteSpace(p.LocationName) ? p.LocationCode : $"{p.LocationCode} {p.LocationName}"
                        : string.Empty, search))
            .Select(x =>
            {
                var parent = x.ParentId.HasValue ? lookup.GetValueOrDefault(x.ParentId.Value) : null;
                return new
                {
                    x.Id, x.ParentId,
                    x.LocationTypeCode,
                    locationTypeDisplayName = Resolve(x.LocationTypeCode),
                    x.LocationCode,
                    locationName = x.LocationName ?? string.Empty,
                    parentDisplayName = parent is null ? "-"
                        : string.IsNullOrWhiteSpace(parent.LocationName) ? parent.LocationCode
                        : $"{parent.LocationCode} - {parent.LocationName}",
                    x.SortOrder, x.MaxWeightCapacity, x.VolumeCapacity, x.IsActive,
                    x.IsMachinePark, x.IsStorageArea
                };
            });
        return Json(filtered);
    }

    [HttpGet]
    public async Task<IActionResult> GetLocation(int id, CancellationToken ct)
    {
        var all = await _logisticsConfigurationService.GetLocationsAsync(ct);
        var item = all.FirstOrDefault(x => x.Id == id);
        if (item is null) return NotFound();
        return Json(new
        {
            item.Id, item.ParentId,
            locationTypeCode = NormalizeLocationTypeCode(item.LocationTypeCode),
            item.LocationCode,
            locationName = item.LocationName ?? string.Empty,
            item.SortOrder, item.MaxWeightCapacity, item.VolumeCapacity, item.IsActive,
            item.IsMachinePark, item.IsStorageArea
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveLocationJson([FromBody] LocationInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.LocationTypeCode) || string.IsNullOrWhiteSpace(input.LocationCode))
            return Json(new { success = false, message = "Lokasyon tipi ve kod bos olamaz." });
        try
        {
            var typeCode = NormalizeLocationTypeCode(input.LocationTypeCode);
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                await _logisticsConfigurationService.UpdateLocationAsync(
                    new UpdateLocationRequest(input.Id.Value, input.ParentId, typeCode, input.LocationCode,
                        input.LocationName, input.SortOrder, input.MaxWeightCapacity, input.VolumeCapacity,
                        input.IsActive, input.IsMachinePark, input.IsStorageArea), ct);
            }
            else
            {
                await _logisticsConfigurationService.CreateLocationAsync(
                    new CreateLocationRequest(input.ParentId, typeCode, input.LocationCode,
                        input.LocationName, input.SortOrder, input.MaxWeightCapacity, input.VolumeCapacity,
                        input.IsActive, input.IsMachinePark, input.IsStorageArea), ct);
            }
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteLocationJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteLocationAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex) when (ex.GetType().Name == "SqlException")
        {
            var msg = ex.Message ?? "";
            string friendly;
            if (msg.Contains("FK_Machine_Location", StringComparison.OrdinalIgnoreCase))
                friendly = "Bu lokasyon en az bir makine tarafindan kullaniliyor; once makineleri baska bir lokasyona tasiyin.";
            else if (msg.Contains("FK_Location_Parent", StringComparison.OrdinalIgnoreCase))
                friendly = "Bu lokasyonun alt kirilimlari var; once alt lokasyonlari siliniz.";
            else if (msg.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase) || msg.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
                friendly = "Bu lokasyon baska bir kayit tarafindan referansli; once iliskili kayitlari kaldirin. (" + msg + ")";
            else
                friendly = "Veritabani hatasi: " + msg;
            return Json(new { success = false, message = friendly });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lokasyon silinemedi: " + ex.Message });
        }
    }

    // ── Lokasyon Tipleri (dinamik) ──────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetLocationTypes(CancellationToken ct)
    {
        var types = await _logisticsConfigurationService.GetLocationTypesAsync(ct);
        return Json(types.Select(t => new
        {
            id = t.Id,
            code = t.Code,
            name = t.Name,
            sortOrder = t.SortOrder,
            isActive = t.IsActive,
        }));
    }

    [HttpPost]
    public async Task<IActionResult> SaveLocationType([FromBody] SaveLocationTypeInput input, CancellationToken ct)
    {
        try
        {
            var req = new SaveLocationTypeRequest(
                input.Id,
                input.Code ?? string.Empty,
                input.Name ?? string.Empty,
                input.SortOrder,
                input.IsActive);
            var id = await _logisticsConfigurationService.SaveLocationTypeAsync(req, ct);
            return Json(new { success = true, id });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteLocationType(int id, CancellationToken ct)
    {
        var (ok, err) = await _logisticsConfigurationService.DeleteLocationTypeAsync(id, ct);
        return Json(new { success = ok, message = err });
    }

    // ── Malzeme - Lokasyon eslestirmesi (cok-cogu + bir varsayilan) ─────────
    [HttpGet]
    public async Task<IActionResult> GetItemLocations(int itemId, CancellationToken ct)
    {
        var links = await _logisticsConfigurationService.GetItemLocationsAsync(itemId, ct);
        var allLocations = await _logisticsConfigurationService.GetLocationsAsync(ct);

        // Yalniz yaprak (alt kirilimi olmayan) lokasyonlar secilebilir
        var parentIds = allLocations
            .Where(x => x.ParentId.HasValue)
            .Select(x => x.ParentId!.Value)
            .ToHashSet();

        var byId = allLocations.ToDictionary(x => x.Id);
        string buildPath(int id)
        {
            var parts = new List<string>();
            var guard = 0;
            int? cur = id;
            while (cur.HasValue && guard++ < 10 && byId.TryGetValue(cur.Value, out var node))
            {
                var label = string.IsNullOrWhiteSpace(node.LocationName)
                    ? node.LocationCode
                    : node.LocationCode + " - " + node.LocationName;
                parts.Insert(0, label);
                cur = node.ParentId;
            }
            return string.Join(" › ", parts);
        }

        return Json(new
        {
            links = links.Select(l => new
            {
                locationId = l.LocationId,
                locationCode = l.LocationCode,
                locationName = l.LocationName,
                locationTypeCode = l.LocationTypeCode,
                locationPath = buildPath(l.LocationId),
                isDefault = l.IsDefault,
                sortOrder = l.SortOrder
            }),
            availableLocations = allLocations
                .Where(x => x.IsActive && !parentIds.Contains(x.Id))
                .OrderBy(x => x.SortOrder).ThenBy(x => x.LocationCode)
                .Select(x => new
                {
                    id = x.Id,
                    locationCode = x.LocationCode,
                    locationName = x.LocationName,
                    locationTypeCode = x.LocationTypeCode,
                    locationPath = buildPath(x.Id)
                })
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveItemLocations([FromBody] SaveItemLocationsInput input, CancellationToken ct)
    {
        if (input.ItemId <= 0)
            return Json(new { success = false, message = "Malzeme karti ID gerekli." });

        var items = (input.Items ?? [])
            .Where(x => x.LocationId > 0)
            .Select(x => new SaveItemLocationItem(x.LocationId, x.IsDefault))
            .ToList();

        // Tekrar eden lokasyon kontrolu
        var ids = items.Select(x => x.LocationId).ToList();
        if (ids.Distinct().Count() != ids.Count)
            return Json(new { success = false, message = "Ayni lokasyon birden fazla kez secilemez." });

        // En fazla bir default
        if (items.Count(x => x.IsDefault) > 1)
            return Json(new { success = false, message = "Yalnizca bir lokasyon varsayilan olabilir." });

        await _logisticsConfigurationService.SaveItemLocationsAsync(input.ItemId, items, ct);
        return Json(new { success = true });
    }

    // ── Helpers (LogisticsController'dakilerin kopyasi — sonraki refactor'da shared service'e) ──
    private static string LocationTypeDisplayName(string? code) => code?.ToUpperInvariant() switch
    {
        "FACTORY" => "Fabrika",
        "SECTION" => "Bolum",
        "SHELF"   => "Raf",
        "BIN"     => "Hucre",
        _         => code ?? "-",
    };

    private static string NormalizeLocationTypeCode(string? code) =>
        string.Equals(code, "AISLE", StringComparison.OrdinalIgnoreCase) ? "SECTION" : code ?? "SECTION";

    private static bool ContainsInsensitive(string? source, string value) =>
        source is not null && source.Contains(value, StringComparison.OrdinalIgnoreCase);
}

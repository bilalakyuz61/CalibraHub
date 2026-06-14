using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// UnitController — Ölçü Birimi tanimlamalari aggregate'inin ayri controller'i (rapor §2.3).
///
/// Tasinmis endpoint'ler:
///   - GET  /Logistics/Units                  → SmartBoard liste
///   - GET  /Logistics/Units/BoardEntities    → in-place refresh JSON
///   - POST /Logistics/UnitToggle             → aktif/pasif degisikligi
///   - GET  /Logistics/UnitEdit               → form
///   - GET  /Logistics/GetAllMeasureUnits     → JSON liste (filtreli)
///   - GET  /Logistics/GetMeasureUnit/{id}    → JSON tekil
///   - POST /Logistics/SaveMeasureUnitJson    → JSON (insert/update)
///   - POST /Logistics/DeleteMeasureUnitJson  → JSON soft delete
///
/// LogisticsController'da kalan: SaveUnit + DeleteUnit (form-post + ViewModel
/// helper'a bagli eski endpoint'ler) — kullanim azaldikca taşinacak.
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.MeasureUnits)]
public sealed class UnitController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;

    public UnitController(ILogisticsConfigurationService logisticsConfigurationService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
    }

    [HttpGet]
    public async Task<IActionResult> Units(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        // Explicit view path — split sonrasi view'lar /Views/Logistics/ altinda kaldi.
        return View("~/Views/Logistics/MeasureUnitDefinitions.cshtml", new MeasureUnitsSmartBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Logistics/Units/BoardEntities")]
    public async Task<IActionResult> UnitsBoardEntities(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return Json(config);
    }

    [HttpPost("/Logistics/UnitToggle")]
    public async Task<IActionResult> UnitToggle([FromQuery] int id, [FromQuery] bool enabled, CancellationToken ct)
    {
        var all  = await _logisticsConfigurationService.GetUnitsAsync(ct);
        var item = all.FirstOrDefault(x => x.Id == id);
        if (item is null) return Json(new { success = false, message = "Birim bulunamadı" });
        try
        {
            await _logisticsConfigurationService.UpdateUnitAsync(
                new UpdateUnitRequest(id, item.Code, item.Name, item.IntlCode, item.SortOrder, enabled), ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var all = (await _logisticsConfigurationService.GetUnitsAsync(ct))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // SmartBoardBuilder (rapor §2.5)
        return CalibraHub.Application.SmartBoard.SmartBoard.For(all)
            .WithBoardKey("logistics-units")
            .WithTitle("Ölçü Birimleri")
            .WithIcon("Ruler", "emerald")
            .WithSearchPlaceholder("Hızlı ara… (kod, ad)")
            .WithEmptyText("Henüz ölçü birimi tanımlanmamış")
            .WithRefreshUrl("/Logistics/Units/BoardEntities")
            .AddHeaderAction("new", "Yeni Birim", "Plus", "/Logistics/UnitEdit")
            .MapEntities(u =>
            {
                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(u.Id, u.Name, subtitle: u.Code)
                    .WithDescription(u.IntlCode ?? string.Empty)
                    .WithStatusBadge(u.IsActive ? "Aktif" : "Pasif", u.IsActive ? "emerald" : "slate")
                    .AddNumericWidget("w_sort", "Sıra", u.SortOrder.ToString(), color: "slate");

                if (u.IntlCode is not null)
                    eb.AddTextWidget("w_intl", "Uluslararası Kod", u.IntlCode, color: "cyan");
                else
                    eb.AddTextWidget("w_intl", "Uluslararası Kod", "—", color: "slate");

                eb.WithNavigateAction("Düzenle", "Edit2", $"/Logistics/UnitEdit?id={u.Id}");
                eb.AddExtraAction("Edit2", "amber", "Düzenle", "navigate", url: $"/Logistics/UnitEdit?id={u.Id}");
                eb.AddExtraAction(
                    icon: u.IsActive ? "ToggleRight" : "ToggleLeft",
                    color: u.IsActive ? "orange" : "emerald",
                    tooltip: u.IsActive ? "Devre Dışı Bırak" : "Etkinleştir",
                    type: "api-post",
                    apiUrl: $"/Logistics/UnitToggle?id={u.Id}&enabled={(!u.IsActive).ToString().ToLowerInvariant()}");
                eb.AddExtraAction("Trash2", "red", "Sil", "api-post",
                    apiUrl: $"/Logistics/DeleteMeasureUnitJson?id={u.Id}",
                    confirm: $"\"{u.Name}\" birimini silmek istediğinizden emin misiniz?");
                return eb;
            })
            .Build();
    }

    [HttpGet]
    public async Task<IActionResult> UnitEdit(int? id, CancellationToken ct)
    {
        if (id.HasValue)
        {
            var all  = await _logisticsConfigurationService.GetUnitsAsync(ct);
            var item = all.FirstOrDefault(x => x.Id == id.Value);
            if (item is null) return NotFound();
            return View("~/Views/Logistics/UnitEdit.cshtml", new UnitEditViewModel
            {
                Id       = item.Id,
                UnitCode = item.Code,
                UnitName = item.Name,
                IntlCode = item.IntlCode,
                SortOrder = item.SortOrder,
                IsActive = item.IsActive,
            });
        }
        return View("~/Views/Logistics/UnitEdit.cshtml", new UnitEditViewModel { IsActive = true });
    }

    // ── JSON API ──────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAllMeasureUnits(string? search, CancellationToken ct)
    {
        var all = await _logisticsConfigurationService.GetUnitsAsync(ct);
        var filtered = all
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(search) ||
                ContainsInsensitive(x.Code, search) ||
                ContainsInsensitive(x.Name, search))
            .Select(x => new { x.Id, Code = x.Code, Name = x.Name, x.IntlCode, x.SortOrder, x.IsActive });
        return Json(filtered);
    }

    [HttpGet]
    public async Task<IActionResult> GetMeasureUnit(int id, CancellationToken ct)
    {
        var all = await _logisticsConfigurationService.GetUnitsAsync(ct);
        var item = all.FirstOrDefault(x => x.Id == id);
        if (item is null) return NotFound();
        return Json(new { item.Id, Code = item.Code, Name = item.Name, item.IntlCode, item.SortOrder, item.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> SaveMeasureUnitJson([FromBody] UnitInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.UnitCode) || string.IsNullOrWhiteSpace(input.UnitName))
            return Json(new { success = false, message = "Kod ve ad bos olamaz." });
        try
        {
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                await _logisticsConfigurationService.UpdateUnitAsync(
                    new UpdateUnitRequest(input.Id.Value, input.UnitCode, input.UnitName, input.IntlCode, input.SortOrder, input.IsActive), ct);
            }
            else
            {
                await _logisticsConfigurationService.CreateUnitAsync(
                    new CreateUnitRequest(input.UnitCode, input.UnitName, input.IntlCode, input.SortOrder, input.IsActive), ct);
            }
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMeasureUnitJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteUnitAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    private static bool ContainsInsensitive(string? text, string search) =>
        text is not null && text.Contains(search, StringComparison.OrdinalIgnoreCase);
}

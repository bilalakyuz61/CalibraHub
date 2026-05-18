using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// MaterialGroupController — Malzeme Grubu aggregate'inin ayri controller'i (rapor §2.3).
///
/// Tasinmis endpoint'ler (11 endpoint):
///   - GET  /Logistics/MaterialGroups            → SmartBoard liste
///   - GET  /Logistics/MaterialGroupEdit         → form
///   - POST /Logistics/SaveMaterialGroupJson     → JSON
///   - POST /Logistics/DeleteMaterialGroupJson   → JSON
///   - GET  /Logistics/GetAllMaterialGroups      → JSON liste (kategori filtreli)
///   - POST /Logistics/UpsertMaterialGroup       → JSON upsert (kategori listesi doner)
///   - POST /Logistics/DeleteMaterialGroupInline → JSON inline sil
///   - GET  /Logistics/MaterialGroupLookup       → lookup search
///   - GET  /Logistics/GetMaterialGroupMappings  → stock card → grup eslesmesi
///   - POST /Logistics/SaveMaterialGroupMappings → eslesme kaydet
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
public sealed class MaterialGroupController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;

    public MaterialGroupController(ILogisticsConfigurationService logisticsConfigurationService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
    }

    [HttpGet]
    public async Task<IActionResult> MaterialGroups(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return View(new MaterialGroupsSmartBoardViewModel { BoardConfig = config });
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var groups  = await _logisticsConfigurationService.GetMaterialGroupsAsync(null, ct);
        var ordered = groups.OrderBy(g => g.GroupCategory).ThenBy(g => g.GroupCode).ToList();

        // SmartBoardBuilder (rapor §2.5)
        return CalibraHub.Application.SmartBoard.SmartBoard.For(ordered)
            .WithBoardKey("logistics-material-groups")
            .WithTitle("Malzeme Grupları", subtitle: $"{ordered.Count} grup")
            .WithIcon("Tag", "violet")
            .WithSearchPlaceholder("Hızlı ara… (kod, açıklama, kategori)")
            .WithEmptyText("Henüz malzeme grubu tanımlanmamış")
            .AddHeaderAction("new", "Yeni Grup", "Plus", "/Logistics/MaterialGroupEdit")
            .MapEntities(g =>
            {
                var color = g.GroupCategory switch { 1 => "violet", 2 => "indigo", 3 => "blue", 4 => "cyan", _ => "teal" };
                return CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(g.Id, g.GroupCode, subtitle: $"Grup {g.GroupCategory}")
                    .WithDescription(g.GroupDescription ?? string.Empty)
                    .AddTextWidget("w_cat", "Kategori", $"Grup {g.GroupCategory}", color: color)
                    .WithEditAndDelete(
                        editUrl:       $"/Logistics/MaterialGroupEdit?id={g.Id}",
                        deleteApiUrl:  $"/Logistics/DeleteMaterialGroupJson?id={g.Id}",
                        deleteConfirm: $"Bu grubu silmek istediğinize emin misiniz? ({g.GroupCode})");
            })
            .Build();
    }

    [HttpGet]
    public async Task<IActionResult> MaterialGroupEdit(int? id, CancellationToken ct)
    {
        if (id.HasValue)
        {
            var all  = await _logisticsConfigurationService.GetMaterialGroupsAsync(null, ct);
            var item = all.FirstOrDefault(g => g.Id == id.Value);
            if (item is null) return NotFound();
            return View(new MaterialGroupEditViewModel
            {
                Id               = item.Id,
                GroupCategory    = item.GroupCategory,
                GroupCode        = item.GroupCode,
                GroupDescription = item.GroupDescription,
            });
        }
        return View(new MaterialGroupEditViewModel { GroupCategory = 1 });
    }

    [HttpPost]
    public async Task<IActionResult> SaveMaterialGroupJson([FromBody] SaveMaterialGroupRequest request, CancellationToken ct)
    {
        if (request is null) return Json(new { success = false, message = "Geçersiz istek." });
        try
        {
            if (request.Id is > 0)
                await _logisticsConfigurationService.UpdateMaterialGroupAsync(request, ct);
            else
                await _logisticsConfigurationService.CreateMaterialGroupAsync(request, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMaterialGroupJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteMaterialGroupAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllMaterialGroups(int? category, CancellationToken ct)
    {
        var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(category, ct);
        return Json(groups.Select(g => new { id = g.Id, category = g.GroupCategory, code = g.GroupCode, description = g.GroupDescription ?? string.Empty }));
    }

    [HttpPost]
    public async Task<IActionResult> UpsertMaterialGroup([FromBody] SaveMaterialGroupRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { success = false, message = "Geçersiz istek." });
        try
        {
            if (request.Id is > 0)
                await _logisticsConfigurationService.UpdateMaterialGroupAsync(request, ct);
            else
                await _logisticsConfigurationService.CreateMaterialGroupAsync(request, ct);
            var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(request.GroupCategory, ct);
            return Ok(new { success = true, groups = groups.Select(g => new { id = g.Id, category = g.GroupCategory, code = g.GroupCode, description = g.GroupDescription ?? string.Empty }) });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMaterialGroupInline([FromBody] DeleteMaterialGroupBody body, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteMaterialGroupAsync(body.Id, ct);
            var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(body.Category, ct);
            return Ok(new { success = true, groups = groups.Select(g => new { id = g.Id, category = g.GroupCategory, code = g.GroupCode, description = g.GroupDescription ?? string.Empty }) });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> MaterialGroupLookup(int? category, string? q, CancellationToken ct)
    {
        var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(category, ct);
        var q2 = q?.Trim() ?? string.Empty;
        var result = groups
            .Where(g => string.IsNullOrWhiteSpace(q2) ||
                        g.GroupCode.Contains(q2, StringComparison.OrdinalIgnoreCase) ||
                        (g.GroupDescription?.Contains(q2, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(g => g.GroupCode)
            .Take(30)
            .Select(g => new { code = g.GroupCode, description = g.GroupDescription ?? string.Empty })
            .ToArray();
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialGroupMappings(int stockCardId, CancellationToken ct)
    {
        var mappings = await _logisticsConfigurationService.GetMaterialGroupMappingsAsync(stockCardId, ct);
        return Json(mappings.Select(m => new { slotOrder = m.SlotOrder, groupCode = m.GroupCode, groupDescription = m.GroupDescription ?? string.Empty }));
    }

    [HttpPost]
    public async Task<IActionResult> SaveMaterialGroupMappings([FromBody] SaveMaterialGroupMappingsRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { success = false, message = "Geçersiz istek." });
        try
        {
            await _logisticsConfigurationService.SaveMaterialGroupMappingsAsync(request, ct);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}

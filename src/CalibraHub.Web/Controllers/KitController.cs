using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// KitController — Kit (Paket Urun) icerik yonetimi. Kit, Items'ta TypeId=10 (Kit)
/// tipinde bir malzeme kartidir; icerigi ItemKit + ItemKitLine tablolarinda tutulur.
/// BomController'in yalin analoğu — rota/fire/multi-level yok, versiyon + fiyat modu var.
///
///   - GET  /Logistics/KitEdit        → sade kit editor view
///   - GET  /Logistics/GetKit         → itemId (veya materialCode) ile aktif kit icerigi (JSON)
///   - POST /Logistics/SaveKit        → JSON upsert (yeni=VersionNo 1, mevcut=VersionNo++)
///   - POST /Logistics/DeleteKitJson  → JSON soft delete
///
/// Yetki: MaterialCardEdit (kit bir malzeme kartidir; ayri bir yetki uretilmez).
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.MaterialCardEdit)]
public sealed class KitController : Controller
{
    private readonly ILogisticsConfigurationService _logistics;

    public KitController(ILogisticsConfigurationService logistics)
    {
        _logistics = logistics;
    }

    [HttpGet]
    public IActionResult KitEdit(int? itemId, string? code, string? name)
    {
        // code/name MaterialCardEdit "Kit Icerigi" sekmesinden query ile gelir
        // (yeni kit henuz kaydedilmediginde GetKit itemCode/itemName dondurmez).
        ViewBag.Title = "Kit İçeriği Düzenle";
        ViewData["KitItemId"] = itemId ?? 0;
        ViewData["KitItemCode"] = code ?? string.Empty;
        ViewData["KitItemName"] = name ?? string.Empty;
        return View("~/Views/Logistics/KitEdit.cshtml");
    }

    [HttpGet]
    public async Task<IActionResult> GetKit(int itemId, string? materialCode, CancellationToken ct)
    {
        // itemId oncelikli; yoksa materialCode → itemId cozumle (MaterialCardEdit kit
        // sekmesi durum sorgusu kod ile gelebilir).
        if (itemId <= 0 && !string.IsNullOrWhiteSpace(materialCode))
        {
            var items = await _logistics.GetItemsForLookupAsync(ct);
            var match = items.FirstOrDefault(i =>
                string.Equals(i.Code?.Trim(), materialCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) itemId = match.Id;
        }
        if (itemId <= 0) return Ok(new { found = false });

        var kit = await _logistics.GetKitByItemAsync(itemId, ct);
        if (kit is null) return Ok(new { found = false });

        return Ok(BuildKitResponse(kit));
    }

    [HttpPost]
    public async Task<IActionResult> SaveKit([FromBody] SaveItemKitRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { success = false, message = "Geçersiz istek." });

        try
        {
            var id = await _logistics.SaveKitAsync(request, CurrentUserId(), ct);
            return Ok(new { success = true, id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteKitJson(int itemId, CancellationToken ct)
    {
        try
        {
            await _logistics.DeleteKitAsync(itemId, CurrentUserId(), ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    private static object BuildKitResponse(ItemKitDto kit) => new
    {
        found       = true,
        id          = kit.Id,
        itemId      = kit.ItemId,
        itemCode    = kit.ItemCode,
        itemName    = kit.ItemName,
        versionNo   = kit.VersionNo,
        priceMode   = kit.PriceMode,
        fixedPrice  = kit.FixedPrice,
        description = kit.Description,
        lines       = kit.Lines.Select(l => new
        {
            itemId                = l.ItemId,
            componentMaterialCode = l.ItemCode,
            componentMaterialName = l.ItemName,
            configId              = l.ConfigId,
            componentConfigCode   = l.ConfigCode,
            quantity              = l.Quantity,
            note                  = l.Note,
        }),
    };
}

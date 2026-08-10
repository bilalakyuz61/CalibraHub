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
///   - GET  /Logistics/KitEdit        → (eski editor sayfasi) malzeme kartina 302 redirect
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

    /// <summary>
    /// 2026-08-06 (kullanici karari): kit icerigi icin AYRI SAYFA YOK — editor artik
    /// Malzeme Karti "Kit Icerigi" sekmesine gomulu (Views/Logistics/_KitEditorPane.cshtml).
    /// Bu action yalnizca eski baglantilar/yer imleri kirilmasin diye malzeme kartina
    /// yonlendirir (kalici degil: icerik tasindi, adres degisti).
    /// </summary>
    [HttpGet]
    public IActionResult KitEdit(int? itemId)
    {
        return (itemId is > 0)
            ? RedirectToAction("MaterialCardEdit", "Logistics", new { id = itemId.Value })
            : RedirectToAction("MaterialCards", "Logistics");
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

    /// <summary>
    /// Kit revizyon gecmisi ozeti (salt-okunur). Kit kartinin TUM gecmisi — kit silinip
    /// yeniden kurulmus olsa bile kesintisiz doner.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> KitRevisions(int itemId, CancellationToken ct)
    {
        if (itemId <= 0) return Ok(new { items = Array.Empty<object>() });

        var revisions = await _logistics.GetKitRevisionsAsync(itemId, ct);
        return Ok(new
        {
            items = revisions.Select(r => new
            {
                id         = r.Id,
                itemKitId  = r.ItemKitId,
                revisionNo = r.RevisionNo,
                priceMode  = r.PriceMode,
                fixedPrice = r.FixedPrice,
                lineCount  = r.LineCount,
                createdBy  = r.CreatedBy,
                createdAt  = r.CreatedAt,
            }),
        });
    }

    /// <summary>Tek revizyonun bilesen dokumu (JSON snapshot'tan cozulur).</summary>
    [HttpGet]
    public async Task<IActionResult> KitRevisionDetail(int id, CancellationToken ct)
    {
        var detail = await _logistics.GetKitRevisionDetailAsync(id, ct);
        if (detail is null) return Ok(new { found = false });

        return Ok(new
        {
            found       = true,
            id          = detail.Id,
            revisionNo  = detail.RevisionNo,
            priceMode   = detail.PriceMode,
            fixedPrice  = detail.FixedPrice,
            description = detail.Description,
            createdBy   = detail.CreatedBy,
            createdAt   = detail.CreatedAt,
            lines       = detail.Lines.Select(l => new
            {
                itemId                = l.ItemId,
                componentMaterialCode = l.ItemCode,
                componentMaterialName = l.ItemName,
                configId              = l.ConfigId,
                componentConfigCode   = l.ConfigCode,
                quantity              = l.Quantity,
                unitId                = l.UnitId,
                unit                  = l.UnitCode,
                unitPrice             = l.UnitPrice,
                note                  = l.Note,
            }),
        });
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
            unitPrice             = l.UnitPrice,   // yalniz PriceMode=FixedComponent iken dolu
            unitId                = l.UnitId,      // secili olcu birimi (Unit.Id) — NULL = bilesenin varsayilan birimi
            unit                  = l.UnitCode,    // goruntuleme icin birim kodu (KitEdit.cshtml l.unit okur)
        }),
    };
}

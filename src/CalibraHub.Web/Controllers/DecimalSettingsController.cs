using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Ondalık Ayarları — Ayarlar menüsü altındaki yönetim ekranı.
///   - GET  /Admin/DecimalSettings           → view
///   - GET  /Admin/DecimalSettings/ListJson  → '*' + tüm formlar (LEFT JOIN ayar)
///   - POST /Admin/DecimalSettings/SaveJson  → upsert (CSRF)
///   - POST /Admin/DecimalSettings/ResetJson → form kaydını sil → varsayılana dön (CSRF)
///
/// Runtime endpoint'i (Effective) DecimalRuntimeController'dadır — tüm oturumlu
/// kullanıcılar erişir, PermissionScope'a tabi değildir.
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.CompanySettings)]
public sealed class DecimalSettingsController : Controller
{
    private readonly IDecimalSettingService _decimals;

    public DecimalSettingsController(IDecimalSettingService decimals)
    {
        _decimals = decimals;
    }

    private int? GetUserId() =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;

    [HttpGet("/Admin/DecimalSettings")]
    public IActionResult Index()
    {
        ViewData["AdminMenu"] = "settings";
        return View();
    }

    [HttpGet("/Admin/DecimalSettings/ListJson")]
    public async Task<IActionResult> ListJson(CancellationToken ct)
    {
        var rows = await _decimals.GetPageRowsAsync(ct);
        return Json(new { ok = true, rows });
    }

    [HttpPost("/Admin/DecimalSettings/SaveJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveJson([FromBody] SaveDecimalSettingRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FormCode))
            return Json(new { ok = false, error = "FormCode zorunlu." });
        try
        {
            await _decimals.SaveAsync(request, GetUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    public sealed record ResetRequest(string FormCode);

    [HttpPost("/Admin/DecimalSettings/ResetJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetJson([FromBody] ResetRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FormCode))
            return Json(new { ok = false, error = "FormCode zorunlu." });
        try
        {
            await _decimals.ResetAsync(request.FormCode, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }
}

/// <summary>
/// Runtime ondalık okuma — hesaplama yapan HER ekran kullanır (yalnızca oturum
/// gerekir, ayar yönetim yetkisi gerekmez). Yeni ekran yazarken:
///   fetch('/Decimals/Effective?formCode=SALES_QUOTE') → { quantity, unitPrice, amount, rate, exchangeRate }
/// </summary>
[Authorize]
public sealed class DecimalRuntimeController : Controller
{
    private readonly IDecimalSettingService _decimals;

    public DecimalRuntimeController(IDecimalSettingService decimals)
    {
        _decimals = decimals;
    }

    [HttpGet("/Decimals/Effective")]
    public async Task<IActionResult> Effective(string? formCode, CancellationToken ct)
    {
        var dec = await _decimals.GetEffectiveAsync(formCode, ct);
        return Json(new
        {
            ok = true,
            formCode = dec.FormCode,
            quantity = dec.Quantity,
            unitPrice = dec.UnitPrice,
            fxUnitPrice = dec.FxUnitPrice,
            amount = dec.Amount,
            rate = dec.Rate,
            exchangeRate = dec.ExchangeRate,
            source = dec.Source,
        });
    }
}

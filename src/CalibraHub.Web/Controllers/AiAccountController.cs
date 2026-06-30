using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-23 — Kullanıcının kendi AI override key'leri (Profil → AI Anahtarlarım).
///   GET    /Account/AiKeys                   → liste (tüm aktif provider'lar + user override durumu)
///   POST   /Account/AiKeys/save              → bir provider için kendi key'ini gir
///   POST   /Account/AiKeys/delete/{providerId}→ override sil (şirket default'una dön)
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.CompanySettings)]
public sealed class AiAccountController : Controller
{
    private readonly IAiUserKeyService _service;
    private readonly ILogger<AiAccountController> _logger;

    public AiAccountController(IAiUserKeyService service, ILogger<AiAccountController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("/Account/AiKeys")]
    public async Task<IActionResult> AiKeys(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0) return Unauthorized();
        var list = await _service.ListForUserAsync(userId, ct);
        return Json(new { ok = true, keys = list });
    }

    [HttpPost("/Account/AiKeys/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AiKeysSave([FromBody] SaveAiUserKeyRequest req, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0) return Unauthorized();
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });

        try
        {
            await _service.SaveAsync(userId, req, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AiUserKey save hatası");
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Account/AiKeys/delete/{providerId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AiKeysDelete(int providerId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0) return Unauthorized();
        try
        {
            await _service.DeleteAsync(userId, providerId, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : 0;
    }
}

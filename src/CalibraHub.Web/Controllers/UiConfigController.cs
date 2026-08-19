using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Kullanıcı bazlı basit UI tercihleri — şema gerektirmeyen anahtar/değer ayarları
/// (IUserSettingRepository / UserSettings tablosu). 2026-08-19: belge kalem listesi
/// görünüm modu (KART/DATAGRID) — bkz. CalibraLineItemsGrid frontend sözleşmesi.
/// </summary>
[Authorize]
public sealed class UiConfigController : Controller
{
    private readonly IUserSettingRepository _userSettings;
    private readonly ILogger<UiConfigController> _logger;

    public UiConfigController(IUserSettingRepository userSettings, ILogger<UiConfigController> logger)
    {
        _userSettings = userSettings;
        _logger = logger;
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;

    public sealed record LineViewModeRequest(string? Mode);

    /// <summary>
    /// Belge kalem listesi görünüm modunu (KART/DATAGRID) kaydeder — kullanıcı bazlı.
    /// Geçersiz mode değeri REDDEDİLİR (sessizce "card"a düşürülmez) — istemci kendi
    /// tarafında zaten yalnız "card"/"grid" gönderir; beklenmeyen bir değer istemci
    /// hatasına işaret eder ve kullanıcıya bildirilmelidir.
    /// </summary>
    [HttpPost("/UiConfig/LineViewMode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LineViewMode([FromBody] LineViewModeRequest? request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Json(new { ok = false, error = "Oturum kullanıcısı çözümlenemedi." });

        var mode = request?.Mode?.Trim();
        if (!UiConfigKeys.IsValidViewMode(mode))
            return Json(new { ok = false, error = "Geçersiz görünüm modu. 'card' veya 'grid' olmalı." });

        mode = UiConfigKeys.NormalizeViewMode(mode);

        try
        {
            await _userSettings.SetAsync(userId.Value, UiConfigKeys.LineGridViewMode, mode, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kalem görünüm modu kaydedilemedi (userId={UserId}, mode={Mode})", userId, mode);
            return Json(new { ok = false, error = "Görünüm modu kaydedilemedi. Ayrıntılar sunucu loglarında." });
        }
    }
}

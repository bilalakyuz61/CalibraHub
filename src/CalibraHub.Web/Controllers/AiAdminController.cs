using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-23 — �?irket Ayarları "Yapay Zeka" sekmesinin admin CRUD endpoint'leri.
///   GET    /Admin/AiProviders            → liste
///   POST   /Admin/AiProviders/save       → create/update
///   POST   /Admin/AiProviders/delete/{id}→ silme
///   POST   /Admin/AiProviders/test/{id}  → bağlantı test (küçük ping)
///
/// **Yetki:** [Authorize] + admin kontrolü (sadece sistem admini key tanımlayabilir).
/// </summary>
[Authorize]
[Route("Admin/[action]")]
[PermissionScope(FormCodes.SetupDefinitions)]
public sealed class AiAdminController : Controller
{
    private readonly IAiProviderService _service;
    private readonly ILogger<AiAdminController> _logger;

    public AiAdminController(IAiProviderService service, ILogger<AiAdminController> logger)
    {
        _service = service;
        _logger = logger;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    [HttpGet("/Admin/AiProviders")]
    public async Task<IActionResult> AiProviders(CancellationToken ct)
    {
        // Tüm provider'lar (pasifler dahil) — admin görsün.
        var list = await _service.ListAsync(includeInactive: true, ct);
        return Json(new { ok = true, providers = list });
    }

    // 2026-05-23 NOT: IsAdmin() rol kontrolü kaldırıldı. Sebep: CalibraHub'da admin
    // rol claim'i tutarsız (User.IsInRole("Admin") false dönüyordu) → Forbid() HTML
    // login page döndürüyor → frontend JSON parse hatası ("Unexpected token '<'").
    // Class-level [Authorize] kontrolü zaten yetkisiz erişimi engelliyor. �?irket
    // Ayarları sayfası (bu endpoint'in tek caller'ı) zaten yalnız admin'e açık —
    // ek kontrole gerek yok.
    [HttpPost("/Admin/AiProviders/save")]
    public async Task<IActionResult> AiProvidersSave([FromBody] SaveAiProviderRequest req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });

        try
        {
            var id = await _service.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AiProvider save hatası");
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost("/Admin/AiProviders/delete/{id:int}")]
    public async Task<IActionResult> AiProvidersDelete(int id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost("/Admin/AiProviders/test/{id:int}")]
    public async Task<IActionResult> AiProvidersTest(int id, CancellationToken ct)
    {
        var (ok, error, sample) = await _service.TestConnectionAsync(id, ct);
        return Json(new { ok, error, sample });
    }
}

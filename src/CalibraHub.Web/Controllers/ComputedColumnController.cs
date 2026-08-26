using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Hesaplanan Kolon yönetimi — liste ekranlarına SQL VIEW'dan beslenen salt-okunur kolon.
///
/// SystemAdmin-only (SetupDefinitions): tanım, veritabanı şemasına doğrudan bakan bir
/// araçtır ve /ViewBuilder ile birlikte kullanılır — aynı kapı, aynı bağlam.
///
/// Uçlar SQL KABUL ETMEZ. Gelen her şey tanımlayıcıdır ve depo katmanında sys kataloğuna
/// karşı doğrulanır; doğrulanmayan ad sorguya hiç girmez.
/// </summary>
[Authorize]
[PermissionScope(FormCodes.SetupDefinitions)]
public sealed class ComputedColumnController : Controller
{
    private readonly IComputedColumnRepository _repo;
    private readonly ILogger<ComputedColumnController> _logger;

    public ComputedColumnController(IComputedColumnRepository repo, ILogger<ComputedColumnController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet("/ComputedColumn")]
    public IActionResult Index() => View("~/Views/ComputedColumn/Index.cshtml");

    [HttpGet("/ComputedColumn/api/list")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            return Json(new { ok = true, items = await _repo.GetAllAsync(ct) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Liste okunamadı.");
            return Json(new { ok = false, error = "Liste okunamadı." });
        }
    }

    /// <summary>Seçilebilecek view'lar + kolonları. Tanım ekranındaki açılır listeleri besler.</summary>
    [HttpGet("/ComputedColumn/api/sources")]
    public async Task<IActionResult> Sources(CancellationToken ct)
    {
        try
        {
            return Json(new { ok = true, sources = await _repo.GetSourcesAsync(ct) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Kaynak view listesi okunamadı.");
            return Json(new { ok = false, error = "Kaynak listesi okunamadı." });
        }
    }

    /// <summary>
    /// Kaydetmeden ÖNCE deneme okuması. Süreyi de döndürür: yavaş bir view'ın liste
    /// ekranını kilitleyeceği ancak burada fark edilebilir.
    /// </summary>
    [HttpPost("/ComputedColumn/api/preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview([FromBody] SaveComputedColumnRequest request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        var result = await _repo.PreviewAsync(request, 3, ct);
        return Json(new
        {
            ok = result.Ok,
            error = result.Error,
            elapsedMs = result.ElapsedMs,
            rows = result.Rows,
        });
    }

    [HttpPost("/ComputedColumn/api/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveComputedColumnRequest request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var id = await _repo.SaveAsync(request, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (ArgumentException ex)
        {
            // Doğrulama hatası kullanıcıya AYNEN döner — "view bulunamadı", "kolon yok" gibi
            // mesajlar teşhis ettirir; jenerik bir metin kullanıcıyı çıkmaza sokardı.
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Kaydetme başarısız. Label={Label}", request.Label);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/ComputedColumn/api/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromQuery] int id, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Silme başarısız. Id={Id}", id);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    private int? CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;
}

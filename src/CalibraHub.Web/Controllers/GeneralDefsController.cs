using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Genel Tanımlamalar sayfası — Üretim Tanımlamaları benzeri gruplu tanım ekranı.
/// İlk grup: Adres Tanımlama (Ülke → Şehir → İlçe hiyerarşisi). İleride yeni
/// genel tanım grupları aynı sayfaya sekme olarak eklenir.
///
///   GET  /GeneralDefs                    → sayfa
///   GET  /GeneralDefs/Countries          → ülke listesi (şehir sayısıyla)
///   POST /GeneralDefs/SaveCountry        → ekle/güncelle (CSRF)
///   POST /GeneralDefs/DeleteCountry      → sil (şehir varsa engellenir)
///   GET  /GeneralDefs/Cities?countryId=  → şehir listesi
///   POST /GeneralDefs/SaveCity | DeleteCity
///   GET  /GeneralDefs/Districts?cityId=  → ilçe listesi
///   POST /GeneralDefs/SaveDistrict | DeleteDistrict
/// </summary>
[Authorize]
[Route("GeneralDefs")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.GeneralDefs)]
public sealed class GeneralDefsController : Controller
{
    private readonly IAddressDefinitionRepository _repo;

    public GeneralDefsController(IAddressDefinitionRepository repo)
    {
        _repo = repo;
    }

    private int? GetUserId() =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;

    [HttpGet("")]
    public IActionResult Index() => View();

    // ── Ülke ─────────────────────────────────────────────────────────────
    [HttpGet("Countries")]
    public async Task<IActionResult> Countries(CancellationToken ct)
    {
        var rows = await _repo.ListCountriesAsync(ct);
        return Json(new { ok = true, rows });
    }

    [HttpPost("SaveCountry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCountry([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _repo.SaveCountryAsync(request?.Id, request?.Name ?? "", GetUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteCountry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCountry([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteCountryAsync(request?.Id ?? 0, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    // ── Şehir ────────────────────────────────────────────────────────────
    [HttpGet("Cities")]
    public async Task<IActionResult> Cities(int countryId, CancellationToken ct)
    {
        var rows = await _repo.ListCitiesAsync(countryId, ct);
        return Json(new { ok = true, rows });
    }

    [HttpPost("SaveCity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCity([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _repo.SaveCityAsync(request?.Id, request?.ParentId ?? 0, request?.Name ?? "", GetUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteCity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCity([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteCityAsync(request?.Id ?? 0, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    // ── İlçe ─────────────────────────────────────────────────────────────
    [HttpGet("Districts")]
    public async Task<IActionResult> Districts(int cityId, CancellationToken ct)
    {
        var rows = await _repo.ListDistrictsAsync(cityId, ct);
        return Json(new { ok = true, rows });
    }

    [HttpPost("SaveDistrict")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDistrict([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _repo.SaveDistrictAsync(request?.Id, request?.ParentId ?? 0, request?.Name ?? "", GetUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteDistrict")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDistrict([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteDistrictAsync(request?.Id ?? 0, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }
}

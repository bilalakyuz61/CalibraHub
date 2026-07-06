using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Genel Tanımlamalar — Üretim Tanımlamaları pattern'i: _GeneralDefsTabs sekme barı +
/// her sekme bir SmartBoard (C-Grid) listesi. İlk grup: Adres Tanımlama
/// (Ülkeler / Şehirler / İlçeler).
///
///   GET  /GeneralDefs                     → Countries'e redirect
///   GET  /GeneralDefs/Countries|Cities|Districts        → SmartBoard sayfaları
///   GET  /GeneralDefs/*BoardConfig                      → in-place refresh JSON
///   GET  /GeneralDefs/CountryEdit|CityEdit|DistrictEdit → form (yeni/düzenle)
///   GET  /GeneralDefs/CountriesLookup / CitiesLookup    → edit dropdown'ları
///   POST /GeneralDefs/SaveCountry|SaveCity|SaveDistrict → kaydet (CSRF, JSON body)
///   POST /GeneralDefs/DeleteCountryJson?id= (vb.)       → SmartCard sil aksiyonu (CSRF)
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
    public IActionResult Index() => Redirect("/GeneralDefs/Countries");

    // ══════════════════════════════════════════════════════════════════
    // Ülkeler
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("Countries")]
    public async Task<IActionResult> Countries(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildCountriesBoardConfigAsync(ct);
        return View();
    }

    [HttpGet("CountriesBoardConfig")]
    public async Task<IActionResult> CountriesBoardConfig(CancellationToken ct)
        => Json(await BuildCountriesBoardConfigAsync(ct));

    private async Task<object> BuildCountriesBoardConfigAsync(CancellationToken ct)
    {
        var rows = await _repo.ListCountriesAsync(ct);
        var entities = rows.Select(c => new
        {
            id          = c.Id,
            title       = c.Name,
            subtitle    = c.Code,
            statusBadge = (object)new { label = "Aktif", color = "emerald" },
            widgets = new object[]
            {
                new { id = "w_cities", type = "data", dataType = "numeric", label = "Şehir",
                      value = c.CityCount.ToString(), detail = "şehir", color = "indigo" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/GeneralDefs/CountryEdit?id={c.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil", icon = "Trash2",
                apiUrl    = $"/GeneralDefs/DeleteCountryJson?id={c.Id}",
                apiMethod = "POST",
                confirm   = $"Bu ülkeyi silmek istediğinize emin misiniz? ({c.Name})",
            },
        }).ToArray();

        return new
        {
            boardKey          = "generaldefs-countries",
            title             = "Ülke Tanımlamaları",
            subtitle          = $"{rows.Count} ülke",
            icon              = "Globe",
            iconColor         = "indigo",
            refreshUrl        = "/GeneralDefs/CountriesBoardConfig",
            searchPlaceholder = "Hızlı ara… (ülke adı)",
            emptyText         = "Henüz ülke tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Ülke", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/CountryEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_cities", type = "data", dataType = "numeric", label = "Şehir" },
            },
            entities,
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // Şehirler
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("Cities")]
    public async Task<IActionResult> Cities(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildCitiesBoardConfigAsync(ct);
        return View();
    }

    [HttpGet("CitiesBoardConfig")]
    public async Task<IActionResult> CitiesBoardConfig(CancellationToken ct)
        => Json(await BuildCitiesBoardConfigAsync(ct));

    private async Task<object> BuildCitiesBoardConfigAsync(CancellationToken ct)
    {
        var rows = await _repo.ListAllCitiesAsync(ct);
        var countryOptions = rows.Select(r => r.CountryName).Distinct().OrderBy(x => x)
            .Select(x => new { value = x, label = x }).Cast<object>().ToList();

        var entities = rows.Select(c => new
        {
            id          = c.Id,
            title       = c.Name,
            subtitle    = c.CountryName,
            statusBadge = (object)new { label = "Aktif", color = "emerald" },
            widgets = new object[]
            {
                new { id = "w_country",   type = "data", dataType = "options", label = "Ülke",
                      value = c.CountryName, color = "blue" },
                new { id = "w_districts", type = "data", dataType = "numeric", label = "İlçe",
                      value = c.DistrictCount.ToString(), detail = "ilçe", color = "indigo" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/GeneralDefs/CityEdit?id={c.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil", icon = "Trash2",
                apiUrl    = $"/GeneralDefs/DeleteCityJson?id={c.Id}",
                apiMethod = "POST",
                confirm   = $"Bu şehri silmek istediğinize emin misiniz? ({c.Name})",
            },
        }).ToArray();

        return new
        {
            boardKey          = "generaldefs-cities",
            title             = "Şehir Tanımlamaları",
            subtitle          = $"{rows.Count} şehir",
            icon              = "Building2",
            iconColor         = "blue",
            refreshUrl        = "/GeneralDefs/CitiesBoardConfig",
            searchPlaceholder = "Hızlı ara… (şehir, ülke)",
            emptyText         = "Henüz şehir tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Şehir", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/CityEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_country",   type = "data", dataType = "options", label = "Ülke", options = countryOptions },
                new { id = "w_districts", type = "data", dataType = "numeric", label = "İlçe" },
            },
            entities,
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // İlçeler
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("Districts")]
    public async Task<IActionResult> Districts(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildDistrictsBoardConfigAsync(ct);
        return View();
    }

    [HttpGet("DistrictsBoardConfig")]
    public async Task<IActionResult> DistrictsBoardConfig(CancellationToken ct)
        => Json(await BuildDistrictsBoardConfigAsync(ct));

    private async Task<object> BuildDistrictsBoardConfigAsync(CancellationToken ct)
    {
        var rows = await _repo.ListAllDistrictsAsync(ct);
        var cityOptions = rows.Select(r => r.CityName).Distinct().OrderBy(x => x)
            .Select(x => new { value = x, label = x }).Cast<object>().ToList();

        var entities = rows.Select(d => new
        {
            id          = d.Id,
            title       = d.Name,
            subtitle    = $"{d.CityName} / {d.CountryName}",
            statusBadge = (object)new { label = "Aktif", color = "emerald" },
            widgets = new object[]
            {
                new { id = "w_city",    type = "data", dataType = "options", label = "Şehir",
                      value = d.CityName, color = "blue" },
                new { id = "w_country", type = "data", dataType = "options", label = "Ülke",
                      value = d.CountryName, color = "slate" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/GeneralDefs/DistrictEdit?id={d.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil", icon = "Trash2",
                apiUrl    = $"/GeneralDefs/DeleteDistrictJson?id={d.Id}",
                apiMethod = "POST",
                confirm   = $"Bu ilçeyi silmek istediğinize emin misiniz? ({d.Name})",
            },
        }).ToArray();

        return new
        {
            boardKey          = "generaldefs-districts",
            title             = "İlçe Tanımlamaları",
            subtitle          = $"{rows.Count} ilçe",
            icon              = "MapPin",
            iconColor         = "violet",
            refreshUrl        = "/GeneralDefs/DistrictsBoardConfig",
            searchPlaceholder = "Hızlı ara… (ilçe, şehir, ülke)",
            emptyText         = "Henüz ilçe tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni İlçe", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/DistrictEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_city",    type = "data", dataType = "options", label = "Şehir", options = cityOptions },
                new { id = "w_country", type = "data", dataType = "options", label = "Ülke" },
            },
            entities,
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // Edit sayfaları
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("CountryEdit")]
    public async Task<IActionResult> CountryEdit(int? id, CancellationToken ct)
    {
        ViewBag.Item = id is > 0 ? await _repo.GetCountryAsync(id.Value, ct) : null;
        if (id is > 0 && ViewBag.Item is null) return NotFound();
        return View();
    }

    [HttpGet("CityEdit")]
    public async Task<IActionResult> CityEdit(int? id, CancellationToken ct)
    {
        ViewBag.Item = id is > 0 ? await _repo.GetCityAsync(id.Value, ct) : null;
        if (id is > 0 && ViewBag.Item is null) return NotFound();
        return View();
    }

    [HttpGet("DistrictEdit")]
    public async Task<IActionResult> DistrictEdit(int? id, CancellationToken ct)
    {
        DistrictDto? item = id is > 0 ? await _repo.GetDistrictAsync(id.Value, ct) : null;
        if (id is > 0 && item is null) return NotFound();
        ViewBag.Item = item;
        // Cascade ön seçimi için ilçenin şehri üzerinden ülke id'si
        ViewBag.CountryId = item is not null ? (await _repo.GetCityAsync(item.CityId, ct))?.CountryId : null;
        return View();
    }

    // ── Edit dropdown lookup'ları ─────────────────────────────────────
    [HttpGet("CountriesLookup")]
    public async Task<IActionResult> CountriesLookup(CancellationToken ct)
    {
        var rows = await _repo.ListCountriesAsync(ct);
        return Json(rows.Select(c => new { id = c.Id, name = c.Name }));
    }

    [HttpGet("CitiesLookup")]
    public async Task<IActionResult> CitiesLookup(int countryId, CancellationToken ct)
    {
        var rows = await _repo.ListCitiesAsync(countryId, ct);
        return Json(rows.Select(c => new { id = c.Id, name = c.Name }));
    }

    // ══════════════════════════════════════════════════════════════════
    // Kaydet / Sil
    // ══════════════════════════════════════════════════════════════════
    [HttpPost("SaveCountry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCountry([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try { return Json(new { ok = true, id = await _repo.SaveCountryAsync(request?.Id, request?.Name ?? "", GetUserId(), ct) }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("SaveCity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCity([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try { return Json(new { ok = true, id = await _repo.SaveCityAsync(request?.Id, request?.ParentId ?? 0, request?.Name ?? "", GetUserId(), ct) }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("SaveDistrict")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDistrict([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try { return Json(new { ok = true, id = await _repo.SaveDistrictAsync(request?.Id, request?.ParentId ?? 0, request?.Name ?? "", GetUserId(), ct) }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    // SmartCard sil aksiyonları — apiUrl query-id pattern'i (DeleteMachineJson benzeri)
    [HttpPost("DeleteCountryJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCountryJson(int id, CancellationToken ct)
    {
        try { await _repo.DeleteCountryAsync(id, ct); return Json(new { ok = true }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteCityJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCityJson(int id, CancellationToken ct)
    {
        try { await _repo.DeleteCityAsync(id, ct); return Json(new { ok = true }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteDistrictJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDistrictJson(int id, CancellationToken ct)
    {
        try { await _repo.DeleteDistrictAsync(id, ct); return Json(new { ok = true }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }
}

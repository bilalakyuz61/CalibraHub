using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Genel Tanımlamalar — Üretim Tanımlamaları pattern'i: _GeneralDefsTabs sekme barı +
/// her sekme bir SmartBoard (C-Grid) listesi. İki grup:
/// 1) Adres Tanımlamaları: Ülkeler / Şehirler / İlçeler / Mahalle-Köy (birleşik).
/// 2) Lokasyon Tanımlamaları: Bölümler / Alt Bölümler.
/// Köy hem ilçe altında (mahalle ile aynı hizada) hem mahalle altında tanımlanabilir.
/// </summary>
[Authorize]
[Route("GeneralDefs")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.GeneralDefs)]
public sealed class GeneralDefsController : Controller
{
    private readonly IAddressDefinitionRepository _repo;
    private readonly ILocationSectionRepository _sections;

    public GeneralDefsController(IAddressDefinitionRepository repo, ILocationSectionRepository sections)
    {
        _repo = repo;
        _sections = sections;
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
            description = c.ForeignName,
            statusBadge = (object)new { label = "Aktif", color = "emerald" },
            widgets = new object[]
            {
                new { id = "w_code",     type = "data", dataType = "text",    label = "Ülke Kodu",
                      value = c.Code ?? "—", color = "violet" },
                new { id = "w_currency", type = "data", dataType = "text",    label = "Para Birimi",
                      value = c.CurrencyCode ?? "—", detail = c.CurrencyName, color = "amber" },
                new { id = "w_cities",   type = "data", dataType = "numeric", label = "Şehir",
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
            searchPlaceholder = "Hızlı ara… (ad, kod, yabancı isim)",
            emptyText         = "Henüz ülke tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Ülke", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/CountryEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_code",     type = "data", dataType = "text",    label = "Ülke Kodu" },
                new { id = "w_currency", type = "data", dataType = "text",    label = "Para Birimi" },
                new { id = "w_cities",   type = "data", dataType = "numeric", label = "Şehir" },
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
                new { id = "w_plate",     type = "data", dataType = "text",    label = "Plaka",
                      value = c.PlateCode ?? "—", color = "violet" },
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
            searchPlaceholder = "Hızlı ara… (şehir, plaka, ülke)",
            emptyText         = "Henüz şehir tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Şehir", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/CityEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_plate",     type = "data", dataType = "text",    label = "Plaka" },
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
    // Mahalle / Köy — aynı idari düzey, TEK birleşik board (tür rozetiyle)
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("Localities")]
    public async Task<IActionResult> Localities(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildLocalitiesBoardConfigAsync(ct);
        return View();
    }

    [HttpGet("LocalitiesBoardConfig")]
    public async Task<IActionResult> LocalitiesBoardConfig(CancellationToken ct)
        => Json(await BuildLocalitiesBoardConfigAsync(ct));

    // Eski ayrı sekme rotaları — birleşik ekrana yönlendir (kayıtlı tab/link'ler kırılmasın)
    [HttpGet("Neighborhoods")]
    public IActionResult Neighborhoods() => Redirect("/GeneralDefs/Localities");

    [HttpGet("Villages")]
    public IActionResult Villages() => Redirect("/GeneralDefs/Localities");

    private async Task<object> BuildLocalitiesBoardConfigAsync(CancellationToken ct)
    {
        var neighborhoods = await _repo.ListAllNeighborhoodsAsync(ct);
        var villages = await _repo.ListAllVillagesAsync(ct);

        var districtOptions = neighborhoods.Select(r => r.DistrictName)
            .Concat(villages.Select(r => r.DistrictName))
            .Distinct().OrderBy(x => x)
            .Select(x => new { value = x, label = x }).Cast<object>().ToList();
        var typeOptions = new List<object>
        {
            new { value = "Mahalle", label = "Mahalle" },
            new { value = "Köy",     label = "Köy" },
        };

        // Entity id çakışmasın diye tür ön ekli string id ("n5" / "v3") kullanılır —
        // SmartBoard id'yi yalnızca React key + highlight karşılaştırmasında kullanır.
        var entities = new List<object>();
        foreach (var n in neighborhoods)
        {
            entities.Add(new
            {
                id          = $"n{n.Id}",
                title       = n.Name,
                subtitle    = $"{n.DistrictName} / {n.CityName}",
                statusBadge = (object)new { label = "Mahalle", color = "emerald" },
                widgets = new object[]
                {
                    new { id = "w_type",     type = "data", dataType = "options", label = "Tür",
                          value = "Mahalle", color = "emerald" },
                    new { id = "w_district", type = "data", dataType = "options", label = "İlçe",
                          value = n.DistrictName, color = "violet" },
                    new { id = "w_city",     type = "data", dataType = "options", label = "Şehir",
                          value = n.CityName, color = "blue" },
                    new { id = "w_info",     type = "data", dataType = "text",    label = "Bağlı Köy",
                          value = n.VillageCount.ToString(), detail = "köy", color = "indigo" },
                },
                primaryAction = new
                {
                    label = "Düzenle", icon = "Edit", color = "amber",
                    url = $"/GeneralDefs/NeighborhoodEdit?id={n.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label     = "Sil", icon = "Trash2",
                    apiUrl    = $"/GeneralDefs/DeleteNeighborhoodJson?id={n.Id}",
                    apiMethod = "POST",
                    confirm   = $"Bu mahalleyi silmek istediğinize emin misiniz? ({n.Name})",
                },
            });
        }
        foreach (var v in villages)
        {
            entities.Add(new
            {
                id          = $"v{v.Id}",
                title       = v.Name,
                subtitle    = v.NeighborhoodName is not null
                    ? $"{v.NeighborhoodName} Mah. / {v.DistrictName}"
                    : $"{v.DistrictName} / {v.CityName}",
                statusBadge = (object)new { label = "Köy", color = "amber" },
                widgets = new object[]
                {
                    new { id = "w_type",     type = "data", dataType = "options", label = "Tür",
                          value = "Köy", color = "amber" },
                    new { id = "w_district", type = "data", dataType = "options", label = "İlçe",
                          value = v.DistrictName, color = "violet" },
                    new { id = "w_city",     type = "data", dataType = "options", label = "Şehir",
                          value = v.CityName, color = "blue" },
                    new { id = "w_info",     type = "data", dataType = "text",    label = "Bağlı Olduğu",
                          value = v.NeighborhoodName ?? "İlçe altında", detail = (string?)null,
                          color = v.NeighborhoodName is not null ? "emerald" : "slate" },
                },
                primaryAction = new
                {
                    label = "Düzenle", icon = "Edit", color = "amber",
                    url = $"/GeneralDefs/VillageEdit?id={v.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label     = "Sil", icon = "Trash2",
                    apiUrl    = $"/GeneralDefs/DeleteVillageJson?id={v.Id}",
                    apiMethod = "POST",
                    confirm   = $"Bu köyü silmek istediğinize emin misiniz? ({v.Name})",
                },
            });
        }

        return new
        {
            boardKey          = "generaldefs-localities",
            title             = "Mahalle / Köy Tanımlamaları",
            subtitle          = $"{neighborhoods.Count} mahalle · {villages.Count} köy",
            icon              = "Home",
            iconColor         = "emerald",
            refreshUrl        = "/GeneralDefs/LocalitiesBoardConfig",
            searchPlaceholder = "Hızlı ara… (mahalle, köy, ilçe, şehir)",
            emptyText         = "Henüz mahalle veya köy tanımlanmamış",
            actions = new object[]
            {
                new { id = "newNg", label = "Yeni Mahalle", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/NeighborhoodEdit" },
                new { id = "newVl", label = "Yeni Köy", icon = "Trees",
                      url = "/GeneralDefs/VillageEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_type",     type = "data", dataType = "options", label = "Tür", options = typeOptions },
                new { id = "w_district", type = "data", dataType = "options", label = "İlçe", options = districtOptions },
                new { id = "w_city",     type = "data", dataType = "options", label = "Şehir" },
                new { id = "w_info",     type = "data", dataType = "text",    label = "Bağlı Olduğu" },
            },
            entities = entities.ToArray(),
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
        ViewBag.CountryId = item is not null ? (await _repo.GetCityAsync(item.CityId, ct))?.CountryId : null;
        return View();
    }

    [HttpGet("NeighborhoodEdit")]
    public async Task<IActionResult> NeighborhoodEdit(int? id, CancellationToken ct)
    {
        NeighborhoodDto? item = id is > 0 ? await _repo.GetNeighborhoodAsync(id.Value, ct) : null;
        if (id is > 0 && item is null) return NotFound();
        ViewBag.Item = item;
        // Cascade ön seçimleri: ilçe → şehir → ülke
        if (item is not null)
        {
            var district = await _repo.GetDistrictAsync(item.DistrictId, ct);
            ViewBag.CityId = district?.CityId;
            ViewBag.CountryId = district is not null ? (await _repo.GetCityAsync(district.CityId, ct))?.CountryId : null;
        }
        return View();
    }

    [HttpGet("VillageEdit")]
    public async Task<IActionResult> VillageEdit(int? id, CancellationToken ct)
    {
        VillageDto? item = id is > 0 ? await _repo.GetVillageAsync(id.Value, ct) : null;
        if (id is > 0 && item is null) return NotFound();
        ViewBag.Item = item;
        if (item is not null)
        {
            var district = await _repo.GetDistrictAsync(item.DistrictId, ct);
            ViewBag.CityId = district?.CityId;
            ViewBag.CountryId = district is not null ? (await _repo.GetCityAsync(district.CityId, ct))?.CountryId : null;
        }
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

    [HttpGet("DistrictsLookup")]
    public async Task<IActionResult> DistrictsLookup(int cityId, CancellationToken ct)
    {
        var rows = await _repo.ListDistrictsAsync(cityId, ct);
        return Json(rows.Select(d => new { id = d.Id, name = d.Name }));
    }

    [HttpGet("NeighborhoodsLookup")]
    public async Task<IActionResult> NeighborhoodsLookup(int districtId, CancellationToken ct)
    {
        var rows = await _repo.ListNeighborhoodsAsync(districtId, ct);
        return Json(rows.Select(n => new { id = n.Id, name = n.Name }));
    }

    [HttpGet("CurrenciesLookup")]
    public async Task<IActionResult> CurrenciesLookup(CancellationToken ct)
    {
        var rows = await _repo.ListCurrenciesLookupAsync(ct);
        return Json(rows.Select(c => new { id = c.Id, code = c.Code, name = c.Name }));
    }

    // ══════════════════════════════════════════════════════════════════
    // Kaydet
    // ══════════════════════════════════════════════════════════════════
    [HttpPost("SaveCountry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCountry([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _repo.SaveCountryAsync(
                request?.Id, request?.Name ?? "", request?.Code, request?.CurrencyId, request?.ForeignName, GetUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("SaveCity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCity([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _repo.SaveCityAsync(
                request?.Id, request?.ParentId ?? 0, request?.Name ?? "", request?.PlateCode, GetUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("SaveDistrict")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDistrict([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try { return Json(new { ok = true, id = await _repo.SaveDistrictAsync(request?.Id, request?.ParentId ?? 0, request?.Name ?? "", GetUserId(), ct) }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("SaveNeighborhood")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNeighborhood([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try { return Json(new { ok = true, id = await _repo.SaveNeighborhoodAsync(request?.Id, request?.ParentId ?? 0, request?.Name ?? "", GetUserId(), ct) }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("SaveVillage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVillage([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try
        {
            // ParentId = DistrictId; NeighborhoodId doluysa köy mahalle altına bağlanır
            var id = await _repo.SaveVillageAsync(
                request?.Id, request?.ParentId ?? 0, request?.NeighborhoodId, request?.Name ?? "", GetUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════
    // SmartCard sil aksiyonları (apiUrl query-id pattern'i)
    // ══════════════════════════════════════════════════════════════════
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

    [HttpPost("DeleteNeighborhoodJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNeighborhoodJson(int id, CancellationToken ct)
    {
        try { await _repo.DeleteNeighborhoodAsync(id, ct); return Json(new { ok = true }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteVillageJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVillageJson(int id, CancellationToken ct)
    {
        try { await _repo.DeleteVillageAsync(id, ct); return Json(new { ok = true }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    // ══════════════════════════════════════════════════════════════════
    // LOKASYON TANIMLAMALARI — Bölümler
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("Sections")]
    public async Task<IActionResult> Sections(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildSectionsBoardConfigAsync(ct);
        return View();
    }

    [HttpGet("SectionsBoardConfig")]
    public async Task<IActionResult> SectionsBoardConfig(CancellationToken ct)
        => Json(await BuildSectionsBoardConfigAsync(ct));

    private async Task<object> BuildSectionsBoardConfigAsync(CancellationToken ct)
    {
        var rows = await _sections.ListSectionsAsync(ct);
        var entities = rows.Select(s => new
        {
            id          = s.Id,
            title       = s.Name,
            subtitle    = s.SubSectionCount > 0 ? $"{s.SubSectionCount} alt bölüm" : "Alt bölüm yok",
            statusBadge = (object)new { label = "Aktif", color = "emerald" },
            widgets = new object[]
            {
                new { id = "w_subs", type = "data", dataType = "numeric", label = "Alt Bölüm",
                      value = s.SubSectionCount.ToString(), detail = "alt bölüm", color = "indigo" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/GeneralDefs/SectionEdit?id={s.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil", icon = "Trash2",
                apiUrl    = $"/GeneralDefs/DeleteSectionJson?id={s.Id}",
                apiMethod = "POST",
                confirm   = $"Bu bölümü silmek istediğinize emin misiniz? ({s.Name})",
            },
        }).ToArray();

        return new
        {
            boardKey          = "generaldefs-sections",
            title             = "Bölüm Tanımlamaları",
            subtitle          = $"{rows.Count} bölüm",
            icon              = "LayoutGrid",
            iconColor         = "indigo",
            refreshUrl        = "/GeneralDefs/SectionsBoardConfig",
            searchPlaceholder = "Hızlı ara… (bölüm adı)",
            emptyText         = "Henüz bölüm tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Bölüm", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/SectionEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_subs", type = "data", dataType = "numeric", label = "Alt Bölüm" },
            },
            entities,
        };
    }

    // ══════════════════════════════════════════════════════════════════
    // LOKASYON TANIMLAMALARI — Alt Bölümler
    // ══════════════════════════════════════════════════════════════════
    [HttpGet("SubSections")]
    public async Task<IActionResult> SubSections(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildSubSectionsBoardConfigAsync(ct);
        return View();
    }

    [HttpGet("SubSectionsBoardConfig")]
    public async Task<IActionResult> SubSectionsBoardConfig(CancellationToken ct)
        => Json(await BuildSubSectionsBoardConfigAsync(ct));

    private async Task<object> BuildSubSectionsBoardConfigAsync(CancellationToken ct)
    {
        var rows = await _sections.ListAllSubSectionsAsync(ct);
        var sectionOptions = rows.Select(r => r.SectionName).Distinct().OrderBy(x => x)
            .Select(x => new { value = x, label = x }).Cast<object>().ToList();

        var entities = rows.Select(x => new
        {
            id          = x.Id,
            title       = x.Name,
            subtitle    = x.SectionName,
            statusBadge = (object)new { label = "Aktif", color = "emerald" },
            widgets = new object[]
            {
                new { id = "w_section", type = "data", dataType = "options", label = "Bölüm",
                      value = x.SectionName, color = "violet" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/GeneralDefs/SubSectionEdit?id={x.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil", icon = "Trash2",
                apiUrl    = $"/GeneralDefs/DeleteSubSectionJson?id={x.Id}",
                apiMethod = "POST",
                confirm   = $"Bu alt bölümü silmek istediğinize emin misiniz? ({x.Name})",
            },
        }).ToArray();

        return new
        {
            boardKey          = "generaldefs-subsections",
            title             = "Alt Bölüm Tanımlamaları",
            subtitle          = $"{rows.Count} alt bölüm",
            icon              = "Grid3x3",
            iconColor         = "violet",
            refreshUrl        = "/GeneralDefs/SubSectionsBoardConfig",
            searchPlaceholder = "Hızlı ara… (alt bölüm, bölüm)",
            emptyText         = "Henüz alt bölüm tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Alt Bölüm", icon = "Plus", variant = "primary",
                      url = "/GeneralDefs/SubSectionEdit" },
            },
            masterWidgets = new List<object>
            {
                new { id = "w_section", type = "data", dataType = "options", label = "Bölüm", options = sectionOptions },
            },
            entities,
        };
    }

    // ── Lokasyon edit sayfaları + lookup + kaydet/sil ─────────────────
    [HttpGet("SectionEdit")]
    public async Task<IActionResult> SectionEdit(int? id, CancellationToken ct)
    {
        ViewBag.Item = id is > 0 ? await _sections.GetSectionAsync(id.Value, ct) : null;
        if (id is > 0 && ViewBag.Item is null) return NotFound();
        return View();
    }

    [HttpGet("SubSectionEdit")]
    public async Task<IActionResult> SubSectionEdit(int? id, CancellationToken ct)
    {
        ViewBag.Item = id is > 0 ? await _sections.GetSubSectionAsync(id.Value, ct) : null;
        if (id is > 0 && ViewBag.Item is null) return NotFound();
        return View();
    }

    [HttpGet("SectionsLookup")]
    public async Task<IActionResult> SectionsLookup(CancellationToken ct)
    {
        var rows = await _sections.ListSectionsAsync(ct);
        return Json(rows.Select(s => new { id = s.Id, name = s.Name }));
    }

    [HttpPost("SaveSection")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSection([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try { return Json(new { ok = true, id = await _sections.SaveSectionAsync(request?.Id, request?.Name ?? "", GetUserId(), ct) }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("SaveSubSection")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSubSection([FromBody] SaveAddressDefRequest request, CancellationToken ct)
    {
        try { return Json(new { ok = true, id = await _sections.SaveSubSectionAsync(request?.Id, request?.ParentId ?? 0, request?.Name ?? "", GetUserId(), ct) }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteSectionJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSectionJson(int id, CancellationToken ct)
    {
        try { await _sections.DeleteSectionAsync(id, ct); return Json(new { ok = true }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    [HttpPost("DeleteSubSectionJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubSectionJson(int id, CancellationToken ct)
    {
        try { await _sections.DeleteSubSectionAsync(id, ct); return Json(new { ok = true }); }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }
}

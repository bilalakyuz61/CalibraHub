using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// PTT posta kodu katalog dropdown API'leri + cariye bagli teslim adres CRUD'leri.
/// Tum endpoint'ler JSON doner — ContactEdit sayfasi JS tarafindan cagirilir.
/// </summary>
[Authorize]
[Route("[controller]/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.Contacts)]
public sealed class AddressController : Controller
{
    private readonly IAddressRepository _repo;
    private readonly IHttpClientFactory _httpClientFactory;

    public AddressController(IAddressRepository repo, IHttpClientFactory httpClientFactory)
    {
        _repo = repo;
        _httpClientFactory = httpClientFactory;
    }

    // ── Katalog durumu (PTT veri yuklu mu?) ─────────────────────────
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var count = await _repo.GetPostalLocalityCountAsync(ct);
        return Json(new { count });
    }

    // ── Cascade dropdown'lar ────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Cities(string? countryCode, CancellationToken ct)
    {
        var list = await _repo.GetCitiesAsync(countryCode, ct);
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> Districts(string city, string? countryCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(city)) return Json(Array.Empty<string>());
        var list = await _repo.GetDistrictsAsync(countryCode, city, ct);
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> Neighborhoods(string city, string district, string? countryCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(district))
            return Json(Array.Empty<object>());
        var list = await _repo.GetNeighborhoodsAsync(countryCode, city, district, ct);
        return Json(list.Select(x => new { name = x.NeighborhoodName, postalCode = x.PostalCode }));
    }

    [HttpGet]
    public async Task<IActionResult> ByPostalCode(string postalCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postalCode)) return Json(null);
        var row = await _repo.FindByPostalCodeAsync(postalCode, ct);
        if (row == null) return Json(null);
        return Json(new
        {
            countryCode = row.CountryCode,
            cityName = row.CityName,
            districtName = row.DistrictName,
            neighborhoodName = row.NeighborhoodName,
            postalCode = row.PostalCode
        });
    }

    // ── PTT import: JSON gonderilir, DB'ye yuklenir ────────────────
    public sealed class ImportPttBody
    {
        public bool ClearExisting { get; set; } = true;
        public List<ImportPttRow> Rows { get; set; } = new();
    }

    public sealed class ImportPttRow
    {
        public string? CountryCode { get; set; } = "TR";
        public string? CityCode { get; set; }
        public string CityName { get; set; } = "";
        public string DistrictName { get; set; } = "";
        public string NeighborhoodName { get; set; } = "";
        public string? PostalCode { get; set; }
    }

    public sealed class ImportFromUrlBody
    {
        /// <summary>Tek URL veya newline ile ayrilmis cok URL (split dataset'ler icin).</summary>
        public string Url { get; set; } = "";
        public bool ClearExisting { get; set; } = true;
    }

    /// <summary>
    /// Sunucu tarafinda public bir veya birden fazla URL'den JSON cekip katalogu doldurur.
    /// Browser CORS sinirlamalarini bypass eder. JSON dizisi key esnekliği destekli:
    /// city: cityName/il/city/sehir_adi
    /// district: districtName/ilce/ilçe/district/ilce_adi
    /// neighborhood: neighborhoodName/mahalle/neighborhood/koy/mahalle_adi
    /// postal code: postalCode/postaKodu/pk/postcode/zip
    /// city code: cityCode/ilKodu/plaka/sehir_id
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ImportFromUrl([FromBody] ImportFromUrlBody body, CancellationToken ct)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Url))
            return Json(new { success = false, message = "URL bos olamaz." });

        var urls = body.Url
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (urls.Count == 0)
            return Json(new { success = false, message = "Gecerli URL bulunamadi." });

        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(3);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CalibraHub-AddressImporter/1.0");

            var allEntities = new List<PostalLocality>();
            var perUrlCounts = new List<object>();

            foreach (var url in urls)
            {
                var json = await http.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);

                // JSON kok bir array degilse: { "data": [...] } veya { "results": [...] } gibi
                // wrapper olabilir — dizi alani tara.
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var firstArr = FindFirstArrayProperty(root);
                    if (firstArr.HasValue) root = firstArr.Value;
                    else return Json(new { success = false, message = "JSON icinde dizi (array) bulunamadi: " + url });
                }
                if (root.ValueKind != JsonValueKind.Array)
                    return Json(new { success = false, message = "JSON kok elemani bir dizi olmali: " + url });

                int beforeCount = allEntities.Count;
                // Hem flat hem nested format desteklenir
                foreach (var item in root.EnumerateArray())
                    WalkLocalityNode(item, null, null, null, allEntities);
                perUrlCounts.Add(new { url, parsed = allEntities.Count - beforeCount });
            }

            if (allEntities.Count == 0)
                return Json(new { success = false, message = "JSON'larda gecerli satir bulunamadi (city/district/neighborhood eksik)." });

            await _repo.BulkInsertPostalLocalitiesAsync(allEntities, body.ClearExisting, ct);
            return Json(new { success = true, inserted = allEntities.Count, perUrl = perUrlCounts });
        }
        catch (HttpRequestException ex) { return Json(new { success = false, message = "URL erisilemedi: " + ex.Message }); }
        catch (JsonException ex)        { return Json(new { success = false, message = "JSON ayristirilamadi: " + ex.Message }); }
        catch (Exception ex)            { return Json(new { success = false, message = ex.Message }); }
    }

    private static string? Pick(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            }
        }
        return null;
    }

    private static readonly string[] _cityKeys     = { "cityName", "il", "city", "City", "sehir_adi", "sehirAdi", "ilAdi", "ilName" };
    private static readonly string[] _districtKeys = { "districtName", "ilce", "ilçe", "district", "District", "ilce_adi", "ilceAdi", "ilceName" };
    private static readonly string[] _ngKeys       = { "neighborhoodName", "mahalle", "neighborhood", "koy", "köy", "Neighborhood", "mahalle_adi", "mahalleAdi", "mahalleName" };
    private static readonly string[] _postalKeys   = { "postalCode", "postaKodu", "pk", "postcode", "zip", "posta_kodu", "postaKod" };
    private static readonly string[] _cityCodeKeys = { "cityCode", "ilKodu", "plaka", "ilkodu", "sehir_id", "sehirId", "ilId" };
    private static readonly string[] _countryKeys  = { "countryCode", "ulke", "ülke", "country", "ulkeKodu" };
    // Iç içe yapilarin alt-array'leri
    private static readonly string[] _districtArrayKeys = { "ilceler", "districts", "Districts", "ilceList" };
    private static readonly string[] _ngArrayKeys       = { "mahalleler", "neighborhoods", "Neighborhoods", "mahalleList", "koyler" };

    /// <summary>
    /// Hem flat (her satir city+district+neighborhood) hem nested (city.ilceler[].mahalleler[])
    /// yapilari recursive olarak yuruyup PostalLocality satirlari uretir. Parent'tan gelen
    /// city/district context'leri child node'lara propagate edilir.
    /// </summary>
    private static void WalkLocalityNode(JsonElement node,
        string? parentCity, string? parentDistrict, string? parentCountry,
        List<PostalLocality> output)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        var country  = Pick(node, _countryKeys) ?? parentCountry ?? "TR";
        var cityHere = Pick(node, _cityKeys) ?? parentCity;
        var distHere = Pick(node, _districtKeys) ?? parentDistrict;
        var ngHere   = Pick(node, _ngKeys);
        var pkHere   = Pick(node, _postalKeys);
        var cityCode = Pick(node, _cityCodeKeys);

        // Eger bu node bir mahalle ise (yani neighborhood adi var), yaprak satir olarak ekle
        if (!string.IsNullOrWhiteSpace(ngHere)
            && !string.IsNullOrWhiteSpace(cityHere)
            && !string.IsNullOrWhiteSpace(distHere))
        {
            output.Add(new PostalLocality
            {
                CountryCode      = country.Trim().ToUpperInvariant(),
                CityCode         = cityCode?.Trim(),
                CityName         = cityHere.Trim(),
                DistrictName     = distHere.Trim(),
                NeighborhoodName = ngHere.Trim(),
                PostalCode       = pkHere?.Trim()
            });
        }

        // Alt seviyelere recurse — districts veya neighborhoods array varsa
        foreach (var arrKey in _districtArrayKeys.Concat(_ngArrayKeys))
        {
            if (node.TryGetProperty(arrKey, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in arr.EnumerateArray())
                    WalkLocalityNode(child, cityHere, distHere, country, output);
            }
        }
    }

    /// <summary>JSON object icindeki ilk array property'i bulur (wrapper data { results: [...] } icin).</summary>
    private static JsonElement? FindFirstArrayProperty(JsonElement obj)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array) return prop.Value;
        }
        // Bir kademe daha dene
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                var nested = FindFirstArrayProperty(prop.Value);
                if (nested.HasValue) return nested.Value;
            }
        }
        return null;
    }

    public sealed class PreviewUrlBody { public string Url { get; set; } = ""; }

    /// <summary>
    /// URL'i indirir, parse eder, ilk 5 satiri + toplam tahmini doner.
    /// Format dogrulamak icin — DB'ye yazmaz.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PreviewUrl([FromBody] PreviewUrlBody body, CancellationToken ct)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Url))
            return Json(new { success = false, message = "URL bos." });
        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CalibraHub-AddressImporter/1.0");
            var json = await http.GetStringAsync(body.Url.Trim(), ct);
            using var doc = JsonDocument.Parse(json);

            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var firstArr = FindFirstArrayProperty(root);
                if (firstArr.HasValue) root = firstArr.Value;
                else return Json(new { success = false, message = "JSON icinde dizi bulunamadi." });
            }
            if (root.ValueKind != JsonValueKind.Array)
                return Json(new { success = false, message = "JSON kok elemani bir dizi olmali." });

            var output = new List<PostalLocality>();
            foreach (var item in root.EnumerateArray())
            {
                WalkLocalityNode(item, null, null, null, output);
                if (output.Count >= 5) break;
            }
            // Toplam icin parse devam etmeyelim — sadece root array buyuklugunu raporla
            var rootCount = root.GetArrayLength();
            return Json(new
            {
                success = true,
                rootArrayLength = rootCount,
                parsedSample = output.Take(5).Select(x => new
                {
                    countryCode = x.CountryCode,
                    cityCode = x.CityCode,
                    cityName = x.CityName,
                    districtName = x.DistrictName,
                    neighborhoodName = x.NeighborhoodName,
                    postalCode = x.PostalCode
                })
            });
        }
        catch (HttpRequestException ex) { return Json(new { success = false, message = "URL erisilemedi: " + ex.Message }); }
        catch (JsonException ex)        { return Json(new { success = false, message = "JSON ayristirilamadi: " + ex.Message }); }
        catch (Exception ex)            { return Json(new { success = false, message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> ImportPtt([FromBody] ImportPttBody body, CancellationToken ct)
    {
        if (body?.Rows == null || body.Rows.Count == 0)
            return Json(new { success = false, message = "Yuklenecek satir yok." });
        try
        {
            var entities = body.Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.CityName)
                         && !string.IsNullOrWhiteSpace(r.DistrictName)
                         && !string.IsNullOrWhiteSpace(r.NeighborhoodName))
                .Select(r => new PostalLocality
                {
                    CountryCode      = string.IsNullOrWhiteSpace(r.CountryCode) ? "TR" : r.CountryCode!.Trim().ToUpperInvariant(),
                    CityCode         = r.CityCode?.Trim(),
                    CityName         = r.CityName.Trim(),
                    DistrictName     = r.DistrictName.Trim(),
                    NeighborhoodName = r.NeighborhoodName.Trim(),
                    PostalCode       = r.PostalCode?.Trim()
                }).ToList();
            await _repo.BulkInsertPostalLocalitiesAsync(entities, body.ClearExisting, ct);
            return Json(new { success = true, inserted = entities.Count });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Cariye bagli teslim adresleri ──────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ByContact(int contactId, CancellationToken ct)
    {
        var list = await _repo.GetAddressesByContactAsync(contactId, ct);
        return Json(list.Select(ToJson));
    }

    public sealed class SaveAddressInput
    {
        public int? Id { get; set; }
        public int ContactId { get; set; }
        public string? Name { get; set; }
        public string? CountryCode { get; set; } = "TR";
        public string? CityName { get; set; }
        public string? DistrictName { get; set; }
        public string? NeighborhoodName { get; set; }
        public string? PostalCode { get; set; }
        public string? AddressLine { get; set; }
        public bool IsDefault { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> SaveAddress([FromBody] SaveAddressInput input, CancellationToken ct)
    {
        if (input == null || input.ContactId <= 0 || string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "ContactId ve Isim zorunludur." });
        try
        {
            var entity = new ContactAddress
            {
                Id               = input.Id ?? 0,
                ContactId        = input.ContactId,
                Name             = input.Name!.Trim(),
                CountryCode      = string.IsNullOrWhiteSpace(input.CountryCode) ? "TR" : input.CountryCode!.Trim().ToUpperInvariant(),
                CityName         = input.CityName?.Trim(),
                DistrictName     = input.DistrictName?.Trim(),
                NeighborhoodName = input.NeighborhoodName?.Trim(),
                PostalCode       = input.PostalCode?.Trim(),
                AddressLine      = input.AddressLine?.Trim(),
                IsDefault        = input.IsDefault,
                CreatedAt        = DateTime.Now
            };
            int id;
            if (entity.Id > 0)
            {
                await _repo.UpdateAddressAsync(entity, ct);
                id = entity.Id;
            }
            else
            {
                id = await _repo.AddAddressAsync(entity, ct);
            }
            if (input.IsDefault) await _repo.SetDefaultAddressAsync(input.ContactId, id, ct);
            return Json(new { success = true, id });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    public sealed class DeleteAddressBody { public int Id { get; set; } }

    [HttpPost]
    public async Task<IActionResult> DeleteAddress([FromBody] DeleteAddressBody body, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteAddressAsync(body.Id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    public sealed class SetDefaultBody { public int ContactId { get; set; } public int AddressId { get; set; } }

    [HttpPost]
    public async Task<IActionResult> SetDefault([FromBody] SetDefaultBody body, CancellationToken ct)
    {
        await _repo.SetDefaultAddressAsync(body.ContactId, body.AddressId, ct);
        return Json(new { success = true });
    }

    private static object ToJson(ContactAddress a) => new
    {
        id = a.Id,
        contactId = a.ContactId,
        name = a.Name,
        countryCode = a.CountryCode,
        cityName = a.CityName,
        districtName = a.DistrictName,
        neighborhoodName = a.NeighborhoodName,
        postalCode = a.PostalCode,
        addressLine = a.AddressLine,
        isDefault = a.IsDefault,
        createdAt = a.CreatedAt
    };
}

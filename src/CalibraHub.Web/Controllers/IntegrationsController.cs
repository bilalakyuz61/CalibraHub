using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Entegrasyon Wizard list + wizard endpoint'leri (Sprint 2).
///
/// Razor sayfalari:
///   GET  /Integrations              → Liste sayfasi (SmartBoard)
///   GET  /Integrations/Wizard       → Yeni entegrasyon wizard
///   GET  /Integrations/Wizard/{id}  → Mevcut entegrasyon wizard (edit)
///
/// JSON API (React tarafindan tuketilir):
///   GET    /Integrations/api/list                        — SmartBoard data
///   GET    /Integrations/api/{id}                        — Wizard edit yukleme
///   POST   /Integrations/api/save                        — Wizard kaydet
///   POST   /Integrations/api/delete/{id}                 — Sil
///   POST   /Integrations/api/toggle/{id}                 — Aktif/Pasif
///   POST   /Integrations/api/duplicate/{id}              — Kopyala
///   POST   /Integrations/api/test                        — Step 4 dry-run
///   GET    /Integrations/api/forms                       — Step 1 form picker
///   GET    /Integrations/api/forms/{formCode}/fields     — Step 1 alan listesi
///   GET    /Integrations/api/profiles                    — Step 2 ApiProfile dropdown
///   GET    /Integrations/api/endpoints?profileId=...     — Step 2 endpoint listesi
///   GET    /Integrations/api/endpoints/{id}              — Step 2 endpoint detay (schema)
///   GET    /Integrations/api/sample?formCode=&recordId=  — Step 4 ornek kayit
///
/// Sprint 3 ek olarak gelecek: /Integrations/Runs (audit log SmartBoard).
/// </summary>
[Authorize]
public sealed class IntegrationsController : Controller
{
    private readonly IIntegrationService _service;
    private readonly IIntegrationRepository _repo;
    private readonly IIntegrationApiProfileRepository _apiProfileRepo;
    private readonly IFormMetadataService _formMeta;
    private readonly IIntegrationLookupFunctionRegistry _functionRegistry;

    public IntegrationsController(
        IIntegrationService service,
        IIntegrationRepository repo,
        IIntegrationApiProfileRepository apiProfileRepo,
        IFormMetadataService formMeta,
        IIntegrationLookupFunctionRegistry functionRegistry)
    {
        _service = service;
        _repo = repo;
        _apiProfileRepo = apiProfileRepo;
        _formMeta = formMeta;
        _functionRegistry = functionRegistry;
    }

    // ── Razor sayfalari ────────────────────────────────────────────────

    [HttpGet("/Integrations")]
    public IActionResult Index() => View();

    [HttpGet("/Integrations/Wizard")]
    public IActionResult NewWizard()
    {
        ViewBag.IntegrationId = (int?)null;
        return View("Wizard");
    }

    [HttpGet("/Integrations/Wizard/{id:int}")]
    public IActionResult EditWizard(int id)
    {
        ViewBag.IntegrationId = id;
        return View("Wizard");
    }

    [HttpGet("/Integrations/Endpoints")]
    public IActionResult EndpointsPage() => View("Endpoints");

    [HttpGet("/Integrations/Runs")]
    public IActionResult RunsPage() => View("Runs");

    // ── JSON API — Liste ───────────────────────────────────────────────

    [HttpGet("/Integrations/api/list")]
    public async Task<IActionResult> ListApi(
        [FromQuery] bool includeInactive = true,
        CancellationToken ct = default)
    {
        try
        {
            var list = await _service.ListAsync(includeInactive, ct);
            return Json(new { success = true, items = list });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Detay / Wizard load ─────────────────────────────────

    [HttpGet("/Integrations/api/{id:int}")]
    public async Task<IActionResult> GetDetailApi(int id, CancellationToken ct)
    {
        try
        {
            var dto = await _service.GetDetailAsync(id, ct);
            if (dto is null) return Json(new { success = false, error = "Bulunamadi" });
            return Json(new { success = true, integration = dto });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Save (yeni / update) ────────────────────────────────

    [HttpPost("/Integrations/api/save")]
    public async Task<IActionResult> SaveApi(
        [FromBody] SaveIntegrationRequest request,
        CancellationToken ct)
    {
        if (request is null) return Json(new { success = false, error = "Request bos." });
        try
        {
            var userName = User?.Identity?.Name ?? "system";
            var id = await _service.SaveAsync(request, userName, ct);
            return Json(new { success = true, id });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = $"Beklenmedik hata: {ex.Message}" });
        }
    }

    // ── JSON API — Sil ──────────────────────────────────────────────────

    [HttpPost("/Integrations/api/delete/{id:int}")]
    public async Task<IActionResult> DeleteApi(int id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Toggle Active ───────────────────────────────────────

    [HttpPost("/Integrations/api/toggle/{id:int}")]
    public async Task<IActionResult> ToggleApi(int id, CancellationToken ct)
    {
        try
        {
            var newState = await _service.ToggleActiveAsync(id, ct);
            return Json(new { success = true, isActive = newState });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Kopyala ────────────────────────────────────────────

    [HttpPost("/Integrations/api/duplicate/{id:int}")]
    public async Task<IActionResult> DuplicateApi(int id, CancellationToken ct)
    {
        try
        {
            var userName = User?.Identity?.Name ?? "system";
            var newId = await _service.DuplicateAsync(id, userName, ct);
            return Json(new { success = true, id = newId });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Wizard Step 4 dry-run test ──────────────────────────

    [HttpPost("/Integrations/api/test")]
    public async Task<IActionResult> TestApi(
        [FromBody] TestIntegrationRequest request,
        CancellationToken ct)
    {
        if (request?.Integration is null)
            return Json(new { success = false, error = "Request bos." });
        try
        {
            var userName = User?.Identity?.Name ?? "system";
            var result = await _service.TestAsync(request, userName, ct);
            return Json(new { success = result.Success, result });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = $"Beklenmedik hata: {ex.Message}" });
        }
    }

    // ── JSON API — Step 1: Form picker ─────────────────────────────────

    [HttpGet("/Integrations/api/forms")]
    public async Task<IActionResult> FormsApi(CancellationToken ct)
    {
        try
        {
            var forms = await _formMeta.ListFormsAsync(ct);
            return Json(new { success = true, forms });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// [LEGACY] Eski wrapper-tablosu (IntegrationLookupFunction) kayitlari — geriye uyumluluk icin
    /// hala doner ama Wizard "Fonksiyon" source dropdown'i artik <see cref="DbFunctionsApi"/> kullanir.
    /// </summary>
    [HttpGet("/Integrations/api/lookup-functions")]
    public IActionResult LookupFunctionsApi()
    {
        try
        {
            var functions = _functionRegistry.List();
            return Json(new { success = true, functions });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Wizard "Fonksiyon" source dropdown'i icin DB'de tanimli SQL fonksiyon listesi.
    /// Kullanici DB tarafinda 3-paramli scalar function tanimlar; bu endpoint sys.objects'tan
    /// listeyi doner. 3-paramli olanlar onerilen (UI'da basa).
    /// </summary>
    [HttpGet("/Integrations/api/db-functions")]
    public async Task<IActionResult> DbFunctionsApi(CancellationToken ct)
    {
        try
        {
            var functions = await _functionRegistry.ListDbFunctionsAsync(ct);
            return Json(new { success = true, functions });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("/Integrations/api/forms/{formCode}/fields")]
    public async Task<IActionResult> FormFieldsApi(string formCode, CancellationToken ct)
    {
        try
        {
            var fields = await _formMeta.GetFieldsAsync(formCode, ct);
            return Json(new { success = true, fields });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Step 2: ApiProfile + Endpoint dropdown'lari ─────────

    [HttpGet("/Integrations/api/profiles")]
    public async Task<IActionResult> ProfilesApi(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            // CompanyId 1 sabit — multi-tenant zaten per-company DB ile cozulmus.
            var profiles = await _apiProfileRepo.GetByCompanyAsync(1, ct);
            var filtered = includeInactive ? profiles : profiles.Where(p => p.IsActive);
            return Json(new
            {
                success = true,
                profiles = filtered.Select(p => BuildProfileSummary(p))
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Profile detayı — auth_config_json dahil (edit modal için).
    /// Şirket Ayarları → Entegrasyon API ekranı ile aynı veri döner.
    /// </summary>
    [HttpGet("/Integrations/api/profiles/{id:guid}")]
    public async Task<IActionResult> ProfileDetailApi(Guid id, CancellationToken ct)
    {
        try
        {
            var p = await _apiProfileRepo.GetByIdAsync(id, ct);
            if (p is null) return Json(new { success = false, error = "Profile bulunamadı." });
            return Json(new
            {
                success = true,
                profile = new
                {
                    id              = p.Id,
                    companyId       = p.CompanyId,
                    name            = p.Name,
                    baseUrl         = p.BaseUrl,
                    authType        = p.AuthType,
                    authConfigJson  = p.AuthConfigJson,
                    isActive        = p.IsActive,
                    createdAt       = p.CreatedAt,
                    updatedAt       = p.UpdatedAt,
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    public sealed record SaveProfileRequest(
        Guid? Id,
        string Name,
        string BaseUrl,
        string AuthType,         // "None" | "OAuth2Password" | "BearerStatic" | "BasicAuth" | "ApiKey"
        string? AuthConfigJson,  // tipe göre JSON (UI'da apBuildAuthConfig formatı)
        bool IsActive);

    [HttpPost("/Integrations/api/profiles/save")]
    public async Task<IActionResult> SaveProfileApi(
        [FromBody] SaveProfileRequest req,
        CancellationToken ct)
    {
        if (req is null) return Json(new { success = false, error = "Request boş." });
        if (string.IsNullOrWhiteSpace(req.Name))    return Json(new { success = false, error = "Ad zorunlu." });
        if (string.IsNullOrWhiteSpace(req.BaseUrl)) return Json(new { success = false, error = "Base URL zorunlu." });

        try
        {
            // AuthConfigJson geçerli JSON mu? (boş veya None ise null olabilir)
            if (!string.IsNullOrWhiteSpace(req.AuthConfigJson))
            {
                try { System.Text.Json.JsonDocument.Parse(req.AuthConfigJson); }
                catch (Exception jex) { return Json(new { success = false, error = "AuthConfigJson geçerli JSON değil: " + jex.Message }); }
            }

            var profile = new Domain.Entities.IntegrationApiProfile
            {
                Id             = req.Id is null || req.Id == Guid.Empty ? Guid.NewGuid() : req.Id.Value,
                CompanyId      = 1,
                Name           = req.Name.Trim(),
                BaseUrl        = req.BaseUrl.Trim().TrimEnd('/'),
                AuthType       = string.IsNullOrWhiteSpace(req.AuthType) ? "None" : req.AuthType.Trim(),
                AuthConfigJson = string.IsNullOrWhiteSpace(req.AuthConfigJson) ? null : req.AuthConfigJson,
                IsActive       = req.IsActive,
                UpdatedAt      = DateTime.UtcNow,
            };
            await _apiProfileRepo.UpsertAsync(profile, ct);
            return Json(new { success = true, id = profile.Id });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("/Integrations/api/profiles/delete/{id:guid}")]
    public async Task<IActionResult> DeleteProfileApi(Guid id, CancellationToken ct)
    {
        try
        {
            await _apiProfileRepo.DeleteAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            // FK violation muhtemel — bu profile'a bağlı endpoint varsa
            var msg = ex.Message ?? "";
            string friendly = msg.Contains("FK_", StringComparison.OrdinalIgnoreCase) ||
                              msg.Contains("REFERENCE", StringComparison.OrdinalIgnoreCase)
                ? "Bu profile'a bağlı endpoint(ler) var; önce onları silin veya başka profile'a taşıyın."
                : ex.Message;
            return Json(new { success = false, error = friendly });
        }
    }

    [HttpPost("/Integrations/api/profiles/toggle/{id:guid}")]
    public async Task<IActionResult> ToggleProfileApi(Guid id, CancellationToken ct)
    {
        try
        {
            var p = await _apiProfileRepo.GetByIdAsync(id, ct);
            if (p is null) return Json(new { success = false, error = "Profile bulunamadı." });
            p.IsActive  = !p.IsActive;
            p.UpdatedAt = DateTime.UtcNow;
            await _apiProfileRepo.UpsertAsync(p, ct);
            return Json(new { success = true, isActive = p.IsActive });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Test Bağlantısı — OAuth2Password için token endpoint çağrılır, başarılıysa OK.
    /// Diğer auth tipleri için "Auth uygulanabilir mi?" kontrolü (config geçerli mi).
    /// </summary>
    [HttpPost("/Integrations/api/profiles/test/{id:guid}")]
    public async Task<IActionResult> TestProfileApi(
        [FromServices] IIntegrationAuthHandler auth,
        Guid id,
        CancellationToken ct)
    {
        try
        {
            var p = await _apiProfileRepo.GetByIdAsync(id, ct);
            if (p is null) return Json(new { success = false, error = "Profile bulunamadı." });

            // Token cache'i invalidate — taze test
            auth.InvalidateToken(p.Id);

            // Dummy request — auth'u uygulayıp hata varsa yakala
            using var dummyReq = new HttpRequestMessage(HttpMethod.Get, p.BaseUrl ?? "http://localhost");
            try
            {
                await auth.ApplyAuthAsync(dummyReq, p, ct);
                var hasAuthHeader = dummyReq.Headers.Authorization is not null
                                  || dummyReq.Headers.Any(h => h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase));
                return Json(new
                {
                    success = true,
                    authType = p.AuthType,
                    hasAuthHeader,
                    message = (p.AuthType ?? "None").Equals("None", StringComparison.OrdinalIgnoreCase)
                        ? "Auth tipi 'None' — bağlantı testi yapılmadı (auth gerekmez)."
                        : hasAuthHeader
                            ? "✓ Auth başarıyla uygulandı (token alındı / header eklendi)."
                            : "Auth uygulandı ama header eklenmedi — config eksik olabilir.",
                });
            }
            catch (Exception authEx)
            {
                return Json(new
                {
                    success = false,
                    authType = p.AuthType,
                    error = authEx.Message,
                });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Profile dropdown'una "OAuth2 Password — bilal · 5 ek alan" gibi anlamli
    /// detay verir (UI ipucu). authConfigJson icindeki sirlar (password vb.)
    /// gonderilmez — sadece username + extraFields key sayisi.
    /// </summary>
    private static object BuildProfileSummary(Domain.Entities.IntegrationApiProfile p)
    {
        string? username = null;
        int extraFieldsCount = 0;
        string? tokenEndpoint = null;
        string authSummary = p.AuthType ?? "None";

        if (!string.IsNullOrWhiteSpace(p.AuthConfigJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(p.AuthConfigJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("username", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.String)
                    username = u.GetString();
                if (root.TryGetProperty("tokenEndpoint", out var te) && te.ValueKind == System.Text.Json.JsonValueKind.String)
                    tokenEndpoint = te.GetString();
                if (root.TryGetProperty("extraFields", out var ef) && ef.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var _ in ef.EnumerateObject()) extraFieldsCount++;
                }
            }
            catch { /* gecersiz config — sessiz skip */ }
        }

        // Insan dostu auth ozeti: "OAuth2 Password — bilal · 5 ek alan"
        var parts = new List<string>();
        switch ((p.AuthType ?? "").ToLowerInvariant())
        {
            case "oauth2password": parts.Add("OAuth2 Password"); break;
            case "bearer": case "bearerstatic": parts.Add("Bearer Token"); break;
            case "basic":  case "basicauth":   parts.Add("Basic Auth"); break;
            case "apikey":                     parts.Add("API Key"); break;
            case "none": case "":              parts.Add("Auth yok"); break;
            default:                           parts.Add(p.AuthType!); break;
        }
        if (!string.IsNullOrWhiteSpace(username)) parts.Add(username!);
        if (extraFieldsCount > 0)                 parts.Add($"{extraFieldsCount} ek alan");
        authSummary = string.Join(" · ", parts);

        return new
        {
            id = p.Id,
            name = p.Name,
            baseUrl = p.BaseUrl,
            authType = p.AuthType,
            authSummary,
            username,
            tokenEndpoint,
            extraFieldsCount,
        };
    }

    [HttpGet("/Integrations/api/endpoints")]
    public async Task<IActionResult> EndpointsApi(
        [FromQuery] Guid? profileId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var list = profileId.HasValue
                ? await _repo.ListEndpointsByProfileAsync(profileId.Value, ct)
                : await _repo.ListEndpointsAsync(includeInactive, ct);

            // Profile adi icin lookup
            var profileMap = new Dictionary<Guid, string>();
            foreach (var ep in list)
            {
                if (!profileMap.ContainsKey(ep.ApiProfileId))
                {
                    var profile = await _apiProfileRepo.GetByIdAsync(ep.ApiProfileId, ct);
                    profileMap[ep.ApiProfileId] = profile?.Name ?? "(silinmis)";
                }
            }

            return Json(new
            {
                success = true,
                endpoints = list.Select(ep => new IntegrationEndpointListItemDto(
                    Id: ep.Id,
                    ApiProfileId: ep.ApiProfileId,
                    ApiProfileName: profileMap[ep.ApiProfileId],
                    Name: ep.Name,
                    HttpMethod: ep.HttpMethod,
                    UrlTemplate: ep.UrlTemplate,
                    IsActive: ep.IsActive))
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("/Integrations/api/endpoints/save")]
    public async Task<IActionResult> SaveEndpointApi(
        [FromBody] SaveIntegrationEndpointRequest req,
        CancellationToken ct)
    {
        Console.WriteLine($"[SaveEndpointApi] req={(req is null ? "null" : $"id={req.Id}, name={req.Name}, bodyLen={req.BodySchema?.Length ?? 0}")}");
        if (req is null)
            return Json(new { success = false, error = "Request bos." });
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.UrlTemplate))
            return Json(new { success = false, error = "Ad ve URL zorunlu." });
        if (req.ApiProfileId == Guid.Empty)
            return Json(new { success = false, error = "ApiProfile secimi zorunlu." });

        try
        {
            var userName = User?.Identity?.Name ?? "system";

            if (req.Id <= 0)
            {
                // Create
                var entity = new Domain.Entities.IntegrationEndpoint
                {
                    ApiProfileId = req.ApiProfileId,
                    Name = req.Name.Trim(),
                    HttpMethod = string.IsNullOrWhiteSpace(req.HttpMethod) ? "POST" : req.HttpMethod.Trim().ToUpperInvariant(),
                    UrlTemplate = req.UrlTemplate.Trim(),
                    BodySchema = req.BodySchema,
                    Description = req.Description,
                    IsActive = req.IsActive,
                    CreatedBy = userName,
                };
                var id = await _repo.AddEndpointAsync(entity, ct);
                return Json(new { success = true, id });
            }
            else
            {
                // Update
                var existing = await _repo.GetEndpointByIdAsync(req.Id, ct);
                if (existing is null) return Json(new { success = false, error = "Endpoint bulunamadi." });
                existing.ApiProfileId = req.ApiProfileId;
                existing.Name = req.Name.Trim();
                existing.HttpMethod = string.IsNullOrWhiteSpace(req.HttpMethod) ? "POST" : req.HttpMethod.Trim().ToUpperInvariant();
                existing.UrlTemplate = req.UrlTemplate.Trim();
                existing.BodySchema = req.BodySchema;
                existing.Description = req.Description;
                existing.IsActive = req.IsActive;
                existing.UpdatedBy = userName;
                existing.Updated = DateTime.UtcNow;
                await _repo.UpdateEndpointAsync(existing, ct);
                return Json(new { success = true, id = existing.Id });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveEndpointApi] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            if (ex.InnerException != null)
                Console.Error.WriteLine($"[SaveEndpointApi INNER] {ex.InnerException.Message}");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("/Integrations/api/endpoints/delete/{id:int}")]
    public async Task<IActionResult> DeleteEndpointApi(int id, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteEndpointAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            // FK violation muhtemel — bu endpoint'i kullanan Integration varsa silinemez
            var msg = ex.Message ?? "";
            string friendly = msg.Contains("FK_Integration_Endpoint", StringComparison.OrdinalIgnoreCase)
                ? "Bu endpoint'i kullanan en az 1 entegrasyon var; once o entegrasyonu silin veya baska endpoint'e tasiyin."
                : ex.Message;
            return Json(new { success = false, error = friendly });
        }
    }

    [HttpPost("/Integrations/api/endpoints/toggle/{id:int}")]
    public async Task<IActionResult> ToggleEndpointApi(int id, CancellationToken ct)
    {
        try
        {
            var ep = await _repo.GetEndpointByIdAsync(id, ct);
            if (ep is null) return Json(new { success = false, error = "Endpoint bulunamadi." });
            ep.IsActive = !ep.IsActive;
            ep.UpdatedBy = User?.Identity?.Name ?? "system";
            ep.Updated = DateTime.UtcNow;
            await _repo.UpdateEndpointAsync(ep, ct);
            return Json(new { success = true, isActive = ep.IsActive });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Endpoint katalog — Endpoint Edit modal'indaki URL S,ablonu combobox'i icin.
    ///
    /// Veri kaynagi: <see cref="IEndpointCatalogService"/> — Resources/Integration/
    /// klasorundeki CSV/doc dosyalarindan derlenmis statik liste (suanda 320 Netsis
    /// NetOpenX REST endpoint). DB'den DEGIL — kullanici CSV import etmemis olsa
    /// bile combobox dolu gelir.
    ///
    /// Filtre: ?provider=Netsis|Logo|Custom (default: hepsi)
    /// </summary>
    [HttpGet("/Integrations/api/endpoint-catalog")]
    public IActionResult EndpointCatalogApi(
        [FromServices] IEndpointCatalogService catalog,
        [FromQuery] string? provider)
    {
        try
        {
            var items = catalog.GetByProvider(provider)
                .Select(i => new
                {
                    provider    = i.Provider,
                    resource    = i.Resource,
                    methodName  = i.MethodName,
                    httpMethod  = i.HttpMethod,
                    urlTemplate = i.UrlTemplate,
                    inputType   = i.InputType,
                    returnType  = i.ReturnType,
                    category    = i.Category,
                    summary     = i.Summary,
                    // UI tutarliligi icin "name" (combobox eski API ile uyumlu)
                    name        = string.IsNullOrEmpty(i.MethodName) ? i.Resource : $"{i.Resource} {i.MethodName}",
                });

            return Json(new
            {
                success = true,
                items,
                providers = catalog.GetAll()
                    .Select(i => i.Provider)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p)
                    .ToList(),
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("/Integrations/api/endpoints/{id:int}")]
    public async Task<IActionResult> EndpointDetailApi(int id, CancellationToken ct)
    {
        try
        {
            var ep = await _repo.GetEndpointByIdAsync(id, ct);
            if (ep is null) return Json(new { success = false, error = "Endpoint bulunamadi." });
            var profile = await _apiProfileRepo.GetByIdAsync(ep.ApiProfileId, ct);
            return Json(new
            {
                success = true,
                endpoint = new IntegrationEndpointDto(
                    Id: ep.Id,
                    ApiProfileId: ep.ApiProfileId,
                    ApiProfileName: profile?.Name ?? "(silinmis)",
                    Name: ep.Name,
                    HttpMethod: ep.HttpMethod,
                    UrlTemplate: ep.UrlTemplate,
                    BodySchema: ep.BodySchema,
                    Description: ep.Description,
                    IsActive: ep.IsActive)
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Field Docs (Body Schema icin Turkce aciklama / allowed values) ──
    //
    // Wizard Step 2 (Body Schema JSON tree) icin her field'in dokumantasyonunu
    // doner. Provider + Resource bazli DB-tabanli katalog (IIntegrationDocCatalogService).
    // UI tarafinda JSON tree'de eslesen field path'lerin yanina (i) ikonu rendlenir,
    // hover'da tooltip ile aciklama + allowed values + ornek gosterilir.
    //
    // Query: ?provider=Netsis&resource=ItemSlips
    //   veya: ?endpointId=42  (endpoint'in UrlTemplate'inden resource otomatik cikarilir)

    [HttpGet("/Integrations/api/field-docs")]
    public async Task<IActionResult> FieldDocsApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        [FromQuery] string? provider, [FromQuery] string? resource,
        [FromQuery] int? endpointId, CancellationToken ct)
    {
        // endpointId verildiyse provider/resource'u otomatik cikar
        if (endpointId.HasValue)
        {
            var ep = await _repo.GetEndpointByIdAsync(endpointId.Value, ct);
            if (ep is not null)
            {
                if (string.IsNullOrWhiteSpace(resource))
                    resource = ExtractResourceFromUrl(ep.UrlTemplate);
                if (string.IsNullOrWhiteSpace(provider))
                {
                    // ApiProfile uzerinden provider'i cek; yoksa default "Netsis"
                    var profile = await _apiProfileRepo.GetByIdAsync(ep.ApiProfileId, ct);
                    provider = profile?.ProviderCode ?? "Netsis";
                }
            }
        }

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(resource))
            return Json(new { success = false, docs = (object?)null, error = "provider+resource veya endpointId zorunlu." });

        var docs = await catalog.GetFieldDocsAsync(provider, resource, ct);
        var docMap = new Dictionary<string, object>(docs.Count);
        foreach (var (path, dto) in docs)
        {
            var allowed = dto.Enum?.Values is { Count: > 0 }
                ? dto.Enum.Values
                    .Select(v => (object)new { value = v.Value, label = v.Label, technicalCode = v.TechnicalCode })
                    .ToList()
                : null;
            docMap[path] = new
            {
                label         = dto.Label,
                description   = dto.Description,
                allowedValues = allowed,
                example       = dto.Example,
                notes         = dto.Notes,
                isRequired    = dto.IsRequired,
                enumCode      = dto.Enum?.Code,
            };
        }
        return Json(new
        {
            success  = true,
            provider = provider,
            resource = resource,
            count    = docMap.Count,
            docs     = docMap,
        });
    }

    private static string? ExtractResourceFromUrl(string? urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(urlTemplate)) return null;
        var parts = urlTemplate.Trim('/').Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "v2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[i], "v1", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        return parts.LastOrDefault();
    }

    // ── JSON API — Doc Catalog (Provider / Enum / FieldDoc) admin CRUD ────
    //
    // Wizard tarafi field-docs endpoint'i bu kataloga DB uzerinden erisir.
    // Admin sayfalari (Ayarlar → Enum Tanimlari / Alan Aciklamalari) bu endpoint'leri
    // kullanir. Cache otomatik invalidate edilir save sonrasi.

    [HttpGet("/Integrations/api/doc-catalog/providers")]
    public async Task<IActionResult> ListProvidersApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var items = await catalog.ListProvidersAsync(includeInactive, ct);
            return Json(new { success = true, items });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Integrations/api/doc-catalog/providers/save")]
    public async Task<IActionResult> SaveProviderApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        [FromBody] CalibraHub.Application.Contracts.SaveIntegrationProviderRequest req,
        CancellationToken ct)
    {
        try
        {
            var actor = User?.Identity?.Name;
            var id = await catalog.SaveProviderAsync(req, actor, ct);
            return Json(new { success = true, id });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Integrations/api/doc-catalog/providers/delete/{id:int}")]
    public async Task<IActionResult> DeleteProviderApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        int id, CancellationToken ct)
    {
        try
        {
            var actor = User?.Identity?.Name;
            await catalog.DeleteProviderAsync(id, actor, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpGet("/Integrations/api/doc-catalog/enums")]
    public async Task<IActionResult> ListEnumsApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        [FromQuery] int? providerId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var items = await catalog.ListEnumsAsync(providerId, includeInactive, ct);
            return Json(new { success = true, items });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpGet("/Integrations/api/doc-catalog/enums/{id:int}")]
    public async Task<IActionResult> GetEnumApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        int id, CancellationToken ct)
    {
        try
        {
            var item = await catalog.GetEnumAsync(id, ct);
            if (item is null) return Json(new { success = false, error = "Bulunamadi" });
            return Json(new { success = true, item });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Integrations/api/doc-catalog/enums/save")]
    public async Task<IActionResult> SaveEnumApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        [FromBody] CalibraHub.Application.Contracts.SaveIntegrationEnumDefinitionRequest req,
        CancellationToken ct)
    {
        try
        {
            var actor = User?.Identity?.Name;
            var id = await catalog.SaveEnumAsync(req, actor, ct);
            return Json(new { success = true, id });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Integrations/api/doc-catalog/enums/delete/{id:int}")]
    public async Task<IActionResult> DeleteEnumApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        int id, CancellationToken ct)
    {
        try
        {
            var actor = User?.Identity?.Name;
            await catalog.DeleteEnumAsync(id, actor, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpGet("/Integrations/api/doc-catalog/field-docs")]
    public async Task<IActionResult> ListFieldDocsApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        [FromQuery] int? providerId,
        [FromQuery] string? resource,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var items = await catalog.ListFieldDocsAsync(providerId, resource, includeInactive, ct);
            return Json(new { success = true, items });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpGet("/Integrations/api/doc-catalog/field-docs/{id:int}")]
    public async Task<IActionResult> GetFieldDocApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        int id, CancellationToken ct)
    {
        try
        {
            var item = await catalog.GetFieldDocAsync(id, ct);
            if (item is null) return Json(new { success = false, error = "Bulunamadi" });
            return Json(new { success = true, item });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Integrations/api/doc-catalog/field-docs/save")]
    public async Task<IActionResult> SaveFieldDocApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        [FromBody] CalibraHub.Application.Contracts.SaveIntegrationFieldDocRequest req,
        CancellationToken ct)
    {
        try
        {
            var actor = User?.Identity?.Name;
            var id = await catalog.SaveFieldDocAsync(req, actor, ct);
            return Json(new { success = true, id });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    [HttpPost("/Integrations/api/doc-catalog/field-docs/delete/{id:int}")]
    public async Task<IActionResult> DeleteFieldDocApi(
        [FromServices] CalibraHub.Application.Abstractions.Services.IIntegrationDocCatalogService catalog,
        int id, CancellationToken ct)
    {
        try
        {
            var actor = User?.Identity?.Name;
            await catalog.DeleteFieldDocAsync(id, actor, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
    }

    // ── JSON API — Form bazli aktif Manual entegrasyonlar ──────────────
    //
    // Form ekranlarinda "Bu kaydi entegrasyona gonder" butonu icin.
    // _IntegrationButtons.cshtml partial bu endpoint'i ?formCode=X ile cagirir,
    // donen liste icin her birine "Sim. Calistir" buton render eder.

    [HttpGet("/Integrations/api/by-form/{formCode}")]
    public async Task<IActionResult> ByFormApi(string formCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode))
            return Json(new { success = false, error = "formCode zorunlu" });
        try
        {
            // Manual trigger'a sahip aktif entegrasyonlar
            var integrations = await _repo.ListByFormCodeAsync(
                formCode.Trim(), Domain.Enums.IntegrationTriggerType.Manual, ct);

            var items = new List<object>();
            foreach (var i in integrations.Where(x => x.IsActive))
            {
                var ep = i.TargetEndpointId is > 0
                    ? await _repo.GetEndpointByIdAsync(i.TargetEndpointId.Value, ct)
                    : null;
                items.Add(new
                {
                    id = i.Id,
                    name = i.Name,
                    description = i.Description,
                    endpointName = ep?.Name,
                    httpMethod = ep?.HttpMethod,
                });
            }
            return Json(new { success = true, items });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Run audit log ───────────────────────────────────────
    //
    // Hub uzerine ucuncu tab eklenecek (Calistirma Logu) ya da ayri sayfa
    // (/Integrations/Runs). Status filtreleri: Success/Failed/Skipped/Retrying.

    [HttpGet("/Integrations/api/runs")]
    public async Task<IActionResult> ListRunsApi(
        [FromQuery] int? integrationId,
        [FromQuery] string? status,
        [FromQuery] int sinceDays = 7,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        try
        {
            var runs = await _repo.ListAllRunsAsync(integrationId, status, sinceDays, limit, ct);

            // Integration adi/endpoint adi join icin kucuk lookup
            var integrationIds = runs.Select(r => r.IntegrationId).Distinct().ToList();
            var integrationMap = new Dictionary<int, (string Name, string Form)>();
            foreach (var iid in integrationIds)
            {
                var integ = await _repo.GetByIdAsync(iid, ct);
                if (integ is not null)
                    integrationMap[iid] = (integ.Name, integ.SourceFormCode);
            }

            return Json(new
            {
                success = true,
                runs = runs.Select(r => new
                {
                    id = r.Id,
                    integrationId = r.IntegrationId,
                    integrationName = integrationMap.TryGetValue(r.IntegrationId, out var x) ? x.Name : "(silinmis)",
                    sourceFormCode  = integrationMap.TryGetValue(r.IntegrationId, out var y) ? y.Form : null,
                    triggerType = r.TriggerType.ToString(),
                    sourceRecordId = r.SourceRecordId,
                    startedAt = r.StartedAt,
                    finishedAt = r.FinishedAt,
                    durationMs = r.DurationMs,
                    status = r.Status.ToString(),
                    httpStatusCode = r.HttpStatusCode,
                    errorMessage = r.ErrorMessage,
                    retryAttempt = r.RetryAttempt,
                    triggeredBy = r.TriggeredBy,
                }),
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("/Integrations/api/runs/{id:long}")]
    public async Task<IActionResult> RunDetailApi(long id, CancellationToken ct)
    {
        try
        {
            var run = await _repo.GetRunByIdAsync(id, ct);
            if (run is null) return Json(new { success = false, error = "Run kaydi bulunamadi." });
            var integ = await _repo.GetByIdAsync(run.IntegrationId, ct);
            return Json(new
            {
                success = true,
                run = new
                {
                    id = run.Id,
                    integrationId = run.IntegrationId,
                    integrationName = integ?.Name ?? "(silinmis)",
                    sourceFormCode = integ?.SourceFormCode,
                    triggerType = run.TriggerType.ToString(),
                    sourceRecordId = run.SourceRecordId,
                    startedAt = run.StartedAt,
                    finishedAt = run.FinishedAt,
                    durationMs = run.DurationMs,
                    status = run.Status.ToString(),
                    httpStatusCode = run.HttpStatusCode,
                    requestBody = run.RequestBody,
                    responseBody = run.ResponseBody,
                    errorMessage = run.ErrorMessage,
                    retryAttempt = run.RetryAttempt,
                    triggeredBy = run.TriggeredBy,
                },
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Endpoint bulk import (CSV seed) ─────────────────────
    //
    // Kullanim: Hub > Endpointler tab > "Toplu Import" butonu.
    // CSV format (NetsisRestEndpoints.csv standardi):
    //   "Resource","Method","HttpMethod","UrlTemplate","InputType","ReturnType"
    //   "ARPs","PostInternal","POST","/api/v2/ARPs","ARPs","TResult`1"
    //
    // Idempotent: ayni (ApiProfileId + HttpMethod + UrlTemplate) varsa skip,
    // yoksa eklenir. Profile yoksa request'teki NewProfileName + NewProfileBaseUrl
    // ile bir tane olusturulur.

    [HttpPost("/Integrations/api/endpoints/bulk-import")]
    public async Task<IActionResult> BulkImportEndpointsApi(
        [FromBody] BulkImportEndpointsRequest req,
        CancellationToken ct)
    {
        if (req is null)
            return Json(new { success = false, error = "Request bos." });
        if (string.IsNullOrWhiteSpace(req.CsvText))
            return Json(new { success = false, error = "CSV icerigi bos." });

        try
        {
            var userName = User?.Identity?.Name ?? "system";

            // 1) Profile cozumle: id verildiyse onu kullan, yoksa newProfileName + baseUrl ile yarat
            Guid profileId;
            if (req.ApiProfileId.HasValue && req.ApiProfileId.Value != Guid.Empty)
            {
                var existing = await _apiProfileRepo.GetByIdAsync(req.ApiProfileId.Value, ct);
                if (existing is null)
                    return Json(new { success = false, error = "Belirtilen API Profile bulunamadi." });
                profileId = existing.Id;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(req.NewProfileName) || string.IsNullOrWhiteSpace(req.NewProfileBaseUrl))
                    return Json(new { success = false, error = "Yeni profile icin Ad ve Base URL zorunlu." });

                var profile = new Domain.Entities.IntegrationApiProfile
                {
                    Id = Guid.NewGuid(),
                    CompanyId = 1,
                    Name = req.NewProfileName.Trim(),
                    BaseUrl = req.NewProfileBaseUrl.Trim().TrimEnd('/'),
                    AuthType = "None",
                    IsActive = true,
                };
                await _apiProfileRepo.UpsertAsync(profile, ct);
                profileId = profile.Id;
            }

            // 2) Mevcut endpoint'leri cek (idempotent dedup icin)
            var existingEps = await _repo.ListEndpointsByProfileAsync(profileId, ct);
            var existingKeys = new HashSet<string>(
                existingEps.Select(e => $"{(e.HttpMethod ?? "").ToUpperInvariant()} {(e.UrlTemplate ?? "").Trim()}"),
                StringComparer.OrdinalIgnoreCase);

            // 3) CSV parse
            var rows = ParseCsv(req.CsvText);
            if (rows.Count == 0)
                return Json(new { success = false, error = "CSV'de gecerli satir bulunamadi." });

            int created = 0, skipped = 0, errors = 0;
            var errorMessages = new List<string>();

            foreach (var row in rows)
            {
                try
                {
                    if (row.Count < 4) { errors++; continue; }
                    var resource    = row[0]?.Trim() ?? "";
                    var method      = row[1]?.Trim() ?? "";
                    var httpMethod  = (row[2]?.Trim() ?? "POST").ToUpperInvariant();
                    var urlTemplate = row[3]?.Trim() ?? "";
                    var inputType   = row.Count > 4 ? row[4]?.Trim() : null;
                    var returnType  = row.Count > 5 ? row[5]?.Trim() : null;

                    if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(urlTemplate))
                    {
                        errors++; continue;
                    }

                    var dedupKey = $"{httpMethod} {urlTemplate}";
                    if (existingKeys.Contains(dedupKey))
                    {
                        skipped++;
                        continue;
                    }

                    var name = string.IsNullOrWhiteSpace(method)
                        ? resource
                        : $"{resource} {method}";

                    var description = string.IsNullOrWhiteSpace(inputType) && string.IsNullOrWhiteSpace(returnType)
                        ? null
                        : $"Input: {inputType ?? "-"} | Return: {returnType ?? "-"}";

                    var entity = new Domain.Entities.IntegrationEndpoint
                    {
                        ApiProfileId = profileId,
                        Name = name.Length > 200 ? name.Substring(0, 200) : name,
                        HttpMethod = httpMethod,
                        UrlTemplate = urlTemplate,
                        BodySchema = null,
                        Description = description,
                        IsActive = true,
                        CreatedBy = userName,
                    };
                    await _repo.AddEndpointAsync(entity, ct);
                    existingKeys.Add(dedupKey);
                    created++;
                }
                catch (Exception ex)
                {
                    errors++;
                    if (errorMessages.Count < 5) errorMessages.Add(ex.Message);
                }
            }

            return Json(new
            {
                success = true,
                profileId,
                total = rows.Count,
                created,
                skipped,
                errors,
                errorMessages,
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Basit CSV parser: cift-tirnak literal'larini destekler, virgulle ayirir.
    /// Header satirini atlar (ilk hucresi "Resource"/"resource" ise).
    /// </summary>
    private static List<List<string>> ParseCsv(string csvText)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrWhiteSpace(csvText)) return rows;

        var lines = csvText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        bool firstNonEmpty = true;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        // Escaped quote ("")?
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') inQuote = true;
                    else if (c == ',') { cells.Add(cur.ToString()); cur.Clear(); }
                    else cur.Append(c);
                }
            }
            cells.Add(cur.ToString());

            // Header satirini atla
            if (firstNonEmpty)
            {
                firstNonEmpty = false;
                if (cells.Count > 0 &&
                    string.Equals(cells[0]?.Trim(), "Resource", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            rows.Add(cells);
        }

        return rows;
    }

    // ── JSON API — Body Schema Otomatik Cekme ──────────────────────────
    //
    // EndpointEditModal > Body Schema tab > "📥 Otomatik Çek" butonu.
    // Endpoint kaydedilmeden once de calisir — cunku request UrlTemplate +
    // ApiProfileId verir (geciciler dahil).
    //
    // 2-katmanli fallback (BodySchemaResolver):
    //   1. POST {baseUrl}/api/v2/{Resource}/Describe — Netsis NetOpenX
    //   2. POST-probe — bos {} body, hata cevabini parse
    // V2: Swagger/OpenAPI parser eklenecek.

    public sealed record ResolveBodySchemaRequest(
        Guid ApiProfileId,
        string HttpMethod,
        string UrlTemplate);

    [HttpPost("/Integrations/api/endpoints/resolve-body")]
    public async Task<IActionResult> ResolveBodySchemaApi(
        [FromServices] IBodySchemaResolver resolver,
        [FromBody] ResolveBodySchemaRequest req,
        CancellationToken ct)
    {
        if (req is null || req.ApiProfileId == Guid.Empty || string.IsNullOrWhiteSpace(req.UrlTemplate))
            return Json(new { success = false, error = "ApiProfileId ve UrlTemplate zorunlu." });

        try
        {
            var profile = await _apiProfileRepo.GetByIdAsync(req.ApiProfileId, ct);
            if (profile is null) return Json(new { success = false, error = "API Profile bulunamadi." });

            var fakeEndpoint = new Domain.Entities.IntegrationEndpoint
            {
                ApiProfileId = req.ApiProfileId,
                Name = "(resolve)",
                HttpMethod = string.IsNullOrWhiteSpace(req.HttpMethod) ? "POST" : req.HttpMethod.Trim().ToUpperInvariant(),
                UrlTemplate = req.UrlTemplate.Trim(),
            };

            var result = await resolver.ResolveAsync(fakeEndpoint, profile, ct);
            return Json(new
            {
                success = result.Success,
                source = result.Source,
                bodyJson = result.BodyJson,
                error = result.ErrorMessage,
                httpStatusCode = result.HttpStatusCode,
                durationMs = result.DurationMs,
                // Field metadata — Services / Definitions katmanindan gelir;
                // UI'da "Zorunlu alanlar" panelinde + Step 3 mapping ekraninda kullanilir.
                fields = result.Fields?.Select(f => new
                {
                    path      = f.Path,
                    type      = f.Type,
                    required  = f.Required,
                    maxLength = f.MaxLength,
                    @enum     = f.Enum,
                }),
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Body Template (sablon galerisi) ─────────────────────
    //
    // Kullanim: EndpointEditModal > Body Schema tab > "Şablon Galerisi" butonu.
    // 5 baslangic sablonu seed edilmis durumda (CalibraDatabaseInitializer).
    // Kullanici kendi sablonunu da kaydedebilir (gelecek genisleme).

    [HttpGet("/Integrations/api/body-templates")]
    public async Task<IActionResult> ListBodyTemplatesApi(
        [FromServices] IBodyTemplateRepository templateRepo,
        [FromQuery] string? category,
        [FromQuery] string? provider,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        try
        {
            var list = await templateRepo.ListAsync(category, provider, search, ct);
            return Json(new { success = true, templates = list });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("/Integrations/api/body-templates/use/{id:int}")]
    public async Task<IActionResult> UseBodyTemplateApi(
        [FromServices] IBodyTemplateRepository templateRepo,
        int id,
        CancellationToken ct)
    {
        try
        {
            var t = await templateRepo.GetByIdAsync(id, ct);
            if (t is null) return Json(new { success = false, error = "Sablon bulunamadi." });
            await templateRepo.IncrementUsageAsync(id, ct);
            return Json(new { success = true, bodyJson = t.BodyJson });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    public sealed record SaveBodyTemplateRequest(
        string Category,           // 'Sales' | 'Purchase' | 'Customer' | 'Stock' | 'Bank' | 'Custom' | ...
        string Name,
        string? DocType,
        string? ProviderHint,      // 'Netsis' | 'Logo' | 'Custom'
        string? UrlPattern,
        string? HttpMethod,
        string  BodyJson,
        string? Description,
        string? Tags);

    [HttpPost("/Integrations/api/body-templates")]
    public async Task<IActionResult> CreateBodyTemplateApi(
        [FromServices] IBodyTemplateRepository templateRepo,
        [FromBody] SaveBodyTemplateRequest req,
        CancellationToken ct)
    {
        if (req is null) return Json(new { success = false, error = "Request bos." });
        if (string.IsNullOrWhiteSpace(req.Name))     return Json(new { success = false, error = "Ad zorunlu." });
        if (string.IsNullOrWhiteSpace(req.Category)) return Json(new { success = false, error = "Kategori zorunlu." });
        if (string.IsNullOrWhiteSpace(req.BodyJson)) return Json(new { success = false, error = "BodyJson bos olamaz." });

        // Body geçerli JSON mu kontrol et
        try { System.Text.Json.JsonDocument.Parse(req.BodyJson); }
        catch (Exception ex) { return Json(new { success = false, error = "BodyJson gecerli JSON degil: " + ex.Message }); }

        try
        {
            var entity = new Domain.Entities.BodyTemplate
            {
                Category     = req.Category.Trim(),
                Name         = req.Name.Trim(),
                DocType      = string.IsNullOrWhiteSpace(req.DocType)      ? null : req.DocType.Trim(),
                ProviderHint = string.IsNullOrWhiteSpace(req.ProviderHint) ? null : req.ProviderHint.Trim(),
                UrlPattern   = string.IsNullOrWhiteSpace(req.UrlPattern)   ? null : req.UrlPattern.Trim(),
                HttpMethod   = string.IsNullOrWhiteSpace(req.HttpMethod)   ? null : req.HttpMethod.Trim().ToUpperInvariant(),
                BodyJson     = req.BodyJson,
                Description  = string.IsNullOrWhiteSpace(req.Description)  ? null : req.Description.Trim(),
                Tags         = string.IsNullOrWhiteSpace(req.Tags)         ? null : req.Tags.Trim(),
                IsBuiltIn    = false,
                IsActive     = true,
                CreatedBy    = User?.Identity?.Name ?? "system",
            };
            var id = await templateRepo.AddAsync(entity, ct);
            return Json(new { success = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("/Integrations/api/body-templates/delete/{id:int}")]
    public async Task<IActionResult> DeleteBodyTemplateApi(
        [FromServices] IBodyTemplateRepository templateRepo,
        int id,
        CancellationToken ct)
    {
        try
        {
            await templateRepo.DeleteAsync(id, ct);
            return Json(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Step 4: ornek kayit ─────────────────────────────────

    [HttpGet("/Integrations/api/sample")]
    public async Task<IActionResult> SampleApi(
        [FromQuery] string formCode,
        [FromQuery] string? recordId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode))
            return Json(new { success = false, error = "formCode zorunlu." });
        try
        {
            var sample = await _formMeta.GetSampleRecordAsync(formCode, recordId, ct);
            if (sample is null) return Json(new { success = false, error = "Ornek kayit bulunamadi." });
            return Json(new { success = true, sample });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ── JSON API — Aktarim Kuyrugu ─────────────────────────────────────
    //
    // "Aktarim Kuyrugu" sayfasinin 4 endpoint'i:
    //   GET  /Integrations/api/queue/integrations            — sol panel: Manual entegrasyonlar + sayilar
    //   GET  /Integrations/api/queue/{id}?filter=&search=    — secili entegrasyonun kayit listesi
    //   POST /Integrations/api/queue/run                     — toplu/tekli aktarim ({ integrationId, recordIds })
    //   POST /Integrations/api/queue/skip                    — "haric tut" ({ integrationId, recordIds, reason })
    //   POST /Integrations/api/queue/restore                 — Skipped'i geri al ({ integrationId, recordIds })

    [HttpGet("/Integrations/api/queue/integrations")]
    public async Task<IActionResult> QueueIntegrationsApi(
        [FromServices] IIntegrationQueueService queueService,
        CancellationToken ct)
    {
        try
        {
            var items = await queueService.ListManualIntegrationsAsync(ct);
            return Json(new { success = true, items });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("/Integrations/api/queue/{integrationId:int}")]
    public async Task<IActionResult> QueueListApi(
        [FromServices] IIntegrationQueueService queueService,
        int integrationId,
        [FromQuery] string? filter,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        try
        {
            var f = ParseFilter(filter);
            var result = await queueService.ListAsync(integrationId, f, search, page, pageSize, ct);
            return Json(new
            {
                success = true,
                rows    = result.Rows,
                total   = result.TotalCount,
                summary = new
                {
                    pending = result.Pending,
                    failed  = result.Failed,
                    sent    = result.Sent,
                    skipped = result.Skipped,
                },
                page,
                pageSize,
                filter = f.ToString(),
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    public sealed record QueueRunRequest(int IntegrationId, IReadOnlyList<string> RecordIds);

    [HttpPost("/Integrations/api/queue/run")]
    public async Task<IActionResult> QueueRunApi(
        [FromServices] IIntegrationRunner runner,
        [FromBody] QueueRunRequest? request,
        CancellationToken ct)
    {
        if (request is null || request.IntegrationId <= 0 || request.RecordIds is null || request.RecordIds.Count == 0)
            return Json(new { success = false, error = "IntegrationId ve recordIds zorunlu." });

        var triggeredBy = User?.Identity?.Name ?? "system";
        var results = new List<object>();
        int ok = 0, fail = 0;

        // Sirali dispatch — bir hata digerlerini durdurmaz (devam et stratejisi)
        foreach (var recordId in request.RecordIds.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(recordId)) continue;
            try
            {
                var res = await runner.RunAsync(
                    integrationId:   request.IntegrationId,
                    sourceRecordId:  recordId,
                    triggerType:     CalibraHub.Domain.Enums.IntegrationTriggerType.Manual,
                    triggeredBy:     triggeredBy,
                    ct:              ct);
                if (res.Success) ok++; else fail++;
                results.Add(new
                {
                    recordId,
                    success      = res.Success,
                    runId        = res.RunId,
                    httpStatus   = res.HttpStatusCode,
                    errorMessage = res.ErrorMessage,
                });
            }
            catch (Exception ex)
            {
                fail++;
                results.Add(new
                {
                    recordId,
                    success      = false,
                    runId        = (long?)null,
                    httpStatus   = (int?)null,
                    errorMessage = ex.Message,
                });
            }
        }

        return Json(new { success = true, total = results.Count, ok, fail, results });
    }

    public sealed record QueueSkipRequest(int IntegrationId, IReadOnlyList<string> RecordIds, string? Reason);

    [HttpPost("/Integrations/api/queue/skip")]
    public async Task<IActionResult> QueueSkipApi(
        [FromServices] IIntegrationRecordStatusRepository statusRepo,
        [FromBody] QueueSkipRequest? request,
        CancellationToken ct)
    {
        if (request is null || request.IntegrationId <= 0 || request.RecordIds is null || request.RecordIds.Count == 0)
            return Json(new { success = false, error = "IntegrationId ve recordIds zorunlu." });

        try
        {
            var actor = User?.Identity?.Name ?? "system";
            await statusRepo.SkipManyAsync(request.IntegrationId, request.RecordIds, request.Reason, actor, ct);
            return Json(new { success = true, count = request.RecordIds.Count });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    public sealed record QueueRestoreRequest(int IntegrationId, IReadOnlyList<string> RecordIds);

    [HttpPost("/Integrations/api/queue/restore")]
    public async Task<IActionResult> QueueRestoreApi(
        [FromServices] IIntegrationRecordStatusRepository statusRepo,
        [FromBody] QueueRestoreRequest? request,
        CancellationToken ct)
    {
        if (request is null || request.IntegrationId <= 0 || request.RecordIds is null || request.RecordIds.Count == 0)
            return Json(new { success = false, error = "IntegrationId ve recordIds zorunlu." });

        try
        {
            var actor = User?.Identity?.Name ?? "system";
            await statusRepo.RestoreManyAsync(request.IntegrationId, request.RecordIds, actor, ct);
            return Json(new { success = true, count = request.RecordIds.Count });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    private static QueueFilter ParseFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return QueueFilter.Active;
        return raw.Trim().ToLowerInvariant() switch
        {
            "active"  => QueueFilter.Active,
            "pending" => QueueFilter.Pending,
            "failed"  => QueueFilter.Failed,
            "sent"    => QueueFilter.Sent,
            "skipped" => QueueFilter.Skipped,
            "all"     => QueueFilter.All,
            _         => QueueFilter.Active,
        };
    }

    // Razor sayfasi mount noktasi — React buradan boot eder
    [HttpGet("/Integrations/Queue")]
    public IActionResult QueuePage() => View("Queue");
}

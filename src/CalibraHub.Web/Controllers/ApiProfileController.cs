using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ApiProfileController — Integration API Profile (HTTP entegrasyon profilleri)
/// JSON CRUD endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - POST /Admin/TestRestApiConnectionJson      → REST API ping (stub)
///   - GET  /Admin/GetApiProfilesJson              → liste (per company)
///   - GET  /Admin/GetIntegrationEndpointCatalogJson?vendor=Netsis  → onceden tanimli endpoint kataloga
///   - POST /Admin/SaveApiProfileJson              → upsert
///   - POST /Admin/DeleteApiProfileJson?id=        → soft delete
/// </summary>
[Authorize]
public sealed class ApiProfileController : Controller
{
    private readonly IIntegrationApiProfileRepository _apiProfileRepo;

    public ApiProfileController(IIntegrationApiProfileRepository apiProfileRepo)
    {
        _apiProfileRepo = apiProfileRepo;
    }

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    [HttpPost("/Admin/TestRestApiConnectionJson")]
    public IActionResult TestRestApiConnectionJson([FromBody] TestRestApiInput input)
    {
        // TODO: implement REST API connection test
        return Json(new { success = false, message = "Baglanti testi henuz desteklenmiyor. Konfigurasyonu kaydedip entegrasyon tetiklenerek test edilebilir." });
    }

    [HttpGet("/Admin/GetApiProfilesJson")]
    public async Task<IActionResult> GetApiProfilesJson(CancellationToken ct)
    {
        try
        {
            var companyId = GetCompanyId();
            var profiles = await _apiProfileRepo.GetByCompanyAsync(companyId, ct);
            return Json(profiles.Select(p => new
            {
                id = p.Id, name = p.Name, authType = p.AuthType,
                baseUrl = p.BaseUrl, authConfigJson = p.AuthConfigJson, isActive = p.IsActive,
                providerCode = p.ProviderCode,
            }).ToArray());
        }
        catch (Exception ex)
        {
            return Json(new { error = true, message = ex.Message });
        }
    }

    [HttpGet("/Admin/GetIntegrationEndpointCatalogJson")]
    public IActionResult GetIntegrationEndpointCatalogJson(string vendor = "Netsis")
    {
        IReadOnlyList<CalibraHub.Application.Services.IntegrationEndpointEntry> entries =
            vendor.Equals("Netsis", StringComparison.OrdinalIgnoreCase)
                ? CalibraHub.Application.Services.IntegrationEndpointCatalog.Netsis
                : Array.Empty<CalibraHub.Application.Services.IntegrationEndpointEntry>();
        return Json(entries.Select(e => new
        {
            path = e.Path,
            method = e.Method,
            title = e.Title,
            description = e.Description,
            bodyTemplate = e.BodyTemplate
        }).ToArray());
    }

    [HttpPost("/Admin/SaveApiProfileJson")]
    public async Task<IActionResult> SaveApiProfileJson([FromBody] ApiProfileInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Profil adi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.BaseUrl))
            return Json(new { success = false, message = "Base URL zorunludur." });
        try
        {
            CalibraHub.Domain.Entities.IntegrationApiProfile profile;
            if (input.Id.HasValue)
            {
                profile = await _apiProfileRepo.GetByIdAsync(input.Id.Value, ct)
                          ?? throw new InvalidOperationException("Profil bulunamadi.");
                profile.Name = input.Name;
                profile.AuthType = input.AuthType ?? "None";
                profile.BaseUrl = input.BaseUrl;
                profile.AuthConfigJson = input.AuthConfigJson;
                profile.IsActive = input.IsActive;
                profile.ProviderCode = string.IsNullOrWhiteSpace(input.ProviderCode) ? null : input.ProviderCode.Trim();
                profile.UpdatedAt = DateTime.Now;
            }
            else
            {
                profile = new CalibraHub.Domain.Entities.IntegrationApiProfile
                {
                    CompanyId = GetCompanyId(),
                    Name = input.Name,
                    AuthType = input.AuthType ?? "None",
                    BaseUrl = input.BaseUrl,
                    AuthConfigJson = input.AuthConfigJson,
                    IsActive = input.IsActive,
                    ProviderCode = string.IsNullOrWhiteSpace(input.ProviderCode) ? null : input.ProviderCode.Trim(),
                };
            }
            await _apiProfileRepo.UpsertAsync(profile, ct);
            return Json(new { success = true, message = "Profil kaydedildi." });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveApiProfileJson ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine($"[SaveApiProfileJson STACK] {ex.StackTrace}");
            if (ex.InnerException != null)
                Console.Error.WriteLine($"[SaveApiProfileJson INNER] {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("/Admin/DeleteApiProfileJson")]
    public async Task<IActionResult> DeleteApiProfileJson(Guid id, CancellationToken ct)
    {
        try
        {
            await _apiProfileRepo.DeleteAsync(id, ct);
            return Json(new { success = true, message = "Silindi." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}

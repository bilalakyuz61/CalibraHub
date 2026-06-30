using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// IntegratorSettingsJsonController — Entegrator (Mikro/Logo/eFinans) ayarlari
/// JSON CRUD + bagli test/pull endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/GetIntegratorFormDataJson     → dropdown'lar (sirket + saglayici)
///   - GET  /Admin/GetIntegratorsListJson        → liste (search + companyId)
///   - GET  /Admin/GetIntegratorJson?id=         → tekil
///   - POST /Admin/SaveIntegratorSettingsJson    → upsert
///   - POST /Admin/DeleteIntegratorSettingsJson  → soft-delete
///   - POST /Admin/TestIntegratorConnectionJson  → baglanti testi
///   - POST /Admin/PullIntegratorDataJson        → manuel veri cekme
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.Integrations)]
public sealed class IntegratorSettingsJsonController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;
    private readonly IDocumentImportService _documentImportService;

    public IntegratorSettingsJsonController(
        IAdminReadService adminReadService,
        IAdminManagementService adminManagementService,
        IDocumentImportService documentImportService)
    {
        _adminReadService = adminReadService;
        _adminManagementService = adminManagementService;
        _documentImportService = documentImportService;
    }

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private static bool TryParseIntegratorProvider(string value, out IntegratorProvider provider) =>
        Enum.TryParse(value, true, out provider) &&
        Enum.IsDefined(provider) &&
        provider != IntegratorProvider.Unknown;

    [HttpGet("/Admin/GetIntegratorFormDataJson")]
    public async Task<IActionResult> GetIntegratorFormDataJson(CancellationToken ct)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(ct);
        var companies = snapshot.Companies.Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { id = x.Id, name = x.Name });
        var providers = Enum.GetValues<IntegratorProvider>()
            .Where(p => p != IntegratorProvider.Unknown)
            .Select(p => new { value = p.ToString(), label = p.ToString() });
        var currentCompanyId = GetCompanyId();
        return Json(new { companies, providers, currentCompanyId });
    }

    [HttpGet("/Admin/GetIntegratorsListJson")]
    public async Task<IActionResult> GetIntegratorsListJson(string? search, int? companyId, CancellationToken ct)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(ct);
        var q = snapshot.Integrators.AsEnumerable();
        if (companyId.HasValue) q = q.Where(x => x.CompanyId == companyId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(x => (x.Name ?? "").ToLowerInvariant().Contains(s) ||
                              (x.Username ?? "").ToLowerInvariant().Contains(s));
        }
        return Json(q.Select(x => new
        {
            x.Id, x.Name, x.CompanyName, x.Provider, x.BaseUrl, x.CompanyTaxNumber,
            x.Username, hasSecret = !string.IsNullOrWhiteSpace(x.Secret),
            x.PollingIntervalSeconds, x.MaxRecordsPerPull, x.LogRetentionDays,
            x.IncludeReceivedDocumentsInPull, x.MarkDownloadedDocumentsAsReceived,
            x.IncludeIssuedEInvoicesInPull, x.IncludeIssuedEArchivesInPull, x.IncludeIssuedEDispatchesInPull,
            x.IsActive
        }).ToArray());
    }

    [HttpGet("/Admin/GetIntegratorJson")]
    public async Task<IActionResult> GetIntegratorJson(int id, CancellationToken ct)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(ct);
        var x = snapshot.Integrators.FirstOrDefault(i => i.Id == id);
        if (x is null) return Json(null);
        return Json(new
        {
            x.Id, x.CompanyId, provider = x.Provider.ToString(), x.Name, x.BaseUrl, x.CompanyTaxNumber,
            x.Username, x.Secret, x.PollingIntervalSeconds, x.MaxRecordsPerPull, x.LogRetentionDays,
            x.IncludeReceivedDocumentsInPull, x.MarkDownloadedDocumentsAsReceived,
            x.IncludeIssuedEInvoicesInPull, x.IncludeIssuedEArchivesInPull, x.IncludeIssuedEDispatchesInPull,
            x.IsActive, x.ScheduleEnabled, x.AppStr, x.Source, x.AppVersion,
            x.TimeoutSeconds, x.LookbackDays
        });
    }

    [HttpPost("/Admin/SaveIntegratorSettingsJson")]
    public async Task<IActionResult> SaveIntegratorSettingsJson([FromBody] IntegratorSettingsJsonInput input, CancellationToken ct)
    {
        if (!TryParseIntegratorProvider(input.Provider, out var provider))
            return Json(new { success = false, message = "Gecerli bir saglayici seciniz." });
        var companyId = GetCompanyId();
        if (companyId == 0)
            return Json(new { success = false, message = "Sirket kimlik bilgisi alinamadi." });
        try
        {
            var savedId = await _adminManagementService.SaveIntegratorSettingsAsync(
                new SaveIntegratorSettingsRequest(
                    null, companyId, provider, input.Name, input.BaseUrl,
                    input.CompanyTaxNumber, input.Username, input.Secret,
                    input.PollingIntervalSeconds, input.MaxRecordsPerPull, input.LogRetentionDays,
                    input.IncludeReceivedDocumentsInPull, input.MarkDownloadedDocumentsAsReceived,
                    input.IncludeIssuedEInvoicesInPull, input.IncludeIssuedEArchivesInPull,
                    input.IncludeIssuedEDispatchesInPull, input.IsActive, input.ScheduleEnabled, input.AppStr, input.Source, input.AppVersion,
                    input.TimeoutSeconds, input.LookbackDays),
                ct);
            return Json(new { success = true, message = "Entegrator ayari kaydedildi.", id = savedId });
        }
        catch (ArgumentException ex) { return Json(new { success = false, message = ex.Message }); }
        catch (Exception ex) { return Json(new { success = false, message = "Kayit hatasi: " + "İşlem sırasında bir hata oluştu." }); }
    }

    [HttpPost("/Admin/DeleteIntegratorSettingsJson")]
    public async Task<IActionResult> DeleteIntegratorSettingsJson(int id, CancellationToken ct)
    {
        try
        {
            await _adminManagementService.DeleteIntegratorSettingsAsync(id, ct);
            return Json(new { success = true, message = "Entegrator ayari silindi." });
        }
        catch (ArgumentException ex) { return Json(new { success = false, message = ex.Message }); }
    }

    [HttpPost("/Admin/TestIntegratorConnectionJson")]
    public async Task<IActionResult> TestIntegratorConnectionJson([FromBody] IntegratorSettingsJsonInput input, CancellationToken ct)
    {
        if (!TryParseIntegratorProvider(input.Provider, out var provider))
            return Json(new { success = false, message = "Gecerli bir saglayici seciniz." });
        var companyId = GetCompanyId();
        if (companyId == 0)
            return Json(new { success = false, message = "Sirket kimlik bilgisi alinamadi." });
        try
        {
            var result = await _adminManagementService.TestIntegratorConnectionAsync(
                new TestIntegratorConnectionRequest(
                    companyId, provider, input.Name, input.BaseUrl,
                    input.CompanyTaxNumber, input.Username, input.Secret,
                    input.PollingIntervalSeconds, input.MaxRecordsPerPull, input.LogRetentionDays,
                    input.IncludeReceivedDocumentsInPull, input.MarkDownloadedDocumentsAsReceived,
                    input.IncludeIssuedEInvoicesInPull, input.IncludeIssuedEArchivesInPull,
                    input.IncludeIssuedEDispatchesInPull, input.AppStr, input.Source, input.AppVersion,
                    input.TimeoutSeconds, input.LookbackDays),
                ct);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }
        catch (ArgumentException ex) { return Json(new { success = false, message = ex.Message }); }
        catch (Exception ex) { return Json(new { success = false, message = "Baglanti hatasi: " + "İşlem sırasında bir hata oluştu." }); }
    }

    [HttpPost("/Admin/PullIntegratorDataJson")]
    public async Task<IActionResult> PullIntegratorDataJson(CancellationToken ct)
    {
        try
        {
            var result = await _documentImportService.ImportFromActiveIntegratorsAsync(ct);
            var msg = $"Veri cekme tamamlandi. Yeni kayit: {result.ImportedCount}, atlanan: {result.SkippedCount}.";
            if (result.Notes.Count > 0) msg += " " + string.Join(" ", result.Notes.Take(2));
            return Json(new { success = true, message = msg });
        }
        catch (Exception ex) { return Json(new { success = false, message = $"Veri cekme islemi basarisiz: {"İşlem sırasında bir hata oluştu."}" }); }
    }
}

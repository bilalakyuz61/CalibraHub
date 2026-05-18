using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ParametersController — Sirket Parametreleri (Genel + per-form key/value)
/// endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/Parameters                   → view (sol tab listesi)
///   - POST /Admin/SaveGeneralParametersJson    → Genel tab (Company entity)
///   - GET  /Admin/ParametersList?formCode=     → key/value liste
///   - POST /Admin/SaveParameter                → key/value upsert
///   - POST /Admin/DeleteParameter              → key/value sil
/// </summary>
[Authorize]
public sealed class ParametersController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;
    private readonly ICompanyParameterService _companyParameters;

    public ParametersController(
        IAdminReadService adminReadService,
        IAdminManagementService adminManagementService,
        ICompanyParameterService companyParameters)
    {
        _adminReadService = adminReadService;
        _adminManagementService = adminManagementService;
        _companyParameters = companyParameters;
    }

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    /// <summary>
    /// /Admin/Parameters → sabit sol-tab listesi (Genel, Onay Islemleri, Is Emri,
    /// Satis Teklifi). Her tab kodla gomulu hardcoded UI kontrollerini gosterir.
    /// </summary>
    [HttpGet("/Admin/Parameters")]
    public async Task<IActionResult> Parameters(CancellationToken cancellationToken)
    {
        ViewData["AdminMenu"] = "settings";

        // Genel tab'i icin: E-Belge Onay Sistemi switch'i (Sirket Ayarlari'ndan tasindi).
        var companyId = GetCompanyId();
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var company = snapshot.Companies.FirstOrDefault(x => x.Id == companyId);
        ViewData["IsEDocumentApprovalEnabled"] = company?.IsEDocumentApprovalEnabled ?? false;

        return View("~/Views/Admin/Parameters.cshtml");
    }

    /// <summary>
    /// Genel tab'indaki sirket-seviyesi parametreleri kaydeder.
    /// Su an sadece IsEDocumentApprovalEnabled var; ileride yeni Company entity
    /// property'leri eklenebilir.
    /// </summary>
    [HttpPost("/Admin/SaveGeneralParametersJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGeneralParametersJson(
        [FromBody] GeneralParametersInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var companyId = GetCompanyId();
            var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
            var company = snapshot.Companies.FirstOrDefault(x => x.Id == companyId);
            if (company is null) return Json(new { ok = false, error = "Sirket bulunamadi." });

            await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    company.Id,
                    company.Name,
                    company.Title,
                    company.Address,
                    company.City,
                    company.District,
                    company.PostalCode,
                    company.TaxOffice,
                    company.TaxNumber,
                    input.IsEDocumentApprovalEnabled,
                    company.IsActive,
                    company.DatabaseConnectionString),
                cancellationToken);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpGet("/Admin/ParametersList")]
    public async Task<IActionResult> ParametersList(string? formCode, CancellationToken ct)
    {
        var list = await _companyParameters.ListAsync(formCode, ct);
        return Json(list);
    }

    [HttpPost("/Admin/SaveParameter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveParameter([FromBody] SetCompanyParameterRequest req, CancellationToken ct)
    {
        try
        {
            await _companyParameters.SetAsync(req, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost("/Admin/DeleteParameter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteParameter([FromBody] DeleteCompanyParameterRequest req, CancellationToken ct)
    {
        try
        {
            await _companyParameters.DeleteAsync(req.FormCode, req.ParamKey, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    public sealed record GeneralParametersInput(bool IsEDocumentApprovalEnabled);
    public sealed record DeleteCompanyParameterRequest(string FormCode, string ParamKey);
}

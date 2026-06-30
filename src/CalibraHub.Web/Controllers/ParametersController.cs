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
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.CompanySettings)]
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

    private const string ApprovalFormCode = "APPROVAL";
    private const string InvoiceApprovalKey  = "INVOICE_APPROVAL_ENABLED";
    private const string DispatchApprovalKey = "DISPATCH_APPROVAL_ENABLED";

    private const string ProductionFormCode = "PRODUCTION";
    public  const string ShopFloorMaxPinAttemptsKey = "SHOPFLOOR_MAX_PIN_ATTEMPTS";
    public  const int    ShopFloorMaxPinAttemptsDefault = 5;

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

        // Onay İşlemleri tab'i: alış faturası + irsaliye onay parametreleri
        var approvalParams = await _companyParameters.ListAsync(ApprovalFormCode, cancellationToken);
        ViewData["IsInvoiceApprovalEnabled"]  = approvalParams
            .FirstOrDefault(p => p.ParamKey == InvoiceApprovalKey)?.ParamValue == "true";
        ViewData["IsDispatchApprovalEnabled"] = approvalParams
            .FirstOrDefault(p => p.ParamKey == DispatchApprovalKey)?.ParamValue == "true";

        // Üretim tab'i: shop-floor PIN lockout limiti
        var productionParams = await _companyParameters.ListAsync(ProductionFormCode, cancellationToken);
        var rawPin = productionParams
            .FirstOrDefault(p => p.ParamKey == ShopFloorMaxPinAttemptsKey)?.ParamValue;
        ViewData["ShopFloorMaxPinAttempts"] =
            int.TryParse(rawPin, out var pinLim) && pinLim >= 0 && pinLim <= 50
                ? pinLim
                : ShopFloorMaxPinAttemptsDefault;

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
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
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
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
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
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Admin/SaveApprovalParametersJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveApprovalParametersJson(
        [FromBody] ApprovalParametersInput input,
        CancellationToken ct)
    {
        try
        {
            await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                ApprovalFormCode, InvoiceApprovalKey,
                input.IsInvoiceApprovalEnabled ? "true" : "false",
                CalibraHub.Domain.Enums.CompanyParameterDataType.Bool), ct);

            await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                ApprovalFormCode, DispatchApprovalKey,
                input.IsDispatchApprovalEnabled ? "true" : "false",
                CalibraHub.Domain.Enums.CompanyParameterDataType.Bool), ct);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost("/Admin/SaveProductionParametersJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProductionParametersJson(
        [FromBody] ProductionParametersInput input,
        CancellationToken ct)
    {
        try
        {
            if (input.ShopFloorMaxPinAttempts < 0 || input.ShopFloorMaxPinAttempts > 50)
                return Json(new { ok = false, error = "Hatalı PIN limiti 0 ile 50 arasında olmalı." });

            await _companyParameters.SetAsync(new SetCompanyParameterRequest(
                ProductionFormCode,
                ShopFloorMaxPinAttemptsKey,
                input.ShopFloorMaxPinAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CalibraHub.Domain.Enums.CompanyParameterDataType.Int), ct);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    public sealed record GeneralParametersInput(bool IsEDocumentApprovalEnabled);
    public sealed record ApprovalParametersInput(bool IsInvoiceApprovalEnabled, bool IsDispatchApprovalEnabled);
    public sealed record ProductionParametersInput(int ShopFloorMaxPinAttempts);
    public sealed record DeleteCompanyParameterRequest(string FormCode, string ParamKey);
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Admin;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// CompanySettingsController — Sirket bilgileri + SMTP profili + WhatsApp ayarlari
/// view + form-post endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/CompanySettings   → view (Company + SMTP + WhatsApp config)
///   - POST /Admin/CompanySettings   → upsert Company + SMTP (form-post)
/// </summary>
[Authorize]
public sealed class CompanySettingsController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;

    public CompanySettingsController(
        IAdminReadService adminReadService,
        IAdminManagementService adminManagementService)
    {
        _adminReadService = adminReadService;
        _adminManagementService = adminManagementService;
    }

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    [HttpGet("/Admin/CompanySettings")]
    public async Task<IActionResult> CompanySettings(
        [FromServices] IWhatsAppService whatsAppService,
        [FromServices] IWhatsAppSafetyRulesRepository safetyRepo,
        CancellationToken cancellationToken)
    {
        ViewData["AdminMenu"] = "company-settings";
        ViewBag.WhatsAppConfig = await whatsAppService.GetConfigAsync(cancellationToken);
        ViewBag.WhatsAppSafetyRules = await safetyRepo.GetAsync(cancellationToken);
        var companyId = GetCompanyId();
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var myCompany = snapshot.Companies.FirstOrDefault(x => x.Id == companyId);

        var input = myCompany is null
            ? new CompanyInput()
            : new CompanyInput
            {
                Id = myCompany.Id,
                Name = myCompany.Name,
                Title = myCompany.Title,
                Address = myCompany.Address,
                City = myCompany.City,
                District = myCompany.District,
                PostalCode = myCompany.PostalCode,
                TaxOffice = myCompany.TaxOffice,
                TaxNumber = myCompany.TaxNumber,
                IsEDocumentApprovalEnabled = myCompany.IsEDocumentApprovalEnabled,
                IsActive = myCompany.IsActive,
                DatabaseConnectionString = myCompany.DatabaseConnectionString
            };

        // SMTP profili yukle
        var smtpProfiles = snapshot.SmtpProfiles.Where(x => x.CompanyId == companyId).ToArray();
        var currentSmtp = smtpProfiles.FirstOrDefault();
        var smtpInput = currentSmtp != null
            ? new SmtpProfileInput
            {
                Id = currentSmtp.Id,
                CompanyId = currentSmtp.CompanyId,
                Name = currentSmtp.Name,
                FromEmail = currentSmtp.FromEmail,
                FromDisplayName = currentSmtp.FromDisplayName,
                Host = currentSmtp.Host,
                Port = currentSmtp.Port,
                Username = currentSmtp.Username,
                Password = currentSmtp.Password,
                AuthMethod = currentSmtp.AuthMethod,
                OAuth2ClientId = currentSmtp.OAuth2ClientId,
                OAuth2ClientSecret = currentSmtp.OAuth2ClientSecret,
                OAuth2RefreshToken = currentSmtp.OAuth2RefreshToken,
                UseSsl = currentSmtp.UseSsl,
                IsActive = currentSmtp.IsActive
            }
            : new SmtpProfileInput { CompanyId = companyId };

        return View("~/Views/Admin/CompanySettings.cshtml", new CompanyManagementViewModel
        {
            Companies = Array.Empty<CompanyDto>(),
            ListState = new GridListStateViewModel(),
            Input = input,
            SmtpInput = smtpInput,
            CurrentSmtp = currentSmtp
        });
    }

    [HttpPost("/Admin/CompanySettings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompanySettings(
        CompanyInput input,
        [Bind(Prefix = "SmtpInput")] SmtpProfileInput smtpInput,
        CancellationToken cancellationToken)
    {
        ViewData["AdminMenu"] = "company-settings";
        input.Id ??= GetCompanyId();

        ModelState.Clear();

        try
        {
            await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    input.Id,
                    input.Name,
                    input.Title,
                    input.Address,
                    input.City,
                    input.District,
                    input.PostalCode,
                    input.TaxOffice,
                    input.TaxNumber,
                    input.IsEDocumentApprovalEnabled,
                    input.IsActive,
                    input.DatabaseConnectionString),
                cancellationToken);

            // SMTP kaydet (host doluysa)
            if (!string.IsNullOrWhiteSpace(smtpInput.Host))
            {
                smtpInput.CompanyId ??= input.Id;
                smtpInput.Name = string.IsNullOrWhiteSpace(smtpInput.Name) ? "Varsayilan" : smtpInput.Name;
                try
                {
                    await _adminManagementService.SaveSmtpProfileAsync(
                        new SaveSmtpProfileRequest(
                            smtpInput.Id, smtpInput.CompanyId!.Value,
                            smtpInput.Name, smtpInput.FromEmail ?? "", smtpInput.FromDisplayName ?? "",
                            smtpInput.Host, smtpInput.Port,
                            smtpInput.Username ?? "", smtpInput.Password ?? "",
                            smtpInput.AuthMethod ?? "Normal",
                            smtpInput.OAuth2ClientId, smtpInput.OAuth2ClientSecret, smtpInput.OAuth2RefreshToken,
                            smtpInput.UseSsl, smtpInput.IsActive),
                        cancellationToken);
                }
                catch (ArgumentException smtpEx)
                {
                    TempData["AdminWarning"] = "Sirket kaydedildi ancak SMTP hatasi: " + smtpEx.Message;
                    return RedirectToAction(nameof(CompanySettings));
                }
            }

            TempData["AdminSuccess"] = "Sirket bilgileri kaydedildi.";
            return RedirectToAction(nameof(CompanySettings));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("~/Views/Admin/CompanySettings.cshtml", new CompanyManagementViewModel
            {
                Companies = Array.Empty<CompanyDto>(),
                ListState = new GridListStateViewModel(),
                Input = input
            });
        }
    }
}

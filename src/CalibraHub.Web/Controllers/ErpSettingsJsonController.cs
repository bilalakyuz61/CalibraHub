using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ErpSettingsJsonController — ERP baglanti ayarlari (Netsis/Mikro vb. SQL
/// baglantisi) JSON CRUD endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/GetErpSettingsJson         → liste (search)
///   - GET  /Admin/GetErpSettingJson?id=      → tekil (sifre dahil — admin amaci)
///   - POST /Admin/SaveErpSettingsJson        → upsert
///   - POST /Admin/DeleteErpSettingsJson      → soft-delete
///   - POST /Admin/TestErpConnectionJson      → SQL baglanti testi
/// </summary>
[Authorize]
public sealed class ErpSettingsJsonController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;

    public ErpSettingsJsonController(
        IAdminReadService adminReadService,
        IAdminManagementService adminManagementService)
    {
        _adminReadService = adminReadService;
        _adminManagementService = adminManagementService;
    }

    [HttpGet("/Admin/GetErpSettingsJson")]
    public async Task<IActionResult> GetErpSettingsJson(string? search, CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var items = snapshot.ErpConnections.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLowerInvariant();
            items = items.Where(x =>
                (x.Company ?? "").ToLowerInvariant().Contains(search) ||
                (x.CompanyName ?? "").ToLowerInvariant().Contains(search) ||
                (x.Business ?? "").ToLowerInvariant().Contains(search));
        }
        return Json(items.Select(x => new
        {
            x.Id, x.CompanyId, x.CompanyName, x.Provider,
            x.Company, x.Business, x.Branch, x.Username,
            passwordMasked = string.IsNullOrWhiteSpace(x.Password) ? "-" : "********",
            x.IsActive
        }).ToArray());
    }

    [HttpGet("/Admin/GetErpSettingJson")]
    public async Task<IActionResult> GetErpSettingJson(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var item = snapshot.ErpConnections.FirstOrDefault(x => x.Id == id);
        if (item is null) return Json(new { success = false, message = "Kayit bulunamadi." });
        return Json(new
        {
            item.Id, item.CompanyId, item.CompanyName, item.Provider,
            item.Company, item.Business, item.Branch, item.Username,
            item.Password, item.IsActive
        });
    }

    [HttpPost("/Admin/SaveErpSettingsJson")]
    public async Task<IActionResult> SaveErpSettingsJson([FromBody] ErpConnectionInput input, CancellationToken cancellationToken)
    {
        if (!input.CompanyId.HasValue)
            return Json(new { success = false, message = "Sirket secimi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Company))
            return Json(new { success = false, message = "Sirket alani zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Business))
            return Json(new { success = false, message = "Isletme alani zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Branch))
            return Json(new { success = false, message = "Sube alani zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Username))
            return Json(new { success = false, message = "Kullanici adi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Password))
            return Json(new { success = false, message = "Sifre zorunludur." });

        try
        {
            await _adminManagementService.SaveErpConnectionSettingsAsync(
                new SaveErpConnectionSettingsRequest(
                    input.Id,
                    input.CompanyId.Value,
                    input.Company,
                    input.Business,
                    input.Branch,
                    input.Username,
                    input.Password,
                    input.IsActive),
                cancellationToken);
            return Json(new { success = true, message = "ERP baglanti ayari kaydedildi." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("/Admin/DeleteErpSettingsJson")]
    public async Task<IActionResult> DeleteErpSettingsJson(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _adminManagementService.DeleteErpConnectionAsync(id, cancellationToken);
            return Json(new { success = true, message = "Silindi." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("/Admin/TestErpConnectionJson")]
    public async Task<IActionResult> TestErpConnectionJson([FromBody] ErpConnectionInput input, CancellationToken cancellationToken)
    {
        if (!input.CompanyId.HasValue || string.IsNullOrWhiteSpace(input.Company) ||
            string.IsNullOrWhiteSpace(input.Business) || string.IsNullOrWhiteSpace(input.Branch) ||
            string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password))
            return Json(new { success = false, message = "SQL testi icin tum zorunlu alanlari doldurunuz." });

        try
        {
            var result = await _adminManagementService.TestErpConnectionAsync(
                new TestErpConnectionRequest(
                    input.CompanyId.Value,
                    input.Company,
                    input.Business,
                    input.Branch,
                    input.Username,
                    input.Password),
                cancellationToken);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}

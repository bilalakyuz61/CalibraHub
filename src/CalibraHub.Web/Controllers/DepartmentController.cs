using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.SmartBoard;
using CalibraHub.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// DepartmentController — Departman JSON CRUD + SmartBoard endpoint'leri
/// (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler (JSON CRUD + Board + Edit view):
///   - GET  /Admin/GetDepartmentsJson           → liste (search + companyId filtre)
///   - POST /Admin/SaveDepartmentJson           → yeni
///   - GET  /Admin/GetDepartmentJson?id=        → tekil
///   - POST /Admin/UpdateDepartmentJson         → guncelle
///   - POST /Admin/DeleteDepartmentJson?id=     → soft-delete
///   - POST /Admin/ToggleDepartmentJson         → aktif/pasif switch
///   - GET  /Admin/GetDepartmentsLookup         → PersonnelEdit dropdown
///   - GET  /Admin/DepartmentEdit?id=           → edit view
///   - GET  /Admin/DepartmentsBoardConfig       → SmartBoard refresh
///
/// AdminController'da kalan (BuildDepartmentViewModelAsync helper'a bagli):
///   - GET  /Admin/Departments (view) + form-post Departments
/// </summary>
[Authorize]
public sealed class DepartmentController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;

    public DepartmentController(
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

    [HttpGet("/Admin/GetDepartmentsJson")]
    public async Task<IActionResult> GetDepartmentsJson(string? search, int? companyId, CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var items = snapshot.Departments.AsEnumerable();
        if (companyId.HasValue)
            items = items.Where(x => x.CompanyId == companyId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            items = items.Where(x => (x.Name ?? "").ToLowerInvariant().Contains(q));
        }
        return Json(items.Select(x => new
        {
            x.Id, x.CompanyId, x.CompanyName, x.Name, x.IsActive
        }).ToArray());
    }

    [HttpPost("/Admin/SaveDepartmentJson")]
    public async Task<IActionResult> SaveDepartmentJson([FromBody] DepartmentCreateInput input, CancellationToken cancellationToken)
    {
        var companyId = input.CompanyId ?? GetCompanyId();
        if (companyId <= 0)
            return Json(new { ok = false, success = false, error = "Sirket secimi zorunludur.", message = "Sirket secimi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { ok = false, success = false, error = "Ad zorunludur.", message = "Ad zorunludur." });

        try
        {
            await _adminManagementService.CreateDepartmentAsync(
                new CreateDepartmentRequest(companyId, input.Name),
                cancellationToken);
            return Json(new { ok = true, success = true, message = "Departman olusturuldu." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { ok = false, success = false, error = ex.Message, message = ex.Message });
        }
    }

    [HttpGet("/Admin/GetDepartmentJson")]
    public async Task<IActionResult> GetDepartmentJson(int id, CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var dept = snapshot.Departments.FirstOrDefault(x => x.Id == id);
        if (dept is null) return NotFound();
        return Json(new
        {
            dept.Id, dept.CompanyId, dept.CompanyName,
            dept.Name, dept.IsActive
        });
    }

    [HttpPost("/Admin/UpdateDepartmentJson")]
    public async Task<IActionResult> UpdateDepartmentJson([FromBody] DepartmentUpdateInput input, CancellationToken cancellationToken)
    {
        if (input.Id <= 0)
            return Json(new { ok = false, success = false, error = "Departman secimi zorunludur.", message = "Departman secimi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { ok = false, success = false, error = "Ad zorunludur.", message = "Ad zorunludur." });

        try
        {
            await _adminManagementService.UpdateDepartmentAsync(
                new UpdateDepartmentRequest(input.Id, input.Name, input.IsActive),
                cancellationToken);
            return Json(new { ok = true, success = true, message = "Departman guncellendi." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { ok = false, success = false, error = ex.Message, message = ex.Message });
        }
    }

    [HttpPost("/Admin/DeleteDepartmentJson")]
    public async Task<IActionResult> DeleteDepartmentJson(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _adminManagementService.DeleteDepartmentAsync(id, cancellationToken);
            return Json(new { ok = true, success = true, message = "Departman silindi." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { ok = false, success = false, error = ex.Message, message = ex.Message });
        }
    }

    /// <summary>Departman aktif/pasif toggle (SmartCard switch davranisi).</summary>
    [HttpPost("/Admin/ToggleDepartmentJson")]
    public async Task<IActionResult> ToggleDepartmentJson(int id, bool isActive, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
            var dept = snapshot.Departments.FirstOrDefault(x => x.Id == id);
            if (dept is null) return Json(new { ok = false, error = "Departman bulunamadi." });

            await _adminManagementService.UpdateDepartmentAsync(
                new UpdateDepartmentRequest(id, dept.Name, isActive),
                cancellationToken);
            return Json(new { ok = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Personel formu icin departman lookup (per company). PersonnelEdit'teki
    /// Departman select'i bu endpoint'i cagirir. companyId verilmezse aktif
    /// kullanicinin sirketi kullanilir.
    /// </summary>
    [HttpGet("/Admin/GetDepartmentsLookup")]
    public async Task<IActionResult> GetDepartmentsLookup(int? companyId, CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var cid = companyId ?? GetCompanyId();
        var items = snapshot.Departments
            .Where(d => d.IsActive && (cid <= 0 || d.CompanyId == cid))
            .OrderBy(d => d.Name)
            .Select(d => new { id = d.Id, name = d.Name, companyId = d.CompanyId, companyName = d.CompanyName })
            .ToArray();
        return Json(items);
    }

    [HttpGet("/Admin/DepartmentEdit")]
    public async Task<IActionResult> DepartmentEdit(int? id, CancellationToken cancellationToken)
    {
        ViewData["AdminMenu"] = "departments";
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var companies = snapshot.Companies.Where(c => c.IsActive).OrderBy(c => c.Name).ToArray();

        DepartmentDto? dept = null;
        if (id.HasValue && id.Value > 0)
        {
            dept = snapshot.Departments.FirstOrDefault(x => x.Id == id.Value);
            if (dept is null) return NotFound();
        }
        ViewBag.Companies = companies;
        return View("~/Views/Admin/DepartmentEdit.cshtml", dept);
    }

    /// <summary>SmartBoard in-place refresh — Delete sonrasi config'i tekrar ceker.</summary>
    [HttpGet("/Admin/DepartmentsBoardConfig")]
    public async Task<IActionResult> DepartmentsBoardConfig(CancellationToken cancellationToken)
    {
        var board = await BuildDepartmentBoardConfigAsync(cancellationToken);
        return Json(board);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    // Rapor §2.2: SmartBoardBuilder migration referans ornek
    private async Task<object> BuildDepartmentBoardConfigAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var currentCompanyId = GetCompanyId();
        var departments = snapshot.Departments
            .Where(d => currentCompanyId <= 0 || d.CompanyId == currentCompanyId)
            .OrderBy(d => d.Name)
            .ToArray();

        return SmartBoard.For(departments)
            .WithBoardKey("admin-departments")
            .WithTitle("Departman Tanimlamalari", subtitle: $"{departments.Length} departman")
            .WithIcon("Building2", "blue")
            .WithRefreshUrl("/Admin/DepartmentsBoardConfig")
            .WithSearchPlaceholder("Hizli ara... (ad, sirket)")
            .WithEmptyText("Henuz departman tanimlanmamis")
            .AddHeaderAction("new", "Yeni Departman", "Plus", "/Admin/DepartmentEdit")
            .MapEntities(d =>
                SmartBoardEntity.For(d.Id, d.Name)
                    .AddStatusWidget("w_active", "Durum", d.IsActive)
                    .WithEditAndDelete(
                        editUrl:       $"/Admin/DepartmentEdit?id={d.Id}",
                        deleteApiUrl:  $"/Admin/DeleteDepartmentJson?id={d.Id}",
                        deleteConfirm: $"Bu departmani silmek istediginize emin misiniz? ({d.Name})"))
            .Build();
    }
}

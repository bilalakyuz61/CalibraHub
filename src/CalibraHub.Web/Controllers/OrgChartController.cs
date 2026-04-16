using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class OrgChartController : Controller
{
    private readonly IOrgChartRepository _orgChartRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public OrgChartController(
        IOrgChartRepository orgChartRepository,
        IUserProfileRepository userProfileRepository,
        IDepartmentRepository departmentRepository)
    {
        _orgChartRepository = orgChartRepository;
        _userProfileRepository = userProfileRepository;
        _departmentRepository = departmentRepository;
    }

    [HttpGet]
    public IActionResult Index() => View();

    // ── JSON API ─────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetChartsJson(CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        var charts = await _orgChartRepository.GetChartsByCompanyAsync(companyId, ct);
        return Json(charts.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            isDefault = c.IsDefault,
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetChartDetailJson(Guid chartId, CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        var chart = await _orgChartRepository.GetChartByIdAsync(chartId, ct);
        if (chart is null || chart.CompanyId != companyId)
            return Json(new { success = false, message = "Sema bulunamadi." });

        var nodes = await _orgChartRepository.GetNodesByChartAsync(chartId, ct);
        var users = await _userProfileRepository.GetAllAsync(ct);
        var departments = await _departmentRepository.GetAllAsync(ct);

        var companyUsers = users.Where(u => u.CompanyId == companyId && u.IsActive).ToArray();
        var companyDepts = departments.Where(d => d.CompanyId == companyId && d.IsActive).ToArray();

        return Json(new
        {
            success = true,
            chart = new { id = chart.Id, name = chart.Name, isDefault = chart.IsDefault },
            nodes = nodes.Select(n => new
            {
                id = n.Id,
                userId = n.UserId,
                parentUserId = n.ParentUserId,
                positionTitle = n.PositionTitle,
                sortOrder = n.SortOrder,
            }),
            users = companyUsers.Select(u => new
            {
                id = u.Id,
                fullName = u.FullName,
                email = u.Email,
                employeeCode = u.EmployeeCode,
                departmentId = u.DepartmentId,
                role = u.Role.ToString(),
                supervisorUserId = u.SupervisorUserId,
            }),
            departments = companyDepts.Select(d => new
            {
                id = d.Id,
                name = d.Name,
            }),
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveChartJson([FromBody] SaveChartInput input, CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Sema adi bos olamaz." });

        OrgChart chart;
        if (input.Id.HasValue)
        {
            var existing = await _orgChartRepository.GetChartByIdAsync(input.Id.Value, ct);
            if (existing is null || existing.CompanyId != companyId)
                return Json(new { success = false, message = "Sema bulunamadi." });
            existing.Name = input.Name.Trim();
            existing.UpdatedAt = DateTime.Now;
            chart = existing;
        }
        else
        {
            chart = new OrgChart
            {
                CompanyId = companyId,
                Name = input.Name.Trim(),
                IsDefault = input.IsDefault,
            };
        }

        await _orgChartRepository.SaveChartAsync(chart, ct);
        return Json(new { success = true, id = chart.Id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteChartJson([FromBody] IdInput input, CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        var chart = await _orgChartRepository.GetChartByIdAsync(input.Id, ct);
        if (chart is null || chart.CompanyId != companyId)
            return Json(new { success = false, message = "Sema bulunamadi." });

        await _orgChartRepository.DeleteChartAsync(input.Id, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> SetDefaultChartJson([FromBody] IdInput input, CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        await _orgChartRepository.SetDefaultChartAsync(companyId, input.Id, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> SaveNodesJson([FromBody] SaveNodesInput input, CancellationToken ct)
    {
        var nodes = (input.Nodes ?? []).Select(n => new OrgChartNode
        {
            Id = n.Id ?? Guid.NewGuid(),
            ChartId = input.ChartId,
            UserId = n.UserId,
            ParentUserId = n.ParentUserId,
            PositionTitle = n.PositionTitle,
            SortOrder = n.SortOrder,
        }).ToList();

        await _orgChartRepository.ReplaceNodesAsync(input.ChartId, nodes, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteNodeJson([FromBody] IdInput input, CancellationToken ct)
    {
        await _orgChartRepository.DeleteNodeAsync(input.Id, ct);
        return Json(new { success = true });
    }

    /// <summary>Mevcut supervisor_user_id iliskilerinden varsayilan sema olusturur.</summary>
    [HttpPost]
    public async Task<IActionResult> GenerateDefaultChartJson(CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        var users = await _userProfileRepository.GetAllAsync(ct);
        var companyUsers = users.Where(u => u.CompanyId == companyId && u.IsActive).ToArray();

        var chart = new OrgChart
        {
            CompanyId = companyId,
            Name = "Varsayilan Organizasyon",
            IsDefault = true,
        };

        // Diger varsayilanlari kaldir
        await _orgChartRepository.SetDefaultChartAsync(companyId, chart.Id, ct);
        await _orgChartRepository.SaveChartAsync(chart, ct);

        var nodes = companyUsers.Select((u, i) => new OrgChartNode
        {
            ChartId = chart.Id,
            UserId = u.Id,
            ParentUserId = u.SupervisorUserId,
            PositionTitle = u.Role.ToString(),
            SortOrder = i,
        }).ToList();

        await _orgChartRepository.ReplaceNodesAsync(chart.Id, nodes, ct);

        return Json(new { success = true, id = chart.Id });
    }

    // ── Helpers & Input Records ──────────────────────────────

    private (int CompanyId, Guid UserId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var companyIdStr = User.FindFirstValue("company_id") ?? string.Empty;
        Guid.TryParse(userIdStr, out var userId);
        int.TryParse(companyIdStr, out var companyId);
        return (companyId, userId);
    }

    public sealed record SaveChartInput(Guid? Id, string Name, bool IsDefault = false);
    public sealed record IdInput(Guid Id);
    public sealed record SaveNodesInput(Guid ChartId, List<NodeInput>? Nodes);
    public sealed record NodeInput(Guid? Id, Guid UserId, Guid? ParentUserId, string? PositionTitle, int SortOrder = 0);
}

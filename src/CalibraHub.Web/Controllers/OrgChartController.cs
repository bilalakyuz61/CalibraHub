using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.OrgChart)]
public sealed class OrgChartController : Controller
{
    private readonly IOrgChartRepository _orgChartRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPersonnelRepository _personnelRepository;
    private readonly OrgChartDomainService _domainService;

    public OrgChartController(
        IOrgChartRepository orgChartRepository,
        IUserProfileRepository userProfileRepository,
        IDepartmentRepository departmentRepository,
        IPersonnelRepository personnelRepository,
        OrgChartDomainService domainService)
    {
        _orgChartRepository = orgChartRepository;
        _userProfileRepository = userProfileRepository;
        _departmentRepository = departmentRepository;
        _personnelRepository = personnelRepository;
        _domainService = domainService;
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
    public async Task<IActionResult> GetChartDetailJson(int chartId, CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        var chart = await _orgChartRepository.GetChartByIdAsync(chartId, ct);
        if (chart is null || chart.CompanyId != companyId)
            return Json(new { success = false, message = "Sema bulunamadi." });

        var nodes = await _orgChartRepository.GetNodesByChartAsync(chartId, ct);
        var users = await _userProfileRepository.GetAllAsync(ct);
        var departments = await _departmentRepository.GetAllAsync(ct);
        var personnel = await _personnelRepository.ListAsync(false, false, ct);

        var companyUsers = users.Where(u => (companyId == 0 || u.CompanyId == companyId) && u.IsActive).ToArray();
        var companyDepts = departments.Where(d => (companyId == 0 || d.CompanyId == companyId) && d.IsActive).ToArray();

        return Json(new
        {
            success = true,
            chart = new { id = chart.Id, name = chart.Name, isDefault = chart.IsDefault },
            nodes = nodes.Select(n => new
            {
                id = n.Id,
                nodeType = n.NodeType.ToString(),
                userId = n.UserId,
                parentUserId = n.ParentUserId,
                parentNodeId = n.ParentNodeId,
                positionTitle = n.PositionTitle,
                sortOrder = n.SortOrder,
                departmentId = n.DepartmentId,
                personnelId = n.PersonnelId,
                displayName = ResolveDisplayName(n, companyUsers, companyDepts, personnel),
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
            personnelList = personnel.Select(p => new
            {
                id = p.Id,
                fullName = p.FullName,
                code = p.Code,
            }),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveChartJson([FromBody] SaveChartInput input, CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Sema adi bos olamaz." });

        OrgChart chart;
        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var existing = await _orgChartRepository.GetChartByIdAsync(input.Id.Value, ct);
            if (existing is null || existing.CompanyId != companyId)
                return Json(new { success = false, message = "Sema bulunamadi." });
            existing.Name = input.Name.Trim();
            existing.Updated = DateTime.UtcNow;
            chart = existing;
        }
        else
        {
            chart = new OrgChart
            {
                CompanyId = companyId,
                Name = input.Name.Trim(),
            };
            if (input.IsDefault) chart.MarkAsDefault();
        }

        await _orgChartRepository.SaveChartAsync(chart, ct);
        return Json(new { success = true, id = chart.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultChartJson([FromBody] IdInput input, CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        await _orgChartRepository.SetDefaultChartAsync(companyId, input.Id, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNodesJson([FromBody] SaveNodesInput input, CancellationToken ct)
    {
        var nodes = (input.Nodes ?? []).Select((n, i) =>
        {
            var nodeType = n.NodeType switch
            {
                "Department" => OrgChartNodeType.Department,
                "Personnel"  => OrgChartNodeType.Personnel,
                "Vacant"     => OrgChartNodeType.Vacant,
                _            => OrgChartNodeType.User,
            };
            return new OrgChartNode
            {
                Id            = n.Id ?? 0,
                ChartId       = input.ChartId,
                UserId        = n.UserId,
                ParentUserId  = n.ParentUserId,
                ParentNodeId  = n.ParentNodeId,
                PositionTitle = n.PositionTitle,
                SortOrder     = n.SortOrder,
                NodeType      = nodeType,
                DepartmentId  = n.DepartmentId,
                PersonnelId   = n.PersonnelId,
            };
        }).ToList();

        await _orgChartRepository.ReplaceNodesAsync(input.ChartId, nodes, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNodeJson([FromBody] IdInput input, CancellationToken ct)
    {
        await _orgChartRepository.DeleteNodeAsync(input.Id, ct);
        return Json(new { success = true });
    }

    // ── Delta Endpoints (Sprint 1 - yeni) ───────────────────

    /// <summary>Tek node'u yeni parent altına taşır. Cycle koruması aktif.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveNodeJson([FromBody] MoveNodeInput input, CancellationToken ct)
    {
        var nodes = await _orgChartRepository.GetNodesByChartAsync(input.ChartId, ct);
        if (_domainService.WouldCreateCycle(nodes, input.NodeId, input.NewParentNodeId))
            return Json(new { success = false, message = "Bu taşıma döngü yaratır." });

        await _orgChartRepository.MoveNodeAsync(input.NodeId, input.NewParentNodeId, input.NewSortOrder, ct);
        return Json(new { success = true });
    }

    /// <summary>Yeni node ekler (User/Department/Personnel/Vacant).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNodeJson([FromBody] AddNodeInput input, CancellationToken ct)
    {
        var nodeType = input.NodeType switch
        {
            "Department" => OrgChartNodeType.Department,
            "Personnel"  => OrgChartNodeType.Personnel,
            "Vacant"     => OrgChartNodeType.Vacant,
            _            => OrgChartNodeType.User,
        };

        var node = new OrgChartNode
        {
            ChartId       = input.ChartId,
            NodeType      = nodeType,
            UserId        = nodeType == OrgChartNodeType.User ? input.RefId : null,
            DepartmentId  = nodeType == OrgChartNodeType.Department ? (int?)input.IntRefId : null,
            PersonnelId   = nodeType == OrgChartNodeType.Personnel  ? (int?)input.IntRefId : null,
            ParentNodeId  = input.ParentNodeId,
            PositionTitle = input.PositionTitle,
            SortOrder     = input.SortOrder,
        };

        await _orgChartRepository.SaveNodeAsync(node, ct);
        return Json(new { success = true, id = node.Id });
    }

    /// <summary>Node siler. cascade=true ise alt ağacı da siler.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveNodeJson([FromBody] RemoveNodeInput input, CancellationToken ct)
    {
        if (input.Cascade)
        {
            var allNodes = await _orgChartRepository.GetNodesByChartAsync(input.ChartId, ct);
            var subtree = _domainService.GetSubtree(allNodes, input.NodeId);
            foreach (var id in subtree)
                await _orgChartRepository.DeleteNodeAsync(id, ct);
        }
        else
        {
            await _orgChartRepository.DeleteNodeAsync(input.NodeId, ct);
        }
        return Json(new { success = true });
    }

    /// <summary>Şema bütünlük kontrolü — uyarı listesi döner.</summary>
    [HttpGet]
    public async Task<IActionResult> ValidateChartJson(int chartId, CancellationToken ct)
    {
        var nodes = await _orgChartRepository.GetNodesByChartAsync(chartId, ct);
        var warnings = _domainService.Validate(nodes);
        return Json(new { valid = warnings.Count == 0, warnings });
    }

    /// <summary>Mevcut supervisor_user_id iliskilerinden varsayilan sema olusturur.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateDefaultChartJson(CancellationToken ct)
    {
        var (companyId, _) = GetCurrentUser();
        var users = await _userProfileRepository.GetAllAsync(ct);
        var companyUsers = users.Where(u => (companyId == 0 || u.CompanyId == companyId) && u.IsActive).ToArray();

        var chart = new OrgChart
        {
            CompanyId = companyId,
            Name = "Varsayilan Organizasyon",
        };
        chart.MarkAsDefault();

        // Save first to get the generated INT identity ID
        await _orgChartRepository.SaveChartAsync(chart, ct);

        // Now set as default using the real ID
        await _orgChartRepository.SetDefaultChartAsync(companyId, chart.Id, ct);

        // Create nodes without ParentNodeId initially
        var nodeList = companyUsers.Select((u, i) => new OrgChartNode
        {
            ChartId       = chart.Id,
            UserId        = u.Id,
            ParentUserId  = u.SupervisorUserId,
            PositionTitle = u.Role.ToString(),
            SortOrder     = i,
            NodeType      = OrgChartNodeType.User,
        }).ToList();

        // Insert all nodes (ReplaceNodesAsync sets node.Id after insertion)
        await _orgChartRepository.ReplaceNodesAsync(chart.Id, nodeList, ct);

        // Now node.Id is populated — build userId → nodeId map
        var userToNode = nodeList
            .Where(n => n.UserId.HasValue)
            .ToDictionary(n => n.UserId!.Value, n => n.Id);

        // Link parent nodes via MoveNodeAsync
        foreach (var node in nodeList.Where(n =>
            n.ParentUserId.HasValue &&
            userToNode.TryGetValue(n.ParentUserId.Value, out _)))
        {
            var parentNodeId = userToNode[node.ParentUserId!.Value];
            if (parentNodeId != node.Id) // avoid self-reference
                await _orgChartRepository.MoveNodeAsync(node.Id, parentNodeId, node.SortOrder, ct);
        }

        return Json(new { success = true, id = chart.Id });
    }

    // ── Helpers ──────────────────────────────────────────────

    private (int CompanyId, int UserId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var companyIdStr = User.FindFirstValue("company_id") ?? string.Empty;
        int.TryParse(userIdStr, out var userId);
        int.TryParse(companyIdStr, out var companyId);
        return (companyId, userId);
    }

    private static string? ResolveDisplayName(
        OrgChartNode node,
        IEnumerable<Domain.Entities.UserProfile> users,
        IEnumerable<Domain.Entities.Department> departments,
        IEnumerable<Application.Contracts.PersonnelDto> personnel)
    {
        return node.NodeType switch
        {
            OrgChartNodeType.User =>
                node.UserId.HasValue
                    ? users.FirstOrDefault(u => u.Id == node.UserId.Value)?.FullName
                    : null,
            OrgChartNodeType.Department =>
                node.DepartmentId.HasValue
                    ? departments.FirstOrDefault(d => d.Id == node.DepartmentId.Value)?.Name
                    : null,
            OrgChartNodeType.Personnel =>
                node.PersonnelId.HasValue
                    ? personnel.FirstOrDefault(p => p.Id == node.PersonnelId.Value)?.FullName
                    : null,
            OrgChartNodeType.Vacant => node.PositionTitle ?? "Boş Kadro",
            _ => null,
        };
    }

    // ── Input Records ────────────────────────────────────────

    public sealed record SaveChartInput(int? Id, string Name, bool IsDefault = false);
    public sealed record IdInput(int Id);
    public sealed record SaveNodesInput(int ChartId, List<NodeInput>? Nodes);
    public sealed record NodeInput(
        int? Id,
        int? UserId,
        int? ParentUserId,
        int? ParentNodeId,
        string? PositionTitle,
        string NodeType = "User",
        int? DepartmentId = null,
        int? PersonnelId = null,
        int SortOrder = 0);
    public sealed record MoveNodeInput(int ChartId, int NodeId, int? NewParentNodeId, int NewSortOrder = 0);
    public sealed record AddNodeInput(
        int ChartId,
        string NodeType,
        int? RefId,
        int? IntRefId,
        int? ParentNodeId,
        string? PositionTitle,
        int SortOrder = 0);
    public sealed record RemoveNodeInput(int ChartId, int NodeId, bool Cascade = false);
}

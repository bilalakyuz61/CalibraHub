using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using NCalc;

namespace CalibraHub.Application.Workflow;

/// <summary>
/// NCalc Expression'larına OrgChart hiyerarşi fonksiyonlarını ekler.
/// WorkflowEngine.EvaluateConditionAsync çağırılmadan önce bu handler kaydedilir.
/// Handler'lar async event (EvaluateAsyncFunction) üzerinden bağlanır — repository
/// çağrıları thread bloklamadan await edilir; ifade EvaluateAsync ile değerlendirilmelidir.
///
/// Desteklenen fonksiyonlar:
///   OrgChart.SupervisorOf(userId)        → string | null
///   OrgChart.SubordinatesOf(userId)      → string[] (CSV)
///   OrgChart.DepartmentOf(userId)        → int | null
///   OrgChart.HierarchyLevel(userId)      → int (root=0)
///   User.HasRole(userId, role)           → bool
/// </summary>
public sealed class OrgChartNCalcFunctions(
    IOrgChartRepository orgChartRepo,
    IUserProfileRepository userRepo)
{
    public void Register(Expression expr)
    {
        expr.EvaluateAsyncFunction += async (name, args) =>
        {
            switch (name)
            {
                case "OrgChart.SupervisorOf":
                {
                    var userId = (await args.Parameters.EvaluateAsync(0))?.ToString() ?? "";
                    args.Result = await GetSupervisorOf(userId);
                    break;
                }
                case "OrgChart.SubordinatesOf":
                {
                    var userId = (await args.Parameters.EvaluateAsync(0))?.ToString() ?? "";
                    args.Result = await GetSubordinatesOf(userId);
                    break;
                }
                case "OrgChart.DepartmentOf":
                {
                    var userId = (await args.Parameters.EvaluateAsync(0))?.ToString() ?? "";
                    args.Result = await GetDepartmentOf(userId);
                    break;
                }
                case "OrgChart.HierarchyLevel":
                {
                    var userId = (await args.Parameters.EvaluateAsync(0))?.ToString() ?? "";
                    args.Result = await GetHierarchyLevel(userId);
                    break;
                }
                case "User.HasRole":
                {
                    var userId = (await args.Parameters.EvaluateAsync(0))?.ToString() ?? "";
                    var role   = (await args.Parameters.EvaluateAsync(1))?.ToString() ?? "";
                    args.Result = await HasRole(userId, role);
                    break;
                }
            }
        };
    }

    // ── Implementations ───────────────────────────────────────────────────

    private async Task<string?> GetSupervisorOf(string userId)
    {
        var (node, nodes) = await FindNodeAndSiblings(userId);
        if (node?.ParentNodeId is null) return null;
        var parent = nodes.FirstOrDefault(n => n.Id == node.ParentNodeId);
        return parent?.UserId?.ToString();
    }

    private async Task<string> GetSubordinatesOf(string userId)
    {
        var (node, nodes) = await FindNodeAndSiblings(userId);
        if (node is null) return "";
        var children = nodes.Where(n => n.ParentNodeId == node.Id).Select(n => n.UserId?.ToString()).OfType<string>();
        return string.Join(",", children);
    }

    private async Task<int?> GetDepartmentOf(string userId)
    {
        if (!int.TryParse(userId, out var uid)) return null;
        var users = await userRepo.GetAllAsync(default);
        return users.FirstOrDefault(u => u.Id == uid)?.DepartmentId;
    }

    private async Task<int> GetHierarchyLevel(string userId)
    {
        var (node, nodes) = await FindNodeAndSiblings(userId);
        if (node is null) return -1;
        var depth = 0;
        var current = node;
        while (current.ParentNodeId is not null)
        {
            current = nodes.FirstOrDefault(n => n.Id == current.ParentNodeId);
            if (current is null) break;
            depth++;
        }
        return depth;
    }

    private async Task<bool> HasRole(string userId, string role)
    {
        if (!int.TryParse(userId, out var uid)) return false;
        var users = await userRepo.GetAllAsync(default);
        var user  = users.FirstOrDefault(u => u.Id == uid);
        return user is not null &&
               string.Equals(user.Role.ToString(), role, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(OrgChartNode? Node, IReadOnlyCollection<OrgChartNode> Nodes)>
        FindNodeAndSiblings(string userId)
    {
        if (!int.TryParse(userId, out var uid)) return (null, []);
        var charts = await orgChartRepo.GetChartsByCompanyAsync(0 /* company-level ignored — per-DB */, default);
        foreach (var chart in charts)
        {
            var nodes = await orgChartRepo.GetNodesByChartAsync(chart.Id, default);
            var node  = nodes.FirstOrDefault(n => n.UserId == uid);
            if (node is not null) return (node, nodes);
        }
        return (null, []);
    }
}

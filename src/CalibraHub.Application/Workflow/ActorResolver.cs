using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using NCalc;

namespace CalibraHub.Application.Workflow;

/// <summary>
/// WorkflowNode ActorType tanımını çalışma zamanında bir kullanıcı ID'sine çözer.
///
/// User      → ActorRefId doğrudan userId
/// Role      → o rolde aktif ilk kullanıcı (lock-free — ilk işlem yapan kilitler)
/// Department→ departman yöneticisi (şimdilik boş — Sprint 5'te genişletilir)
/// Expression→ NCalc değerlendirmesi → userId string
/// </summary>
public sealed class ActorResolver(IUserProfileRepository userRepo) : IActorResolver
{
    public async Task<string?> ResolveAsync(
        WorkflowNode node,
        Dictionary<string, object?> context,
        CancellationToken ct = default)
    {
        if (node.ActorType is null) return null;

        return node.ActorType switch
        {
            WorkflowActorType.User       => node.ActorRefId,
            WorkflowActorType.Role       => await ResolveByRoleAsync(node.ActorRefId, ct),
            WorkflowActorType.Department => await ResolveByDepartmentAsync(node.ActorRefId, ct),
            WorkflowActorType.Expression => EvaluateExpression(node.ActorExpression, context),
            _                            => null,
        };
    }

    private async Task<string?> ResolveByRoleAsync(string? role, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        var users = await userRepo.GetAllAsync(ct);
        var match = users.FirstOrDefault(u =>
            string.Equals(u.Role.ToString(), role, StringComparison.OrdinalIgnoreCase));
        return match?.Id.ToString();
    }

    private async Task<string?> ResolveByDepartmentAsync(string? departmentId, CancellationToken ct)
    {
        // Sprint 5'te IOrgChartRepository üzerinden genişletilecek
        if (string.IsNullOrWhiteSpace(departmentId)) return null;
        var users = await userRepo.GetAllAsync(ct);
        var match = users.FirstOrDefault(u => u.DepartmentId?.ToString() == departmentId);
        return match?.Id.ToString();
    }

    private static string? EvaluateExpression(string? expression, Dictionary<string, object?> context)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        try
        {
            var expr = new Expression(expression);
            foreach (var kv in context)
                expr.Parameters[kv.Key] = kv.Value;
            var result = expr.Evaluate();
            return result?.ToString();
        }
        catch { return null; }
    }
}

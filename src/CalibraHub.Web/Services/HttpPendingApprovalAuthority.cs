using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Web.Authorization;

namespace CalibraHub.Web.Services;

/// <summary>
/// Onayda Bekleyenler kapsamı — APPROVAL_PENDING form izinleri üzerinden belirlenir:
///   VIEW_ALL  → "all"        (tüm şirket)
///   VIEW_DEPT → "department" (departman)
///   VIEW_OWN  → "mine"       (sadece kendi)
/// Department user listesi: aynı DepartmentId'ye sahip Personnel'lardan UserId toplar.
/// </summary>
public sealed class HttpPendingApprovalAuthority : IPendingApprovalAuthority
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IPersonnelRepository _personnelRepository;
    private readonly IPermissionService _permissionService;

    public HttpPendingApprovalAuthority(
        IHttpContextAccessor accessor,
        IPersonnelRepository personnelRepository,
        IPermissionService permissionService)
    {
        _accessor           = accessor;
        _personnelRepository = personnelRepository;
        _permissionService  = permissionService;
    }

    public async Task<IReadOnlyList<string>> GetAvailableScopesAsync(CancellationToken ct)
    {
        var (userId, role, deptId) = CurrentUserContext();
        if (userId <= 0) return new[] { PendingApprovalScope.Mine };

        var scope = await _permissionService.GetAccessScopeAsync(
            userId, role, deptId, FormCodes.ApprovalPending, "VIEW", ct);

        return scope switch
        {
            AccessScope.All        => new[] { PendingApprovalScope.Mine, PendingApprovalScope.Department, PendingApprovalScope.All },
            AccessScope.Department => new[] { PendingApprovalScope.Mine, PendingApprovalScope.Department },
            _                      => new[] { PendingApprovalScope.Mine },
        };
    }

    public async Task<(string UserId, IReadOnlyCollection<string>? DepartmentUserIds)> ResolveContextAsync(string scope, CancellationToken ct)
    {
        var userIdStr = CurrentUserId();

        if (string.Equals(scope, PendingApprovalScope.Department, StringComparison.OrdinalIgnoreCase))
        {
            var depUsers = await TryGetDepartmentColleaguesAsync(ct);
            return (userIdStr, depUsers);
        }

        return (userIdStr, null);
    }

    private (int UserId, CalibraHub.Domain.Enums.UserRole Role, int? DeptId) CurrentUserContext()
    {
        var user = _accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return (0, CalibraHub.Domain.Enums.UserRole.Operator, null);

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId) || userId <= 0)
            return (0, CalibraHub.Domain.Enums.UserRole.Operator, null);

        var roleStr = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var role = roleStr switch
        {
            "SystemAdmin"       => CalibraHub.Domain.Enums.UserRole.SystemAdmin,
            "DepartmentManager" => CalibraHub.Domain.Enums.UserRole.DepartmentManager,
            "Approver"          => CalibraHub.Domain.Enums.UserRole.Approver,
            _                   => CalibraHub.Domain.Enums.UserRole.Operator,
        };

        var deptStr = user.FindFirstValue("department_id");
        int? deptId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        return (userId, role, deptId);
    }

    private string CurrentUserId()
        => _accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private async Task<IReadOnlyCollection<string>?> TryGetDepartmentColleaguesAsync(CancellationToken ct)
    {
        var (userId, _, _) = CurrentUserContext();
        if (userId <= 0) return null;

        try
        {
            var all = await _personnelRepository.ListAsync(includeInactive: false, onlyOperators: false, ct);
            var me = all.FirstOrDefault(p => p.UserId == userId);
            if (me == null || string.IsNullOrEmpty(me.Department)) return null;

            return all
                .Where(p => string.Equals(p.Department, me.Department, StringComparison.OrdinalIgnoreCase)
                            && p.UserId.HasValue)
                .Select(p => p.UserId!.Value.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return null; }
    }
}

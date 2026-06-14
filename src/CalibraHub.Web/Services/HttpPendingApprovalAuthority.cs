using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Web.Services;

/// <summary>
/// 2026-05-26 — HttpContext tabanli yetki cozumleyici.
/// Su andaki kurallar (gecici, ileride Forms/Permissions ile genisleyecek):
///   - "mine"       : her authenticated user.
///   - "department" : kullanicinin Personnel.DepartmentId ataması varsa.
///   - "all"        : userName == "admin@calibra.local" veya Admin role.
/// Department user listesi: ayni DepartmentId'ye sahip Personnel'lardan UserId'leri toplar.
/// </summary>
public sealed class HttpPendingApprovalAuthority : IPendingApprovalAuthority
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IPersonnelRepository _personnelRepository;

    public HttpPendingApprovalAuthority(
        IHttpContextAccessor accessor,
        IPersonnelRepository personnelRepository)
    {
        _accessor = accessor;
        _personnelRepository = personnelRepository;
    }

    public async Task<IReadOnlyList<string>> GetAvailableScopesAsync(CancellationToken ct)
    {
        var scopes = new List<string> { PendingApprovalScope.Mine };

        var userName = CurrentUserName();
        var isAdmin = !string.IsNullOrEmpty(userName) &&
                      (string.Equals(userName, "admin@calibra.local", StringComparison.OrdinalIgnoreCase)
                       || (_accessor.HttpContext?.User?.IsInRole("Admin") ?? false));

        // Departman yetkisi: kullanicinin atanmis Personnel kaydi varsa
        var personnel = await TryGetCurrentPersonnelAsync(ct);
        if (personnel != null && !string.IsNullOrEmpty(personnel.Department))
            scopes.Add(PendingApprovalScope.Department);

        if (isAdmin)
            scopes.Add(PendingApprovalScope.All);

        return scopes;
    }

    public async Task<(string UserId, IReadOnlyCollection<string>? DepartmentUserIds)> ResolveContextAsync(string scope, CancellationToken ct)
    {
        var userId = CurrentUserId();

        if (string.Equals(scope, PendingApprovalScope.Department, StringComparison.OrdinalIgnoreCase))
        {
            var depUsers = await TryGetDepartmentColleaguesAsync(ct);
            return (userId, depUsers);
        }

        return (userId, null);
    }

    private string CurrentUserId()
        => _accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private string? CurrentUserName()
        => _accessor.HttpContext?.User?.Identity?.Name;

    private async Task<PersonnelDto?> TryGetCurrentPersonnelAsync(CancellationToken ct)
    {
        var userIdStr = CurrentUserId();
        if (!int.TryParse(userIdStr, out var userIdInt) || userIdInt <= 0) return null;
        try
        {
            var list = await _personnelRepository.ListAsync(includeInactive: false, onlyOperators: false, ct);
            return list.FirstOrDefault(p => p.UserId == userIdInt);
        }
        catch { return null; }
    }

    private async Task<IReadOnlyCollection<string>?> TryGetDepartmentColleaguesAsync(CancellationToken ct)
    {
        var me = await TryGetCurrentPersonnelAsync(ct);
        if (me == null || string.IsNullOrEmpty(me.Department)) return null;

        try
        {
            var all = await _personnelRepository.ListAsync(includeInactive: false, onlyOperators: false, ct);
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

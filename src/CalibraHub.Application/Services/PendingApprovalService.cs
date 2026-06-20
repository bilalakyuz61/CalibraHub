using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services;

/// <summary>
/// 2026-05-26 — IPendingApprovalService implementasyonu.
/// userId + scope parametre olarak gelir (Controller'da User claim'inden cikarilir).
/// Yetki kontrolu su an basit: scope=all sadece userName == "admin@calibra.local"
/// veya "Admin" role'unde olanlar icin. Yeterli olmadiginda Forms/Permissions
/// bazli resolver enjekte edilecek.
/// </summary>
public sealed class PendingApprovalService : IPendingApprovalService
{
    private readonly IApprovalInstanceRepository _repo;
    private readonly IPendingApprovalAuthority _authority;

    public PendingApprovalService(
        IApprovalInstanceRepository repo,
        IPendingApprovalAuthority authority)
    {
        _repo = repo;
        _authority = authority;
    }

    public async Task<IReadOnlyList<PendingApprovalGroupDto>> GetGroupsAsync(string scope, CancellationToken ct)
    {
        var allowed = await EnsureScopeAllowedAsync(scope, ct);
        var (userId, depUsers) = await _authority.ResolveContextAsync(allowed, ct);

        var items = await _repo.GetPendingForUserAsync(userId, allowed, depUsers, ct);

        return items
            .GroupBy(x => new { x.DocumentTypeId, x.DocumentTypeName })
            .Select(g => new PendingApprovalGroupDto(
                DocumentTypeId:   g.Key.DocumentTypeId,
                DocumentTypeCode: null,
                DocumentTypeName: string.IsNullOrEmpty(g.Key.DocumentTypeName) ? "Belirsiz" : g.Key.DocumentTypeName!,
                Count:            g.Count()))
            .OrderByDescending(g => g.Count)
            .ToArray();
    }

    public async Task<IReadOnlyList<PendingApprovalItemDto>> GetListAsync(string scope, int? documentTypeId, CancellationToken ct)
    {
        var allowed = await EnsureScopeAllowedAsync(scope, ct);
        var (userId, depUsers) = await _authority.ResolveContextAsync(allowed, ct);

        var items = await _repo.GetPendingForUserAsync(userId, allowed, depUsers, ct);
        if (documentTypeId.HasValue)
        {
            items = items.Where(x => x.DocumentTypeId == documentTypeId.Value).ToArray();
        }
        return items;
    }

    public async Task<PendingApprovalDetailDto?> GetDetailAsync(int instanceId, string scope, CancellationToken ct)
    {
        var allowed = await EnsureScopeAllowedAsync(scope, ct);
        var (userId, depUsers) = await _authority.ResolveContextAsync(allowed, ct);
        var visible = await _repo.GetPendingForUserAsync(userId, allowed, depUsers, ct);
        var item = visible.FirstOrDefault(x => x.InstanceId == instanceId);
        if (item is null) return null; // yetki yok veya artik beklemiyor

        // Repository'den adim listesi al, header'i zengin (belge bilgili) item ile degistir
        var detail = await _repo.GetPendingDetailAsync(instanceId, ct);
        if (detail is null) return null;
        return detail with { Header = item };
    }

    public Task<IReadOnlyList<string>> GetAvailableScopesAsync(CancellationToken ct)
        => _authority.GetAvailableScopesAsync(ct);

    public Task<IReadOnlyList<ExtraColumnMetaDto>> GetViewColumnMetaAsync(string viewName, CancellationToken ct)
        => _repo.GetViewColumnMetaAsync(viewName, ct);

    public Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>> GetViewRowDataAsync(
        string viewName, IReadOnlyCollection<int> instanceIds, CancellationToken ct)
        => _repo.GetViewRowDataAsync(viewName, instanceIds, ct);

    private async Task<string> EnsureScopeAllowedAsync(string requested, CancellationToken ct)
    {
        var available = await _authority.GetAvailableScopesAsync(ct);
        if (string.IsNullOrEmpty(requested) || !available.Contains(requested, StringComparer.OrdinalIgnoreCase))
        {
            // Yetkili olmadigi scope istendiyse en kisitliya dus
            return available.Contains(PendingApprovalScope.Mine, StringComparer.OrdinalIgnoreCase)
                ? PendingApprovalScope.Mine
                : available[0];
        }
        return requested.ToLowerInvariant();
    }
}

/// <summary>
/// Yetki/context cozumleyici — Web katmanindaki HttpContext'i bilen
/// adapter implementasyonu (HttpContext'ten user claim ve departman bilgisini cikarir).
/// Application layer ASP.NET'ten bagimsiz kalir.
/// </summary>
public interface IPendingApprovalAuthority
{
    /// <summary>Kullanicinin secebilecegi scope listesi (mine her zaman var; department/all yetki sahibine).</summary>
    Task<IReadOnlyList<string>> GetAvailableScopesAsync(CancellationToken ct);

    /// <summary>UserId (ApproverId ile karsilastirmaya hazir) ve departman icin user kimligi listesi.</summary>
    Task<(string UserId, IReadOnlyCollection<string>? DepartmentUserIds)> ResolveContextAsync(string scope, CancellationToken ct);
}

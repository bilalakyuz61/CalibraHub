using System.Text.Json;
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
    private readonly IApprovalFlowRepository _flowRepo;

    public PendingApprovalService(
        IApprovalInstanceRepository repo,
        IPendingApprovalAuthority authority,
        IApprovalFlowRepository flowRepo)
    {
        _repo = repo;
        _authority = authority;
        _flowRepo = flowRepo;
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

        var choiceArms = await GetChoiceArmsAsync(item.FlowId, item.StepOrder, ct);
        return detail with { Header = item, ChoiceArms = choiceArms };
    }

    private async Task<IReadOnlyList<ChoiceArmDto>?> GetChoiceArmsAsync(int flowId, int stepOrder, CancellationToken ct)
    {
        try
        {
            var flow = await _flowRepo.GetByIdAsync(flowId, ct);
            if (flow is null) return null;
            var step = flow.Steps.FirstOrDefault(s => s.StepOrder == stepOrder
                && (string.IsNullOrEmpty(s.NodeType) || string.Equals(s.NodeType, "step", StringComparison.OrdinalIgnoreCase)));
            if (step?.NodeData is null) return null;

            using var doc = JsonDocument.Parse(step.NodeData);
            if (!doc.RootElement.TryGetProperty("extraInputs", out var arr)) return null;

            var arms = new List<ChoiceArmDto>();
            foreach (var el in arr.EnumerateArray())
            {
                if (!el.TryGetProperty("kind", out var kind) || kind.GetString() != "out") continue;
                if (!el.TryGetProperty("label", out var label)) continue;
                var labelStr = label.GetString();
                if (string.IsNullOrWhiteSpace(labelStr)) continue;
                if (!el.TryGetProperty("id", out var id)) continue;
                var armId = id.GetString();
                if (string.IsNullOrWhiteSpace(armId)) continue;
                arms.Add(new ChoiceArmDto(armId, labelStr));
            }
            return arms.Count > 0 ? arms : null;
        }
        catch { return null; }
    }

    public Task<IReadOnlyList<string>> GetAvailableScopesAsync(CancellationToken ct)
        => _authority.GetAvailableScopesAsync(ct);

    public async Task<IReadOnlyList<PendingApprovalGroupDto>> GetCompletedGroupsAsync(string scope, CancellationToken ct)
    {
        var allowed = await EnsureScopeAllowedAsync(scope, ct);
        var (userId, depUsers) = await _authority.ResolveContextAsync(allowed, ct);
        var items = await _repo.GetCompletedForUserAsync(userId, allowed, depUsers, ct);
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

    public async Task<IReadOnlyList<PendingApprovalItemDto>> GetCompletedListAsync(string scope, int? documentTypeId, CancellationToken ct)
    {
        var allowed = await EnsureScopeAllowedAsync(scope, ct);
        var (userId, depUsers) = await _authority.ResolveContextAsync(allowed, ct);
        var items = await _repo.GetCompletedForUserAsync(userId, allowed, depUsers, ct);
        if (documentTypeId.HasValue)
            items = items.Where(x => x.DocumentTypeId == documentTypeId.Value).ToArray();
        return items;
    }

    public async Task<PendingApprovalDetailDto?> GetCompletedDetailAsync(int instanceId, string scope, CancellationToken ct)
    {
        var allowed = await EnsureScopeAllowedAsync(scope, ct);
        var (userId, depUsers) = await _authority.ResolveContextAsync(allowed, ct);
        var visible = await _repo.GetCompletedForUserAsync(userId, allowed, depUsers, ct);
        var item = visible.FirstOrDefault(x => x.InstanceId == instanceId);
        if (item is null) return null;

        var detail = await _repo.GetPendingDetailAsync(instanceId, ct);
        if (detail is null) return null;
        return detail with { Header = item };
    }

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

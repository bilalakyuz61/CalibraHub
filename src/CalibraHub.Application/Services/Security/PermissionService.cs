using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Security;

/// <summary>
/// 2026-06-06 — Yetkilendirme servisi çekirdeği. Resolver:
///   1) SystemAdmin → true (shortcut)
///   2) UserPermission(UserId)        → IsGranted (yüksek öncelik)
///   3) UserPermission(DepartmentId)  → IsGranted
///   4) Default deny
///
/// Sorgular cache'lenmez (F1) — F4'te per-request cache eklenir.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly IPermissionDefRepository _defRepo;
    private readonly IPermissionGrantRepository _grantRepo;
    private readonly IFormRepository _formRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PermissionService> _logger;

    // 10sn — departman izni değiştiğinde sadece sentinel key temizleniyor;
    // aynı dept'e bağlı kullanıcıların bireysel key'leri (perm:grants:u{uid}:d{deptId})
    // IMemoryCache prefix-removal desteklemediği için manuel temizlenemiyor.
    // Kısa TTL stale window'u 60s → 10s'e indirerek riski minimize eder.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    public PermissionService(
        IPermissionDefRepository defRepo,
        IPermissionGrantRepository grantRepo,
        IFormRepository formRepo,
        IMemoryCache cache,
        ILogger<PermissionService> logger)
    {
        _defRepo = defRepo;
        _grantRepo = grantRepo;
        _formRepo = formRepo;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Cache invalidation — admin bir kullanıcı/departman izinlerini değiştirince
    /// kullanılır. PermissionController.Save endpoint'leri çağırır.
    /// </summary>
    public void InvalidateCache(int? userId = null, int? departmentId = null)
    {
        if (userId.HasValue)
        {
            // Hem dept=null key'i hem de kullanıcının gerçek departmanıyla olan key'i temizle.
            // GetGrantsCachedAsync(userId, deptId) → key = perm:grants:u{uid}:d{deptId or '-'}
            // Sadece null key temizlenirse; dept'li kullanıcıda stale cache 60sn boyunca kalır.
            _cache.Remove(GrantsCacheKey(userId.Value, null));
            if (departmentId.HasValue)
                _cache.Remove(GrantsCacheKey(userId.Value, departmentId));
        }
        if (departmentId.HasValue)
        {
            // Departman değişti → bu dept'e bağlı tüm kullanıcı cache'leri etkilenebilir.
            // Kullanıcı bazlı key'ler (userId, deptId) hepsini bilmeden temizlemek mümkün değil;
            // TTL (60sn) ile düşer. Sadece dept-only sentinel key'i temizliyoruz.
            _cache.Remove(GrantsCacheKey(0, departmentId));
        }
    }

    /// <summary>
    /// PermissionDef katalogu değişti (widget IsPermissionControlled toggle) — defs cache'ini anında temizle.
    /// </summary>
    public void InvalidateDefsCache() => _cache.Remove(DefsCacheKey());

    internal static string DefsCacheKey() => "perm:defs:active";
    private static string GrantsCacheKey(int userId, int? departmentId) =>
        $"perm:grants:u{userId}:d{(departmentId.HasValue ? departmentId.Value.ToString() : "-")}";
    private static string FormSortCacheKey() => "perm:formsort";

    private Task<IReadOnlyList<Domain.Entities.PermissionDef>> GetDefsCachedAsync(CancellationToken ct) =>
        _cache.GetOrCreateAsync(DefsCacheKey(), async entry =>
        {
            entry.SetAbsoluteExpiration(CacheTtl);
            return await _defRepo.ListAsync(includeInactive: false, ct);
        })!;

    private Task<IReadOnlyList<Domain.Entities.PermissionGrant>> GetGrantsCachedAsync(
        int userId, int? departmentId, CancellationToken ct) =>
        _cache.GetOrCreateAsync(GrantsCacheKey(userId, departmentId), async entry =>
        {
            entry.SetAbsoluteExpiration(CacheTtl);
            return await _grantRepo.ListForUserAndDepartmentAsync(userId, departmentId, ct);
        })!;

    /// <summary>
    /// FormCode → SortOrder lookup. Menü sıralaması ile aynı: dbo.Forms.SortOrder.
    /// </summary>
    private Task<Dictionary<string, int>> BuildFormSortMapAsync(CancellationToken ct) =>
        _cache.GetOrCreateAsync(FormSortCacheKey(), async entry =>
        {
            entry.SetAbsoluteExpiration(CacheTtl);
            var forms = await _formRepo.GetAllAsync(ct);
            return forms.ToDictionary(f => f.FormCode, f => f.SortOrder, StringComparer.OrdinalIgnoreCase);
        })!;

    /// <summary>
    /// Action önceliği — UI'da göstermek için. İzleme (Özel) en üstte, hiyerarşik akış:
    /// VIEW_OWN → VIEW → CREATE → EDIT_OWN → EDIT_ALL → DELETE_OWN → DELETE_ALL.
    /// BUTTON:* özel action'lar en altta.
    /// </summary>
    private static int ActionSortPriority(string actionCode) => actionCode switch
    {
        "VIEW_OWN"    => 0,
        "VIEW_DEPT"   => 3,
        "VIEW"        => 5,
        "CREATE"      => 10,
        "EDIT_OWN"    => 20,
        "EDIT_DEPT"   => 25,
        "EDIT_ALL"    => 30,
        "DELETE_OWN"  => 40,
        "DELETE_DEPT" => 45,
        "DELETE_ALL"  => 50,
        _             => 100,
    };

    public async Task<bool> CheckAsync(
        int userId, UserRole role, int? departmentId,
        string formCode, string actionCode, CancellationToken ct)
    {
        // 1) SystemAdmin her zaman izinli
        if (role == UserRole.SystemAdmin) return true;

        // 1.5) DepartmentManager (Admin) → SetupDefinitions ve Scheduler hariç tüm ekranlar izinli.
        // "Sistem Ayarları" sayfaları (entegratör şifreleri, SMTP, ERP, log, vb.) yalnızca SystemAdmin'e özel.
        if (role == UserRole.DepartmentManager)
        {
            var adminBlockedForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                FormCodes.SetupDefinitions,
                FormCodes.Scheduler,
            };
            return !adminBlockedForms.Contains(formCode);
        }

        // 2) PermissionDef yoksa default deny (catalog'da bilinmeyen izin)
        var allDefs = await GetDefsCachedAsync(ct);
        var permDef = allDefs.FirstOrDefault(d =>
            string.Equals(d.FormCode, formCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.ActionCode, actionCode, StringComparison.OrdinalIgnoreCase));
        if (permDef is null || !permDef.IsActive)
        {
            _logger.LogDebug("[PERM][DIAG] DENY u={UserId} dept={DeptId} {FormCode}:{ActionCode} → def bulunamadı/pasif (toplam aktif def: {DefCount})", userId, departmentId?.ToString() ?? "-", formCode, actionCode, allDefs.Count);
            return false;
        }

        // 3) Kullanıcı + departman satırları (cache'li)
        var grants = await GetGrantsCachedAsync(userId, departmentId, ct);
        var relevant = grants.Where(g => g.PermissionDefId == permDef.Id).ToList();

        // 4) Önce kullanıcı override
        var userGrant = relevant.FirstOrDefault(g => g.UserId == userId);
        if (userGrant is not null)
        {
            _logger.LogDebug("[PERM][DIAG] {Result} u={UserId} dept={DeptId} {FormCode}:{ActionCode} → user grant defId={DefId}", userGrant.IsGranted ? "ALLOW" : "DENY", userId, departmentId?.ToString() ?? "-", formCode, actionCode, permDef.Id);
            return userGrant.IsGranted;
        }

        // 5) Sonra departman bazlı
        if (departmentId.HasValue)
        {
            var deptGrant = relevant.FirstOrDefault(g => g.DepartmentId == departmentId.Value);
            if (deptGrant is not null)
            {
                _logger.LogDebug("[PERM][DIAG] {Result} u={UserId} dept={DeptId} {FormCode}:{ActionCode} → dept grant defId={DefId}", deptGrant.IsGranted ? "ALLOW" : "DENY", userId, departmentId, formCode, actionCode, permDef.Id);
                return deptGrant.IsGranted;
            }
        }

        // 6) Hiçbiri yok → deny
        _logger.LogDebug("[PERM][DIAG] DENY u={UserId} dept={DeptId} {FormCode}:{ActionCode} → grant yok (defId={DefId}, toplam grant: {GrantCount}, ilgili: {RelevantCount})", userId, departmentId?.ToString() ?? "-", formCode, actionCode, permDef.Id, grants.Count, relevant.Count);
        return false;
    }

    public async Task<bool> CheckAnyAsync(
        int userId, UserRole role, int? departmentId,
        string formCode, IReadOnlyList<string> actionCodes, CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return true;
        foreach (var action in actionCodes)
        {
            if (await CheckAsync(userId, role, departmentId, formCode, action, ct))
                return true;
        }
        return false;
    }

    public async Task<bool> CheckAnyForFormAsync(
        int userId, UserRole role, int? departmentId, string formCode, CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return true;
        var defs = await GetDefsCachedAsync(ct);
        var actionCodes = defs
            .Where(d => d.IsActive && string.Equals(d.FormCode, formCode, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.ActionCode)
            .ToList();
        if (actionCodes.Count == 0) return false;
        return await CheckAnyAsync(userId, role, departmentId, formCode, actionCodes, ct);
    }

    // ── Scope-based helpers ───────────────────────────────────────────────

    // Her operasyon tipi için Genel / Departman / Özel action kodları
    private static (string All, string Dept, string Own)? OperationActionTriplet(string operation) =>
        operation.ToUpperInvariant() switch
        {
            "VIEW"   => (PermissionDef.StandardActions.View,      PermissionDef.StandardActions.ViewDept,   PermissionDef.StandardActions.ViewOwn),
            "EDIT"   => (PermissionDef.StandardActions.EditAll,   PermissionDef.StandardActions.EditDept,   PermissionDef.StandardActions.EditOwn),
            "DELETE" => (PermissionDef.StandardActions.DeleteAll, PermissionDef.StandardActions.DeleteDept, PermissionDef.StandardActions.DeleteOwn),
            _        => null,
        };

    public async Task<AccessScope> GetAccessScopeAsync(
        int userId, UserRole role, int? departmentId,
        string formCode, string operation, CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return AccessScope.All;

        // DepartmentManager → SetupDefinitions ve Scheduler dışında her şeye All
        if (role == UserRole.DepartmentManager)
        {
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { FormCodes.SetupDefinitions, FormCodes.Scheduler };
            return blocked.Contains(formCode) ? AccessScope.None : AccessScope.All;
        }

        var triplet = OperationActionTriplet(operation);
        if (triplet is null) return AccessScope.None;
        var (actionAll, actionDept, actionOwn) = triplet.Value;

        if (await CheckAsync(userId, role, departmentId, formCode, actionAll,  ct)) return AccessScope.All;
        if (await CheckAsync(userId, role, departmentId, formCode, actionDept, ct)) return AccessScope.Department;
        if (await CheckAsync(userId, role, departmentId, formCode, actionOwn,  ct)) return AccessScope.Own;
        return AccessScope.None;
    }

    public async Task<bool> CheckRecordAccessAsync(
        int userId, UserRole role, int? userDeptId,
        string formCode, string operation,
        int recordCreatorId, int? recordCreatorDeptId,
        CancellationToken ct)
    {
        var scope = await GetAccessScopeAsync(userId, role, userDeptId, formCode, operation, ct);
        return scope switch
        {
            AccessScope.All        => true,
            AccessScope.Department => userDeptId.HasValue && userDeptId == recordCreatorDeptId,
            AccessScope.Own        => userId == recordCreatorId,
            _                      => false,
        };
    }

    public async Task<IReadOnlyList<EffectivePermissionDto>> GetEffectivePermissionsAsync(
        int userId, UserRole role, int? departmentId, CancellationToken ct)
    {
        var defs        = await GetDefsCachedAsync(ct);
        var grants      = await GetGrantsCachedAsync(userId, departmentId, ct);
        var formSortMap = await BuildFormSortMapAsync(ct);

        var result = new List<EffectivePermissionDto>(defs.Count);
        foreach (var d in defs)
        {
            var formSort = formSortMap.TryGetValue(d.FormCode, out var so) ? so : int.MaxValue;

            // SystemAdmin tüm izinler için "DEFAULT, true" görür (admin UI bilgisi)
            if (role == UserRole.SystemAdmin)
            {
                result.Add(new EffectivePermissionDto(
                    d.Id, d.FormCode, d.ActionCode, d.Label, d.Category,
                    Source: "DEFAULT", IsAllowed: true, FormSortOrder: formSort));
                continue;
            }

            var userGrant = grants.FirstOrDefault(g => g.PermissionDefId == d.Id && g.UserId == userId);
            if (userGrant is not null)
            {
                result.Add(new EffectivePermissionDto(
                    d.Id, d.FormCode, d.ActionCode, d.Label, d.Category,
                    Source: "USER", IsAllowed: userGrant.IsGranted, FormSortOrder: formSort));
                continue;
            }

            if (departmentId.HasValue)
            {
                var deptGrant = grants.FirstOrDefault(g =>
                    g.PermissionDefId == d.Id && g.DepartmentId == departmentId.Value);
                if (deptGrant is not null)
                {
                    result.Add(new EffectivePermissionDto(
                        d.Id, d.FormCode, d.ActionCode, d.Label, d.Category,
                        Source: "DEPARTMENT", IsAllowed: deptGrant.IsGranted, FormSortOrder: formSort));
                    continue;
                }
            }

            result.Add(new EffectivePermissionDto(
                d.Id, d.FormCode, d.ActionCode, d.Label, d.Category,
                Source: "DEFAULT", IsAllowed: false, FormSortOrder: formSort));
        }

        // Menü sıralaması ile aynı düzen: FormSortOrder ASC, sonra action önceliği
        // (VIEW → CREATE → EDIT_OWN → EDIT_ALL → DELETE_OWN → DELETE_ALL → BUTTON:*).
        return result
            .OrderBy(x => x.FormSortOrder)
            .ThenBy(x => x.FormCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => ActionSortPriority(x.ActionCode))
            .ThenBy(x => x.ActionCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<EffectivePermissionDto>> GetDepartmentPermissionsAsync(
        int departmentId, CancellationToken ct)
    {
        var defs        = await GetDefsCachedAsync(ct);
        var grants      = await _grantRepo.ListByDepartmentAsync(departmentId, ct); // sadece dept — küçük liste, cache gereksiz
        var formSortMap = await BuildFormSortMapAsync(ct);

        var result = new List<EffectivePermissionDto>(defs.Count);
        foreach (var d in defs)
        {
            var formSort = formSortMap.TryGetValue(d.FormCode, out var so) ? so : int.MaxValue;
            var grant = grants.FirstOrDefault(g => g.PermissionDefId == d.Id);
            result.Add(new EffectivePermissionDto(
                d.Id, d.FormCode, d.ActionCode, d.Label, d.Category,
                Source: grant is not null ? "DEPARTMENT" : "DEFAULT",
                IsAllowed: grant?.IsGranted ?? false,
                FormSortOrder: formSort));
        }

        return result
            .OrderBy(x => x.FormSortOrder)
            .ThenBy(x => x.FormCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ActionCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

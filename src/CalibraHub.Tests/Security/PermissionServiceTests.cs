using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CalibraHub.Tests.Security;

/// <summary>
/// PermissionService.CheckAsync yetki matrisi — resolver sırası:
///   SystemAdmin → true, DepartmentManager → (SetupDefinitions/Scheduler hariç) true,
///   def yok/pasif → deny, user grant → dept grant → default deny.
/// DepartmentManager bypass'ı CLAUDE.md'de belgeli bilinçli karardır; bu testler
/// o sözleşmeyi kilitler — bypass kaldırılacaksa önce testler bilinçli güncellenir.
/// </summary>
public sealed class PermissionServiceTests
{
    private const string Form = "DOCUMENT_NEED";
    private const int UserId = 42;
    private const int DeptId = 7;

    // ── Fixture yardımcıları ──────────────────────────────────────────────

    private static PermissionDef Def(int id, string form, string action, bool active = true) => new()
    {
        Id = id,
        FormCode = form,
        ActionCode = action,
        Label = $"{form}:{action}",
        IsActive = active,
    };

    private static PermissionGrant Grant(int defId, int? userId = null, int? deptId = null, bool granted = true) => new()
    {
        PermissionDefId = defId,
        UserId = userId,
        DepartmentId = deptId,
        IsGranted = granted,
    };

    private static PermissionService CreateService(
        IReadOnlyList<PermissionDef>? defs = null,
        IReadOnlyList<PermissionGrant>? grants = null) => new(
        new FakeDefRepo(defs ?? []),
        new FakeGrantRepo(grants ?? []),
        new FakeFormRepo(),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<PermissionService>.Instance);

    // ── Rol kısayolları ───────────────────────────────────────────────────

    [Fact]
    public async Task SystemAdmin_TanimsizFormdaBile_HerZamanIzinli()
    {
        var svc = CreateService(); // katalog tamamen boş
        Assert.True(await svc.CheckAsync(UserId, UserRole.SystemAdmin, null, Form, "DELETE_ALL", default));
    }

    [Fact]
    public async Task DepartmentManager_NormalForma_GrantOlmadanIzinli()
    {
        var svc = CreateService(); // grant tablosu boş — bypass DB'ye hiç bakmamalı
        Assert.True(await svc.CheckAsync(UserId, UserRole.DepartmentManager, DeptId, Form, "EDIT_ALL", default));
    }

    [Theory]
    [InlineData(FormCodes.SetupDefinitions)]
    public async Task DepartmentManager_KisitliFormlara_Red(string blockedForm)
    {
        var svc = CreateService();
        Assert.False(await svc.CheckAsync(UserId, UserRole.DepartmentManager, DeptId, blockedForm, "VIEW", default));
    }

    // 2026-07-11 — Scheduler (Zamanlanmış Görevler) artık iş ekranı → admin erişebilir (bloktan çıkarıldı).
    [Fact]
    public async Task DepartmentManager_Scheduler_Izinli()
    {
        var svc = CreateService();
        Assert.True(await svc.CheckAsync(UserId, UserRole.DepartmentManager, DeptId, FormCodes.Scheduler, "VIEW", default));
    }

    // ── Default deny ──────────────────────────────────────────────────────

    [Fact]
    public async Task KatalogdaOlmayanIzin_VarsayilanRed()
    {
        var svc = CreateService(defs: [Def(1, Form, "VIEW")]);
        Assert.False(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form, "DELETE_ALL", default));
    }

    [Fact]
    public async Task PasifDef_GrantOlsaBileRed()
    {
        var svc = CreateService(
            defs:   [Def(1, Form, "VIEW", active: false)],
            grants: [Grant(1, userId: UserId)]);
        Assert.False(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form, "VIEW", default));
    }

    [Fact]
    public async Task GrantYok_Red()
    {
        var svc = CreateService(defs: [Def(1, Form, "VIEW")]);
        Assert.False(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form, "VIEW", default));
    }

    // ── Grant çözümleme sırası ────────────────────────────────────────────

    [Fact]
    public async Task KullaniciGrantTrue_Izinli()
    {
        var svc = CreateService(
            defs:   [Def(1, Form, "VIEW")],
            grants: [Grant(1, userId: UserId)]);
        Assert.True(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form, "VIEW", default));
    }

    [Fact]
    public async Task KullaniciRedOverride_DepartmanTrueOlsaBile_Red()
    {
        var svc = CreateService(
            defs: [Def(1, Form, "VIEW")],
            grants:
            [
                Grant(1, deptId: DeptId, granted: true),
                Grant(1, userId: UserId, granted: false), // kullanıcı override her zaman kazanır
            ]);
        Assert.False(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form, "VIEW", default));
    }

    [Fact]
    public async Task DepartmanGrantTrue_KullaniciOverrideYoksa_Izinli()
    {
        var svc = CreateService(
            defs:   [Def(1, Form, "VIEW")],
            grants: [Grant(1, deptId: DeptId)]);
        Assert.True(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form, "VIEW", default));
    }

    [Fact]
    public async Task BaskaDepartmaninGranti_Islemez()
    {
        var svc = CreateService(
            defs:   [Def(1, Form, "VIEW")],
            grants: [Grant(1, deptId: 999)]);
        Assert.False(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form, "VIEW", default));
    }

    [Fact]
    public async Task FormVeActionKodu_BuyukKucukHarfDuyarsiz()
    {
        var svc = CreateService(
            defs:   [Def(1, Form, "VIEW")],
            grants: [Grant(1, userId: UserId)]);
        Assert.True(await svc.CheckAsync(UserId, UserRole.Operator, DeptId, Form.ToLowerInvariant(), "view", default));
    }

    // ── Scope çözümleme ───────────────────────────────────────────────────

    [Theory]
    [InlineData("EDIT_ALL",  AccessScope.All)]
    [InlineData("EDIT_DEPT", AccessScope.Department)]
    [InlineData("EDIT_OWN",  AccessScope.Own)]
    public async Task GetAccessScope_EnGenisGrantliAksiyonuDondurur(string grantedAction, AccessScope expected)
    {
        var defs = new[]
        {
            Def(1, Form, "EDIT_ALL"),
            Def(2, Form, "EDIT_DEPT"),
            Def(3, Form, "EDIT_OWN"),
        };
        var defId = grantedAction switch { "EDIT_ALL" => 1, "EDIT_DEPT" => 2, _ => 3 };
        var svc = CreateService(defs, [Grant(defId, userId: UserId)]);

        var scope = await svc.GetAccessScopeAsync(UserId, UserRole.Operator, DeptId, Form, "EDIT", default);
        Assert.Equal(expected, scope);
    }

    [Fact]
    public async Task GetAccessScope_GrantYoksa_None()
    {
        var svc = CreateService(defs: [Def(1, Form, "EDIT_ALL")]);
        Assert.Equal(AccessScope.None,
            await svc.GetAccessScopeAsync(UserId, UserRole.Operator, DeptId, Form, "EDIT", default));
    }

    // ── Kayıt-seviyesi erişim ─────────────────────────────────────────────

    [Fact]
    public async Task OwnScope_SadeceKendiKaydinaIzinli()
    {
        var svc = CreateService(
            defs:   [Def(1, Form, "EDIT_OWN")],
            grants: [Grant(1, userId: UserId)]);

        Assert.True(await svc.CheckRecordAccessAsync(
            UserId, UserRole.Operator, DeptId, Form, "EDIT",
            recordCreatorId: UserId, recordCreatorDeptId: DeptId, default));
        Assert.False(await svc.CheckRecordAccessAsync(
            UserId, UserRole.Operator, DeptId, Form, "EDIT",
            recordCreatorId: 999, recordCreatorDeptId: DeptId, default));
    }

    [Fact]
    public async Task DeptScope_SadeceAyniDepartmanKaydinaIzinli()
    {
        var svc = CreateService(
            defs:   [Def(1, Form, "EDIT_DEPT")],
            grants: [Grant(1, userId: UserId)]);

        Assert.True(await svc.CheckRecordAccessAsync(
            UserId, UserRole.Operator, DeptId, Form, "EDIT",
            recordCreatorId: 999, recordCreatorDeptId: DeptId, default));
        Assert.False(await svc.CheckRecordAccessAsync(
            UserId, UserRole.Operator, DeptId, Form, "EDIT",
            recordCreatorId: 999, recordCreatorDeptId: 123, default));
    }

    // ── Fakes — yalnızca PermissionService'in kullandığı üyeler gerçek ────

    private sealed class FakeDefRepo(IReadOnlyList<PermissionDef> defs) : IPermissionDefRepository
    {
        public Task<IReadOnlyList<PermissionDef>> ListAsync(bool includeInactive, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PermissionDef>>(
                includeInactive ? defs : defs.Where(d => d.IsActive).ToList());

        public Task<PermissionDef?> GetByIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PermissionDef?> GetByFormAndActionAsync(string formCode, string actionCode, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PermissionDef>> ListByFormAsync(string formCode, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> SaveAsync(PermissionDef entity, CancellationToken ct) => throw new NotSupportedException();
        public Task BulkUpsertAsync(IReadOnlyList<PermissionDef> entities, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeGrantRepo(IReadOnlyList<PermissionGrant> grants) : IPermissionGrantRepository
    {
        public Task<IReadOnlyList<PermissionGrant>> ListForUserAndDepartmentAsync(int userId, int? departmentId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PermissionGrant>>(grants
                .Where(g => g.UserId == userId || (departmentId.HasValue && g.DepartmentId == departmentId))
                .ToList());

        public Task<IReadOnlyList<PermissionGrant>> ListByDepartmentAsync(int departmentId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PermissionGrant>>(grants.Where(g => g.DepartmentId == departmentId).ToList());

        public Task<IReadOnlyList<PermissionGrant>> ListByGroupAsync(int groupId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PermissionGrant>>(grants.Where(g => g.GroupId == groupId).ToList());

        public Task<PermissionGrant?> GetByIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PermissionGrant>> ListByUserAsync(int userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> SaveAsync(PermissionGrant entity, CancellationToken ct) => throw new NotSupportedException();
        public Task BulkReplaceForOwnerAsync(int? userId, int? departmentId, IReadOnlyList<PermissionGrant> entities, CancellationToken ct) => throw new NotSupportedException();
        public Task BulkReplaceForGroupAsync(int groupId, IReadOnlyList<PermissionGrant> entities, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteByUserAsync(int userId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteByDepartmentAsync(int departmentId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeFormRepo : IFormRepository
    {
        public Task<IReadOnlyCollection<FormDto>> GetAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<FormDto>>([]);

        public Task<FormDto?> GetByIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<FormDto?> GetByCodeAsync(string formCode, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CreateAsync(CreateFormRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(UpdateFormRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }
}

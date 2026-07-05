using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Yetki grubu (rol) + üyelik erişimi. Sistem DB'sinde yaşar
/// (UserPermission/PermissionDef ile aynı yerde — OpenSystemConnectionAsync).
/// </summary>
public interface IPermissionGroupRepository
{
    /// <summary>Gruplar + üye sayıları. includeInactive=true admin listesi içindir.</summary>
    Task<IReadOnlyList<PermissionGroupDto>> ListAsync(bool includeInactive, CancellationToken ct);

    Task<PermissionGroup?> GetAsync(int id, CancellationToken ct);

    /// <summary>Insert/update. Ad benzersizliği ihlalinde InvalidOperationException fırlatır.</summary>
    Task<int> SaveAsync(PermissionGroup group, CancellationToken ct);

    /// <summary>Grubun üyeleri (kullanıcı adı/e-posta ile).</summary>
    Task<IReadOnlyList<PermissionGroupMemberDto>> ListMembersAsync(int groupId, CancellationToken ct);

    /// <summary>Üyelik listesini toplu replace eder.</summary>
    Task ReplaceMembersAsync(int groupId, IReadOnlyList<int> userIds, int? createdById, CancellationToken ct);

    /// <summary>Kullanıcının üye olduğu AKTİF gruplar.</summary>
    Task<IReadOnlyList<PermissionGroupDto>> ListGroupsForUserAsync(int userId, CancellationToken ct);
}

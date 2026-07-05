using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Yetki atama kaydı — bir izni KULLANICIYA, GRUBA veya DEPARTMANA bağlar (tek tablo + tek owner).
/// **DB Tablo adı:** UserPermission. Class adı C# enum'u (UserPermission) ile çakışmasın
/// diye PermissionGrant olarak adlandırıldı.
///
/// **Sahip kuralı:** Tam olarak biri dolu olmalı: UserId, DepartmentId veya GroupId.
/// DB CHECK constraint (CK_UserPermission_OneOwner) bunu garanti eder.
///
/// **Resolution priority (PermissionService.CheckAsync):**
///   1) SystemAdmin → her zaman izinli
///   2) PermissionGrant(UserId=u) → varsa IsGranted değeri
///   3) PermissionGrant(GroupId ∈ u'nun aktif grupları) → herhangi biri İZİN ise izin (union-allow)
///   4) PermissionGrant(DepartmentId=u.DepartmentId) → varsa IsGranted değeri
///   5) Default deny
///
/// IsGranted=false (açıkça reddet) gruba/departmana verilmiş izni belirli
/// kullanıcıdan almak için kullanılır (kullanıcı override en yüksek öncelik).
/// </summary>
[Description("Yetki ataması: kullanıcı (UserId), grup (GroupId) VEYA departman (DepartmentId) — tam biri dolu olur. DB tablo: UserPermission.")]
public sealed class PermissionGrant
{
    public int Id { get; init; }

    /// <summary>Doluysa kullanıcı bazlı atama (en yüksek öncelik).</summary>
    public int? UserId { get; set; }

    /// <summary>Doluysa departman bazlı atama.</summary>
    public int? DepartmentId { get; set; }

    /// <summary>Doluysa yetki grubu bazlı atama (2026-07-06). Öncelik: User &gt; Group &gt; Department.</summary>
    public int? GroupId { get; set; }

    public int PermissionDefId { get; set; }

    /// <summary>TRUE = izin verildi; FALSE = açıkça reddet (override için).</summary>
    public bool IsGranted { get; set; }

    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? CreatedById { get; set; }

    /// <summary>Sahip tipi — yardımcı. 'USER' / 'GROUP' / 'DEPARTMENT'.</summary>
    public string OwnerType => UserId.HasValue ? "USER" : GroupId.HasValue ? "GROUP" : "DEPARTMENT";

    public void EnsureValid()
    {
        var owners = (UserId.HasValue ? 1 : 0) + (DepartmentId.HasValue ? 1 : 0) + (GroupId.HasValue ? 1 : 0);
        DomainException.ThrowIf(owners != 1,
            "PermissionGrant'ta tam olarak biri (UserId, DepartmentId veya GroupId) dolu olmalı.");
        DomainException.ThrowIf(PermissionDefId <= 0,
            "PermissionDefId zorunlu.");
    }
}

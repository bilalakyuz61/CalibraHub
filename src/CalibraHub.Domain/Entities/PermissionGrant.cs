using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Yetki atama kaydı — bir izni KULLANICIYA veya DEPARTMANA bağlar (tek tablo + tek owner).
/// **DB Tablo adı:** UserPermission. Class adı C# enum'u (UserPermission) ile çakışmasın
/// diye PermissionGrant olarak adlandırıldı.
///
/// **Sahip kuralı (XOR):** Tam olarak UserId veya DepartmentId DOLU olmalı, ikisi birden olamaz,
/// ikisi de boş olamaz. DB CHECK constraint bunu garanti eder.
///
/// **Resolution priority (PermissionService.CheckAsync):**
///   1) SystemAdmin → her zaman izinli
///   2) PermissionGrant(UserId=u) → varsa IsGranted değeri
///   3) PermissionGrant(DepartmentId=u.DepartmentId) → varsa IsGranted değeri
///   4) Default deny
///
/// IsGranted=false (açıkça reddet) departmana verilmiş izni belirli kullanıcıdan
/// almak için kullanılır.
/// </summary>
[Description("Yetki ataması: kullanıcı (UserId) VEYA departman (DepartmentId) — biri dolu olur. DB tablo: UserPermission. 2026-06-06.")]
public sealed class PermissionGrant
{
    public int Id { get; init; }

    /// <summary>NULL ise satır departman bazlı; doluysa kullanıcı bazlı atama.</summary>
    public int? UserId { get; set; }

    /// <summary>NULL ise satır kullanıcı bazlı; doluysa departman bazlı atama.</summary>
    public int? DepartmentId { get; set; }

    public int PermissionDefId { get; set; }

    /// <summary>TRUE = izin verildi; FALSE = açıkça reddet (override için).</summary>
    public bool IsGranted { get; set; }

    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? CreatedById { get; set; }

    /// <summary>Sahip tipi — yardımcı (UserId/DepartmentId'ye bakar). 'USER' / 'DEPARTMENT'.</summary>
    public string OwnerType => UserId.HasValue ? "USER" : "DEPARTMENT";

    public void EnsureValid()
    {
        var userSet = UserId.HasValue;
        var deptSet = DepartmentId.HasValue;
        DomainException.ThrowIf(userSet == deptSet,
            "PermissionGrant'ta tam olarak biri (UserId veya DepartmentId) dolu olmalı.");
        DomainException.ThrowIf(PermissionDefId <= 0,
            "PermissionDefId zorunlu.");
    }
}

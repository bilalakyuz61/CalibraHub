using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// 2026-06-12 — Bir <see cref="DataVisibilityRule"/>'ın kapsadığı satırları görmesine
/// İZİN verilen principal. PermissionGrant ile aynı XOR deseni: tam olarak
/// <see cref="UserId"/> VEYA <see cref="DepartmentId"/> dolu olur.
///
/// Kısıtlama modeli: bir (alan,değer) için izinli principal kümesi, o değere değen tüm
/// kuralların grant'larının BİRLEŞİMİdir. Bir satır, eşleştiği herhangi bir kısıtlı değerde
/// kullanıcı bu kümede değilse gizlenir.
/// </summary>
[Description("Veri görünürlük kuralı izinlisi: kullanıcı (UserId) VEYA departman (DepartmentId). DB tablo: DataVisibilityGrant. 2026-06-12.")]
public sealed class DataVisibilityGrant
{
    public int Id { get; init; }
    public int RuleId { get; set; }

    /// <summary>NULL ise departman bazlı; doluysa kullanıcı bazlı izin.</summary>
    public int? UserId { get; set; }

    /// <summary>NULL ise kullanıcı bazlı; doluysa departman bazlı izin.</summary>
    public int? DepartmentId { get; set; }

    /// <summary>Sahip tipi — 'USER' / 'DEPARTMENT'.</summary>
    public string OwnerType => UserId.HasValue ? "USER" : "DEPARTMENT";

    public void EnsureValid()
    {
        var userSet = UserId.HasValue;
        var deptSet = DepartmentId.HasValue;
        DomainException.ThrowIf(userSet == deptSet,
            "DataVisibilityGrant'ta tam olarak biri (UserId veya DepartmentId) dolu olmalı.");
    }
}

using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Yetki grubu (rol) — "Satış Ekibi", "Depo Operatörü", "Muhasebe" gibi izin paketleri.
/// Kullanıcılar UserPermissionGroup üzerinden birden fazla gruba üye olabilir;
/// grubun grant'ları UserPermission tablosunda GroupId sahipli satırlar olarak tutulur.
///
/// Çözümleme: kullanıcı override &gt; grup birleşimi (herhangi bir grup İZİN veriyorsa izin)
/// &gt; departman &gt; deny. Grup pasifleştirilince (IsActive=0) grant'ları çözümlemeye girmez.
/// Fiziksel silme yok — deaktivasyon (Users pattern'i ile tutarlı).
/// </summary>
[Description("Yetki grubu (rol) tanımı — üyeler UserPermissionGroup, izinler UserPermission.GroupId üzerinden bağlanır.")]
public sealed class PermissionGroup
{
    public int Id { get; init; }

    /// <summary>Grup adı — şirket içinde benzersiz (UX_PermissionGroup_Name).</summary>
    public required string Name { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

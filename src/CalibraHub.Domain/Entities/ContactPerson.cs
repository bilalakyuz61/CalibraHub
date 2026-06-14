using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Cari'ye bagli iletisim kisileri — Satis Muduru, Uretim Sorumlusu, CFO/CTO vb.
/// Contact'in kendi Phone/Mobile/Email/WaPhone alanlari CARI'nin firma seviyesi
/// iletisimidir; ContactPerson satirlari ise o firmadaki KISILERE aittir.
/// </summary>
[Description("Cariye bagli iletisim kisileri (firma calisanlari/temsilcileri). Her kayit TitleId (unvan lookup'a FK), FullName, opsiyonel Phone/Email/Notes ve IsPrimary bayragi tasir.")]
public sealed class ContactPerson
{
    public int Id { get; init; }

    /// <summary>FK -> Contact.Id (CASCADE)</summary>
    public int ContactId { get; init; }

    /// <summary>FK -> ContactPersonTitle.Id. Yeni model — tum unvan referanslari bu ID uzerinden.</summary>
    public int? TitleId { get; init; }

    /// <summary>JOIN-derived display alani — repo SELECT'inde ContactPersonTitle.Name'den gelir. Yazilamaz.</summary>
    public string? TitleName { get; init; }

    /// <summary>Ad soyad.</summary>
    public required string FullName { get; init; }

    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Notes { get; init; }

    /// <summary>Birincil iletisim kisisi mi? Liste ekraninda one cikarilir.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Soft delete bayragi (silme islemi IsActive=false yapar).</summary>
    public bool IsActive { get; init; } = true;

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
    public int? CreatedById { get; init; }
    public int? UpdatedById { get; init; }
}

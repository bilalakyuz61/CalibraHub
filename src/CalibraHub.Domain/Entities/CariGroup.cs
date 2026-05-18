using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>Cari grup tanimi — cari hesaplari segmentlere ayirmak icin (Toptan/Perakende/VIP vb.).</summary>
[Description("Cari grup tanimlari. Contact.ContactGroupId bu tabloya FK verir; tasarim kurallari grup uzerinden filtreleme yapabilir.")]
public sealed class CariGroup
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Grubun ait oldugu sirket. FK -> Company.Id (per-tenant izolasyon).")]
    public int CompanyId { get; set; }

    [Description("Grup kodu — kullaniciya gosterim icin (otomatik turetilebilir, runtime karsilastirmasi ID uzerinden).")]
    public required string Code { get; init; }

    [Description("Grup adi — UI'da gorunen baslik (Toptan, Perakende, VIP vb.). Per-company unique.")]
    public required string Name { get; init; }

    [Description("Liste/dropdown siralamasi (kucukten buyuge).")]
    public int SortOrder { get; init; }

    [Description("Soft delete — pasif gruplar listede gosterilmez.")]
    public bool IsActive { get; init; } = true;

    public DateTime CreatedAt { get; init; }
}

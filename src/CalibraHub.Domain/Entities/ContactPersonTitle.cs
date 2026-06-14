using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// ContactPerson icin onceden tanimli unvan lookup tablosu.
/// Per-company DB'de yasar (kendi sirketinin unvan listesi). Bulk mail/segment
/// filtreleri (ornek: "tum CFO'lara gonder") icin TitleId uzerinden join yapilir.
/// </summary>
[Description("Cariye bagli iletisim kisilerinin secebilecegi onceden tanimli unvan listesi. IsSystem=1 olanlar seed gelir ve silinemez; sadece IsActive toggle yapilabilir.")]
public sealed class ContactPersonTitle
{
    public int Id { get; init; }

    /// <summary>Unvan adi — kullaniciya goruntulenir, unique (case-insensitive, aktif kayitlar arasinda).</summary>
    public required string Name { get; init; }

    /// <summary>Liste siralamasi (kucuk once). Sistem seed icin 10..160 araliginda; kullanici eklemeleri 999.</summary>
    public int SortOrder { get; init; }

    /// <summary>true ise seed kayittir, silinemez (sadece IsActive toggle). Kullanici eklediginde false.</summary>
    public bool IsSystem { get; init; }

    /// <summary>Soft delete bayragi.</summary>
    public bool IsActive { get; init; } = true;

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
    public int? CreatedById { get; init; }
    public int? UpdatedById { get; init; }
}

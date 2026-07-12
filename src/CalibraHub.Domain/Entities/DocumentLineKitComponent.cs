using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Belge kalemine (kit satirina) bagli DONMUS kit icerik snapshot'i. Bir kit belgeye eklendiginde o anki aktif ItemKit icerigi (VersionNo + bilesenler) buraya kopyalanir; kit sonradan revize edilse bile bu belge eski icerigi tasir (revizyon sonraki kullanimi etkiler kurali). Faz 3'te irsaliye patlatmasi bu snapshot'tan uretilir. DocumentLineId FK -> DocumentLine.Id (kit satiri, ON DELETE CASCADE). Quantity = 1 kit icin gereken bilesen adedi (kit satirinin Quantity'si ile carpilir).")]
public sealed class DocumentLineKitComponent
{
    public int Id { get; init; }

    /// <summary>Kit satiri. FK -> DocumentLine.Id.</summary>
    public int DocumentLineId { get; init; }

    /// <summary>Kit'in kendisi olan malzeme karti (denormalize — Faz 3 patlatmada kullanilir).</summary>
    public int KitItemId { get; init; }

    /// <summary>Snapshot alindigi ItemKit.VersionNo (hangi versiyon donduruldu).</summary>
    public int KitVersionNo { get; init; }

    /// <summary>Bilesen malzeme karti. FK -> Items.Id.</summary>
    public int ComponentItemId { get; init; }

    /// <summary>Bilesenin varyanti (ItemConfiguration.Id) — kombinasyon-takipli bilesen icin.</summary>
    public int? ConfigId { get; init; }

    /// <summary>1 kit icin gereken bilesen adedi.</summary>
    public decimal Quantity { get; init; }

    public DateTime Created { get; init; } = DateTime.UtcNow;

    // ── Transient display (Items JOIN ile; tabloya yazilmaz) ──
    public string? ComponentCode { get; set; }
    public string? ComponentName { get; set; }
    public string? ConfigCode { get; set; }
}

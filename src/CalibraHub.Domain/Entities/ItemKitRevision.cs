using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Kit (paket urun) revizyon gecmisi — kit icerigi her kaydedildiginde o anki yapilandirmanin JSON snapshot'i saklanir. RevisionNo, kaydin olustugu andaki ItemKit.VersionNo ile aynidir; belgelerdeki DocumentLineKitComponent.KitVersionNo bu revizyona isaret eder. Salt-okunur gecmis: geri yukleme yoktur.")]
public sealed class ItemKitRevision
{
    public int Id { get; init; }
    public int ItemKitId { get; init; }
    /// <summary>Kit malzeme kartinin Items.id degeri (ItemKit.ItemId kopyasi — sorgu kolayligi).</summary>
    public int ItemId { get; init; }
    /// <summary>
    /// Snapshot alindigi andaki ItemKit.VersionNo. Icerik degismeden yapilan kayitlarda
    /// VersionNo artar ama revizyon YAZILMAZ → numaralarda bosluk olabilir (v3, v5...).
    /// Bir belgenin KitVersionNo'suna karsilik gelen icerik: o degerden KUCUK VEYA ESIT
    /// en buyuk RevisionNo.
    /// </summary>
    public int RevisionNo { get; init; }
    public required string PriceMode { get; init; }
    public decimal? FixedPrice { get; init; }
    public string? Description { get; init; }
    public int LineCount { get; init; }
    /// <summary>Semantik parmak izi (SHA-256 hex) — icerik degismemisse yeni revizyon yazilmaz.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>Kit basligi + bilesen listesi (kod/ad/miktar/birim/fiyat) JSON snapshot'i.</summary>
    public required string Snapshot { get; init; }
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
}

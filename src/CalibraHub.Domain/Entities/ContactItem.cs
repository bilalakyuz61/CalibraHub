using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Cari × stok eslestirmesi. Ayni stok birden fazla cariden alinabilir; her cari kendi kod/ad/notunu verebilir.")]
public sealed class ContactItem
{
    public int Id { get; init; }

    /// <summary>FK -> Contact.Id (CASCADE)</summary>
    public int ContactId { get; init; }

    /// <summary>FK -> Items.Id (CASCADE) — bizim stok kartimiz</summary>
    public int ItemId { get; init; }

    /// <summary>Cari'nin bu stoga verdigi ozel kod (tedarikci/musteri kodu).</summary>
    public string? VendorCode { get; init; }

    /// <summary>Cari'nin bu stoga verdigi ozel ad (cari katalogu icin).</summary>
    public string? VendorName { get; init; }

    /// <summary>Aciklama / oncelik / MOQ / kalite gibi serbest not.</summary>
    public string? Notes { get; init; }

    public bool IsActive { get; init; } = true;

    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

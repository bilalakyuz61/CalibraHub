using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>Belge turu (QUOTE, ORDER, INVOICE, vb.) — Document.DocumentTypeId bu tabloya baglidir.</summary>
[Description("Belge turleri sozlugu: teklif, siparis, irsaliye, fatura, etiket vb. her belgeyi ayirir ve raporlama icin SQL view adini (sql_view_name) tutar.")]
public sealed class DocumentType
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Benzersiz kod (QUOTE, ORDER, INVOICE).")]
    public required string Code { get; init; }

    [Description("Kullaniciya gosterilen isim.")]
    public required string Name { get; init; }

    public string? SqlViewName { get; init; }

    /// <summary>
    /// Bu belge tipinden basim yaparken URL'den gelen recordId'nin view'da
    /// eslesecegi kolon adi. Sablon olustururken secilen view'in bu kolonu
    /// icermesi ZORUNLUDUR; icermiyorsa kullanicinin sablon kaydetmesine
    /// izin verilmez.
    /// Ornekler:
    ///   satis_teklifi/fatura/irsaliye → 'BelgeId' (Document.id)
    ///   urun_barkodu/raf_etiketi      → 'id'     (Items.id / Location.id)
    /// </summary>
    public string? RequiredKeyColumn { get; init; }

    public string? Description { get; init; }
    // NOT (Faz N revize): UI yonlendirme + entegrasyon metadata'si Forms tablosunda
    // tutulur, DocumentType'da DEGIL. Cunku ListUrl/Icon/IsTransferable form-seviyesi
    // ozellikleri (her form'un kendi liste sayfasi, ikonu, transferable durumu olur);
    // belge tipi ise raporlama/gruplama icin kavramsal varlık. Forms.FormCode bazinda
    // sorulur. Bu sayede tek-formlu belge tipleri (CONTACTS, ITEMS) icin de calisir.

    [Description("Soft delete — listede gosterilir mi?")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

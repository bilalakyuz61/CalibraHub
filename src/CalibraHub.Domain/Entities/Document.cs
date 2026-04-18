using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Tum ticari belgelerin konsolide baslik tablosu (teklif/siparis/fatura).
/// DocumentTypeId kolonu belge turunu ayirir.
/// </summary>
[Description("Teklif, siparis ve fatura gibi tum ticari belgelerin basligi. DocumentTypeId ile tur ayrilir; DocumentLine tablosu ile 1-N iliskilidir. Tutar hesaplamalari (sub_total, discount, tax, grand_total) burada yazilir.")]
public sealed class Document
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Kullaniciya gosterilen belge numarasi (seri-sayac, benzersiz).")]
    public required string DocumentNumber { get; init; }

    [Description("Belgenin duzenlendigi tarih.")]
    public DateTime DocumentDate { get; set; } = DateTime.Now;

    [Description("Teklif icin gecerlilik tarihi. Siparis/fatura icin genellikle bos.")]
    public DateTime? ValidUntil { get; set; }

    [Description("Belge turunu belirler. FK -> DocumentType.Id (QUOTE/ORDER/INVOICE).")]
    public int? DocumentTypeId { get; set; }

    [Description("Carinin primary key'i. FK -> Contact.Id. Bos ise serbest metin contact_name kullanilir.")]
    public int? ContactId { get; set; }

    [Description("Cari unvani — contact_id ile dolmadi ise manuel girilir.")]
    public string? ContactName { get; set; }

    public string? ContactAddress { get; set; }

    /// <summary>Contact.AccountCode — transient (join ile doldurulur, tabloda saklanmaz).</summary>
    public string? ContactCode { get; set; }

    [Description("Satis temsilcisi. FK -> SalesRepresentative.Id")]
    public int? SalesRepId { get; set; }

    [Description("Belge para birimi (ISO 4217 kodu: TRY, USD, EUR...).")]
    public string Currency { get; set; } = "TRY";

    public decimal SubTotal { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; } = 20m;
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public string? PaymentTerms { get; set; }
    public string? DeliveryTerms { get; set; }
    public string? DeliveryAddress { get; set; }

    [Description("Belgenin yasam dongusu durumu.")]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    [Description("Revizyon numarasi (0 = orijinal).")]
    public int RevisionNo { get; set; }

    [Description("Revizyon durumunda onceki belge id'si. FK -> Document.Id")]
    public int? ParentDocumentId { get; set; }

    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [Description("Soft delete — listede gosterilir mi?")]
    public bool IsActive { get; set; } = true;

    /// <summary>Computed — satir sayisi (liste sorgusu icin).</summary>
    public int LineCount { get; set; }
}

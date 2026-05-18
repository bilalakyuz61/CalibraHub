using System.ComponentModel;
using CalibraHub.Domain.Common;
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

    [Description("Belgenin ait oldugu sirket. FK -> Company.Id. Kayit anindaki oturum kullanicisinin sirketinden otomatik doldurulur.")]
    public int CompanyId { get; set; }

    [Description("Kullaniciya gosterilen belge numarasi (seri-sayac, benzersiz).")]
    public required string DocumentNumber { get; init; }

    [Description("Belgenin duzenlendigi tarih.")]
    public DateTime DocumentDate { get; set; } = DateTime.Now;

    [Description("Teklif icin gecerlilik tarihi. Siparis/fatura icin genellikle bos.")]
    public DateTime? ValidUntil { get; set; }

    [Description("Sipariş için talep edilen teslim tarihi (üst seviye, tüm kalemler için varsayılan). Kalem bazında DocumentLine.DeliveryDate ile override edilebilir.")]
    public DateTime? DeliveryDate { get; set; }

    [Description("Sipariş tarihinden itibaren teslim süresi (gün). UI'da DeliveryDate ile çift yönlü senkron: gün girilince tarih hesaplanır, tarih girilince gün hesaplanır. NULL ise kullanıcı sadece tarih girmiştir veya teslim süresi bilinmiyor.")]
    public int? DeliveryDays { get; set; }

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

    /// <summary>
    /// Bellek-icindeki satir koleksiyonu (rich domain modu icin). Mevcut service'ler
    /// satirlari DocumentLine repository'sinden ayri okuyor — bu koleksiyon onlari etkilemez.
    /// Yeni kod Document.AddLine / RemoveLine cagirir, koleksiyon otomatik dolar.
    /// </summary>
    public List<DocumentLine> Lines { get; init; } = new();

    // ═══════════════════════════════════════════════════════════════════
    // Domain davranis metotlari (rapor §2.2 — rich domain)
    //
    // Mevcut setter'lar GERIYE UYUM icin acik birakildi; eski service'ler
    // (DocumentService, DocumentImportService) hala dogrudan set ediyor.
    // Yeni kod bu davranis metotlarini cagirip invariant garantilerini alir.
    //
    // Status transition kurallari:
    //   Draft     → Sent | Revised | Cancelled
    //   Sent      → Approved | Rejected | Revised | Cancelled
    //   Approved  → Converted | Cancelled
    //   Rejected  → Revised | Cancelled
    //   Revised   → (terminal — yeni revizyon ayri belge)
    //   Cancelled → (terminal)
    //   Converted → (terminal)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mevcut satir tutarlarinin (SubTotal) uzerine iskonto + KDV uygulayip
    /// GrandTotal hesaplar. Iskonto ve vergi NEGATIF olamaz, oran 0-100 araliginda olmali.
    /// Geri uyum: SubTotal disaridan set edilir (DocumentService henuz line'lardan
    /// otomatik hesaplamiyor). Bu metot mevcut SubTotal'i veri kaynagi sayar.
    /// </summary>
    public void RecalculateTotals()
    {
        DomainException.ThrowIf(SubTotal < 0, $"SubTotal negatif olamaz: {SubTotal}");
        DomainException.ThrowIf(DiscountRate < 0 || DiscountRate > 100,
            $"DiscountRate 0-100 araliginda olmali (su an: {DiscountRate})");
        DomainException.ThrowIf(TaxRate < 0 || TaxRate > 100,
            $"TaxRate 0-100 araliginda olmali (su an: {TaxRate})");

        // Yuvarlama: tutar alanlari 2 ondalik (TL/USD/EUR cent precision)
        DiscountAmount = Math.Round(SubTotal * (DiscountRate / 100m), 2, MidpointRounding.AwayFromZero);
        var afterDiscount = SubTotal - DiscountAmount;
        TaxAmount = Math.Round(afterDiscount * (TaxRate / 100m), 2, MidpointRounding.AwayFromZero);
        GrandTotal = afterDiscount + TaxAmount;
    }

    /// <summary>
    /// Belgeyi Draft'tan Sent durumuna gecirir.
    /// Once invariant: sadece Draft'tan Sent'e gecilebilir + zorunlu alanlar dolu olmali.
    /// </summary>
    public void MarkAsSent(string userName)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(userName), "userName zorunlu.");
        DomainException.ThrowIf(Status != DocumentStatus.Draft,
            $"Sadece Draft durumdan Sent'e gecilebilir (su an: {Status})");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(DocumentNumber),
            "Gondermek icin DocumentNumber zorunlu.");

        Status = DocumentStatus.Sent;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gonderilmis belgeyi onaylar (Sent → Approved).
    /// </summary>
    public void Approve(string userName)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(userName), "userName zorunlu.");
        DomainException.ThrowIf(Status != DocumentStatus.Sent,
            $"Sadece Sent durumdaki belge onaylanabilir (su an: {Status})");

        Status = DocumentStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gonderilmis belgeyi reddeder (Sent → Rejected). Reason opsiyonel — Notes alanina eklenir.
    /// </summary>
    public void Reject(string userName, string? reason = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(userName), "userName zorunlu.");
        DomainException.ThrowIf(Status != DocumentStatus.Sent,
            $"Sadece Sent durumdaki belge reddedilebilir (su an: {Status})");

        Status = DocumentStatus.Rejected;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var prefix = string.IsNullOrEmpty(Notes) ? "" : Notes + "\n";
            Notes = $"{prefix}[Red - {userName} - {DateTime.UtcNow:yyyy-MM-dd HH:mm}] {reason.Trim()}";
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Belgeyi iptal eder. Approved/Converted terminal durumlar disinda her zaman uygulanabilir.
    /// </summary>
    public void Cancel(string userName)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(userName), "userName zorunlu.");
        DomainException.ThrowIf(Status == DocumentStatus.Cancelled,
            "Belge zaten iptal edilmis.");
        DomainException.ThrowIf(Status == DocumentStatus.Converted,
            "Siparise donusturulmus belge iptal edilemez.");

        Status = DocumentStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Belgenin duzenlenebilir olup olmadigini kontrol eder.
    /// Cancelled / Converted / Revised terminal — sadece okuma.
    /// </summary>
    public bool IsEditable() =>
        Status is DocumentStatus.Draft or DocumentStatus.Sent or DocumentStatus.Rejected;

    // ── Lines yonetimi (rich domain — bellek-ici koleksiyon) ──────────

    /// <summary>
    /// Satir ekler ve toplamlari yeniden hesaplar. IsEditable() degilse exception.
    /// LineNo otomatik atanir (mevcut max + 1). Line invariant'lari (Quantity > 0 vs.)
    /// CalculateLineTotal cagrisinda kontrol edilir.
    /// </summary>
    public void AddLine(DocumentLine line)
    {
        DomainException.ThrowIf(line is null, "line null olamaz.");
        DomainException.ThrowIf(!IsEditable(),
            $"Belge duzenlenemez durumda ({Status}) — satir eklenemez.");
        DomainException.ThrowIf(line!.ItemId <= 0, "Line.ItemId zorunlu (>0).");

        line.LineNo = Lines.Count == 0 ? 1 : Lines.Max(l => l.LineNo) + 1;
        line.CalculateLineTotal();   // invariant + LineTotal hesabi
        Lines.Add(line);

        RecalculateSubTotalFromLines();
        RecalculateTotals();
    }

    /// <summary>
    /// Satir kaldirir ve toplamlari yeniden hesaplar. Line yoksa NotFoundException.
    /// </summary>
    public void RemoveLine(int lineId)
    {
        DomainException.ThrowIf(!IsEditable(),
            $"Belge duzenlenemez durumda ({Status}) — satir silinemez.");

        var line = Lines.FirstOrDefault(l => l.Id == lineId);
        DomainException.ThrowIf(line is null, $"Line bulunamadi: id={lineId}");
        Lines.Remove(line!);

        RecalculateSubTotalFromLines();
        RecalculateTotals();
    }

    /// <summary>
    /// Bellek-ici Lines koleksiyonundan SubTotal toplam'i. Lines bos ise 0.
    /// </summary>
    public void RecalculateSubTotalFromLines()
    {
        SubTotal = Lines.Sum(l => l.LineTotal);
    }
}

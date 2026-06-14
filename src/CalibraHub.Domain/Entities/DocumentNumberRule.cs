using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Belge numarası türetme kuralı — Tasarım Kuralı (DocLayoutRule) pattern'iyle birebir
/// aynı mimari: belge tipi + opsiyonel filtreler (cari/grup/kullanıcı/şube/tarih) +
/// ağırlık. Birden çok kural eşleşirse en yüksek ağırlıklı kazanır.
///
/// Format: PREFIX + YEAR + MONTH + COUNTER (her parça opsiyonel, padding ile sabit uzunluk).
///   Örnek: prefix='abc' + year='26' + counter='00000001' → "abc2600000001" (toplam 13)
///   Format = "{prefix}{year}{month}{counter}" şablonuyla generate edilir.
///
/// Sayaç state'i ayrı tabloda (DocumentNumberCounter) — concurrent insert'lerde lock + max+1.
/// ResetPeriod ile yıllık/aylık/günlük reset desteklenir (sayaç sıfırdan başlar).
/// </summary>
[Description("Belge numarası türetme kuralı — koşula bağlı (belge tipi/cari/kullanıcı/tarih) prefix+yıl+sayaç formatı.")]
public sealed class DocumentNumberRule
{
    public int Id { get; init; }

    /// <summary>Kural adı — sadece admin UI'da gösterilir (örn. "Standart Sipariş 2026").</summary>
    public required string Name { get; set; }

    /// <summary>Hangi belge tipi için (zorunlu). FK -> document_types.id</summary>
    public required int DocumentTypeId { get; set; }

    // ── Filtreler (Tasarım Kuralı pattern: tüm filtreler AND, NULL = wildcard) ─────────
    public int? ContactId { get; set; }            // belirli cari (ağırlık: 16)
    public int? ContactGroupId { get; set; }       // cari grubu (ağırlık: 8)
    public int? UserId { get; set; }               // belirli kullanıcı (ağırlık: 4)
    public int? BranchId { get; set; }             // şube (ağırlık: 2) — ileride
    public DateTime? FromDate { get; set; }        // bu tarihten itibaren geçerli (ağırlık: 1)
    public DateTime? ToDate { get; set; }          // bu tarihe kadar geçerli

    // ── Format parçaları ────────────────────────────────────────────────────
    /// <summary>Sabit prefix (örn. "abc" / "STK-" / "SLS"). Boş olabilir.</summary>
    public string? Prefix { get; set; }

    /// <summary>Yıl formatı: "yy" (2 hane), "yyyy" (4 hane), NULL/empty (yıl yok).</summary>
    public string? YearFormat { get; set; }

    /// <summary>Ay formatı: "MM" (2 hane), NULL/empty (ay yok). Yıl ile birlikte kullanılır.</summary>
    public string? MonthFormat { get; set; }

    /// <summary>Sayaç hane sayısı (zero-padding). Örn. 8 → "00000001".</summary>
    public int CounterLength { get; set; } = 6;

    /// <summary>Sayaç başlangıç değeri (counter ilk kez üretilince). Default 1.</summary>
    public int CounterStart { get; set; } = 1;

    /// <summary>Sayaç sıfırlama dönemi: None / Yearly / Monthly / Daily.</summary>
    public DocumentNumberResetPeriod ResetPeriod { get; set; } = DocumentNumberResetPeriod.None;

    /// <summary>Toplam karakter uzunluğu (override). NULL ise format'tan hesaplanır.</summary>
    public int? TotalLength { get; set; }

    /// <summary>
    /// Çakışma çözümü için ağırlık (Tasarım Kuralı pattern):
    ///   Cari=16 + Grup=8 + Kullanıcı=4 + Şube=2 + TarihAraligi=1 = 31 max
    /// Birden fazla kural matched ise en yüksek ağırlıklı kazanır. Eşitse Id küçük olan.
    /// Service tarafında otomatik hesaplanır.
    /// </summary>
    public int Weight { get; set; }

    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

/// <summary>
/// Sayaç sıfırlama dönemi — ResetPeriod alanı bu enum'u taşır.
/// State (DocumentNumberCounter) tablosunda her dönem için ayrı satır tutulur.
/// </summary>
public enum DocumentNumberResetPeriod
{
    /// <summary>Hiç sıfırlanmaz — sayaç sürekli artar.</summary>
    None = 0,
    /// <summary>Her yıl 1'den başlar (1 Ocak).</summary>
    Yearly = 1,
    /// <summary>Her ay 1'den başlar.</summary>
    Monthly = 2,
    /// <summary>Her gün 1'den başlar.</summary>
    Daily = 3,
}

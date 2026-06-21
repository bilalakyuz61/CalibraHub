using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Cari (Contact) / Stok (Item) için kod türetme kuralı. Tasarım Kuralları altındaki
/// "Cari Kodu" ve "Stok Kodu" tab'larından yönetilir. Boş kod alanı (Contact.AccountCode,
/// Items.Code) ile yeni kayıt geldiğinde uygulanır — manuel formdan veya Excel import'tan.
///
/// Format: free-form template string, token'lar runtime'da resolve edilir:
///   {Field:KolonAdi}        → Contact/Item DB kolonu (City, TaxCity, Category vb.)
///   {Widget:WidgetKey}      → WidgetTra değeri (form widget'larından — MUSTERI_TIPI gibi)
///   {Counter:N}             → kural-bazlı sayaç (N hane zero-pad), ResetPeriod ile reset
///   {Year:yyyy|yy}          → current year (4 veya 2 hane)
///   {Month:MM}              → current month
///   {Day:dd}                → current day
///
/// Şart-bazlı: <see cref="CodeRuleCondition"/> ile birden fazla kural farklı şartlar için
/// (örn. müşteri/tedarikçi grubu) tanımlanır; Priority en yüksek olan kazanır.
/// Çakışma: üretilen kod DB'de varsa otomatik suffix (-A, -B, ..., -Z) eklenir.
/// </summary>
[Description("Cari/Stok kod türetme kuralı — template + şart + sayaç ile otomatik kod üretir.")]
public sealed class CodeRule
{
    public int Id { get; init; }

    /// <summary>'Contact' veya 'Item'. Hangi entity tipi için geçerli.</summary>
    public required string EntityType { get; set; }

    /// <summary>Kural adı — admin UI'da gösterilir (örn. "Standart Müşteri 2026").</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Template string. Token'lar runtime'da resolve edilir.
    /// Örnek: 'MS-{Field:City}-{Widget:MUSTERI_TIPI}-{Counter:4}' → 'MS-IST-VIP-0001'
    /// </summary>
    public required string Template { get; set; }

    /// <summary>Birden fazla kural eşleşirse: yüksek priority önce kontrol edilir.</summary>
    public int Priority { get; set; }

    /// <summary>Sayaç sıfırlama dönemi. <see cref="DocumentNumberResetPeriod"/> enum reuse.</summary>
    public DocumentNumberResetPeriod ResetPeriod { get; set; } = DocumentNumberResetPeriod.None;

    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }

    /// <summary>Şartlar (UI tarafında array, repo tarafında join ile yüklenir).</summary>
    public IReadOnlyList<CodeRuleCondition> Conditions { get; set; } = Array.Empty<CodeRuleCondition>();
}

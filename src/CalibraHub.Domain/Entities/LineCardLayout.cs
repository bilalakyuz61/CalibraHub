namespace CalibraHub.Domain.Entities;

/// <summary>
/// LineCardLayout — belge kalem KARTI düzen tanımı (2026-08-05).
///
/// Form bazlı (kalem form kodu, örn. SALES_QUOTE_LINES) tek aktif düzen; admin
/// tasarlar, tüm kullanıcılar aynı kartı görür. LayoutJson şekli:
///   {"items":[{"key":"quantity","span":6,"order":3,"visible":true}, ...]}
/// key = grid config sistem kolonu key'i veya "w_{WidgetCode}" (kartta gösterilen
/// widget). span 1-24 (WidgetMas.ColSpan ile aynı ızgara). Bilinmeyen key'ler
/// runtime'da yok sayılır; düzen tanımında olmayan yeni kolonlar varsayılanla
/// sona eklenir — düzen additive-safe'tir.
/// </summary>
public sealed class LineCardLayout
{
    public int Id { get; init; }
    public required string FormCode { get; init; }
    public required string LayoutJson { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? Updated { get; set; }
}

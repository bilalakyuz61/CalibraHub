namespace CalibraHub.Domain.Entities;

/// <summary>
/// WidgetTraLog — Widget degeri degisiklik gecmisi (alan bazli audit).
///
/// Her SaveRecord'da yalnizca GERCEKTEN degisen degerler icin bir satir yazilir
/// (eski deger → yeni deger + kim + ne zaman). Premium ERP'lerdeki "change
/// document" karsiligi: "bu alani kim, ne zaman, hangi degerden degistirdi?"
///
/// WidgetId'ye FK YOKTUR — widget silinse bile audit kaydi yasamaya devam eder.
/// Bu yuzden WidgetCode ve FormId snapshot olarak da saklanir; log okunurken
/// widget hala mevcutsa guncel Label join ile alinir, silinmisse WidgetCode
/// snapshot'i gosterilir.
/// </summary>
public sealed class WidgetValueLog
{
    public long Id { get; init; }
    public int FormId { get; init; }
    public int WidgetId { get; init; }
    /// <summary>Snapshot — widget silinse de log okunabilir kalsin.</summary>
    public required string WidgetCode { get; init; }
    public required string RecordId { get; init; }
    public string? ParentRecordId { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? ChangedBy { get; init; }
    public DateTime ChangedAt { get; init; } = DateTime.Now;
}

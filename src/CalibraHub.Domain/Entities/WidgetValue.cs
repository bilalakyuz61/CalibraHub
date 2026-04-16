namespace CalibraHub.Domain.Entities;

/// <summary>
/// WidgetTra — Kullanicinin girdigi widget degeri (transaction).
///
/// "Her Sey Metindir" kurali: Value her zaman nvarchar(max). Numeric, date,
/// boolean, multi-select — hepsi C# tercuman katmani tarafindan string'e
/// serialize edilir. DB tarafi tipleri bilmez.
///
/// RecordId: Entity'nin business key'i. Ornek: "MTZ-105", "TEKLIF-001", "42".
/// (WidgetId, RecordId) cifti unique — her kayit icin her widget'in tek
/// degeri vardir.
///
/// ParentRecordId: Master-Detail icin (Faz E — grid widget). Bir child kayit
/// (orn. teklif kalemi) hangi parent kayda (orn. teklif) ait? NULL ise bu
/// satir kendisi bir master kayittir.
/// </summary>
public sealed class WidgetValue
{
    public long Id { get; init; }
    public int WidgetId { get; init; }
    public required string RecordId { get; init; }
    public string? ParentRecordId { get; set; }
    public string? Value { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

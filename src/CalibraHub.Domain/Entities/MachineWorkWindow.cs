namespace CalibraHub.Domain.Entities;

/// <summary>
/// Makine Planlama Faz 2 (2026-08-05) — bir makinenin haftalık müsaitlik penceresi. Shift
/// (vardiya) tanımından bilinçli olarak bağımsızdır (kullanıcı kararı) — bir makine birden çok
/// haftalık pencereye sahip olabilir (örn. Pazartesi-Cuma 08:00-17:00 + Cumartesi 08:00-13:00).
/// Gantt'ta müsaitlik dışı zaman gölgelenir (frontend); bu tablo yalnızca veriyi taşır.
/// </summary>
public sealed class MachineWorkWindow
{
    public int Id { get; set; }

    /// <summary>Machine.Id.</summary>
    public int MachineId { get; set; }

    /// <summary>0=Pazar..6=Cumartesi (JS Date.getDay() ile birebir).</summary>
    public byte DayOfWeek { get; set; }

    /// <summary>Yerel gün-ortası dakikası, 0..1440 (duvar saati — UTC DEĞİL, haftalık tekrar).</summary>
    public short StartMinute { get; set; }

    /// <summary>Yerel gün-ortası dakikası, 0..1440. EndMinute &gt; StartMinute olmalı.</summary>
    public short EndMinute { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public DateTime Created { get; set; }
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Makine Planlama (Üretim Çizelgeleme) — Faz 1 Manuel (2026-08-04). Bir operasyonun (veya
/// üretim-dışı bir bakım/duruş/setup ihtiyacının) bir makinede kapladığı planlı zaman bloğu.
/// Ayrı tablo olma sebebi: operasyon bölünebilsin (split → çok blok), makineye üretim-dışı blok
/// konabilsin, Gantt'ın "kaynak×zaman" modeline birebir otursun. Mevcut
/// <see cref="WorkOrderOperation"/>.MachineId/Status korunur — bu yalnız zamanlama katmanı.
/// </summary>
public class MachineScheduleBlock
{
    public int Id { get; set; }

    /// <summary>Bloğun atandığı makine (Machine.Id).</summary>
    public int MachineId { get; set; }

    /// <summary>Planlanan iş emri operasyonu (WorkOrderOperation.Id). NULL = üretim-dışı blok
    /// (bakım/duruş/setup — herhangi bir operasyona bağlı değil).</summary>
    public int? WorkOrderOperationId { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>1=Production/2=Setup/3=Maintenance/4=Down.</summary>
    public byte BlockType { get; set; } = (byte)MachineScheduleBlockType.Production;

    /// <summary>1=Planned/2=Locked/3=Confirmed.</summary>
    public byte Status { get; set; } = (byte)MachineScheduleBlockStatus.Planned;

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public DateTime Created { get; set; }
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

/// <summary>Zaman bloğunun türü. DB'de TINYINT olarak tutulur.</summary>
public enum MachineScheduleBlockType : byte
{
    Production = 1,
    Setup = 2,
    Maintenance = 3,
    Down = 4,
}

/// <summary>Zaman bloğunun planlama durumu. DB'de TINYINT olarak tutulur.</summary>
public enum MachineScheduleBlockStatus : byte
{
    Planned = 1,
    Locked = 2,
    Confirmed = 3,
}

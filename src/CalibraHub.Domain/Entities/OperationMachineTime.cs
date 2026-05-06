using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Operasyon × Makine bazlı üretim süresi. Bir operasyon farklı makinelerde farklı sürede tamamlanabilir. ItemId opsiyonel: dolu ise sadece o ürün için özel süre; boş ise genel makine-operasyon süresi.")]
public sealed class OperationMachineTime
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public int OperationId { get; init; }
    public int MachineId { get; init; }
    public int? ItemId { get; init; }
    /// <summary>Batch miktar — "Şu kadar birim için" (DurationPerUnit bu miktar için süre).</summary>
    public decimal Quantity { get; init; } = 1m;
    /// <summary>Quantity birimi için toplam süre. Birim/dakika hesabı: DurationPerUnit / Quantity.</summary>
    public decimal DurationPerUnit { get; init; }
    public DurationUnit DurationUnit { get; init; } = DurationUnit.Minute;
    public bool IsActive { get; init; } = true;
    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
}

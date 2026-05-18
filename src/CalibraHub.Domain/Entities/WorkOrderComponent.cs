using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Faz 2 — BOM Patlatma sonucu üretilen iş emri bileşen kaydı.
/// Bir iş emri için reçete (Bill of Materials) patlatıldığında her bileşen
/// satırı bu tabloya yazılır. Idempotent re-explode: ExplodeBomAsync mevcut
/// satırları siler ve yeniden oluşturur.
///
/// RequiredQuantity = bomLine.Quantity × workOrder.PlannedQuantity × (1 + ScrapRatio)
/// IssuedQuantity   = depo çıkışı yapılınca (Faz 2.1+) artırılır
/// </summary>
[Description("İş emri bileşen kaydı (Faz 2 BOM patlatma çıktısı). Reçete satırı × planlanan miktar × (1 + fire) formülü ile hesaplanır. IssuedQuantity ile depo çıkışı takibi ileride eklenir.")]
public sealed class WorkOrderComponent
{
    public int Id { get; init; }
    public int WorkOrderId { get; init; }

    /// <summary>Bileşenin Items.Id referansı.</summary>
    public int ItemId { get; init; }

    /// <summary>Konfigürasyon değişkenli bileşenler için ItemConfiguration.Id (opsiyonel).</summary>
    public int? ConfigId { get; init; }

    /// <summary>Patlatma anında hesaplanan ihtiyaç miktarı (fire dahil).</summary>
    public decimal RequiredQuantity { get; init; }

    /// <summary>Depodan çıkış yapılan miktar — başlangıçta 0.</summary>
    public decimal IssuedQuantity { get; init; }

    /// <summary>Reçete satırından kopyalanan fire oranı (0..1).</summary>
    public decimal ScrapRate { get; init; }

    public int? UnitId { get; init; }

    public string? Notes { get; init; }

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
}

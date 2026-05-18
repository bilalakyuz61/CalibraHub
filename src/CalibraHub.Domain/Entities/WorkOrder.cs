using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Uretim is emri header — 1 mamul/emir. Sales order'dan tetiklendiyse WorkOrderSource araciligi ile kaynak DocumentLine(lar)a baglanir. Multi-level patlatma alt is emirlerini ParentWorkOrderId ile zincirler (Faz 2).")]
public sealed class WorkOrder
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string OrderNumber { get; init; }
    public DateTime OrderDate { get; init; }

    public int ItemId { get; init; }
    public int? ConfigId { get; init; }
    public decimal PlannedQuantity { get; init; }
    public decimal ProducedQuantity { get; init; }
    public decimal ScrapQuantity { get; init; }
    public int? UnitId { get; init; }

    /// <summary>
    /// İş emrinin uygulayacağı rota. Faz 3a'da nullable; Release sırasında auto-explosion için
    /// dolu olması gerekir, boşsa Item'a göre Routing aranır.
    /// </summary>
    public int? RoutingId { get; init; }

    public DateTime? PlannedStartDate { get; init; }
    public DateTime? PlannedEndDate { get; init; }
    public DateTime? ActualStartDate { get; init; }
    public DateTime? ActualEndDate { get; init; }

    public WorkOrderStatus Status { get; init; }
    public WorkOrderPriority Priority { get; init; } = WorkOrderPriority.Medium;

    /// <summary>Eski User-bazli atama — backward compat icin tutuluyor, runtime'da kullanilmiyor.</summary>
    public Guid? AssignedUserId { get; init; }

    /// <summary>Yeni atama — dogrudan Personnel.Id (User hesabi gerekmez).</summary>
    public int? AssignedPersonnelId { get; init; }

    public int? WarehouseLocationId { get; init; }

    /// <summary>
    /// Header tercihi olarak default makine. Patlatma sırasında her operasyona
    /// fallback olarak yansır (RoutingOperation kendi MachineId'sini taşımıyorsa).
    /// Operasyon satır seviyesinde override edilebilir (Faz 3).
    /// </summary>
    public int? DefaultMachineId { get; init; }

    public int RevisionNo { get; init; }
    public int? ParentWorkOrderId { get; init; }
    public int? RevisedFromId { get; init; }

    public string? Notes { get; init; }
    public Guid? CreatedBy { get; init; }
    public DateTime Created { get; init; }
    public Guid? UpdatedBy { get; init; }
    public DateTime? Updated { get; init; }
    public bool IsActive { get; init; } = true;
}

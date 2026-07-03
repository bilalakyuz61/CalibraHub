using System.ComponentModel;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Uretim is emri header — 1 mamul/emir. Sales order'dan tetiklendiyse WorkOrderSource araciligi ile kaynak DocumentLine(lar)a baglanir. Multi-level patlatma alt is emirlerini ParentWorkOrderId ile zincirler (Faz 2).")]
public sealed class WorkOrder
{
    public int Id { get; init; }
    public int CompanyId { get; init; }

    /// <summary>Document companion FK (UNIQUE) — belge kimligi (numara/tarih/revizyon/notlar) Document'ta tutulur.</summary>
    public int DocumentId { get; init; }

    public int ItemId { get; init; }
    public int? ConfigId { get; init; }
    public decimal PlannedQuantity { get; init; }

    private decimal _producedQuantity;
    public decimal ProducedQuantity { get => _producedQuantity; init => _producedQuantity = value; }

    private decimal _scrapQuantity;
    public decimal ScrapQuantity { get => _scrapQuantity; init => _scrapQuantity = value; }

    public int? UnitId { get; init; }

    /// <summary>
    /// İş emrinin uygulayacağı rota. Faz 3a'da nullable; Release sırasında auto-explosion için
    /// dolu olması gerekir, boşsa Item'a göre Routing aranır.
    /// </summary>
    public int? RoutingId { get; init; }

    public DateTime? PlannedStartDate { get; init; }
    public DateTime? PlannedEndDate { get; init; }

    private DateTime? _actualStartDate;
    public DateTime? ActualStartDate { get => _actualStartDate; init => _actualStartDate = value; }

    private DateTime? _actualEndDate;
    public DateTime? ActualEndDate { get => _actualEndDate; init => _actualEndDate = value; }

    private WorkOrderStatus _status;
    public WorkOrderStatus Status { get => _status; init => _status = value; }

    public WorkOrderPriority Priority { get; init; } = WorkOrderPriority.Medium;

    /// <summary>Eski User-bazli atama — backward compat icin tutuluyor, runtime'da kullanilmiyor.</summary>
    public int? AssignedUserId { get; init; }

    /// <summary>Yeni atama — dogrudan Personnel.Id (User hesabi gerekmez).</summary>
    public int? AssignedPersonnelId { get; init; }

    public int? WarehouseLocationId { get; init; }

    /// <summary>
    /// Header tercihi olarak default makine. Patlatma sırasında her operasyona
    /// fallback olarak yansır (RoutingOperation kendi MachineId'sini taşımıyorsa).
    /// Operasyon satır seviyesinde override edilebilir (Faz 3).
    /// </summary>
    public int? DefaultMachineId { get; init; }

    /// <summary>
    /// Opsiyonel AR-GE/ÜR-GE proje baglantisi (Document.id). AR-GE seri/prototip mamulu icin
    /// WO acildiginda WorkOrderService otomatik turetir; manuel de secilebilir. Maliyet rollup'ta kullanilir.
    /// </summary>
    public int? ArgeProjectId { get; init; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
    public bool IsActive { get; init; } = true;

    // ── Davranis metodlari (rapor §2.4 — Domain davranisi yayma) ─────────────
    // Status state machine: Planned → Released → InProgress → Completed → Closed
    // Cancelled her durumdan alinabilir (Closed haric — kapali emir iptal edilemez)

    /// <summary>Yayimlama: Planned → Released. Uretime hazirdir.</summary>
    public void Release()
    {
        DomainException.ThrowIf(_status != WorkOrderStatus.Planned,
            $"Sadece Planned durumdaki emir Released'a gecebilir (mevcut: {_status}).");
        _status = WorkOrderStatus.Released;
    }

    /// <summary>Ilk hareket isleme: Released → InProgress (uretim baslangici).</summary>
    public void StartProduction(DateTime? actualStart = null)
    {
        DomainException.ThrowIf(_status != WorkOrderStatus.Released,
            $"Sadece Released durumdaki emir InProgress'e gecebilir (mevcut: {_status}).");
        _status = WorkOrderStatus.InProgress;
        _actualStartDate ??= actualStart ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Uretim hareketi kayit: ProducedQuantity'yi artirir + isteg-e bagli scrap ekler.
    /// Released veya InProgress'te calisir. PlannedQuantity asilirsa DomainException.
    /// </summary>
    public void RegisterProduction(decimal quantity, decimal scrap = 0)
    {
        DomainException.ThrowIf(_status != WorkOrderStatus.Released && _status != WorkOrderStatus.InProgress,
            $"Uretim hareketi sadece Released veya InProgress durumda alinabilir (mevcut: {_status}).");
        DomainException.ThrowIf(quantity < 0, "Miktar negatif olamaz.");
        DomainException.ThrowIf(scrap < 0,    "Hurda miktari negatif olamaz.");

        // Ilk hareket otomatik InProgress'e gecirir
        if (_status == WorkOrderStatus.Released)
        {
            _status = WorkOrderStatus.InProgress;
            _actualStartDate ??= DateTime.UtcNow;
        }

        _producedQuantity += quantity;
        _scrapQuantity    += scrap;
    }

    /// <summary>
    /// Tamamlama: ProducedQuantity >= PlannedQuantity ise Completed'a gecirir.
    /// InProgress haricinde cagrilamaz.
    /// </summary>
    public void MarkAsCompleted()
    {
        DomainException.ThrowIf(_status != WorkOrderStatus.InProgress,
            $"Sadece InProgress durumdaki emir tamamlanabilir (mevcut: {_status}).");
        DomainException.ThrowIf(_producedQuantity < PlannedQuantity,
            $"Tamamlanmis miktar planlanan altinda ({_producedQuantity} < {PlannedQuantity}).");
        _status = WorkOrderStatus.Completed;
        _actualEndDate ??= DateTime.UtcNow;
    }

    /// <summary>Kalici kapatma: Completed → Closed. Hareket alinamaz.</summary>
    public void Close()
    {
        DomainException.ThrowIf(_status != WorkOrderStatus.Completed,
            $"Sadece Completed durumdaki emir kapatilabilir (mevcut: {_status}).");
        _status = WorkOrderStatus.Closed;
    }

    /// <summary>
    /// Iptal: Closed haric her durumdan alinabilir. ActualEndDate set edilir.
    /// </summary>
    public void Cancel()
    {
        DomainException.ThrowIf(_status == WorkOrderStatus.Closed,
            "Kapatilmis emir iptal edilemez (Closed → Cancelled gecisi gecersizdir).");
        DomainException.ThrowIf(_status == WorkOrderStatus.Cancelled,
            "Emir zaten iptal edilmis.");
        _status = WorkOrderStatus.Cancelled;
        _actualEndDate ??= DateTime.UtcNow;
    }

    /// <summary>Helper: Aktif uretim mu (Released/InProgress)?</summary>
    public bool IsInProduction() => _status is WorkOrderStatus.Released or WorkOrderStatus.InProgress;

    /// <summary>Helper: Final durum mu (Closed/Cancelled)?</summary>
    public bool IsFinalized() => _status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled;
}

using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Varlık (ekipman/demirbaş) kaydı. Her varlık bir lokasyona, opsiyonel olarak bir departmana ve zimmetli personele bağlıdır. Kind=Machine olanlar Makine modülünden gelir (MachineId dolu). Bakım/kalibrasyon takvimi + durum (Aktif/Bakımda/Hurda) + geçmiş (AssetEvent) bu kayda bağlanır.")]
public sealed class Asset
{
    public int Id { get; init; }

    /// <summary>Sahip şirket — multi-tenant için (per-company DB olsa da Machine deseni ile tutulur).</summary>
    public int CompanyId { get; init; }

    /// <summary>Kısa benzersiz varlık kodu (UNIQUE per company). Kullanıcı girmez — auto-derive (VRL-xxxxxx).</summary>
    public required string AssetCode { get; init; }

    /// <summary>Varlığın görünür adı (örn. "Mitutoyo Kumpas 0-150").</summary>
    public required string AssetName { get; init; }

    public string? Description { get; init; }

    public AssetKind Kind { get; init; } = AssetKind.Equipment;

    /// <summary>FK Location.Id — varlığın bulunduğu fiziki lokasyon (opsiyonel).</summary>
    public int? LocationId { get; init; }

    /// <summary>FK Department.Id — sahip/sorumlu birim (opsiyonel).</summary>
    public int? DepartmentId { get; init; }

    /// <summary>FK Personnel.Id — zimmetli/sorumlu personel (opsiyonel).</summary>
    public int? AssignedPersonnelId { get; init; }

    /// <summary>FK Machine.Id — bu varlık bir makineyi temsil ediyorsa (Kind=Machine). Birleşik görünüm.</summary>
    public int? MachineId { get; init; }

    public string? SerialNo { get; init; }
    public DateTime? AcquisitionDate { get; init; }
    public DateTime? WarrantyExpiryDate { get; init; }

    // ── BT / Ağ bilgisi (yalnız Kind.IsItAsset() türlerinde anlamlı; zimmetli kullanıcı = AssignedPersonnelId) ──
    /// <summary>IPv4/IPv6 adresi (Bilgisayar/Sunucu/Ağ cihazı).</summary>
    public string? IpAddress { get; init; }
    /// <summary>Ağdaki cihaz adı (örn. PC-MUHASEBE-01).</summary>
    public string? Hostname { get; init; }
    /// <summary>İşletim sistemi (örn. Windows 11 Pro, Ubuntu 22.04, iOS 18).</summary>
    public string? OperatingSystem { get; init; }
    /// <summary>Donanım MAC adresi.</summary>
    public string? MacAddress { get; init; }
    /// <summary>Etki alanı (domain) veya çalışma grubu (workgroup).</summary>
    public string? NetworkDomain { get; init; }

    /// <summary>Araç plakası (yalnız Kind=Vehicle için anlamlı; TR plaka formatında doğrulanır).</summary>
    public string? PlateNo { get; init; }

    // ── Bakım takvimi ─────────────────────────────────────────────
    /// <summary>Parametrik: bu varlığa bakım yapılır mı? Bakım Takibi sadece true olanları izler.</summary>
    public bool IsMaintained { get; init; }
    /// <summary>Periyot değeri (birim <see cref="MaintenancePeriodUnit"/> ile yorumlanır — Gün/Ay/Yıl).</summary>
    public int? MaintenancePeriodDays { get; init; }
    /// <summary>Periyot birimi: Gün/Ay/Yıl (varsayılan Gün — geriye dönük uyum).</summary>
    public AssetPeriodUnit MaintenancePeriodUnit { get; init; } = AssetPeriodUnit.Days;
    /// <summary>Hareketlerden (AssetEvent.Maintenance) türetilir; edit formundan değiştirilemez.</summary>
    public DateTime? LastMaintenanceDate { get; init; }
    /// <summary>LastMaintenanceDate + MaintenancePeriodDays ile türetilir.</summary>
    public DateTime? NextMaintenanceDate { get; init; }
    /// <summary>Hatırlatma worker'ı son olarak hangi NextMaintenanceDate için bildirim attı — tekrar bildirim engeli.</summary>
    public DateTime? MaintenanceRemindedFor { get; init; }

    // ── Kalibrasyon takvimi ───────────────────────────────────────
    /// <summary>Parametrik: bu varlık kalibre edilir mi? Kalibrasyon Takibi sadece true olanları izler.</summary>
    public bool IsCalibrated { get; init; }
    /// <summary>Periyot değeri (birim <see cref="CalibrationPeriodUnit"/> ile yorumlanır — Gün/Ay/Yıl).</summary>
    public int? CalibrationPeriodDays { get; init; }
    /// <summary>Periyot birimi: Gün/Ay/Yıl (varsayılan Gün — geriye dönük uyum).</summary>
    public AssetPeriodUnit CalibrationPeriodUnit { get; init; } = AssetPeriodUnit.Days;
    /// <summary>Hareketlerden (AssetEvent.Calibration) türetilir; edit formundan değiştirilemez.</summary>
    public DateTime? LastCalibrationDate { get; init; }
    /// <summary>LastCalibrationDate + CalibrationPeriodDays ile türetilir.</summary>
    public DateTime? NextCalibrationDate { get; init; }
    public DateTime? CalibrationRemindedFor { get; init; }

    /// <summary>Parametrik: bu varlık zimmetlenebilir mi? Kapalıysa Zimmetleme board'unda görünmez, zimmet açılamaz.</summary>
    public bool IsAssignable { get; init; } = true;

    public AssetStatus Status { get; init; } = AssetStatus.Active;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
    public int? CreatedById { get; init; }
    public int? UpdatedById { get; init; }
}

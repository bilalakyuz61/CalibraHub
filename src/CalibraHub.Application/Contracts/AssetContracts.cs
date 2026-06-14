using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

// ── Varlık Yönetimi (Asset Management) DTO + Request sözleşmeleri ───────────────
// ID tabanlı eşleştirme: FK alanları int; *Name/*Code alanları yalnızca gösterim için
// (Repository/Service join ile doldurur).

/// <summary>Tekil varlık kaydı — join'lenmiş gösterim alanları dahil.</summary>
public sealed record AssetDto(
    int Id,
    string AssetCode,
    string AssetName,
    string? Description,
    AssetKind Kind,
    int? LocationId,
    string? LocationCode,
    string? LocationName,
    int? DepartmentId,
    string? DepartmentName,
    int? AssignedPersonnelId,
    string? AssignedPersonnelName,
    int? MachineId,
    string? MachineCode,
    string? MachineName,
    string? SerialNo,
    DateTime? AcquisitionDate,
    DateTime? WarrantyExpiryDate,
    string? IpAddress,
    string? Hostname,
    string? OperatingSystem,
    string? MacAddress,
    string? NetworkDomain,
    string? PlateNo,
    bool IsMaintained,
    int? MaintenancePeriodDays,
    AssetPeriodUnit MaintenancePeriodUnit,
    DateTime? LastMaintenanceDate,
    DateTime? NextMaintenanceDate,
    bool IsCalibrated,
    int? CalibrationPeriodDays,
    AssetPeriodUnit CalibrationPeriodUnit,
    DateTime? LastCalibrationDate,
    DateTime? NextCalibrationDate,
    AssetStatus Status,
    int SortOrder,
    bool IsActive,
    bool IsAssignable)
{
    public bool IsMachineBacked => MachineId.HasValue;
}

/// <summary>
/// SmartBoard (birleşik görünüm) kart modeli. Hem materialize edilmiş Asset kayıtları
/// hem de henüz materialize edilmemiş Makine kayıtları (IsVirtualMachine=true) bu shape ile gelir.
/// </summary>
public sealed record AssetCardDto(
    string CardId,              // "a{assetId}" veya "m{machineId}"
    int? AssetId,              // null → henüz materialize edilmemiş makine
    int? MachineId,
    string Name,
    string? LocationName,
    string? DepartmentName,
    string? AssignedPersonnelName,
    AssetKind Kind,
    AssetStatus Status,
    bool IsActive,
    bool IsAssignable,
    bool IsMaintained,
    int? MaintenancePeriodDays,
    DateTime? NextMaintenanceDate,
    bool IsCalibrated,
    int? CalibrationPeriodDays,
    DateTime? NextCalibrationDate,
    int SortOrder,
    bool IsVirtualMachine);

public sealed record CreateAssetRequest(
    string AssetName,
    string? Description,
    AssetKind Kind,
    int? LocationId,
    int? DepartmentId,
    int? AssignedPersonnelId,
    int? MachineId,
    string? SerialNo,
    DateTime? AcquisitionDate,
    DateTime? WarrantyExpiryDate,
    string? IpAddress,
    string? Hostname,
    string? OperatingSystem,
    string? MacAddress,
    string? NetworkDomain,
    string? PlateNo,
    bool IsMaintained,
    int? MaintenancePeriodDays,
    AssetPeriodUnit MaintenancePeriodUnit,
    bool IsCalibrated,
    int? CalibrationPeriodDays,
    AssetPeriodUnit CalibrationPeriodUnit,
    AssetStatus Status,
    int SortOrder,
    bool IsActive,
    bool IsAssignable,
    int? UserId);

public sealed record UpdateAssetRequest(
    int Id,
    string AssetName,
    string? Description,
    AssetKind Kind,
    int? LocationId,
    int? DepartmentId,
    int? AssignedPersonnelId,
    string? SerialNo,
    DateTime? AcquisitionDate,
    DateTime? WarrantyExpiryDate,
    string? IpAddress,
    string? Hostname,
    string? OperatingSystem,
    string? MacAddress,
    string? NetworkDomain,
    string? PlateNo,
    bool IsMaintained,
    int? MaintenancePeriodDays,
    AssetPeriodUnit MaintenancePeriodUnit,
    bool IsCalibrated,
    int? CalibrationPeriodDays,
    AssetPeriodUnit CalibrationPeriodUnit,
    AssetStatus Status,
    int SortOrder,
    bool IsActive,
    bool IsAssignable,
    int? UserId);

// ── Geçmiş (AssetEvent) ────────────────────────────────────────────────────────

public sealed record AssetEventDto(
    int Id,
    int AssetId,
    AssetEventType EventType,
    DateTime EventDate,
    int? PerformedByPersonnelId,
    string? PerformedByName,
    string? PerformedByText,
    decimal? Cost,
    AssetEventResult Result,
    string? Notes,
    DateTime? NextDueDate,
    string? DocumentUrl,
    DateTime Created,
    int? CreatedById);

public sealed record CreateAssetEventRequest(
    int AssetId,
    AssetEventType EventType,
    DateTime EventDate,
    int? PerformedByPersonnelId,
    string? PerformedByText,
    decimal? Cost,
    AssetEventResult Result,
    string? Notes,
    DateTime? NextDueDate,
    string? DocumentUrl,
    int? UserId);

// ── Zimmet hareketi (AssetAssignment) ──────────────────────────────────────────

public sealed record AssetAssignmentDto(
    int Id,
    int AssetId,
    int? PersonnelId,
    string? PersonnelName,
    int? DepartmentId,
    string? DepartmentName,
    int? LocationId,
    string? LocationName,
    DateTime AssignDate,
    DateTime? ReturnDate,
    string? AssignNote,
    string? ReturnNote,
    string? DocumentNo,
    DateTime Created,
    int? CreatedById)
{
    public bool IsActive => ReturnDate == null;
}

/// <summary>
/// Zimmet takip raporu satırı — tüm varlıklar arası düz liste: hangi varlık kime zimmetli,
/// ne zaman verildi, ne zaman geri alındı. (Kompleks analiz Grafana'da; bu sade izleme içindir.)
/// </summary>
public sealed record AssignmentReportRowDto(
    int AssignmentId,
    int AssetId,
    string AssetCode,
    string AssetName,
    int? PersonnelId,
    string? PersonnelName,
    string? DepartmentName,
    string? LocationName,
    DateTime AssignDate,
    DateTime? ReturnDate,
    string? DocumentNo,
    string? AssignNote,
    string? ReturnNote)
{
    public bool IsActive => ReturnDate == null;
}

// ── Edit ekranı dropdown verileri ──────────────────────────────────────────────

public sealed record AssetLookupItemDto(int Id, string Label);

public sealed record AssetLocationItemDto(
    int Id, int? ParentId, string Code, string? Name, int SortOrder, bool IsActive);

public sealed record AssetEditLookupsDto(
    IReadOnlyList<AssetLocationItemDto> Locations,
    IReadOnlyList<AssetLookupItemDto> Departments,
    IReadOnlyList<AssetLookupItemDto> Personnel,
    IReadOnlyList<AssetLookupItemDto> Machines);

/// <summary>Hatırlatma worker'ı için: bakım/kalibrasyon tarihi yaklaşan varlık satırı.</summary>
public sealed record AssetReminderDueDto(
    int Id,
    string AssetCode,
    string AssetName,
    int? AssignedPersonnelId,
    bool IsMaintenance,         // true → bakım, false → kalibrasyon
    DateTime DueDate);

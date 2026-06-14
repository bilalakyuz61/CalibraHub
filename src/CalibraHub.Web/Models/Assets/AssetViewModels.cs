using CalibraHub.Domain.Enums;

namespace CalibraHub.Web.Models.Assets;

public sealed class AssetsSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class AssetAssignViewModel
{
    public int Id { get; init; }
    public string? AssetCode { get; init; }
    public string? AssetName { get; init; }
}

public sealed class AssignmentDocumentViewModel
{
    public int AssignmentId { get; init; }
    public string? AssetCode { get; init; }
    public string? AssetName { get; init; }
    public string? SerialNo { get; init; }
    public string? LocationName { get; init; }
    public string? PersonnelName { get; init; }
    public DateTime AssignDate { get; init; }
    public DateTime? ReturnDate { get; init; }
    public string? DocumentNo { get; init; }
    public string? Note { get; init; }
}

public sealed class AssetEditViewModel
{
    public int? Id { get; init; }
    public string? AssetCode { get; init; }
    public string? AssetName { get; init; }
    public string? Description { get; init; }
    public AssetKind Kind { get; init; } = AssetKind.Equipment;

    public int? LocationId { get; init; }
    public int? DepartmentId { get; init; }
    public int? AssignedPersonnelId { get; init; }
    public int? MachineId { get; init; }
    public string? MachineName { get; init; }

    public string? SerialNo { get; init; }
    public DateTime? AcquisitionDate { get; init; }
    public DateTime? WarrantyExpiryDate { get; init; }

    public string? IpAddress { get; init; }
    public string? Hostname { get; init; }
    public string? OperatingSystem { get; init; }
    public string? MacAddress { get; init; }
    public string? NetworkDomain { get; init; }

    public string? PlateNo { get; init; }

    public bool IsMaintained { get; init; }
    public int? MaintenancePeriodDays { get; init; }
    public AssetPeriodUnit MaintenancePeriodUnit { get; init; } = AssetPeriodUnit.Days;
    public DateTime? LastMaintenanceDate { get; init; }
    public DateTime? NextMaintenanceDate { get; init; }

    public bool IsCalibrated { get; init; }
    public int? CalibrationPeriodDays { get; init; }
    public AssetPeriodUnit CalibrationPeriodUnit { get; init; } = AssetPeriodUnit.Days;
    public DateTime? LastCalibrationDate { get; init; }
    public DateTime? NextCalibrationDate { get; init; }

    public AssetStatus Status { get; init; } = AssetStatus.Active;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsAssignable { get; init; } = true;

    public bool IsMachineBacked => MachineId.HasValue;
}

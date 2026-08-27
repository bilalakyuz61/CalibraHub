namespace CalibraHub.Application.Contracts;

public sealed record PersonnelDto(
    int Id,
    int CompanyId,
    string Code,
    string FullName,
    string? Title,
    string? Department,
    string? PinCode,
    string? CardNo,
    bool IsProductionOperator,
    bool IsActive,
    int? UserId,
    string? UserFullName,
    string? Phone,
    string? Email,
    string? Notes,
    DateTime? BirthDate,
    DateTime Created,
    DateTime? Updated,
    int? LocationId = null,
    string? LocationName = null,
    /// <summary>Mobilde operasyon oncesi PIN sorulsun mu? Varsayilan true (mevcut davranis).</summary>
    bool IsMobilePinRequired = true);

public sealed record SavePersonnelRequest(
    int Id,
    string Code,
    string FullName,
    string? Title,
    string? Department,
    string? PinCode,
    string? CardNo,
    bool IsProductionOperator,
    bool IsActive,
    int? UserId,
    string? Phone,
    string? Email,
    string? Notes,
    DateTime? BirthDate,
    int? LocationId = null,
    bool IsMobilePinRequired = true);

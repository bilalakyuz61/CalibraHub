namespace CalibraHub.Application.Contracts;

/// <summary>
/// Cache'lenen kural satırı — sadece eşleşme için gerekli kriter alanları taşır.
/// IDesignProvider buradan in-memory ağırlık hesabı yapar.
/// </summary>
public sealed record DocLayoutRuleMatchRow(
    int Id,
    int LayoutId,
    int? CustomerId,
    int? UserId,
    int? BranchId,
    int? WarehouseId,
    DateTime UpdatedAt,
    int? ContactGroupId = null,
    byte? AccountType   = null);

/// <summary>
/// DocLayoutRule listesi / detay görüntüsü için DTO. Hesaplanmış Weight de taşır.
/// </summary>
public sealed record DocLayoutRuleDto(
    int Id,
    string? DocType,
    string DocTypeLabel,
    int LayoutId,
    string LayoutName,
    int? CustomerId,
    int? UserId,
    int? BranchId,
    int? WarehouseId,
    bool IsActive,
    int Weight,
    DateTime UpdatedAt,
    int? ContactGroupId  = null,
    int? DocumentTypeId  = null,
    byte? AccountType    = null);

/// <summary>
/// Kural kaydetme isteği. NULLable kriterler = "wildcard" (tüm değerler için).
/// DocType (legacy) + DocumentTypeId (yeni FK) birlikte yazilir; runtime DocType
/// kullanmaya devam eder.
/// </summary>
public sealed record SaveDocLayoutRuleRequest(
    int Id,
    string? DocType,
    int LayoutId,
    int? CustomerId,
    int? UserId,
    int? BranchId,
    int? WarehouseId,
    bool IsActive        = true,
    int? ContactGroupId  = null,
    int? DocumentTypeId  = null,
    byte? AccountType    = null);

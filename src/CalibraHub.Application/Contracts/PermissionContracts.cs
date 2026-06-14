namespace CalibraHub.Application.Contracts;

/// <summary>
/// İzin katalog DTO'su (PermissionDef satırı).
/// </summary>
public sealed record PermissionDefDto(
    int Id,
    string FormCode,
    string ActionCode,
    string Label,
    string? Category,
    int SortOrder,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

/// <summary>
/// Yetki atama DTO'su. UserId veya DepartmentId'den biri dolu.
/// </summary>
public sealed record UserPermissionDto(
    int Id,
    int? UserId,
    int? DepartmentId,
    int PermissionDefId,
    bool IsGranted,
    DateTime Created,
    string? CreatedBy);

/// <summary>
/// Kullanıcının efektif izin matrisi — Admin UI'da "Yetki Override" tabında kullanılır.
/// Her satır bir izin tanımı için kaynak + sonuç değerini tutar.
/// </summary>
public sealed record EffectivePermissionDto(
    int PermissionDefId,
    string FormCode,
    string ActionCode,
    string Label,
    string? Category,
    /// <summary>'USER' (override) / 'DEPARTMENT' (departmandan) / 'DEFAULT' (hiçbiri, deny).</summary>
    string Source,
    bool IsAllowed,
    /// <summary>Form'un dbo.Forms tablosundaki SortOrder değeri — menü sıralaması ile aynı.</summary>
    int FormSortOrder = 0);

/// <summary>
/// İzin katalog kaydı oluşturma/güncelleme isteği.
/// </summary>
public sealed record SavePermissionDefRequest(
    int? Id,
    string FormCode,
    string ActionCode,
    string Label,
    string? Category = null,
    int SortOrder = 0,
    bool IsActive = true);

/// <summary>
/// Tek yetki atama (kullanıcı veya departman) save request.
/// UserId XOR DepartmentId — service tarafında validate edilir.
/// </summary>
public sealed record SaveUserPermissionRequest(
    int? Id,
    int? UserId,
    int? DepartmentId,
    int PermissionDefId,
    bool IsGranted);

/// <summary>
/// Toplu atama — örn. bir departman için tüm izin satırlarını replace.
/// </summary>
public sealed record BulkAssignPermissionRequest(
    int? UserId,
    int? DepartmentId,
    IReadOnlyList<PermissionAssignmentItem> Items);

public sealed record PermissionAssignmentItem(
    int PermissionDefId,
    bool IsGranted);

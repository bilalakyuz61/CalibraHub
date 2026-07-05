namespace CalibraHub.Application.Contracts;

/// <summary>
/// Erişim kapsamı — bir operasyon için kullanıcının etkin yetki seviyesi.
/// Öncelik sırası (düşük → yüksek): None &lt; Own &lt; Department &lt; All
/// </summary>
public enum AccessScope
{
    /// <summary>Hiçbir yetki yok — kayıt listesi boş döner, tekil kayıt 403.</summary>
    None,
    /// <summary>Yalnızca kendi oluşturduğu kayıtlar (CreatedById == userId).</summary>
    Own,
    /// <summary>Kendi departmanındaki kullanıcıların oluşturduğu kayıtlar.</summary>
    Department,
    /// <summary>Tüm kayıtlar.</summary>
    All,
}

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

// ── Yetki grupları (2026-07-06) ─────────────────────────────────────────

/// <summary>Grup listesi satırı — üye sayısı dahil (Yetki Yönetimi UI).</summary>
public sealed record PermissionGroupDto(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    int MemberCount);

/// <summary>Grup üyesi — matris başlığı ve üye yönetim modali için.</summary>
public sealed record PermissionGroupMemberDto(
    int UserId,
    string FullName,
    string? Email);

/// <summary>Grup oluştur/güncelle isteği.</summary>
public sealed record SavePermissionGroupRequest(
    int? Id,
    string Name,
    string? Description,
    bool IsActive = true);

/// <summary>Grup üyeliğini toplu replace isteği.</summary>
public sealed record SaveGroupMembersRequest(
    IReadOnlyList<int> UserIds);

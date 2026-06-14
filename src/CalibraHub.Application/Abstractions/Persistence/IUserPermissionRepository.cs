using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// 2026-06-06 — PermissionGrant atama tablosu CRUD. Impl: SqlPermissionGrantRepository.
///
/// Tek tablo + iki sahip türü (UserId XOR DepartmentId). Resolver query'leri için
/// hem User hem Department bazlı bulk fetch metodları sağlanır.
/// </summary>
public interface IPermissionGrantRepository
{
    Task<PermissionGrant?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Bir kullanıcıya doğrudan atanmış tüm izin satırları (UserId=u).</summary>
    Task<IReadOnlyList<PermissionGrant>> ListByUserAsync(int userId, CancellationToken ct);

    /// <summary>Bir departmana atanmış tüm izin satırları (DepartmentId=d).</summary>
    Task<IReadOnlyList<PermissionGrant>> ListByDepartmentAsync(int departmentId, CancellationToken ct);

    /// <summary>
    /// PermissionService.CheckAsync için: kullanıcının kendi override'ı + departmanından gelen
    /// satırları TEK SORGUDA döner. UI'da matrix render için de kullanılır.
    /// </summary>
    Task<IReadOnlyList<PermissionGrant>> ListForUserAndDepartmentAsync(
        int userId, int? departmentId, CancellationToken ct);

    /// <summary>Save (create veya update). UserId XOR DepartmentId kuralı service'de validate edilir.</summary>
    Task<int> SaveAsync(PermissionGrant entity, CancellationToken ct);

    /// <summary>
    /// Bir kullanıcı VEYA departman için izin satırlarını TOPLU REPLACE eder
    /// (admin UI "Kaydet" akışı — eski satırlar silinip yeni liste yazılır).
    /// </summary>
    Task BulkReplaceForOwnerAsync(
        int? userId, int? departmentId,
        IReadOnlyList<PermissionGrant> entities, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Bir kullanıcının tüm izin satırlarını sil (kullanıcı silindiğinde CASCADE çalışır ama programatik kullanım için).</summary>
    Task DeleteByUserAsync(int userId, CancellationToken ct);

    /// <summary>Bir departmanın tüm izin satırlarını sil.</summary>
    Task DeleteByDepartmentAsync(int departmentId, CancellationToken ct);
}

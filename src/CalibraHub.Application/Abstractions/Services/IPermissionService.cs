using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-06-06 — Yetkilendirme çekirdek servisi.
///
/// **Resolution priority (CheckAsync):**
///   1) Kullanıcı SystemAdmin → true (shortcut, hiç DB sorgusu yok)
///   2) UserPermission(UserId=u) varsa → IsGranted değeri (kullanıcı override yüksek öncelik)
///   3) UserPermission(DepartmentId=u.DepartmentId) varsa → IsGranted değeri
///   4) Default → false (deny)
///
/// EDIT_OWN / DELETE_OWN için ek koşul: recordOwnerId == userId şartı sağlanmalı.
/// Bu kontrolü çağıran (controller) yapmalı — service sadece izin "var/yok" cevabı verir.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Bir kullanıcının belirli bir (FormCode, ActionCode) için yetkili olup olmadığını döner.
    /// EDIT_OWN/DELETE_OWN gibi sahip kontrolü gereken action'lar için caller önce CheckAsync
    /// ile izni doğrular, sonra record.CreatedById == userId olduğunu kontrol eder.
    /// </summary>
    Task<bool> CheckAsync(int userId, UserRole role, int? departmentId,
        string formCode, string actionCode, CancellationToken ct);

    /// <summary>
    /// Birden çok action'dan EN AZ BİRİ izinli mi? Örn. update endpoint'i için
    /// EDIT_OWN veya EDIT_ALL yetkilerinden biri yeterli olabilir.
    /// </summary>
    Task<bool> CheckAnyAsync(int userId, UserRole role, int? departmentId,
        string formCode, IReadOnlyList<string> actionCodes, CancellationToken ct);

    /// <summary>
    /// Kullanıcının tüm efektif izinlerini matrisi olarak döner (admin UI "Override" tabı için).
    /// Her PermissionDef için (Source: USER/DEPARTMENT/DEFAULT, IsAllowed: true/false) çifti.
    /// </summary>
    Task<IReadOnlyList<EffectivePermissionDto>> GetEffectivePermissionsAsync(
        int userId, UserRole role, int? departmentId, CancellationToken ct);

    /// <summary>
    /// Bir departmanın tanımlı izin satırlarını matrisi olarak döner (admin UI "Departman → İzinler" tabı için).
    /// Her PermissionDef için (Source: DEPARTMENT/DEFAULT, IsAllowed: true/false).
    /// </summary>
    Task<IReadOnlyList<EffectivePermissionDto>> GetDepartmentPermissionsAsync(
        int departmentId, CancellationToken ct);

    /// <summary>
    /// Admin izinleri toplu kaydetti — ilgili cache satırını temizle.
    /// </summary>
    void InvalidateCache(int? userId = null, int? departmentId = null);

    /// <summary>
    /// PermissionDef katalogu değişince (widget toggle) anında yansısın diye defs cache'ini temizle.
    /// </summary>
    void InvalidateDefsCache();
}

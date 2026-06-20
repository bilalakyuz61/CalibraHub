using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-05-26 — "Onayda Bekleyenler" ekran servisi.
/// Yetki scope'unu cozumler, repository'den scope'lanmis liste ceker,
/// belge turune gore gruplar.
/// </summary>
public interface IPendingApprovalService
{
    /// <summary>Belge turune gore gruplandirilmis sayim (sol panel).</summary>
    Task<IReadOnlyList<PendingApprovalGroupDto>> GetGroupsAsync(string scope, CancellationToken ct);

    /// <summary>Belge turune gore liste (orta panel). docTypeId = null → tum turler.</summary>
    Task<IReadOnlyList<PendingApprovalItemDto>> GetListAsync(string scope, int? documentTypeId, CancellationToken ct);

    /// <summary>Tek instance'in detay (modal). Yetki kontrolu burada da yapilir.</summary>
    Task<PendingApprovalDetailDto?> GetDetailAsync(int instanceId, string scope, CancellationToken ct);

    /// <summary>Mevcut kullanicinin secebilecegi scope'lar (yetkiye gore).</summary>
    Task<IReadOnlyList<string>> GetAvailableScopesAsync(CancellationToken ct);

    /// <summary>
    /// Verilen view adinin kolon meta bilgisini doner.
    /// viewName'in ApprovalFlow.ExtraColumnsView olarak kayitli oldugu dogrulanmalidir (controller).
    /// </summary>
    Task<IReadOnlyList<ExtraColumnMetaDto>> GetViewColumnMetaAsync(string viewName, CancellationToken ct);

    /// <summary>
    /// Verilen view'dan instanceId bazinda satir degerlerini ceker.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>> GetViewRowDataAsync(
        string viewName, IReadOnlyCollection<int> instanceIds, CancellationToken ct);
}

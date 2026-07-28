using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Kalite muayene iş servisi: plan CRUD + muayene kaydı (Document numara + companion + native
/// satırlar) + tolerans karşı otomatik uygun/uygunsuz + Verdict + Disposition (yalnız karar kaydı).
/// </summary>
public interface IQualityService
{
    // ── Plan ──
    Task<IReadOnlyCollection<QualityInspectionPlanListItem>> ListPlansAsync(string? search, byte? inspectionType, CancellationToken ct);
    Task<QualityInspectionPlanDetail?> GetPlanAsync(int planId, CancellationToken ct);
    Task<(bool Ok, string? Error, int Id)> SavePlanAsync(SaveQualityInspectionPlanRequest request, int? userId, CancellationToken ct);
    Task<(bool Ok, string? Error)> DeletePlanAsync(int planId, int? userId, CancellationToken ct);
    Task<QualityPlanLookup?> FindPlanForItemAsync(int itemId, byte inspectionType, CancellationToken ct);

    // ── Muayene ──
    Task<IReadOnlyCollection<QualityInspectionListItem>> ListInspectionsAsync(string? search, byte? status, byte? verdict, CancellationToken ct);
    Task<QualityInspectionDetail?> GetInspectionAsync(int documentId, CancellationToken ct);
    /// <summary>Kaydeder; satırlarda tolerans karşı Result + genel Verdict otomatik hesaplanır. DocumentId döner.</summary>
    Task<(bool Ok, string? Error, int DocumentId)> SaveInspectionAsync(SaveQualityInspectionRequest request, int? userId, CancellationToken ct);
    Task<(bool Ok, string? Error)> ChangeInspectionStatusAsync(ChangeInspectionStatusRequest request, int? userId, CancellationToken ct);
    Task<(bool Ok, string? Error)> SetInspectionDispositionAsync(SetInspectionDispositionRequest request, int? userId, CancellationToken ct);
    Task<(bool Ok, string? Error)> DeleteInspectionAsync(int documentId, int? userId, CancellationToken ct);

    // ── Lookup ──
    Task<IReadOnlyCollection<QualityDefectCodeOption>> GetDefectCodeOptionsAsync(CancellationToken ct);
}

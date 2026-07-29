using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Kalite muayene planı + muayene kaydı veri erişimi. Muayene kaydının Document shell'i
/// IDocumentRepository ile yazılır; bu repo companion (QualityInspection) + satırları + planı yönetir.
/// Stok repo'suna hiç referans yok.
/// </summary>
public interface IQualityRepository
{
    // ── Muayene Planı ─────────────────────────────────────────────
    Task<IReadOnlyCollection<QualityInspectionPlanListItem>> ListPlansAsync(string? search, byte? inspectionType, CancellationToken ct);
    Task<QualityInspectionPlanDetail?> GetPlanAsync(int planId, CancellationToken ct);
    Task<int> UpsertPlanAsync(QualityInspectionPlan plan, IReadOnlyList<QualityInspectionPlanLine> lines, CancellationToken ct);
    Task SoftDeletePlanAsync(int planId, int? userId, CancellationToken ct);
    /// <summary>Malzeme + tip için aktif plan (muayene açılışında satır türetme). Yoksa null.</summary>
    Task<QualityPlanLookup?> FindPlanForItemAsync(int itemId, byte inspectionType, CancellationToken ct);

    // ── Muayene Kaydı (companion + satırlar) ──────────────────────
    Task<IReadOnlyCollection<QualityInspectionListItem>> ListInspectionsAsync(string? search, byte? status, byte? verdict, CancellationToken ct);
    Task<QualityInspection?> GetInspectionByDocumentIdAsync(int documentId, CancellationToken ct);
    Task<QualityInspectionDetail?> GetInspectionDetailAsync(int documentId, CancellationToken ct);
    Task UpsertInspectionAsync(QualityInspection inspection, IReadOnlyList<QualityInspectionLine> lines, CancellationToken ct);
    /// <summary>Yalnız companion durum/karar alanlarını günceller (ChangeStatus / SetDisposition).</summary>
    Task UpdateInspectionStateAsync(int documentId, byte status, byte? verdict, byte? disposition, int? userId, CancellationToken ct);

    // ── Lookup ────────────────────────────────────────────────────
    Task<IReadOnlyCollection<QualityDefectCodeOption>> GetDefectCodeOptionsAsync(CancellationToken ct);
    /// <summary>Plan formu malzeme grubu seçici — CardGroup (CardType=1), hiyerarşi etiketiyle.</summary>
    Task<IReadOnlyCollection<MaterialGroupLookupItem>> ListMaterialGroupsAsync(CancellationToken ct);
}

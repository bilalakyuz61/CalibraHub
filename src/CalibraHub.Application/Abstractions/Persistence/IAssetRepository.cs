using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Varlık (Asset) + Varlık geçmişi (AssetEvent) veri erişimi. Per-company DB;
/// CompanyId her sorguda filtrelenir (SqlServerConnectionFactory.ResolveCurrentCompanyId).
/// </summary>
public interface IAssetRepository
{
    // ── Asset ─────────────────────────────────────────────────────
    Task<IReadOnlyCollection<Asset>> GetAssetsAsync(CancellationToken cancellationToken);
    Task<Asset?> GetAssetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Asset?> GetAssetByMachineIdAsync(int machineId, CancellationToken cancellationToken);
    Task<int> AddAssetAsync(Asset asset, CancellationToken cancellationToken);
    Task UpdateAssetAsync(Asset asset, CancellationToken cancellationToken);
    /// <summary>Varlığı ve bağlı tüm geçmiş kayıtlarını siler (transaction).</summary>
    Task DeleteAssetAsync(int id, CancellationToken cancellationToken);

    // ── AssetEvent (geçmiş) ───────────────────────────────────────
    Task<IReadOnlyCollection<AssetEvent>> GetEventsByAssetAsync(int assetId, CancellationToken cancellationToken);
    Task<AssetEvent?> GetEventByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddEventAsync(AssetEvent assetEvent, CancellationToken cancellationToken);
    Task DeleteEventAsync(int id, CancellationToken cancellationToken);

    // ── Zimmet hareketi (AssetAssignment) ─────────────────────────
    Task<IReadOnlyCollection<AssetAssignment>> GetAssignmentsByAssetAsync(int assetId, CancellationToken cancellationToken);
    /// <summary>Tüm varlıkların zimmet hareketleri (rapor için), AssignDate azalan.</summary>
    Task<IReadOnlyCollection<AssetAssignment>> GetAllAssignmentsAsync(CancellationToken cancellationToken);
    Task<AssetAssignment?> GetActiveAssignmentAsync(int assetId, CancellationToken cancellationToken);
    Task<AssetAssignment?> GetAssignmentByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAssignmentAsync(AssetAssignment assignment, CancellationToken cancellationToken);
    /// <summary>Aktif zimmeti kapatır (ReturnDate + ReturnNote set eder).</summary>
    Task CloseAssignmentAsync(int assignmentId, DateTime returnDate, string? returnNote, int? userId, CancellationToken cancellationToken);

    // ── Hatırlatma (worker) ───────────────────────────────────────
    /// <summary>
    /// Bakım veya kalibrasyon tarihi <paramref name="threshold"/> tarihine kadar gelmiş
    /// ve ilgili tarih için henüz hatırlatma gönderilmemiş aktif varlıkları döner.
    /// </summary>
    Task<IReadOnlyCollection<Asset>> GetAssetsWithDueRemindersAsync(DateTime threshold, CancellationToken cancellationToken);
    Task MarkMaintenanceRemindedAsync(int assetId, DateTime remindedFor, CancellationToken cancellationToken);
    Task MarkCalibrationRemindedAsync(int assetId, DateTime remindedFor, CancellationToken cancellationToken);
}

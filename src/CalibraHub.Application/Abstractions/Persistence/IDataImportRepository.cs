using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Veritabanı üzerinden içe aktarım işlerinin ve çalıştırma loglarının persistence'ı.
/// Per-company şirket DB'sinde izole.
/// </summary>
public interface IDataImportRepository
{
    Task<IReadOnlyList<DataImportJob>> ListJobsAsync(bool includeInactive, CancellationToken ct);

    /// <summary>İşi kolon eşlemeleriyle birlikte yükler.</summary>
    Task<DataImportJob?> GetJobAsync(int id, CancellationToken ct);

    Task<bool> JobNameExistsAsync(string name, int? excludeId, CancellationToken ct);

    /// <summary>İşi ve kolon eşlemelerini kaydeder (kolonlar tam değiştirilir). Id döner.</summary>
    Task<int> SaveJobAsync(DataImportJob job, CancellationToken ct);

    Task DeleteJobAsync(int id, CancellationToken ct);

    Task<bool> ToggleJobActiveAsync(int id, CancellationToken ct);

    /// <summary>Çalıştırma kaydını açar (başlangıç) ve Id döner.</summary>
    Task<long> InsertRunAsync(DataImportRun run, CancellationToken ct);

    /// <summary>Çalıştırma kaydını sonuçlarla kapatır.</summary>
    Task UpdateRunAsync(DataImportRun run, CancellationToken ct);

    Task<IReadOnlyList<DataImportRun>> ListRunsAsync(int? jobId, int take, CancellationToken ct);
}

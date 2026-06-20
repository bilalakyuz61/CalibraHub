using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IReportSourceRepository
{
    Task<IReadOnlyList<ReportSourceDto>> GetAllActiveAsync(CancellationToken ct);
    Task<ReportSourceDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveReportSourceRequest req, string? user, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Materialize (dosya cache) sonrası son güncelleme zamanı + satır sayısını yazar.</summary>
    Task UpdateMaterializedAsync(int id, int rowCount, CancellationToken ct);
}

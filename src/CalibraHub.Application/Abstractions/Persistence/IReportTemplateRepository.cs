using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IReportTemplateRepository
{
    Task<IReadOnlyCollection<ReportTemplate>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ReportTemplate>> GetByDocumentTypeIdAsync(int documentTypeId, CancellationToken cancellationToken);
    Task<ReportTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<ReportTemplate?> GetDefaultByDocumentTypeIdAsync(int documentTypeId, CancellationToken cancellationToken);

    /// <summary>INSERT veya UPDATE. Yeni Id'yi doner (IDENTITY).</summary>
    Task<int> SaveAsync(ReportTemplate entity, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}

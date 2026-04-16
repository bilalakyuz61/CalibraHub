using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IReportTemplateRepository
{
    Task<IReadOnlyCollection<ReportTemplate>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ReportTemplate>> GetByDocumentTypeIdAsync(Guid documentTypeId, CancellationToken cancellationToken);
    Task<ReportTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ReportTemplate?> GetDefaultByDocumentTypeIdAsync(Guid documentTypeId, CancellationToken cancellationToken);
    Task SaveAsync(ReportTemplate entity, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

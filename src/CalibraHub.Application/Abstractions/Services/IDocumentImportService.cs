using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentImportService
{
    Task<ImportResultDto> ImportFromActiveIntegratorsAsync(CancellationToken cancellationToken);
    Task<ImportResultDto> ImportFromActiveIntegratorsAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken);
}

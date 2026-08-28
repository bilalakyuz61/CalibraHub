using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentImportService
{
    Task<ImportResultDto> ImportFromActiveIntegratorsAsync(CancellationToken cancellationToken);
    Task<ImportResultDto> ImportFromActiveIntegratorsAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken);

    /// <summary>
    /// CEVRIMDISI (ERP) kaynagindan ice aktarim. Sirket parametresi Offline DEGILSE hicbir sey
    /// yapmaz ve bos sonuc doner — cagiran taraf (worker) her turda guvenle cagirabilir.
    /// </summary>
    Task<ImportResultDto> ImportFromOfflineSourceAsync(CancellationToken cancellationToken);
}

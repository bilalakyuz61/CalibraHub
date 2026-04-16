using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// SQL View tabanli jenerik Rehber (Lookup) servis katmani.
/// Controller bu interface'i cagirir; repository hatalarini (ArgumentException)
/// pass-through eder, JSON parse + normalize isini yapar.
/// </summary>
public interface IGuideService
{
    Task<IReadOnlyCollection<GuideCatalogItemDto>> GetCatalogAsync(CancellationToken ct);
    Task<GuideSchemaDto?> GetSchemaAsync(string guideCode, CancellationToken ct);

    Task<GuideSearchResultDto?> SearchAsync(
        string guideCode,
        string? search,
        int page,
        int pageSize,
        string? sortColumn,
        string? sortDirection,
        CancellationToken ct,
        IReadOnlyCollection<GuideConstraintDto>? constraints = null);

    Task<GuideResolveDto?> ResolveAsync(string guideCode, string value, CancellationToken ct);
}

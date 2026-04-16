using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services;

/// <summary>
/// Rehber (Lookup) servis katmani — ince bir tercuman.
///
///   - GuideCode'u normalize eder (upper + trim).
///   - GridColumnsJson string'ini string[]'e parse eder.
///   - Repository'yi cagirir, dogrudan DTO'lari doner.
///   - ArgumentException'lari controller'a pass-through eder (400 BadRequest).
/// </summary>
public sealed class GuideService : IGuideService
{
    private readonly IGuideRepository _repository;

    public GuideService(IGuideRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyCollection<GuideCatalogItemDto>> GetCatalogAsync(CancellationToken ct)
    {
        var guides = await _repository.GetAllAsync(ct);
        return guides
            .Select(g => new GuideCatalogItemDto(
                Id: g.Id,
                GuideCode: g.GuideCode,
                GuideLabel: g.GuideLabel,
                ViewName: g.ViewName,
                ValueColumn: g.ValueColumn,
                DisplayColumn: g.DisplayColumn,
                DefaultSortColumn: g.DefaultSortColumn,
                Columns: ParseColumns(g.GridColumnsJson)))
            .ToArray();
    }

    public async Task<GuideSchemaDto?> GetSchemaAsync(string guideCode, CancellationToken ct)
    {
        var guide = await _repository.GetByCodeAsync(Normalize(guideCode), ct);
        if (guide == null) return null;
        return new GuideSchemaDto(
            GuideCode: guide.GuideCode,
            GuideLabel: guide.GuideLabel,
            ValueColumn: guide.ValueColumn,
            DisplayColumn: guide.DisplayColumn,
            Columns: ParseColumns(guide.GridColumnsJson),
            DefaultSortColumn: guide.DefaultSortColumn);
    }

    public async Task<GuideSearchResultDto?> SearchAsync(
        string guideCode, string? search, int page, int pageSize,
        string? sortColumn, string? sortDirection, CancellationToken ct,
        IReadOnlyCollection<GuideConstraintDto>? constraints = null)
    {
        var guide = await _repository.GetByCodeAsync(Normalize(guideCode), ct);
        if (guide == null) return null;
        return await _repository.SearchAsync(guide, search, page, pageSize, sortColumn, sortDirection, ct, constraints);
    }

    public async Task<GuideResolveDto?> ResolveAsync(string guideCode, string value, CancellationToken ct)
    {
        var guide = await _repository.GetByCodeAsync(Normalize(guideCode), ct);
        if (guide == null) return null;
        return await _repository.ResolveAsync(guide, value, ct);
    }

    private static string Normalize(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();

    private static IReadOnlyCollection<string> ParseColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(json);
            return arr ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}

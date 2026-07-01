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

    public async Task<GuideSchemaDto?> GetSchemaAsync(string guideCode, CancellationToken ct)
    {
        var guide = await _repository.GetByCodeAsync(Normalize(guideCode), ct);
        if (guide == null) return null;

        // Defensive: GuideMas.GridColumnsJson view ile drift edebilir (stale seed,
        // legacy duplicate row, view rename/drop). View'in gercek kolonlarini al ve
        // GridColumnsJson ile kesisimini doner — boylece modal header'lari view'da
        // var olmayan kolonlari hic gostermez (search'teki defansif filtre ile uyumlu).
        var declaredCols = ParseColumns(guide.GridColumnsJson);
        IReadOnlyCollection<string> actualCols;
        try
        {
            actualCols = await _repository.GetViewColumnsAsync(guide.ViewName, ct);
        }
        catch
        {
            // View okunamazsa GridColumnsJson'i oldugu gibi don (regression olmasin)
            actualCols = Array.Empty<string>();
        }
        var resolvedCols = actualCols.Count == 0
            ? declaredCols
            : declaredCols
                .Where(c => actualCols.Any(ac => string.Equals(ac, c, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        // Kolon SQL veri tiplerini cek — frontend Alan Ayarlari modal'inda kolon
        // yaninda kucuk chip olarak gosterir. Hata olursa bos donduriur (regression olmasin).
        IReadOnlyDictionary<string, string>? columnTypes = null;
        try
        {
            columnTypes = await _repository.GetViewColumnTypesAsync(guide.ViewName, ct);
        }
        catch
        {
            columnTypes = null;
        }

        return new GuideSchemaDto(
            GuideCode: guide.GuideCode,
            GuideLabel: guide.GuideLabel,
            ValueColumn: guide.ValueColumn,
            DisplayColumn: guide.DisplayColumn,
            Columns: resolvedCols,
            DefaultSortColumn: guide.DefaultSortColumn,
            ColumnTypes: columnTypes);
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

    public async Task<IReadOnlyCollection<string>?> GetDistinctValuesAsync(
        string guideCode, string column, string? search, CancellationToken ct,
        IReadOnlyCollection<GuideConstraintDto>? constraints = null)
    {
        var guide = await _repository.GetByCodeAsync(Normalize(guideCode), ct);
        if (guide == null) return null;
        return await _repository.GetDistinctValuesAsync(guide, column, search, ct, constraints);
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

using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// SQL View tabanli jenerik Rehber (Lookup) servis katmani.
/// Controller bu interface'i cagirir; repository hatalarini (ArgumentException)
/// pass-through eder, JSON parse + normalize isini yapar.
/// </summary>
public interface IGuideService
{
    // PR 3: GetCatalogAsync kaldirildi — UI artik /api/guides/views uzerinden fiziksel
    // view listesini kullaniyor (GuideMas indirection'i gereksiz).
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

    /// <summary>
    /// Bir kolonun DISTINCT degerlerini doner — runtime'da rehber popup'inda
    /// distinct filtre cipleri icin. Kolon guide.GridColumnsJson icinde olmalidir
    /// (allowlist). search non-empty ise sunucu-tarafi LIKE filtresi uygulanir
    /// (alfabetik kuyrukta gizli kalmis degerlere ulasmak icin). null doner: rehber
    /// bulunamadi.
    /// </summary>
    Task<IReadOnlyCollection<string>?> GetDistinctValuesAsync(
        string guideCode,
        string column,
        string? search,
        CancellationToken ct);

    /// <summary>
    /// Rehber bazli varsayilan WHERE filter fragment'ini guncelle.
    /// Bu rehberin kullanildigi tum form alanlarinda runtime'da otomatik AND ile uygulanir.
    /// filterJson NULL veya bos ise filtre kaldirilir. Donus: etkilenen kayit sayisi.
    /// </summary>
    Task<int> SetDefaultFilterAsync(string guideCode, string? filterJson, CancellationToken ct);
}

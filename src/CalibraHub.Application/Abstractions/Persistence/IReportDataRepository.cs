using System.Data;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IReportDataRepository
{
    /// <summary>
    /// Filtre kolonu otomatik tespit edilir (BelgeId > id > Id > ID).
    /// Legacy cagiricilar icin — yeni yerler keyColumn parametreli overload'u kullanmali.
    /// </summary>
    Task<DataTable> GetReportDataAsync(string sqlViewName, int recordId, CancellationToken cancellationToken);

    /// <summary>
    /// Belirli bir key column ile filtrele. Template'in kendi KeyColumn'u varsa bu overload kullanilir.
    /// keyColumn NULL/bos ise auto-detect'e duser.
    /// </summary>
    Task<DataTable> GetReportDataAsync(string sqlViewName, int recordId, string? keyColumn, CancellationToken cancellationToken);

    /// <summary>
    /// Key column + opsiyonel ORDER BY ile filtrele. orderColumn NULL/bos ise siralama
    /// uygulanmaz. orderDirection NULL ise ASC. Hem orderColumn hem direction
    /// regex ile validate edilerek SQL injection riski engellenir.
    /// </summary>
    Task<DataTable> GetReportDataAsync(string sqlViewName, int recordId, string? keyColumn, string? orderColumn, string? orderDirection, CancellationToken cancellationToken);

    Task<DataTable> GetReportDataAsync(string sqlViewName, CancellationToken cancellationToken);

    /// <summary>
    /// Per-company DB'deki "vw_" on ekli tum view adlarini siralanmis sekilde doner.
    /// Sablon tanimlama dialog'undaki combobox icin kullanilir.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableViewsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Belirli bir view'in kolon listesini doner (INFORMATION_SCHEMA).
    /// Create dialog'unda key column dropdown'u doldurmak icin.
    /// </summary>
    Task<IReadOnlyList<string>> GetViewColumnsAsync(string sqlViewName, CancellationToken cancellationToken);
}

using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Raporlama motoru tarafindan uretilen parametrize SQL'i VIEW uzerinde guvenle calistirir.
/// Application katmani SqlClient'i dogrudan cagirmaz; implementasyon Persistence katmanindadir.
/// </summary>
public interface IReportQueryExecutor
{
    /// <summary>
    /// Per-company schema adi (orn. "dbo"). SQL building sirasinda FROM [{schema}].[{view}] icin kullanilir.
    /// </summary>
    string CurrentSchema { get; }

    Task<ReportRawResult> ExecuteAsync(
        string sql,
        IReadOnlyList<ReportSqlParameter> parameters,
        CancellationToken ct);

    /// <summary>
    /// Streaming asenkron yurutme — CSV / JSON export gibi buyuk sonuclari bellekte tutmadan yazar.
    /// onRow false dondururse okuma durur. Caller toplam satir sayisini alir.
    /// </summary>
    Task<int> ExecuteStreamingAsync(
        string sql,
        IReadOnlyList<ReportSqlParameter> parameters,
        Func<IReadOnlyList<string>, CancellationToken, Task> onHeaders,
        Func<IReadOnlyList<object?>, CancellationToken, Task<bool>> onRow,
        CancellationToken ct);
}

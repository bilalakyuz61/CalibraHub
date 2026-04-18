using System.Data;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IReportDataRepository
{
    Task<DataTable> GetReportDataAsync(string sqlViewName, int recordId, CancellationToken cancellationToken);
    Task<DataTable> GetReportDataAsync(string sqlViewName, CancellationToken cancellationToken);
}

using System.Data;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed partial class SqlReportDataRepository : IReportDataRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlReportDataRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<DataTable> GetReportDataAsync(string sqlViewName, Guid recordId, CancellationToken cancellationToken)
    {
        ValidateViewName(sqlViewName);
        var dt = new DataTable("Data");
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM [{_schema}].[{sqlViewName}] WHERE [id] = @RecordId;";
        command.Parameters.Add(new SqlParameter("@RecordId", recordId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        dt.Load(reader);
        return dt;
    }

    public async Task<DataTable> GetReportDataAsync(string sqlViewName, CancellationToken cancellationToken)
    {
        ValidateViewName(sqlViewName);
        var dt = new DataTable("Data");
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM [{_schema}].[{sqlViewName}];";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        dt.Load(reader);
        return dt;
    }

    private static void ValidateViewName(string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName) || !SafeViewNameRegex().IsMatch(viewName))
            throw new ArgumentException($"Gecersiz SQL View adi: {viewName}");
    }

    [GeneratedRegex(@"^vw_[A-Za-z0-9_]{1,120}$")]
    private static partial Regex SafeViewNameRegex();
}

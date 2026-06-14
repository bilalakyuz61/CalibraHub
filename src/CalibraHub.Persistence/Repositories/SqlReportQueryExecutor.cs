using System.Data;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlReportQueryExecutor : IReportQueryExecutor
{
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlReportQueryExecutor(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        CurrentSchema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public string CurrentSchema { get; }

    public async Task<ReportRawResult> ExecuteAsync(
        string sql,
        IReadOnlyList<ReportSqlParameter> parameters,
        CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(ToSqlParameter(p));

        var columns = new List<string>();
        var rows = new List<IReadOnlyList<object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var colCount = reader.FieldCount;
        for (int i = 0; i < colCount; i++) columns.Add(reader.GetName(i));
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[colCount];
            for (int i = 0; i < colCount; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return new ReportRawResult(columns, rows);
    }

    public async Task<int> ExecuteStreamingAsync(
        string sql,
        IReadOnlyList<ReportSqlParameter> parameters,
        Func<IReadOnlyList<string>, CancellationToken, Task> onHeaders,
        Func<IReadOnlyList<object?>, CancellationToken, Task<bool>> onRow,
        CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(ToSqlParameter(p));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var colCount = reader.FieldCount;
        var headers = new string[colCount];
        for (int i = 0; i < colCount; i++) headers[i] = reader.GetName(i);
        await onHeaders(headers, ct);

        int rowCount = 0;
        var buffer = new object?[colCount];
        while (await reader.ReadAsync(ct))
        {
            for (int i = 0; i < colCount; i++) buffer[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            var keepGoing = await onRow(buffer, ct);
            rowCount++;
            if (!keepGoing) break;
        }
        return rowCount;
    }

    private static SqlParameter ToSqlParameter(ReportSqlParameter p)
    {
        var sp = new SqlParameter(p.Name, ResolveSqlDbType(p.DataType));
        sp.Value = p.Value ?? DBNull.Value;
        return sp;
    }

    private static SqlDbType ResolveSqlDbType(ReportDataType? dt) => dt switch
    {
        ReportDataType.String => SqlDbType.NVarChar,
        ReportDataType.Integer => SqlDbType.BigInt,
        ReportDataType.Decimal => SqlDbType.Decimal,
        ReportDataType.Date => SqlDbType.Date,
        ReportDataType.DateTime => SqlDbType.DateTime,
        ReportDataType.Boolean => SqlDbType.Bit,
        _ => SqlDbType.NVarChar
    };
}

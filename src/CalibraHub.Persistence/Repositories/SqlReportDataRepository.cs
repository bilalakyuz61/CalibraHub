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

    public Task<DataTable> GetReportDataAsync(string sqlViewName, int recordId, CancellationToken cancellationToken)
        => GetReportDataAsync(sqlViewName, recordId, null, null, null, cancellationToken);

    public Task<DataTable> GetReportDataAsync(
        string sqlViewName, int recordId, string? keyColumn, CancellationToken cancellationToken)
        => GetReportDataAsync(sqlViewName, recordId, keyColumn, null, null, cancellationToken);

    public async Task<DataTable> GetReportDataAsync(
        string sqlViewName, int recordId, string? keyColumn,
        string? orderColumn, string? orderDirection, CancellationToken cancellationToken)
    {
        ValidateViewName(sqlViewName);
        var dt = new DataTable("Belge");
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Oncelik: kullanici secimi (template.KeyColumn) → auto-detect (BelgeId > id > Id > ID)
        string filterColumn;
        if (!string.IsNullOrWhiteSpace(keyColumn)
            && System.Text.RegularExpressions.Regex.IsMatch(keyColumn, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            filterColumn = keyColumn;
        }
        else
        {
            filterColumn = await DetectFilterColumnAsync(connection, sqlViewName, cancellationToken);
        }

        // ORDER BY (opsiyonel) — kolon ve yon regex ile validate; SQL injection guvenli
        var orderClause = string.Empty;
        if (!string.IsNullOrWhiteSpace(orderColumn)
            && System.Text.RegularExpressions.Regex.IsMatch(orderColumn, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            var dir = (orderDirection ?? "ASC").Trim().ToUpperInvariant();
            if (dir != "ASC" && dir != "DESC") dir = "ASC";
            orderClause = $" ORDER BY [{orderColumn}] {dir}";
        }

        command.CommandText = $"SELECT * FROM [{_schema}].[{sqlViewName}] WHERE [{filterColumn}] = @RecordId{orderClause};";
        command.Parameters.Add(new SqlParameter("@RecordId", recordId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        dt.Load(reader);
        return dt;
    }

    /// <summary>View'daki "BelgeId" > "id" sirasiyla filtre kolonu arar.</summary>
    private async Task<string> DetectFilterColumnAsync(
        SqlConnection connection, string viewName, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @v
              AND COLUMN_NAME IN ('BelgeId', 'id', 'Id', 'ID');
            """;
        cmd.Parameters.Add(new SqlParameter("@v", viewName));
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) cols.Add(r.GetString(0));
        if (cols.Contains("BelgeId")) return "BelgeId";
        if (cols.Contains("id")) return "id";
        if (cols.Contains("Id")) return "Id";
        return "id";
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

    public async Task<IReadOnlyList<string>> GetAvailableViewsAsync(CancellationToken cancellationToken)
    {
        var list = new List<string>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.VIEWS
            WHERE TABLE_SCHEMA = @Schema
              AND TABLE_NAME LIKE 'vw[_]%'
            ORDER BY TABLE_NAME;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", _schema));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(r.GetString(0));
        }
        return list;
    }

    public async Task<IReadOnlyList<string>> GetViewColumnsAsync(string sqlViewName, CancellationToken cancellationToken)
    {
        ValidateViewName(sqlViewName);
        var list = new List<string>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @ViewName
            ORDER BY ORDINAL_POSITION;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema",   _schema));
        cmd.Parameters.Add(new SqlParameter("@ViewName", sqlViewName));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(r.GetString(0));
        }
        return list;
    }

    private static void ValidateViewName(string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName) || !SafeViewNameRegex().IsMatch(viewName))
            throw new ArgumentException($"Gecersiz SQL View adi: {viewName}");
    }

    [GeneratedRegex(@"^vw_[A-Za-z0-9_]{1,120}$")]
    private static partial Regex SafeViewNameRegex();
}

using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace CalibraHub.Infrastructure.Reporting;

public sealed class ReportQueryService : IReportQueryService
{
    private readonly IReportSourceRepository   _sources;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IMemoryCache              _cache;

    public ReportQueryService(
        IReportSourceRepository    sources,
        SqlServerConnectionFactory connectionFactory,
        IMemoryCache               cache)
    {
        _sources           = sources;
        _connectionFactory = connectionFactory;
        _cache             = cache;
    }

    public async Task<ReportQueryResult> QuerySourceAsync(int sourceId, CancellationToken ct)
    {
        var source = await _sources.GetByIdAsync(sourceId, ct)
            ?? throw new InvalidOperationException($"ReportSource #{sourceId} bulunamadı.");

        // Hibrit: Materialize açık → snapshot tablosundan (hızlı), kapalı → doğrudan SQL (canlı, bellek cache).
        if (source.Materialize)
        {
            if (!await SnapshotExistsAsync(sourceId, ct))
                await BuildSnapshotAsync(sourceId, source, ct);   // ilk istekte oluştur
            return await RunSqlAsync($"SELECT * FROM {SnapshotTable(sourceId)}", ct) with { FromCache = true };
        }

        var cacheKey = await SourceCacheKeyAsync(sourceId, ct);
        if (_cache.TryGetValue(cacheKey, out ReportQueryResult? cached))
            return cached! with { FromCache = true };

        var result = await RunSqlAsync(source.SqlQuery, ct);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(source.CacheTtlMinutes));
        return result;
    }

    public async Task<ReportQueryResult> QueryInlineAsync(string sql, int cacheTtlMinutes, CancellationToken ct)
    {
        var cacheKey = await InlineCacheKeyAsync(sql, ct);
        if (_cache.TryGetValue(cacheKey, out ReportQueryResult? cached))
            return cached! with { FromCache = true };

        var result = await RunSqlAsync(sql, ct);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(cacheTtlMinutes > 0 ? cacheTtlMinutes : 5));
        return result;
    }

    public async Task<int> MaterializeSourceAsync(int sourceId, CancellationToken ct)
    {
        var source = await _sources.GetByIdAsync(sourceId, ct)
            ?? throw new InvalidOperationException($"ReportSource #{sourceId} bulunamadı.");
        return await BuildSnapshotAsync(sourceId, source, ct);
    }

    public async Task InvalidateSourceAsync(int sourceId, CancellationToken ct)
        => _cache.Remove(await SourceCacheKeyAsync(sourceId, ct));

    // ── Snapshot tablosu (materialize) ────────────────────────────────────────

    private static string SnapshotTable(int sourceId) => $"[dbo].[ReportSnapshot_{sourceId}]";

    // Web yolu: HttpContext şirket bağlantısı + repo ile materialize.
    private async Task<int> BuildSnapshotAsync(int sourceId, ReportSourceDto source, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var rows = await BuildSnapshotOnConnectionAsync(conn, sourceId, source.SqlQuery, ct);
        await _sources.UpdateMaterializedAsync(sourceId, rows, ct);
        return rows;
    }

    // Worker/zamanlanmış yol: şirket DB'sini AÇIKÇA çöz (HttpContext yok).
    public async Task<int> MaterializeSourceForCompanyAsync(int companyId, int sourceId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionFactory.ResolveConnectionStringForCompany(companyId));
        await conn.OpenAsync(ct);

        string? sql;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT SqlQuery FROM dbo.ReportSource WHERE Id = @Id AND IsActive = 1;";
            cmd.Parameters.Add(new SqlParameter("@Id", sourceId));
            sql = (await cmd.ExecuteScalarAsync(ct)) as string;
        }
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException($"ReportSource #{sourceId} bulunamadı (company {companyId}).");

        var rows = await BuildSnapshotOnConnectionAsync(conn, sourceId, sql, ct);

        await using (var upd = conn.CreateCommand())
        {
            upd.CommandText = "UPDATE dbo.ReportSource SET LastMaterialized = SYSUTCDATETIME(), MaterializedRows = @R WHERE Id = @Id;";
            upd.Parameters.Add(new SqlParameter("@R", rows));
            upd.Parameters.Add(new SqlParameter("@Id", sourceId));
            await upd.ExecuteNonQueryAsync(ct);
        }
        return rows;
    }

    // Kaynağı çalıştır → DataTable → snapshot tablosunu DROP+CREATE → SqlBulkCopy ile doldur.
    private static async Task<int> BuildSnapshotOnConnectionAsync(SqlConnection conn, int sourceId, string sql, CancellationToken ct)
    {
        var dt = new DataTable();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText    = sql;
            cmd.CommandTimeout = 180;   // ağır sorgu
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            dt.Load(reader);
        }

        var table = SnapshotTable(sourceId);
        await using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = $"IF OBJECT_ID('dbo.ReportSnapshot_{sourceId}','U') IS NOT NULL DROP TABLE {table};\n{BuildCreateTable(table, dt)}";
            ddl.CommandTimeout = 60;
            await ddl.ExecuteNonQueryAsync(ct);
        }

        using (var bulk = new SqlBulkCopy(conn) { DestinationTableName = table, BulkCopyTimeout = 180 })
        {
            foreach (DataColumn c in dt.Columns)
                bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await bulk.WriteToServerAsync(dt, ct);
        }

        return dt.Rows.Count;
    }

    private async Task<bool> SnapshotExistsAsync(int sourceId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT CASE WHEN OBJECT_ID('dbo.ReportSnapshot_{sourceId}','U') IS NOT NULL THEN 1 ELSE 0 END;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    private static string BuildCreateTable(string table, DataTable dt)
    {
        var cols = dt.Columns
            .Cast<DataColumn>()
            .Select(c => $"[{c.ColumnName.Replace("]", "]]")}] {SqlType(c.DataType)} NULL");
        return $"CREATE TABLE {table} ({string.Join(", ", cols)});";
    }

    private static string SqlType(Type t) =>
          t == typeof(string)   ? "NVARCHAR(MAX)"
        : t == typeof(int)      ? "INT"
        : t == typeof(long)     ? "BIGINT"
        : t == typeof(short)    ? "SMALLINT"
        : t == typeof(byte)     ? "TINYINT"
        : t == typeof(bool)     ? "BIT"
        : t == typeof(decimal)  ? "DECIMAL(38, 10)"
        : t == typeof(double)   ? "FLOAT"
        : t == typeof(float)    ? "REAL"
        : t == typeof(DateTime) ? "DATETIME2"
        : t == typeof(Guid)     ? "UNIQUEIDENTIFIER"
        : t == typeof(byte[])   ? "VARBINARY(MAX)"
        :                         "NVARCHAR(MAX)";

    // ── SQL çalıştır ──────────────────────────────────────────────────────────

    private async Task<ReportQueryResult> RunSqlAsync(string sql, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText    = sql;
        cmd.CommandTimeout = 30;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);

        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null : ConvertValue(reader.GetValue(i));
            rows.Add(row);
        }

        sw.Stop();
        return new ReportQueryResult(columns, rows, rows.Count, false, sw.ElapsedMilliseconds);
    }

    private static object? ConvertValue(object val) => val switch
    {
        decimal d  => (double)d,
        float   f  => (double)f,
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss"),
        _           => val,
    };

    // ── Cache keys (per-company izolasyon: DB adı) ────────────────────────────

    private string? _dbKey;

    private async Task<string> GetDbKeyAsync(CancellationToken ct)
    {
        if (_dbKey is not null) return _dbKey;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        _dbKey = conn.Database;
        return _dbKey;
    }

    private async Task<string> SourceCacheKeyAsync(int sourceId, CancellationToken ct) =>
        $"rq:src:{await GetDbKeyAsync(ct)}:{sourceId}";

    private async Task<string> InlineCacheKeyAsync(string sql, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
        return $"rq:inline:{await GetDbKeyAsync(ct)}:{hash}";
    }
}

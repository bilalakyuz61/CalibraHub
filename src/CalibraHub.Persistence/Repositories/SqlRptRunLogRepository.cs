using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlRptRunLogRepository : IRptRunLogRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlRptRunLogRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var s = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
        _table = $"[{s}].[RptRunLog]";
    }

    public async Task<long> LogStartAsync(
        int? defId,
        int viewId,
        int userId,
        int? companyId,
        byte[] sqlHash,
        CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_table} ([DefId],[ViewId],[UserId],[CompanyId],[SqlHash])
            VALUES (@DefId,@ViewId,@UserId,@CompanyId,@SqlHash);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        cmd.Parameters.AddWithValue("@DefId", (object?)defId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ViewId", viewId);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
        cmd.Parameters.Add(new SqlParameter("@SqlHash", System.Data.SqlDbType.Binary, 32) { Value = sqlHash });
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null ? Convert.ToInt64(result) : 0;
    }

    public async Task LogEndAsync(long id, int durationMs, int rowCount, string? error, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [DurationMs] = @DurationMs,
                [RowCount]   = @RowCount,
                [Error]      = @Error
            WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@DurationMs", durationMs);
        cmd.Parameters.AddWithValue("@RowCount", rowCount);
        cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<RptRunLog>> GetRecentAsync(int? defId, int? userId, int top, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP (@Top) [Id],[DefId],[ViewId],[UserId],[CompanyId],[StartedAt],
                   [DurationMs],[RowCount],[Error],[SqlHash]
            FROM {_table}
            WHERE (@DefId IS NULL OR [DefId] = @DefId)
              AND (@UserId IS NULL OR [UserId] = @UserId)
            ORDER BY [Id] DESC;";
        cmd.Parameters.AddWithValue("@Top", top);
        cmd.Parameters.AddWithValue("@DefId", (object?)defId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);

        var list = new List<RptRunLog>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RptRunLog
            {
                Id = reader.GetInt64(0),
                DefId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                ViewId = reader.GetInt32(2),
                UserId = reader.GetInt32(3),
                CompanyId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                StartedAt = reader.GetDateTime(5),
                DurationMs = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                RowCount = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Error = reader.IsDBNull(8) ? null : reader.GetString(8),
                SqlHash = reader.IsDBNull(9) ? null : (byte[])reader.GetValue(9)
            });
        }
        return list;
    }
}

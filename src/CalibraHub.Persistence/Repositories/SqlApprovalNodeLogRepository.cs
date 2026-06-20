using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlApprovalNodeLogRepository : IApprovalNodeLogger
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _s;

    public SqlApprovalNodeLogRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _s = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task LogAsync(
        int instanceId, int flowId, int? nodeId,
        string? nodeType, string? nodeName,
        string eventType, string? detail, int? durationMs,
        CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        var sql = $"""
            INSERT INTO [{_s}].[ApprovalFlowRunLog]
                ([InstanceId],[FlowId],[NodeId],[NodeType],[NodeName],[Event],[Detail],[DurationMs])
            VALUES (@InstanceId,@FlowId,@NodeId,@NodeType,@NodeName,@Event,@Detail,@DurationMs)
            """;
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@InstanceId", instanceId);
        cmd.Parameters.AddWithValue("@FlowId",     flowId);
        cmd.Parameters.AddWithValue("@NodeId",     (object?)nodeId     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NodeType",   (object?)nodeType   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NodeName",   (object?)nodeName   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Event",      eventType);
        cmd.Parameters.AddWithValue("@Detail",     (object?)detail     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationMs", (object?)durationMs ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ApprovalNodeLogDto>> GetLogsAsync(int instanceId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        var sql = $"""
            SELECT [Id],[NodeId],[NodeType],[NodeName],[Event],[Detail],[DurationMs],[Created]
            FROM [{_s}].[ApprovalFlowRunLog]
            WHERE [InstanceId] = @InstanceId
            ORDER BY [Created],[Id]
            """;
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@InstanceId", instanceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ApprovalNodeLogDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ApprovalNodeLogDto(
                Id:         reader.GetInt64(0),
                NodeId:     reader.IsDBNull(1) ? null : reader.GetInt32(1),
                NodeType:   reader.IsDBNull(2) ? null : reader.GetString(2),
                NodeName:   reader.IsDBNull(3) ? null : reader.GetString(3),
                Event:      reader.GetString(4),
                Detail:     reader.IsDBNull(5) ? null : reader.GetString(5),
                DurationMs: reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Created:    reader.GetDateTime(7)));
        }
        return list;
    }
}

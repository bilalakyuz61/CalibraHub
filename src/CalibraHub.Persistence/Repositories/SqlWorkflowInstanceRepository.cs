using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWorkflowInstanceRepository(SqlServerConnectionFactory connectionFactory)
    : IWorkflowInstanceRepository
{
    public async Task<WorkflowInstance?> GetByIdAsync(int instanceId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT wi.Id, wi.DefinitionId, wi.SourceType, wi.SourceId, wi.Status,
                   wi.StartedAt, wi.StartedBy, wi.CompletedAt, wi.ContextJson,
                   wi.CreatedById, wi.Created, wi.UpdatedById, wi.Updated,
                   win.Id, win.InstanceId, win.NodeId, win.Status,
                   win.AssignedUserId, win.EnteredAt, win.CompletedAt,
                   win.Action, win.ActionBy, win.Note, win.CreatedById, win.Created
            FROM [WorkflowInstance] wi
            LEFT JOIN [WorkflowInstanceNode] win ON win.InstanceId = wi.Id
            WHERE wi.Id = @Id
            ORDER BY win.Id;
            """;
        cmd.Parameters.AddWithValue("@Id", instanceId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        WorkflowInstance? instance = null;
        while (await reader.ReadAsync(ct))
        {
            instance ??= ReadInstance(reader);
            if (!reader.IsDBNull(12))
                instance.AddNode(ReadInstanceNode(reader, 12));
        }
        return instance;
    }

    public async Task<WorkflowInstance?> GetActiveBySourceAsync(string sourceType, int sourceId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 Id, DefinitionId, SourceType, SourceId, Status,
                         StartedAt, StartedBy, CompletedAt, ContextJson,
                         CreatedById, Created, UpdatedById, Updated
            FROM [WorkflowInstance]
            WHERE SourceType = @SourceType AND SourceId = @SourceId AND Status IN ('Pending','Active')
            ORDER BY Id DESC;
            """;
        cmd.Parameters.AddWithValue("@SourceType", sourceType);
        cmd.Parameters.AddWithValue("@SourceId",   sourceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadInstance(reader) : null;
    }

    public async Task<int> CreateAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO [WorkflowInstance]
                (DefinitionId, SourceType, SourceId, Status, StartedAt, StartedBy, CompletedAt, ContextJson, CreatedById, Created)
            OUTPUT INSERTED.Id
            VALUES (@DefinitionId, @SourceType, @SourceId, @Status, @StartedAt, @StartedBy, @CompletedAt, @ContextJson, @CreatedById, SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@DefinitionId", instance.DefinitionId);
        cmd.Parameters.AddWithValue("@SourceType",   instance.SourceType);
        cmd.Parameters.AddWithValue("@SourceId",     instance.SourceId);
        cmd.Parameters.AddWithValue("@Status",        instance.Status.ToString());
        cmd.Parameters.AddWithValue("@StartedAt",     instance.StartedAt == DateTime.MinValue ? (object)DBNull.Value : instance.StartedAt);
        cmd.Parameters.AddWithValue("@StartedBy",     (object?)instance.StartedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompletedAt",   (object?)instance.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ContextJson",   (object?)instance.ContextJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedById",   (object?)instance.CreatedById ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateStatusAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE [WorkflowInstance]
            SET Status = @Status, CompletedAt = @CompletedAt, Updated = SYSUTCDATETIME()
            WHERE Id = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id",          instance.Id);
        cmd.Parameters.AddWithValue("@Status",       instance.Status.ToString());
        cmd.Parameters.AddWithValue("@CompletedAt",  (object?)instance.CompletedAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<WorkflowInstanceNode?> GetNodeByIdAsync(int instanceNodeId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, InstanceId, NodeId, Status, AssignedUserId,
                   EnteredAt, CompletedAt, Action, ActionBy, Note, CreatedById, Created
            FROM [WorkflowInstanceNode] WHERE Id = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id", instanceNodeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadInstanceNode(reader, 0) : null;
    }

    public async Task<int> CreateInstanceNodeAsync(WorkflowInstanceNode node, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO [WorkflowInstanceNode]
                (InstanceId, NodeId, Status, AssignedUserId, EnteredAt, CompletedAt,
                 Action, ActionBy, Note, CreatedById, Created)
            OUTPUT INSERTED.Id
            VALUES (@InstanceId, @NodeId, @Status, @AssignedUserId, @EnteredAt, @CompletedAt,
                    @Action, @ActionBy, @Note, @CreatedById, SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@InstanceId",     node.InstanceId);
        cmd.Parameters.AddWithValue("@NodeId",         node.NodeId);
        cmd.Parameters.AddWithValue("@Status",         node.Status.ToString());
        cmd.Parameters.AddWithValue("@AssignedUserId", (object?)node.AssignedUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@EnteredAt",      (object?)node.EnteredAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompletedAt",    (object?)node.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Action",         (object?)node.Action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ActionBy",       (object?)node.ActionBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Note",           (object?)node.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedById",    (object?)node.CreatedById ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateInstanceNodeAsync(WorkflowInstanceNode node, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE [WorkflowInstanceNode]
            SET Status = @Status, AssignedUserId = @AssignedUserId,
                EnteredAt = @EnteredAt, CompletedAt = @CompletedAt,
                Action = @Action, ActionBy = @ActionBy, Note = @Note,
                Updated = SYSUTCDATETIME()
            WHERE Id = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id",             node.Id);
        cmd.Parameters.AddWithValue("@Status",         node.Status.ToString());
        cmd.Parameters.AddWithValue("@AssignedUserId", (object?)node.AssignedUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@EnteredAt",      (object?)node.EnteredAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompletedAt",    (object?)node.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Action",         (object?)node.Action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ActionBy",       (object?)node.ActionBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Note",           (object?)node.Note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<WorkflowInstanceNode>> GetActiveNodesByInstanceAsync(
        int instanceId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, InstanceId, NodeId, Status, AssignedUserId,
                   EnteredAt, CompletedAt, Action, ActionBy, Note, CreatedById, Created
            FROM [WorkflowInstanceNode]
            WHERE InstanceId = @InstanceId AND Status IN ('Active','Pending');
            """;
        cmd.Parameters.AddWithValue("@InstanceId", instanceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<WorkflowInstanceNode>();
        while (await reader.ReadAsync(ct))
            result.Add(ReadInstanceNode(reader, 0));
        return result;
    }

    public async Task<int> CountCompletedTokensAtNodeAsync(int instanceId, int nodeId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM [WorkflowInstanceNode]
            WHERE InstanceId = @InstanceId AND NodeId = @NodeId AND Status IN ('Completed','Skipped');
            """;
        cmd.Parameters.AddWithValue("@InstanceId", instanceId);
        cmd.Parameters.AddWithValue("@NodeId",     nodeId);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<(WorkflowInstanceNode Node, WorkflowInstance Instance)>> GetPendingForUserAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT win.Id, win.InstanceId, win.NodeId, win.Status, win.AssignedUserId,
                   win.EnteredAt, win.CompletedAt, win.Action, win.ActionBy, win.Note,
                   win.CreatedById, win.Created,
                   wi.Id, wi.DefinitionId, wi.SourceType, wi.SourceId, wi.Status,
                   wi.StartedAt, wi.StartedBy, wi.CompletedAt, wi.ContextJson,
                   wi.CreatedById, wi.Created, wi.UpdatedById, wi.Updated
            FROM [WorkflowInstanceNode] win
            INNER JOIN [WorkflowInstance] wi ON wi.Id = win.InstanceId
            WHERE win.AssignedUserId = @UserId AND win.Status = 'Active' AND wi.Status = 'Active'
            ORDER BY win.EnteredAt DESC;
            """;
        cmd.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<(WorkflowInstanceNode, WorkflowInstance)>();
        while (await reader.ReadAsync(ct))
        {
            var node     = ReadInstanceNode(reader, 0);
            var instance = ReadInstance(reader, 13);
            result.Add((node, instance));
        }
        return result;
    }

    public async Task<IReadOnlyList<WorkflowInstanceNode>> GetTimedOutActiveNodesAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT win.Id, win.InstanceId, win.NodeId, win.Status, win.AssignedUserId,
                   win.EnteredAt, win.CompletedAt, win.Action, win.ActionBy, win.Note,
                   win.CreatedById, win.Created
            FROM [WorkflowInstanceNode] win
            INNER JOIN [WorkflowNode] wn ON wn.Id = win.NodeId
            INNER JOIN [WorkflowInstance] wi ON wi.Id = win.InstanceId
            WHERE win.Status = 'Active'
              AND wn.TimeoutHours IS NOT NULL
              AND win.EnteredAt IS NOT NULL
              AND wi.Status = 'Active'
              AND DATEADD(HOUR, wn.TimeoutHours, win.EnteredAt) < SYSUTCDATETIME();
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<WorkflowInstanceNode>();
        while (await reader.ReadAsync(ct))
            result.Add(ReadInstanceNode(reader, 0));
        return result;
    }

    // ── Readers ──────────────────────────────────────────────────────────

    private static WorkflowInstance ReadInstance(SqlDataReader r, int offset = 0)
    {
        var o = offset;
        var instance = new WorkflowInstance
        {
            DefinitionId = r.GetInt32(o + 1),
            SourceType   = r.GetString(o + 2),
            SourceId     = r.GetInt32(o + 3),
            Status       = Enum.Parse<WorkflowInstanceStatus>(r.GetString(o + 4)),
            StartedAt    = r.IsDBNull(o + 5) ? DateTime.UtcNow : r.GetDateTime(o + 5),
            StartedBy    = r.IsDBNull(o + 6) ? null : r.GetString(o + 6),
            CompletedAt  = r.IsDBNull(o + 7) ? null : r.GetDateTime(o + 7),
            ContextJson  = r.IsDBNull(o + 8) ? null : r.GetString(o + 8),
            CreatedById  = r.IsDBNull(o + 9) ? null : r.GetInt32(o + 9),
        };
        instance.Id = r.GetInt32(o + 0);
        return instance;
    }

    private static WorkflowInstanceNode ReadInstanceNode(SqlDataReader r, int offset = 0)
    {
        var o = offset;
        return new WorkflowInstanceNode
        {
            Id             = r.GetInt32(o + 0),
            InstanceId     = r.GetInt32(o + 1),
            NodeId         = r.GetInt32(o + 2),
            Status         = Enum.Parse<WorkflowInstanceNodeStatus>(r.GetString(o + 3)),
            AssignedUserId = r.IsDBNull(o + 4) ? null : r.GetString(o + 4),
            EnteredAt      = r.IsDBNull(o + 5) ? null : r.GetDateTime(o + 5),
            CompletedAt    = r.IsDBNull(o + 6) ? null : r.GetDateTime(o + 6),
            Action         = r.IsDBNull(o + 7) ? null : r.GetString(o + 7),
            ActionBy       = r.IsDBNull(o + 8) ? null : r.GetString(o + 8),
            Note           = r.IsDBNull(o + 9) ? null : r.GetString(o + 9),
            CreatedById    = r.IsDBNull(o + 10) ? null : r.GetInt32(o + 10),
        };
    }
}

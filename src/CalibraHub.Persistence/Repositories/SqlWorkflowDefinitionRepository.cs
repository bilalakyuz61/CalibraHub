using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly SqlServerConnectionFactory _cf;
    private readonly string _schema;

    public SqlWorkflowDefinitionRepository(SqlServerConnectionFactory cf, CalibraDatabaseOptions options)
    {
        _cf = cf;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    // ── GetAll ───────────────────────────────────────────────

    public async Task<IReadOnlyList<WorkflowDefinitionDto>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT Id, Name, Description, DocumentTypeId, IsActive, Version, IsPublished, Created, CreatedById
            FROM [{_schema}].[WorkflowDefinition]
            ORDER BY Name;
            """;
        var list = new List<WorkflowDefinitionDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ReadDefinitionRow(r));
        return list;
    }

    // ── GetDetail ────────────────────────────────────────────

    public async Task<WorkflowDefinitionDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT Id, Name, Description, DocumentTypeId, IsActive, Version, IsPublished, Created, CreatedById
            FROM [{_schema}].[WorkflowDefinition] WHERE Id = @Id;

            SELECT Id, DefinitionId, NodeType, Name, PositionX, PositionY,
                   ActorType, ActorRefId, ActorExpression, TimeoutHours, OnRejectPolicy, JoinExpectedTokens
            FROM [{_schema}].[WorkflowNode] WHERE DefinitionId = @Id ORDER BY Id;

            SELECT Id, DefinitionId, FromNodeId, ToNodeId, Label, Condition, Priority, IsDefault
            FROM [{_schema}].[WorkflowTransition] WHERE DefinitionId = @Id ORDER BY Priority, Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var def = ReadDefinitionRow(r);

        await r.NextResultAsync(ct);
        var nodes = new List<WorkflowNodeDto>();
        while (await r.ReadAsync(ct)) nodes.Add(ReadNodeRow(r));

        await r.NextResultAsync(ct);
        var transitions = new List<WorkflowTransitionDto>();
        while (await r.ReadAsync(ct)) transitions.Add(ReadTransitionRow(r));

        return new WorkflowDefinitionDetailDto(def, nodes, transitions);
    }

    // ── GetRaw ───────────────────────────────────────────────

    public async Task<WorkflowDefinition?> GetRawAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT Id, Name, Description, DocumentTypeId, IsActive, Version, IsPublished, CreatedById, Created, UpdatedById, Updated
            FROM [{_schema}].[WorkflowDefinition] WHERE Id = @Id;

            SELECT Id, DefinitionId, NodeType, Name, PositionX, PositionY,
                   ActorType, ActorRefId, ActorExpression, TimeoutHours, OnRejectPolicy, JoinExpectedTokens
            FROM [{_schema}].[WorkflowNode] WHERE DefinitionId = @Id;

            SELECT Id, DefinitionId, FromNodeId, ToNodeId, Label, Condition, Priority, IsDefault
            FROM [{_schema}].[WorkflowTransition] WHERE DefinitionId = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var def = new WorkflowDefinition
        {
            Id = r.GetInt32(0),
            Name = r.GetString(1),
            Description = r.IsDBNull(2) ? null : r.GetString(2),
            DocumentTypeId = r.IsDBNull(3) ? null : r.GetInt32(3),
            IsActive = r.GetBoolean(4),
            Version = r.GetInt32(5),
            IsPublished = r.GetBoolean(6),
            CreatedById = r.IsDBNull(7) ? null : r.GetInt32(7),
            Created = r.GetDateTime(8),
            UpdatedById = r.IsDBNull(9) ? null : r.GetInt32(9),
            Updated = r.IsDBNull(10) ? null : r.GetDateTime(10),
        };

        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            def.AddNode(ReadRawNode(r));

        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            def.Connect(ReadRawTransition(r));

        return def;
    }

    // ── SaveDefinition ───────────────────────────────────────

    public async Task<int> SaveDefinitionAsync(WorkflowDefinition def, int? actor, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (def.Id == 0)
        {
            cmd.CommandText = $"""
                INSERT INTO [{_schema}].[WorkflowDefinition]
                    (Name, Description, DocumentTypeId, IsActive, Version, IsPublished, CreatedById, Created, UpdatedById, Updated)
                OUTPUT INSERTED.Id
                VALUES (@Name, @Desc, @DocTypeId, @IsActive, @Version, @IsPublished, @CreatedById, @Created, @UpdatedById, @Updated);
                """;
        }
        else
        {
            cmd.CommandText = $"""
                UPDATE [{_schema}].[WorkflowDefinition]
                SET Name=@Name, Description=@Desc, DocumentTypeId=@DocTypeId, IsActive=@IsActive,
                    Version=@Version, IsPublished=@IsPublished, UpdatedById=@UpdatedById, Updated=@Updated
                WHERE Id=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", def.Id));
        }
        cmd.Parameters.Add(new SqlParameter("@Name", def.Name));
        cmd.Parameters.Add(new SqlParameter("@Desc", (object?)def.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DocTypeId", (object?)def.DocumentTypeId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive", def.IsActive));
        cmd.Parameters.Add(new SqlParameter("@Version", def.Version));
        cmd.Parameters.Add(new SqlParameter("@IsPublished", def.IsPublished));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)def.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created", def.Created));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)def.UpdatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Updated", (object?)def.Updated ?? DBNull.Value));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    // ── SaveNode ─────────────────────────────────────────────

    public async Task<int> SaveNodeAsync(WorkflowNode node, int? actor, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (node.Id == 0)
        {
            cmd.CommandText = $"""
                INSERT INTO [{_schema}].[WorkflowNode]
                    (DefinitionId, NodeType, Name, PositionX, PositionY, ActorType, ActorRefId,
                     ActorExpression, TimeoutHours, OnRejectPolicy, JoinExpectedTokens, CreatedById, Created, UpdatedById, Updated)
                OUTPUT INSERTED.Id
                VALUES (@DefId, @NodeType, @Name, @PosX, @PosY, @ActorType, @ActorRefId,
                        @ActorExpr, @Timeout, @OnReject, @JoinTokens, @CreatedById, @Created, @UpdatedById, @Updated);
                """;
        }
        else
        {
            cmd.CommandText = $"""
                UPDATE [{_schema}].[WorkflowNode]
                SET Name=@Name, PositionX=@PosX, PositionY=@PosY, ActorType=@ActorType,
                    ActorRefId=@ActorRefId, ActorExpression=@ActorExpr, TimeoutHours=@Timeout,
                    OnRejectPolicy=@OnReject, JoinExpectedTokens=@JoinTokens, UpdatedById=@UpdatedById, Updated=@Updated
                WHERE Id=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", node.Id));
        }
        AddNodeParams(cmd, node, actor);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task DeleteNodeAsync(int nodeId, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM [{_schema}].[WorkflowTransition] WHERE FromNodeId=@Id OR ToNodeId=@Id;
            DELETE FROM [{_schema}].[WorkflowNode] WHERE Id=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", nodeId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── SaveTransition ───────────────────────────────────────

    public async Task<int> SaveTransitionAsync(WorkflowTransition t, int? actor, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (t.Id == 0)
        {
            cmd.CommandText = $"""
                INSERT INTO [{_schema}].[WorkflowTransition]
                    (DefinitionId, FromNodeId, ToNodeId, Label, Condition, Priority, IsDefault, CreatedById, Created, UpdatedById, Updated)
                OUTPUT INSERTED.Id
                VALUES (@DefId, @From, @To, @Label, @Cond, @Prio, @IsDef, @CreatedById, @Created, @UpdatedById, @Updated);
                """;
        }
        else
        {
            cmd.CommandText = $"""
                UPDATE [{_schema}].[WorkflowTransition]
                SET Label=@Label, Condition=@Cond, Priority=@Prio, IsDefault=@IsDef, UpdatedById=@UpdatedById, Updated=@Updated
                WHERE Id=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", t.Id));
        }
        cmd.Parameters.Add(new SqlParameter("@DefId", t.DefinitionId));
        cmd.Parameters.Add(new SqlParameter("@From", t.FromNodeId));
        cmd.Parameters.Add(new SqlParameter("@To", t.ToNodeId));
        cmd.Parameters.Add(new SqlParameter("@Label", (object?)t.Label ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Cond", (object?)t.Condition ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Prio", t.Priority));
        cmd.Parameters.Add(new SqlParameter("@IsDef", t.IsDefault));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)(int?)actor ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created", DateTime.UtcNow));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(int?)actor ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Updated", (object?)t.Updated ?? DBNull.Value));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task DeleteTransitionAsync(int transitionId, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM [{_schema}].[WorkflowTransition] WHERE Id=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", transitionId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteDefinitionAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM [{_schema}].[WorkflowTransition] WHERE DefinitionId=@Id;
            DELETE FROM [{_schema}].[WorkflowNode] WHERE DefinitionId=@Id;
            DELETE FROM [{_schema}].[WorkflowDefinition] WHERE Id=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────

    private static WorkflowDefinitionDto ReadDefinitionRow(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1),
        r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : r.GetInt32(3),
        r.GetBoolean(4), r.GetInt32(5), r.GetBoolean(6),
        r.GetDateTime(7),
        r.IsDBNull(8) ? null : r.GetInt32(8));

    private static WorkflowNodeDto ReadNodeRow(SqlDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
        r.GetInt32(4), r.GetInt32(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetInt32(9),
        r.IsDBNull(10) ? null : r.GetString(10),
        r.IsDBNull(11) ? null : r.GetInt32(11));

    private static WorkflowTransitionDto ReadTransitionRow(SqlDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.GetInt32(6), r.GetBoolean(7));

    private static WorkflowNode ReadRawNode(SqlDataReader r)
    {
        var nodeType = Enum.TryParse<WorkflowNodeType>(r.GetString(2), out var nt) ? nt : WorkflowNodeType.Task;
        var actorType = r.IsDBNull(6) ? (WorkflowActorType?)null
            : Enum.TryParse<WorkflowActorType>(r.GetString(6), out var at) ? at : (WorkflowActorType?)null;
        var onReject = r.IsDBNull(10) ? (WorkflowOnRejectPolicy?)null
            : Enum.TryParse<WorkflowOnRejectPolicy>(r.GetString(10), out var rp) ? rp : (WorkflowOnRejectPolicy?)null;

        return new WorkflowNode
        {
            Id = r.GetInt32(0),
            DefinitionId = r.GetInt32(1),
            NodeType = nodeType,
            Name = r.GetString(3),
            PositionX = r.GetInt32(4),
            PositionY = r.GetInt32(5),
            ActorType = actorType,
            ActorRefId = r.IsDBNull(7) ? null : r.GetString(7),
            ActorExpression = r.IsDBNull(8) ? null : r.GetString(8),
            TimeoutHours = r.IsDBNull(9) ? null : r.GetInt32(9),
            OnRejectPolicy = onReject,
            JoinExpectedTokens = r.IsDBNull(11) ? null : r.GetInt32(11),
        };
    }

    private static WorkflowTransition ReadRawTransition(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        DefinitionId = r.GetInt32(1),
        FromNodeId = r.GetInt32(2),
        ToNodeId = r.GetInt32(3),
        Label = r.IsDBNull(4) ? null : r.GetString(4),
        Condition = r.IsDBNull(5) ? null : r.GetString(5),
        Priority = r.GetInt32(6),
        IsDefault = r.GetBoolean(7),
    };

    private static void AddNodeParams(SqlCommand cmd, WorkflowNode node, int? actor)
    {
        cmd.Parameters.Add(new SqlParameter("@DefId", node.DefinitionId));
        cmd.Parameters.Add(new SqlParameter("@NodeType", node.NodeType.ToString()));
        cmd.Parameters.Add(new SqlParameter("@Name", node.Name));
        cmd.Parameters.Add(new SqlParameter("@PosX", node.PositionX));
        cmd.Parameters.Add(new SqlParameter("@PosY", node.PositionY));
        cmd.Parameters.Add(new SqlParameter("@ActorType", (object?)node.ActorType?.ToString() ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ActorRefId", (object?)node.ActorRefId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ActorExpr", (object?)node.ActorExpression ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Timeout", (object?)node.TimeoutHours ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@OnReject", (object?)node.OnRejectPolicy?.ToString() ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@JoinTokens", (object?)node.JoinExpectedTokens ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)(int?)actor ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created", DateTime.UtcNow));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(int?)actor ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Updated", (object?)node.Updated ?? DBNull.Value));
    }

}

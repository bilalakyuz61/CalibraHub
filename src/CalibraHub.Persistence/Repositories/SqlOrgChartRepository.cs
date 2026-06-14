using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlOrgChartRepository : IOrgChartRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _chartsTable;
    private readonly string _nodesTable;

    public SqlOrgChartRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _chartsTable = $"[{schema}].[OrgChart]";
        _nodesTable = $"[{schema}].[OrgChartNode]";
    }

    // ── Charts ───────────────────────────────────────────────

    public async Task<IReadOnlyCollection<OrgChart>> GetChartsByCompanyAsync(int companyId, CancellationToken ct)
    {
        var list = new List<OrgChart>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [CompanyId], [Name], [IsDefault], [Created], [Updated]
            FROM {_chartsTable}
            WHERE [CompanyId] = @CompanyId
            ORDER BY [IsDefault] DESC, [Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OrgChart
            {
                Id = reader.GetInt32(0),
                CompanyId = reader.GetInt32(1),
                Name = reader.GetString(2),
                Created = reader.GetDateTime(4),
                Updated = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            });
            if (reader.GetBoolean(3)) list[^1].MarkAsDefault();
        }
        return list;
    }

    public async Task<OrgChart?> GetChartByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [CompanyId], [Name], [IsDefault], [Created], [Updated]
            FROM {_chartsTable}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var chart = new OrgChart
        {
            Id = reader.GetInt32(0),
            CompanyId = reader.GetInt32(1),
            Name = reader.GetString(2),
            Created = reader.GetDateTime(4),
            Updated = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
        };
        if (reader.GetBoolean(3)) chart.MarkAsDefault();
        return chart;
    }

    public async Task SaveChartAsync(OrgChart chart, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (chart.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_chartsTable}
                SET [Name] = @Name, [IsDefault] = @IsDefault, [Updated] = @Updated, [UpdatedById] = @UpdatedById
                WHERE [Id] = @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", chart.Id));
            cmd.Parameters.Add(new SqlParameter("@Name", chart.Name));
            cmd.Parameters.Add(new SqlParameter("@IsDefault", chart.IsDefault));
            cmd.Parameters.Add(new SqlParameter("@Updated", (object?)chart.Updated ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)chart.UpdatedById ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_chartsTable} ([CompanyId], [Name], [IsDefault], [Created], [CreatedById])
                VALUES (@CompanyId, @Name, @IsDefault, @Created, @CreatedById);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.Add(new SqlParameter("@CompanyId", chart.CompanyId));
            cmd.Parameters.Add(new SqlParameter("@Name", chart.Name));
            cmd.Parameters.Add(new SqlParameter("@IsDefault", chart.IsDefault));
            cmd.Parameters.Add(new SqlParameter("@Created", chart.Created));
            cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)chart.CreatedById ?? DBNull.Value));
            chart.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
    }

    public async Task DeleteChartAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM {_nodesTable} WHERE [ChartId] = @Id;
            DELETE FROM {_chartsTable} WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetDefaultChartAsync(int companyId, int chartId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_chartsTable} SET [IsDefault] = 0 WHERE [CompanyId] = @CompanyId;
            UPDATE {_chartsTable} SET [IsDefault] = 1 WHERE [Id] = @ChartId AND [CompanyId] = @CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@ChartId", chartId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Nodes ────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<OrgChartNode>> GetNodesByChartAsync(int chartId, CancellationToken ct)
    {
        var list = new List<OrgChartNode>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [ChartId], [UserId], [ParentUserId], [PositionTitle], [SortOrder],
                   ISNULL([NodeType], 'User') AS [NodeType],
                   [DepartmentId], [PersonnelId], [ParentNodeId]
            FROM {_nodesTable}
            WHERE [ChartId] = @ChartId
            ORDER BY [SortOrder];
            """;
        cmd.Parameters.Add(new SqlParameter("@ChartId", chartId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var nodeTypeStr = reader.GetString(6);
            var nodeType = nodeTypeStr switch
            {
                "Department" => OrgChartNodeType.Department,
                "Personnel"  => OrgChartNodeType.Personnel,
                "Vacant"     => OrgChartNodeType.Vacant,
                _            => OrgChartNodeType.User,
            };

            list.Add(new OrgChartNode
            {
                Id           = reader.GetInt32(0),
                ChartId      = reader.GetInt32(1),
                UserId       = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ParentUserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                PositionTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                SortOrder    = reader.GetInt32(5),
                NodeType     = nodeType,
                DepartmentId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                PersonnelId  = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                ParentNodeId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            });
        }
        return list;
    }

    public async Task SaveNodeAsync(OrgChartNode node, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (node.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_nodesTable}
                SET [UserId] = @UserId, [ParentUserId] = @ParentUserId, [ParentNodeId] = @ParentNodeId,
                    [PositionTitle] = @PositionTitle, [SortOrder] = @SortOrder,
                    [NodeType] = @NodeType, [DepartmentId] = @DepartmentId, [PersonnelId] = @PersonnelId
                WHERE [Id] = @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", node.Id));
            AddNodeParams(cmd, node);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_nodesTable}
                    ([ChartId], [UserId], [ParentUserId], [ParentNodeId],
                     [PositionTitle], [SortOrder], [NodeType], [DepartmentId], [PersonnelId])
                VALUES
                    (@ChartId, @UserId, @ParentUserId, @ParentNodeId,
                     @PositionTitle, @SortOrder, @NodeType, @DepartmentId, @PersonnelId);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            AddNodeParams(cmd, node);
            node.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
    }

    public async Task DeleteNodeAsync(int nodeId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_nodesTable} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", nodeId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReplaceNodesAsync(int chartId, IReadOnlyCollection<OrgChartNode> nodes, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // Step 1: Delete all existing nodes for the chart
        await using var delCmd = conn.CreateCommand();
        delCmd.CommandText = $"DELETE FROM {_nodesTable} WHERE [ChartId] = @ChartId;";
        delCmd.Parameters.Add(new SqlParameter("@ChartId", chartId));
        await delCmd.ExecuteNonQueryAsync(ct);

        if (nodes.Count == 0) return;

        // Step 2: Insert all nodes without ParentNodeId, capture old→new ID mapping
        var idMap = new Dictionary<int, int>(); // clientId → newDbId

        foreach (var node in nodes)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_nodesTable}
                    ([ChartId], [UserId], [ParentUserId], [PositionTitle],
                     [SortOrder], [NodeType], [DepartmentId], [PersonnelId])
                VALUES
                    (@ChartId, @UserId, @ParentUserId, @PositionTitle,
                     @SortOrder, @NodeType, @DepartmentId, @PersonnelId);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.Add(new SqlParameter("@ChartId", chartId));
            cmd.Parameters.Add(new SqlParameter("@UserId", (object?)node.UserId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ParentUserId", (object?)node.ParentUserId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@PositionTitle", (object?)node.PositionTitle ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@SortOrder", node.SortOrder));
            cmd.Parameters.Add(new SqlParameter("@NodeType", node.NodeType.ToString()));
            cmd.Parameters.Add(new SqlParameter("@DepartmentId", (object?)node.DepartmentId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@PersonnelId", (object?)node.PersonnelId ?? DBNull.Value));

            var newId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (node.Id > 0) idMap[node.Id] = newId;
            node.Id = newId;
        }

        // Step 3: Update ParentNodeId references using the old→new ID mapping
        foreach (var node in nodes.Where(n => n.ParentNodeId.HasValue))
        {
            if (!idMap.TryGetValue(node.ParentNodeId!.Value, out var newParentId)) continue;
            await using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = $"UPDATE {_nodesTable} SET [ParentNodeId] = @ParentNodeId WHERE [Id] = @Id;";
            updateCmd.Parameters.Add(new SqlParameter("@Id", node.Id));
            updateCmd.Parameters.Add(new SqlParameter("@ParentNodeId", newParentId));
            await updateCmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task MoveNodeAsync(int nodeId, int? newParentNodeId, int newSortOrder, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_nodesTable}
            SET [ParentNodeId] = @ParentNodeId, [SortOrder] = @SortOrder
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", nodeId));
        cmd.Parameters.Add(new SqlParameter("@ParentNodeId", (object?)newParentNodeId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", newSortOrder));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────

    private static void AddNodeParams(SqlCommand cmd, OrgChartNode node)
    {
        cmd.Parameters.Add(new SqlParameter("@ChartId", node.ChartId));
        cmd.Parameters.Add(new SqlParameter("@UserId", (object?)node.UserId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ParentUserId", (object?)node.ParentUserId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ParentNodeId", (object?)node.ParentNodeId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PositionTitle", (object?)node.PositionTitle ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", node.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@NodeType", node.NodeType.ToString()));
        cmd.Parameters.Add(new SqlParameter("@DepartmentId", (object?)node.DepartmentId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PersonnelId", (object?)node.PersonnelId ?? DBNull.Value));
    }
}

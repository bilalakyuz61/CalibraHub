using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
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
        _chartsTable = $"[{schema}].[org_charts]";
        _nodesTable = $"[{schema}].[org_chart_nodes]";
    }

    // ── Charts ───────────────────────────────────────────────

    public async Task<IReadOnlyCollection<OrgChart>> GetChartsByCompanyAsync(int companyId, CancellationToken ct)
    {
        var list = new List<OrgChart>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id], [company_id], [name], [is_default], [Created], [Updated]
            FROM {_chartsTable}
            WHERE [company_id] = @CompanyId
            ORDER BY [is_default] DESC, [name];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OrgChart
            {
                Id = reader.GetGuid(0),
                CompanyId = reader.GetInt32(1),
                Name = reader.GetString(2),
                IsDefault = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4),
                UpdatedAt = reader.GetDateTime(5),
            });
        }
        return list;
    }

    public async Task<OrgChart?> GetChartByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id], [company_id], [name], [is_default], [Created], [Updated]
            FROM {_chartsTable}
            WHERE [id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new OrgChart
        {
            Id = reader.GetGuid(0),
            CompanyId = reader.GetInt32(1),
            Name = reader.GetString(2),
            IsDefault = reader.GetBoolean(3),
            CreatedAt = reader.GetDateTime(4),
            UpdatedAt = reader.GetDateTime(5),
        };
    }

    public async Task SaveChartAsync(OrgChart chart, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_chartsTable} WHERE [id] = @Id)
                UPDATE {_chartsTable}
                SET [name] = @Name, [is_default] = @IsDefault, [Updated] = @UpdatedAt
                WHERE [id] = @Id;
            ELSE
                INSERT INTO {_chartsTable} ([id], [company_id], [name], [is_default], [Created], [Updated])
                VALUES (@Id, @CompanyId, @Name, @IsDefault, @CreatedAt, @UpdatedAt);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", chart.Id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", chart.CompanyId));
        cmd.Parameters.Add(new SqlParameter("@Name", chart.Name));
        cmd.Parameters.Add(new SqlParameter("@IsDefault", chart.IsDefault));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", chart.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", chart.UpdatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteChartAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM {_nodesTable} WHERE [chart_id] = @Id;
            DELETE FROM {_chartsTable} WHERE [id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetDefaultChartAsync(int companyId, Guid chartId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_chartsTable} SET [is_default] = 0 WHERE [company_id] = @CompanyId;
            UPDATE {_chartsTable} SET [is_default] = 1 WHERE [id] = @ChartId AND [company_id] = @CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@ChartId", chartId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Nodes ────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<OrgChartNode>> GetNodesByChartAsync(Guid chartId, CancellationToken ct)
    {
        var list = new List<OrgChartNode>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id], [chart_id], [user_id], [parent_user_id], [position_title], [sort_order]
            FROM {_nodesTable}
            WHERE [chart_id] = @ChartId
            ORDER BY [sort_order];
            """;
        cmd.Parameters.Add(new SqlParameter("@ChartId", chartId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OrgChartNode
            {
                Id = reader.GetGuid(0),
                ChartId = reader.GetGuid(1),
                UserId = reader.GetGuid(2),
                ParentUserId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                PositionTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                SortOrder = reader.GetInt32(5),
            });
        }
        return list;
    }

    public async Task SaveNodeAsync(OrgChartNode node, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_nodesTable} WHERE [id] = @Id)
                UPDATE {_nodesTable}
                SET [user_id] = @UserId, [parent_user_id] = @ParentUserId,
                    [position_title] = @PositionTitle, [sort_order] = @SortOrder
                WHERE [id] = @Id;
            ELSE
                INSERT INTO {_nodesTable} ([id], [chart_id], [user_id], [parent_user_id], [position_title], [sort_order])
                VALUES (@Id, @ChartId, @UserId, @ParentUserId, @PositionTitle, @SortOrder);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", node.Id));
        cmd.Parameters.Add(new SqlParameter("@ChartId", node.ChartId));
        cmd.Parameters.Add(new SqlParameter("@UserId", node.UserId));
        cmd.Parameters.Add(new SqlParameter("@ParentUserId", (object?)node.ParentUserId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PositionTitle", (object?)node.PositionTitle ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", node.SortOrder));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteNodeAsync(Guid nodeId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_nodesTable} WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", nodeId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReplaceNodesAsync(Guid chartId, IReadOnlyCollection<OrgChartNode> nodes, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var sql = new System.Text.StringBuilder();
        sql.AppendLine($"DELETE FROM {_nodesTable} WHERE [chart_id] = @ChartId;");
        cmd.Parameters.Add(new SqlParameter("@ChartId", chartId));

        var i = 0;
        foreach (var node in nodes)
        {
            sql.AppendLine($"""
                INSERT INTO {_nodesTable} ([id], [chart_id], [user_id], [parent_user_id], [position_title], [sort_order])
                VALUES (@Id{i}, @ChartId, @UserId{i}, @ParentUserId{i}, @Title{i}, @Sort{i});
                """);
            cmd.Parameters.Add(new SqlParameter($"@Id{i}", node.Id));
            cmd.Parameters.Add(new SqlParameter($"@UserId{i}", node.UserId));
            cmd.Parameters.Add(new SqlParameter($"@ParentUserId{i}", (object?)node.ParentUserId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter($"@Title{i}", (object?)node.PositionTitle ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter($"@Sort{i}", node.SortOrder));
            i++;
        }

        cmd.CommandText = sql.ToString();
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

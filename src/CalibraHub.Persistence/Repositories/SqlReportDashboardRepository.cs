using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlReportDashboardRepository : IReportDashboardRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _tDash;
    private readonly string _tAccess;

    public SqlReportDashboardRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var s    = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
        _tDash   = $"[{s}].[ReportDashboard]";
        _tAccess = $"[{s}].[ReportDashboardAccess]";
    }

    public async Task SyncAsync(IReadOnlyList<GrafanaDashboardSummary> dashboards, string? actor, CancellationToken ct)
    {
        if (dashboards.Count == 0) return;

        await using var conn = await _factory.OpenConnectionAsync(ct);

        // MERGE her pano için ayrı ayrı (batch MERGE SQL Server'da TVP ister, bunu daha basit tutuyoruz)
        for (var i = 0; i < dashboards.Count; i++)
        {
            var d       = dashboards[i];
            var tagsJson = d.Tags.Count > 0 ? JsonSerializer.Serialize(d.Tags) : null;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                MERGE {_tDash} AS tgt
                USING (SELECT @uid AS GrafanaUid) AS src ON tgt.GrafanaUid = src.GrafanaUid
                WHEN MATCHED THEN
                    UPDATE SET Title = @title, FolderTitle = @folder, Tags = @tags,
                               SortOrder = @sort, IsActive = 1,
                               Updated = SYSUTCDATETIME(), UpdatedBy = @actor
                WHEN NOT MATCHED THEN
                    INSERT (GrafanaUid, Title, FolderTitle, Tags, SortOrder, IsActive, CreatedBy)
                    VALUES (@uid, @title, @folder, @tags, @sort, 1, @actor);
                """;
            cmd.Parameters.AddWithValue("@uid",    d.Uid);
            cmd.Parameters.AddWithValue("@title",  d.Title);
            cmd.Parameters.AddWithValue("@folder", (object?)d.FolderTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tags",   (object?)tagsJson       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sort",   i);
            cmd.Parameters.AddWithValue("@actor",  (object?)actor           ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Grafana'da artık olmayan aktif kayıtları pasif yap
        var paramNames = string.Join(",", dashboards.Select((_, i) => $"@u{i}"));
        await using var deactCmd = conn.CreateCommand();
        deactCmd.CommandText = $"""
            UPDATE {_tDash}
            SET IsActive = 0, Updated = SYSUTCDATETIME()
            WHERE IsActive = 1 AND GrafanaUid NOT IN ({paramNames});
            """;
        for (var i = 0; i < dashboards.Count; i++)
            deactCmd.Parameters.AddWithValue($"@u{i}", dashboards[i].Uid);
        await deactCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ReportDashboard>> GetAllActiveAsync(CancellationToken ct)
    {
        var result = new List<ReportDashboard>();
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT Id, GrafanaUid, Title, FolderTitle, Tags, SortOrder, IsActive,
                   CreatedBy, Created, UpdatedBy, Updated
            FROM {_tDash}
            WHERE IsActive = 1
            ORDER BY SortOrder, Title;
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new ReportDashboard
            {
                Id          = r.GetInt32(0),
                GrafanaUid  = r.GetString(1),
                Title       = r.GetString(2),
                FolderTitle = r.IsDBNull(3)  ? null : r.GetString(3),
                Tags        = r.IsDBNull(4)  ? null : r.GetString(4),
                SortOrder   = r.GetInt32(5),
                IsActive    = r.GetBoolean(6),
                CreatedBy   = r.IsDBNull(7)  ? null : r.GetString(7),
                Created     = r.GetDateTime(8),
                UpdatedBy   = r.IsDBNull(9)  ? null : r.GetString(9),
                Updated     = r.IsDBNull(10) ? null : r.GetDateTime(10),
            });
        return result;
    }

    public async Task<IReadOnlyDictionary<int, (IReadOnlyList<int> UserIds, IReadOnlyList<int> DeptIds)>> GetAllRestrictionsAsync(CancellationToken ct)
    {
        var users = new Dictionary<int, List<int>>();
        var depts = new Dictionary<int, List<int>>();

        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT ReportDashboardId, UserId, DepartmentId FROM {_tAccess};";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var dashId = r.GetInt32(0);
            if (!r.IsDBNull(1))
            {
                if (!users.ContainsKey(dashId)) users[dashId] = [];
                users[dashId].Add(r.GetInt32(1));
            }
            if (!r.IsDBNull(2))
            {
                if (!depts.ContainsKey(dashId)) depts[dashId] = [];
                depts[dashId].Add(r.GetInt32(2));
            }
        }

        var result = new Dictionary<int, (IReadOnlyList<int>, IReadOnlyList<int>)>();
        foreach (var k in users.Keys.Union(depts.Keys))
        {
            users.TryGetValue(k, out var u);
            depts.TryGetValue(k, out var d);
            result[k] = (u ?? [], d ?? []);
        }
        return result;
    }

    public async Task ReplaceAccessAsync(int dashboardId, IReadOnlyList<int> userIds, IReadOnlyList<int> deptIds, string? actor, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var tx   = conn.BeginTransaction();
        try
        {
            await using (var del = conn.CreateCommand())
            {
                del.Transaction  = tx;
                del.CommandText  = $"DELETE FROM {_tAccess} WHERE ReportDashboardId = @id;";
                del.Parameters.AddWithValue("@id", dashboardId);
                await del.ExecuteNonQueryAsync(ct);
            }

            foreach (var uid in userIds.Distinct())
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_tAccess} (ReportDashboardId, UserId, DepartmentId, CreatedBy)
                    VALUES (@dashId, @uid, NULL, @actor);
                    """;
                ins.Parameters.AddWithValue("@dashId", dashboardId);
                ins.Parameters.AddWithValue("@uid",    uid);
                ins.Parameters.AddWithValue("@actor",  (object?)actor ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync(ct);
            }

            foreach (var deptId in deptIds.Distinct())
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_tAccess} (ReportDashboardId, UserId, DepartmentId, CreatedBy)
                    VALUES (@dashId, NULL, @deptId, @actor);
                    """;
                ins.Parameters.AddWithValue("@dashId", dashboardId);
                ins.Parameters.AddWithValue("@deptId", deptId);
                ins.Parameters.AddWithValue("@actor",  (object?)actor ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync(ct);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}

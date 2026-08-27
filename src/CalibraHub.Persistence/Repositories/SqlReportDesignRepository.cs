using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlReportDesignRepository : IReportDesignRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlReportDesignRepository(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> SaveAsync(SaveReportDesignRequest req, string? user, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.ReportDesign (Title, GroupName, Description, PanelsJson, CreatedBy, CompanyId)
            OUTPUT INSERTED.Id
            VALUES (@Title, @GroupName, @Description, @PanelsJson, @User, @CompanyId);
            """;
        cmd.Parameters.Add(new SqlParameter("@Title",      req.Title));
        cmd.Parameters.Add(new SqlParameter("@GroupName",  (object?)req.GroupName?.Trim() ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Description",(object?)req.Description?.Trim() ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PanelsJson", req.PanelsJson));
        cmd.Parameters.Add(new SqlParameter("@User",       (object?)user ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CompanyId",  _connectionFactory.ResolveEffectiveCompanyId()));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(int id, SaveReportDesignRequest req, string? user, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE dbo.ReportDesign
            SET Title       = @Title,
                GroupName   = @GroupName,
                Description = @Description,
                PanelsJson  = @PanelsJson,
                Updated     = SYSUTCDATETIME(),
                UpdatedBy   = @User
            WHERE Id = @Id AND IsActive = 1 AND CompanyId = @CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@Title",      req.Title));
        cmd.Parameters.Add(new SqlParameter("@GroupName",  (object?)req.GroupName?.Trim() ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Description",(object?)req.Description?.Trim() ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PanelsJson", req.PanelsJson));
        cmd.Parameters.Add(new SqlParameter("@User",       (object?)user ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Id",         id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId",  _connectionFactory.ResolveEffectiveCompanyId()));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.ReportDesign SET IsActive = 0 WHERE Id = @Id AND CompanyId = @CompanyId;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(string Title, string PanelsJson, string? GroupName, string? Description)?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Title, PanelsJson, GroupName, Description
            FROM dbo.ReportDesign
            WHERE Id = @Id AND IsActive = 1 AND CompanyId = @CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3)
        );
    }

    public async Task<IReadOnlyList<ReportDesignSummaryDto>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Created, CreatedBy, GroupName
            FROM dbo.ReportDesign
            WHERE IsActive = 1 AND CompanyId = @CompanyId
            ORDER BY GroupName, Title;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        var list = new List<ReportDesignSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ReportDesignSummaryDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
            ));
        }
        return list;
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlReportSourceRepository : IReportSourceRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlReportSourceRepository(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ReportSourceDto>> GetAllActiveAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, Description, SqlQuery, CacheTtlMinutes, IsActive, Created, CreatedBy, Materialize, LastMaterialized, MaterializedRows, RefreshScheduleJson
            FROM dbo.ReportSource
            WHERE IsActive = 1
            ORDER BY Name;
            """;
        var list = new List<ReportSourceDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    public async Task<ReportSourceDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, Description, SqlQuery, CacheTtlMinutes, IsActive, Created, CreatedBy, Materialize, LastMaterialized, MaterializedRows, RefreshScheduleJson
            FROM dbo.ReportSource WHERE Id = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<int> SaveAsync(SaveReportSourceRequest req, string? user, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();

        if (req.Id is null or 0)
        {
            cmd.CommandText = """
                INSERT INTO dbo.ReportSource (Name, Description, SqlQuery, CacheTtlMinutes, Materialize, RefreshScheduleJson, CreatedBy)
                OUTPUT INSERTED.Id
                VALUES (@Name, @Desc, @Sql, @Ttl, @Mat, @Sched, @User);
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE dbo.ReportSource
                SET Name = @Name, Description = @Desc, SqlQuery = @Sql,
                    CacheTtlMinutes = @Ttl, Materialize = @Mat, RefreshScheduleJson = @Sched,
                    UpdatedBy = @User, Updated = SYSUTCDATETIME()
                WHERE Id = @Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", req.Id.Value));
        }

        cmd.Parameters.Add(new SqlParameter("@Name", req.Name));
        cmd.Parameters.Add(new SqlParameter("@Desc", (object?)req.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Sql",  req.SqlQuery));
        cmd.Parameters.Add(new SqlParameter("@Ttl",  req.CacheTtlMinutes));
        cmd.Parameters.Add(new SqlParameter("@Mat",  req.Materialize));
        cmd.Parameters.Add(new SqlParameter("@Sched",(object?)req.RefreshScheduleJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@User", (object?)user ?? DBNull.Value));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.ReportSource SET IsActive = 0, Updated = SYSUTCDATETIME() WHERE Id = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ReportSourceDto Map(SqlDataReader r) => new(
        Id:               r.GetInt32(0),
        Name:             r.GetString(1),
        Description:      r.IsDBNull(2) ? null : r.GetString(2),
        SqlQuery:         r.GetString(3),
        CacheTtlMinutes:  r.GetInt32(4),
        IsActive:         r.GetBoolean(5),
        Created:          r.GetDateTime(6),
        CreatedBy:        r.IsDBNull(7) ? null : r.GetString(7),
        Materialize:      !r.IsDBNull(8) && r.GetBoolean(8),
        LastMaterialized: r.IsDBNull(9) ? null : r.GetDateTime(9),
        MaterializedRows: r.IsDBNull(10) ? null : r.GetInt32(10),
        RefreshScheduleJson: r.IsDBNull(11) ? null : r.GetString(11)
    );

    public async Task UpdateMaterializedAsync(int id, int rowCount, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.ReportSource SET LastMaterialized = SYSUTCDATETIME(), MaterializedRows = @Rows WHERE Id = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Rows", rowCount));
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — LineCardLayout CRUD. Per-company DB (SqlServerConnectionFactory tenant
/// routing). NOT (2026-08-27): CompanyId kolonu tenant izolasyonu icin sonradan eklendi —
/// bu dosyadaki eski yorum ("kolon yok") artik dogru degil, DELETE bu kolona gore suzer.
/// MERGE (UpsertAsync) suanlik ayni sirketten geldigini varsayiyor; grup1.txt kapsaminda
/// yalniz DELETE flag'lendi.
/// </summary>
public sealed class SqlLineCardLayoutRepository : ILineCardLayoutRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlLineCardLayoutRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[LineCardLayout]";
    }

    public async Task<LineCardLayout?> GetActiveAsync(string formCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[FormCode],[LayoutJson],[IsActive],
                   [CreatedById],[CreatedBy],[Created],[UpdatedById],[UpdatedBy],[Updated]
            FROM {_table}
            WHERE [FormCode]=@FormCode AND [IsActive]=1;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpsertAsync(LineCardLayout layout, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            MERGE {_table} AS t
            USING (SELECT @FormCode AS FormCode) AS src
                ON t.[FormCode]=src.FormCode AND t.[IsActive]=1 AND t.[CompanyId]=@CompanyId
            WHEN MATCHED THEN UPDATE SET
                [LayoutJson]=@LayoutJson,
                [UpdatedById]=@UserId, [UpdatedBy]=@UserName, [Updated]=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT
                ([CompanyId],[FormCode],[LayoutJson],[IsActive],[CreatedById],[CreatedBy])
                VALUES (@CompanyId,@FormCode,@LayoutJson,1,@UserId,@UserName);
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@FormCode", layout.FormCode));
        cmd.Parameters.Add(new SqlParameter("@LayoutJson", layout.LayoutJson));
        cmd.Parameters.Add(new SqlParameter("@UserId", (object?)(layout.UpdatedById ?? layout.CreatedById) ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UserName", (object?)(layout.UpdatedBy ?? layout.CreatedBy) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string formCode, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveEffectiveCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [FormCode]=@FormCode AND [CompanyId]=@CompanyId;";
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static LineCardLayout Map(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        FormCode    = r.GetString(1),
        LayoutJson  = r.GetString(2),
        IsActive    = r.GetBoolean(3),
        CreatedById = r.IsDBNull(4) ? null : r.GetInt32(4),
        CreatedBy   = r.IsDBNull(5) ? null : r.GetString(5),
        Created     = r.GetDateTime(6),
        UpdatedById = r.IsDBNull(7) ? null : r.GetInt32(7),
        UpdatedBy   = r.IsDBNull(8) ? null : r.GetString(8),
        Updated     = r.IsDBNull(9) ? null : r.GetDateTime(9),
    };
}

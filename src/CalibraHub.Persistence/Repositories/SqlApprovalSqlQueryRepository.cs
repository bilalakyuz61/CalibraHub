using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// ApprovalSqlQuery CRUD — admin SQL kütüphanesi.
/// SqlApprovalFlowRepository ile aynı ADO.NET pattern'i: per-request connection,
/// raw SqlConnection.CreateCommand.
/// </summary>
public sealed class SqlApprovalSqlQueryRepository : IApprovalSqlQueryRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _s;

    public SqlApprovalSqlQueryRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _s = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<IReadOnlyList<ApprovalSqlQueryEntity>> GetAllAsync(CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[Description],[SqlText],[ParametersJson],[ResultType],
                   [IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM   [{_s}].[ApprovalSqlQuery]
            ORDER  BY [Name];
            """;
        var list = new List<ApprovalSqlQueryEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(MapRow(reader));
        }
        return list;
    }

    public async Task<ApprovalSqlQueryEntity?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[Description],[SqlText],[ParametersJson],[ResultType],
                   [IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM   [{_s}].[ApprovalSqlQuery]
            WHERE  [Id] = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<int> AddAsync(ApprovalSqlQueryEntity entity, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO [{_s}].[ApprovalSqlQuery]
                ([Name],[Description],[SqlText],[ParametersJson],[ResultType],[IsActive],[CreatedById],[Created])
            VALUES
                (@Name,@Description,@SqlText,@ParametersJson,@ResultType,@IsActive,@CreatedById,SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        BindCommon(cmd, entity);
        cmd.Parameters.AddWithValue("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null ? 0 : Convert.ToInt32(result);
    }

    public async Task UpdateAsync(ApprovalSqlQueryEntity entity, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{_s}].[ApprovalSqlQuery]
            SET    [Name] = @Name,
                   [Description] = @Description,
                   [SqlText] = @SqlText,
                   [ParametersJson] = @ParametersJson,
                   [ResultType] = @ResultType,
                   [IsActive] = @IsActive,
                   [UpdatedById] = @UpdatedById,
                   [Updated] = SYSUTCDATETIME()
            WHERE  [Id] = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id", entity.Id);
        BindCommon(cmd, entity);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)entity.UpdatedById ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"DELETE FROM [{_s}].[ApprovalSqlQuery] WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void BindCommon(SqlCommand cmd, ApprovalSqlQueryEntity entity)
    {
        cmd.Parameters.AddWithValue("@Name", entity.Name);
        cmd.Parameters.AddWithValue("@Description", (object?)entity.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SqlText", entity.SqlText);
        cmd.Parameters.AddWithValue("@ParametersJson", (object?)entity.ParametersJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ResultType",
            string.IsNullOrWhiteSpace(entity.ResultType) ? "scalar" : entity.ResultType);
        cmd.Parameters.AddWithValue("@IsActive", entity.IsActive);
    }

    private static ApprovalSqlQueryEntity MapRow(SqlDataReader r)
    {
        return new ApprovalSqlQueryEntity
        {
            Id             = r.GetInt32(0),
            Name           = r.GetString(1),
            Description    = r.IsDBNull(2) ? null : r.GetString(2),
            SqlText        = r.GetString(3),
            ParametersJson = r.IsDBNull(4) ? null : r.GetString(4),
            ResultType     = r.IsDBNull(5) ? "scalar" : r.GetString(5),
            IsActive       = r.GetBoolean(6),
            CreatedById    = r.IsDBNull(7) ? null : r.GetInt32(7),
            Created        = r.GetDateTime(8),
            UpdatedById    = r.IsDBNull(9) ? null : r.GetInt32(9),
            Updated        = r.IsDBNull(10) ? null : r.GetDateTime(10),
        };
    }
}

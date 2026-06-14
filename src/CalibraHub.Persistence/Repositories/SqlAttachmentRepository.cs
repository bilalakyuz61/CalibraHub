using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Calibra master DB'deki Attachment tablosuna erisir.
/// OpenSystemConnectionAsync kullanir — per-company routing'den bagimsizdir.
/// </summary>
public sealed class SqlAttachmentRepository : IAttachmentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private const string Table = "[dbo].[Attachment]";

    public SqlAttachmentRepository(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyCollection<Attachment>> GetByEntityAsync(
        string entityType, string entityId, CancellationToken ct)
    {
        var list = new List<Attachment>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[EntityType],[EntityId],[FileName],
                   [ContentType],[FileSize],[Description],[IsActive],
                   [CreatedById],[Created],[UpdatedById],[Updated]
            FROM {Table}
            WHERE [EntityType] = @EntityType AND [EntityId] = @EntityId AND [IsActive] = 1
            ORDER BY [Created];
            """;
        cmd.Parameters.Add(new SqlParameter("@EntityType", entityType));
        cmd.Parameters.Add(new SqlParameter("@EntityId",   entityId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<string>> GetEntityIdsWithAttachmentAsync(string entityType, CancellationToken ct)
    {
        var set = new List<string>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT DISTINCT [EntityId] FROM {Table} WHERE [EntityType] = @EntityType AND [IsActive] = 1;";
        cmd.Parameters.Add(new SqlParameter("@EntityType", entityType));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            set.Add(reader.GetString(0));
        return set;
    }

    public async Task<Attachment?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[EntityType],[EntityId],[FileName],
                   [ContentType],[FileSize],[Description],[IsActive],
                   [CreatedById],[Created],[UpdatedById],[Updated]
            FROM {Table}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<byte[]?> GetBinaryAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT [BinaryContent] FROM {Table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (byte[])result;
    }

    public async Task<int> AddAsync(Attachment attachment, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Table}
                ([EntityType],[EntityId],[FileName],
                 [ContentType],[FileSize],[Description],[IsActive],
                 [CreatedById],[Created],[BinaryContent])
            VALUES
                (@EntityType,@EntityId,@FileName,
                 @ContentType,@FileSize,@Description,1,
                 @CreatedById,@Created,@BinaryContent);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@EntityType",    attachment.EntityType));
        cmd.Parameters.Add(new SqlParameter("@EntityId",      attachment.EntityId));
        cmd.Parameters.Add(new SqlParameter("@FileName",      attachment.FileName));
        cmd.Parameters.Add(new SqlParameter("@ContentType",   (object?)attachment.ContentType  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FileSize",      attachment.FileSize));
        cmd.Parameters.Add(new SqlParameter("@Description",   (object?)attachment.Description  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedById",   (object?)attachment.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created",       attachment.Created));
        cmd.Parameters.Add(new SqlParameter("@BinaryContent", (object?)attachment.BinaryContent ?? DBNull.Value)
        {
            SqlDbType = System.Data.SqlDbType.VarBinary
        });
        var result = await cmd.ExecuteScalarAsync(ct);
        var newId = Convert.ToInt32(result);
        attachment.Id = newId;
        return newId;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {Table} SET [IsActive] = 0, [Updated] = SYSUTCDATETIME() WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByEntityAsync(string entityType, string entityId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {Table}
            SET [IsActive] = 0, [Updated] = SYSUTCDATETIME()
            WHERE [EntityType] = @EntityType AND [EntityId] = @EntityId AND [IsActive] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@EntityType", entityType));
        cmd.Parameters.Add(new SqlParameter("@EntityId",   entityId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Attachment MapRow(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        EntityType  = r.GetString(1),
        EntityId    = r.GetString(2),
        FileName    = r.GetString(3),
        ContentType = r.IsDBNull(4)  ? null : r.GetString(4),
        FileSize    = r.GetInt64(5),
        Description = r.IsDBNull(6)  ? null : r.GetString(6),
        IsActive    = r.GetBoolean(7),
        CreatedById = r.IsDBNull(8)  ? null : r.GetInt32(8),
        Created     = r.GetDateTime(9),
        UpdatedById = r.IsDBNull(10) ? null : r.GetInt32(10),
        Updated     = r.IsDBNull(11) ? null : r.GetDateTime(11),
    };
}

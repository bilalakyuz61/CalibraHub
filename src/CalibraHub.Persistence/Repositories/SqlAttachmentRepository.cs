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
            SELECT [Id],[EntityType],[EntityId],[FileName],[StoredName],
                   [ContentType],[FileSize],[Description],[IsActive],
                   [CreatedBy],[Created],[UpdatedBy],[Updated]
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

    public async Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[EntityType],[EntityId],[FileName],[StoredName],
                   [ContentType],[FileSize],[Description],[IsActive],
                   [CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM {Table}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<byte[]?> GetBinaryAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT [BinaryContent] FROM {Table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (byte[])result;
    }

    public async Task<Guid> AddAsync(Attachment attachment, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Table}
                ([Id],[EntityType],[EntityId],[FileName],[StoredName],
                 [ContentType],[FileSize],[Description],[IsActive],
                 [CreatedBy],[Created],[BinaryContent])
            VALUES
                (@Id,@EntityType,@EntityId,@FileName,@StoredName,
                 @ContentType,@FileSize,@Description,1,
                 @CreatedBy,@Created,@BinaryContent);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",            attachment.Id));
        cmd.Parameters.Add(new SqlParameter("@EntityType",    attachment.EntityType));
        cmd.Parameters.Add(new SqlParameter("@EntityId",      attachment.EntityId));
        cmd.Parameters.Add(new SqlParameter("@FileName",      attachment.FileName));
        cmd.Parameters.Add(new SqlParameter("@StoredName",    attachment.StoredName));
        cmd.Parameters.Add(new SqlParameter("@ContentType",   (object?)attachment.ContentType  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FileSize",      attachment.FileSize));
        cmd.Parameters.Add(new SqlParameter("@Description",   (object?)attachment.Description  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedBy",     (object?)attachment.CreatedBy    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created",       attachment.Created));
        cmd.Parameters.Add(new SqlParameter("@BinaryContent", (object?)attachment.BinaryContent ?? DBNull.Value)
        {
            SqlDbType = System.Data.SqlDbType.VarBinary
        });
        await cmd.ExecuteNonQueryAsync(ct);
        return attachment.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
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
        Id          = r.GetGuid(0),
        EntityType  = r.GetString(1),
        EntityId    = r.GetString(2),
        FileName    = r.GetString(3),
        StoredName  = r.GetString(4),
        ContentType = r.IsDBNull(5)  ? null : r.GetString(5),
        FileSize    = r.GetInt64(6),
        Description = r.IsDBNull(7)  ? null : r.GetString(7),
        IsActive    = r.GetBoolean(8),
        CreatedBy   = r.IsDBNull(9)  ? null : r.GetString(9),
        Created     = r.GetDateTime(10),
        UpdatedBy   = r.IsDBNull(11) ? null : r.GetString(11),
        Updated     = r.IsDBNull(12) ? null : r.GetDateTime(12),
    };
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Calibra master DB'deki DocumentCategory tablosuna erişir. Attachment ile AYNI bağlantıyı
/// kullanır — OpenSystemConnectionAsync (per-company routing'den bağımsız, tek merkezi master DB).
/// </summary>
public sealed class SqlDocumentCategoryRepository : IDocumentCategoryRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private const string Table = "[dbo].[DocumentCategory]";

    // SELECT sütun listesi — ordinal index'ler MapRow ile eşleşmeli
    private const string SelectCols = """
        [Id],[ParentId],[Name],[SortOrder],[IsActive],
        [CreatedById],[Created],[UpdatedById],[Updated]
        """;

    public SqlDocumentCategoryRepository(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyCollection<DocumentCategory>> ListAllAsync(CancellationToken ct)
    {
        var list = new List<DocumentCategory>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {Table}
            WHERE [IsActive] = 1
            ORDER BY [ParentId], [SortOrder], [Name];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<DocumentCategory>> ListLevel1Async(CancellationToken ct)
    {
        var list = new List<DocumentCategory>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {Table}
            WHERE [ParentId] IS NULL AND [IsActive] = 1
            ORDER BY [SortOrder], [Name];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<DocumentCategory>> ListByParentAsync(int parentId, CancellationToken ct)
    {
        var list = new List<DocumentCategory>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {Table}
            WHERE [ParentId] = @ParentId AND [IsActive] = 1
            ORDER BY [SortOrder], [Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@ParentId", parentId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<DocumentCategory?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM {Table} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<int> AddAsync(DocumentCategory category, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Table}
                ([ParentId],[Name],[SortOrder],[IsActive],[CreatedById],[Created])
            VALUES
                (@ParentId,@Name,@SortOrder,@IsActive,@CreatedById,@Created);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@ParentId",    (object?)category.ParentId    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Name",        category.Name));
        cmd.Parameters.Add(new SqlParameter("@SortOrder",   category.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive",    category.IsActive));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)category.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created",     category.Created));
        var result = await cmd.ExecuteScalarAsync(ct);
        var newId = Convert.ToInt32(result);
        category.Id = newId;
        return newId;
    }

    public async Task UpdateAsync(DocumentCategory category, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {Table}
            SET [ParentId]    = @ParentId,
                [Name]        = @Name,
                [SortOrder]   = @SortOrder,
                [IsActive]    = @IsActive,
                [UpdatedById] = @UpdatedById,
                [Updated]     = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@ParentId",    (object?)category.ParentId    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Name",        category.Name));
        cmd.Parameters.Add(new SqlParameter("@SortOrder",   category.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive",    category.IsActive));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)category.UpdatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Id",          category.Id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SoftDeleteAsync(int id, int? updatedById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        // id bir Kategori (L1) ise altındaki Tip'ler (ParentId = id) de aynı işlemde pasifleşir.
        cmd.CommandText = $"""
            UPDATE {Table}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id OR [ParentId] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ordinal: 0=Id, 1=ParentId, 2=Name, 3=SortOrder, 4=IsActive,
    //          5=CreatedById, 6=Created, 7=UpdatedById, 8=Updated
    private static DocumentCategory MapRow(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        ParentId    = r.IsDBNull(1) ? null : r.GetInt32(1),
        Name        = r.GetString(2),
        SortOrder   = r.GetInt32(3),
        IsActive    = r.GetBoolean(4),
        CreatedById = r.IsDBNull(5) ? null : r.GetInt32(5),
        Created     = r.GetDateTime(6),
        UpdatedById = r.IsDBNull(7) ? null : r.GetInt32(7),
        Updated     = r.IsDBNull(8) ? null : r.GetDateTime(8),
    };
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Calibra master DB'deki Attachment tablosuna erişir.
/// OpenSystemConnectionAsync kullanır — per-company routing'den bağımsızdır.
/// FormId + RefId (INT) polimorfik tasarım. Category + Tags: 2026-06-27.
/// </summary>
public sealed class SqlAttachmentRepository : IAttachmentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private const string Table = "[dbo].[Attachment]";

    // SELECT sütun listesi — ordinal index'ler MapRow ile eşleşmeli
    private const string SelectCols = """
        [Id],[FormId],[RefId],[Title],[Category],[Tags],[FileName],
        [ContentType],[FileSize],[Description],[RevisionNumber],[OriginalId],
        [IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
        """;

    public SqlAttachmentRepository(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyCollection<Attachment>> GetByFormRefAsync(int formId, int refId, CancellationToken ct)
    {
        var list = new List<Attachment>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {Table}
            WHERE [FormId] = @FormId AND [RefId] = @RefId AND [IsActive] = 1
            ORDER BY [Created];
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));
        cmd.Parameters.Add(new SqlParameter("@RefId",  refId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<int>> GetRefIdsWithAttachmentAsync(int formId, CancellationToken ct)
    {
        var set = new List<int>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT DISTINCT [RefId] FROM {Table} WHERE [FormId] = @FormId AND [IsActive] = 1;";
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            set.Add(reader.GetInt32(0));
        return set;
    }

    public async Task<Attachment?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM {Table} WHERE [Id] = @Id;";
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
                ([FormId],[RefId],[Title],[Category],[Tags],[FileName],
                 [ContentType],[FileSize],[Description],
                 [RevisionNumber],[OriginalId],[IsActive],
                 [CreatedById],[Created],[BinaryContent])
            VALUES
                (@FormId,@RefId,@Title,@Category,@Tags,@FileName,
                 @ContentType,@FileSize,@Description,
                 @RevisionNumber,@OriginalId,1,
                 @CreatedById,@Created,@BinaryContent);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId",         attachment.FormId));
        cmd.Parameters.Add(new SqlParameter("@RefId",          attachment.RefId));
        cmd.Parameters.Add(new SqlParameter("@Title",          (object?)attachment.Title          ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Category",       (object?)attachment.Category       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Tags",           (object?)attachment.Tags           ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FileName",       attachment.FileName));
        cmd.Parameters.Add(new SqlParameter("@ContentType",    (object?)attachment.ContentType    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FileSize",       attachment.FileSize));
        cmd.Parameters.Add(new SqlParameter("@Description",    (object?)attachment.Description    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RevisionNumber", attachment.RevisionNumber));
        cmd.Parameters.Add(new SqlParameter("@OriginalId",     (object?)attachment.OriginalId     ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedById",    (object?)attachment.CreatedById    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created",        attachment.Created));
        cmd.Parameters.Add(new SqlParameter("@BinaryContent",  (object?)attachment.BinaryContent  ?? DBNull.Value)
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

    public async Task DeleteByFormRefAsync(int formId, int refId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {Table}
            SET [IsActive] = 0, [Updated] = SYSUTCDATETIME()
            WHERE [FormId] = @FormId AND [RefId] = @RefId AND [IsActive] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));
        cmd.Parameters.Add(new SqlParameter("@RefId",  refId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<Attachment>> GetAllActiveAsync(int? formIdFilter, CancellationToken ct)
    {
        var list = new List<Attachment>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {Table}
            WHERE [IsActive] = 1
              AND (@FormId IS NULL OR [FormId] = @FormId)
            ORDER BY [Created] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", (object?)formIdFilter ?? DBNull.Value));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));
        return list;
    }

    public async Task UpdateMetaAsync(int id, string? title, string? description, string? category, string? tags, int? updatedById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {Table}
            SET [Title]       = @Title,
                [Description] = @Description,
                [Category]    = @Category,
                [Tags]        = @Tags,
                [UpdatedById] = @UpdatedById,
                [Updated]     = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Title",       (object?)title       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Description", (object?)description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Category",    (object?)category    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Tags",        (object?)tags        ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)updatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Id",          id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<Attachment>> GetVersionHistoryAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        // Kök ID bul: OriginalId = null ise bu kök, değilse OriginalId kök.
        // Aynı zincirdeki tüm versiyonları getir (IsActive 0 olanlar dahil).
        cmd.CommandText = $"""
            DECLARE @RootId INT = (
                SELECT COALESCE([OriginalId], @Id)
                FROM {Table} WHERE [Id] = @Id
            );
            SELECT {SelectCols}
            FROM {Table}
            WHERE ([Id] = @RootId OR [OriginalId] = @RootId)
            ORDER BY [RevisionNumber] DESC, [Created] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        var list = new List<Attachment>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));
        return list;
    }

    // ordinal: 0=Id, 1=FormId, 2=RefId, 3=Title, 4=Category, 5=Tags, 6=FileName,
    //          7=ContentType, 8=FileSize, 9=Description, 10=RevisionNumber, 11=OriginalId,
    //          12=IsActive, 13=CreatedById, 14=Created, 15=UpdatedById, 16=Updated
    private static Attachment MapRow(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        FormId         = r.GetInt32(1),
        RefId          = r.GetInt32(2),
        Title          = r.IsDBNull(3)  ? null : r.GetString(3),
        Category       = r.IsDBNull(4)  ? null : r.GetString(4),
        Tags           = r.IsDBNull(5)  ? null : r.GetString(5),
        FileName       = r.GetString(6),
        ContentType    = r.IsDBNull(7)  ? null : r.GetString(7),
        FileSize       = r.GetInt64(8),
        Description    = r.IsDBNull(9)  ? null : r.GetString(9),
        RevisionNumber = r.GetInt16(10),
        OriginalId     = r.IsDBNull(11) ? null : r.GetInt32(11),
        IsActive       = r.GetBoolean(12),
        CreatedById    = r.IsDBNull(13) ? null : r.GetInt32(13),
        Created        = r.GetDateTime(14),
        UpdatedById    = r.IsDBNull(15) ? null : r.GetInt32(15),
        Updated        = r.IsDBNull(16) ? null : r.GetDateTime(16),
    };
}

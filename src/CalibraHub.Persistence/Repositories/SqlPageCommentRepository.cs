using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Sayfa-içi Yorum sistemi — ham ADO.NET erişimi (SqlNoteRepository ile aynı desen).
/// Tablolar pc-db tarafından kuruluyor (KİLİTLİ ŞEMA, bu sınıf migration YAZMAZ):
/// PageComment / PageCommentRevision / PageCommentImage / PageCommentActivity.
/// </summary>
public sealed class SqlPageCommentRepository : IPageCommentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _commentTable;
    private readonly string _revisionTable;
    private readonly string _imageTable;
    private readonly string _activityTable;

    public SqlPageCommentRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _commentTable  = $"[{schema}].[PageComment]";
        _revisionTable = $"[{schema}].[PageCommentRevision]";
        _imageTable    = $"[{schema}].[PageCommentImage]";
        _activityTable = $"[{schema}].[PageCommentActivity]";
    }

    private const string CommentColumns = """
        [Id],[Seq],[ScreenKey],[PageTitle],[FormCode],[Text],[Author],[Status],
        [AppliedVersion],[ResolvedBy],[ResolvedAt],[DevNote],[ClientCreatedAt],
        [Created],[CreatedBy],[Updated],[UpdatedBy]
        """;

    public async Task<IReadOnlyList<PageComment>> ListAllAsync(CancellationToken ct)
    {
        var list = new List<PageComment>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {CommentColumns}
            FROM {_commentTable}
            ORDER BY [Seq] ASC;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapComment(reader));
        return list;
    }

    public async Task<IReadOnlyList<PageComment>> ListByStatusAsync(string status, CancellationToken ct)
    {
        var list = new List<PageComment>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {CommentColumns}
            FROM {_commentTable}
            WHERE [Status] = @Status
            ORDER BY [Seq] ASC;
            """;
        command.Parameters.Add(new SqlParameter("@Status", status));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapComment(reader));
        return list;
    }

    public async Task<PageComment?> GetByIdAsync(string id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {CommentColumns}
            FROM {_commentTable}
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapComment(reader) : null;
    }

    public async Task<PageComment> InsertAsync(PageComment comment, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        // [Status] bilerek yazılmıyor — DB DEFAULT 'approved' devreye girsin.
        command.CommandText = $"""
            INSERT INTO {_commentTable}
                ([Id],[ScreenKey],[PageTitle],[FormCode],[Text],[Author],[ClientCreatedAt],[CreatedBy])
            OUTPUT INSERTED.[Seq], INSERTED.[Status], INSERTED.[Created]
            VALUES (@Id,@ScreenKey,@PageTitle,@FormCode,@Text,@Author,@ClientCreatedAt,@CreatedBy);
            """;
        command.Parameters.Add(new SqlParameter("@Id", comment.Id));
        command.Parameters.Add(new SqlParameter("@ScreenKey", comment.ScreenKey));
        command.Parameters.Add(new SqlParameter("@PageTitle", (object?)comment.PageTitle ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@FormCode", (object?)comment.FormCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Text", comment.Text));
        command.Parameters.Add(new SqlParameter("@Author", (object?)comment.Author ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ClientCreatedAt", (object?)comment.ClientCreatedAt ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CreatedBy", (object?)comment.CreatedBy ?? DBNull.Value));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            comment.Seq = reader.GetInt32(0);
            comment.Status = reader.GetString(1);
            comment.Created = reader.GetDateTime(2);
        }
        return comment;
    }

    public async Task<bool> UpdateStatusAsync(PageComment comment, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_commentTable}
            SET [Status] = @Status, [AppliedVersion] = @AppliedVersion, [ResolvedBy] = @ResolvedBy,
                [ResolvedAt] = @ResolvedAt, [DevNote] = @DevNote,
                [Updated] = SYSUTCDATETIME(), [UpdatedBy] = @UpdatedBy
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Status", comment.Status));
        command.Parameters.Add(new SqlParameter("@AppliedVersion", (object?)comment.AppliedVersion ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ResolvedBy", (object?)comment.ResolvedBy ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ResolvedAt", (object?)comment.ResolvedAt ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DevNote", (object?)comment.DevNote ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UpdatedBy", (object?)comment.UpdatedBy ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Id", comment.Id));
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> UpdateTextAsync(string id, string text, string? updatedBy, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_commentTable}
            SET [Text] = @Text, [Updated] = SYSUTCDATETIME(), [UpdatedBy] = @UpdatedBy
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Text", text));
        command.Parameters.Add(new SqlParameter("@UpdatedBy", (object?)updatedBy ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Id", id));
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_commentTable} WHERE [Id] = @Id;"; // CASCADE → Revision + Image de gider
        command.Parameters.Add(new SqlParameter("@Id", id));
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task AddRevisionAsync(PageCommentRevision revision, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_revisionTable} ([CommentId],[Status],[Version],[Note],[UserName],[ClientCreatedAt])
            VALUES (@CommentId,@Status,@Version,@Note,@UserName,@ClientCreatedAt);
            """;
        command.Parameters.Add(new SqlParameter("@CommentId", revision.CommentId));
        command.Parameters.Add(new SqlParameter("@Status", (object?)revision.Status ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Version", (object?)revision.Version ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Note", (object?)revision.Note ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UserName", (object?)revision.UserName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ClientCreatedAt", (object?)revision.ClientCreatedAt ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PageCommentRevision>> ListAllRevisionsAsync(CancellationToken ct)
    {
        var list = new List<PageCommentRevision>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],[CommentId],[Status],[Version],[Note],[UserName],[ClientCreatedAt],[Created]
            FROM {_revisionTable}
            ORDER BY [CommentId], [Id] ASC;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapRevision(reader));
        return list;
    }

    public async Task<IReadOnlyList<PageCommentRevision>> ListRevisionsForCommentAsync(string commentId, CancellationToken ct)
    {
        var list = new List<PageCommentRevision>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],[CommentId],[Status],[Version],[Note],[UserName],[ClientCreatedAt],[Created]
            FROM {_revisionTable}
            WHERE [CommentId] = @CommentId
            ORDER BY [Id] ASC;
            """;
        command.Parameters.Add(new SqlParameter("@CommentId", commentId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapRevision(reader));
        return list;
    }

    public async Task AddImageAsync(PageCommentImage image, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_imageTable} ([Id],[CommentId],[Mime],[Name],[Bytes],[ClientCreatedAt])
            VALUES (@Id,@CommentId,@Mime,@Name,@Bytes,@ClientCreatedAt);
            """;
        command.Parameters.Add(new SqlParameter("@Id", image.Id));
        command.Parameters.Add(new SqlParameter("@CommentId", image.CommentId));
        command.Parameters.Add(new SqlParameter("@Mime", (object?)image.Mime ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Name", (object?)image.Name ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Bytes", (object?)image.Bytes ?? DBNull.Value)
        {
            SqlDbType = System.Data.SqlDbType.VarBinary
        });
        command.Parameters.Add(new SqlParameter("@ClientCreatedAt", (object?)image.ClientCreatedAt ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PageCommentImage>> ListAllImageMetaAsync(CancellationToken ct)
    {
        var list = new List<PageCommentImage>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],[CommentId],[Mime],[Name],[ClientCreatedAt],[Created]
            FROM {_imageTable}
            ORDER BY [CommentId], [Created] ASC;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapImageMeta(reader));
        return list;
    }

    public async Task<IReadOnlyList<PageCommentImage>> ListImageMetaForCommentAsync(string commentId, CancellationToken ct)
    {
        var list = new List<PageCommentImage>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],[CommentId],[Mime],[Name],[ClientCreatedAt],[Created]
            FROM {_imageTable}
            WHERE [CommentId] = @CommentId
            ORDER BY [Created] ASC;
            """;
        command.Parameters.Add(new SqlParameter("@CommentId", commentId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapImageMeta(reader));
        return list;
    }

    public async Task<PageCommentImage?> GetImageBinaryAsync(string id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],[CommentId],[Mime],[Name],[Bytes],[ClientCreatedAt],[Created]
            FROM {_imageTable}
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new PageCommentImage
        {
            Id = reader.GetString(0),
            CommentId = reader.GetString(1),
            Mime = reader.IsDBNull(2) ? null : reader.GetString(2),
            Name = reader.IsDBNull(3) ? null : reader.GetString(3),
            Bytes = reader.IsDBNull(4) ? null : (byte[])reader[4],
            ClientCreatedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
            Created = reader.GetDateTime(6),
        };
    }

    public async Task<bool> DeleteImageAsync(string id, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_imageTable} WHERE [Id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task AddActivityAsync(PageCommentActivity activity, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_activityTable} ([Action],[ScreenKey],[Detail],[ClientCreatedAt])
            VALUES (@Action,@ScreenKey,@Detail,@ClientCreatedAt);
            """;
        command.Parameters.Add(new SqlParameter("@Action", activity.Action));
        command.Parameters.Add(new SqlParameter("@ScreenKey", (object?)activity.ScreenKey ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Detail", (object?)activity.Detail ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ClientCreatedAt", (object?)activity.ClientCreatedAt ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PageCommentActivity>> ListActivityAsync(int take, CancellationToken ct)
    {
        var list = new List<PageCommentActivity>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Take) [Id],[Action],[ScreenKey],[Detail],[ClientCreatedAt],[Created]
            FROM {_activityTable}
            ORDER BY [Created] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@Take", take));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapActivity(reader));
        return list;
    }

    // ── Mapping ──────────────────────────────────────────────────────────

    private static PageComment MapComment(SqlDataReader r) => new()
    {
        Id = r.GetString(0),
        Seq = r.GetInt32(1),
        ScreenKey = r.GetString(2),
        PageTitle = r.IsDBNull(3) ? null : r.GetString(3),
        FormCode = r.IsDBNull(4) ? null : r.GetString(4),
        Text = r.GetString(5),
        Author = r.IsDBNull(6) ? null : r.GetString(6),
        Status = r.GetString(7),
        AppliedVersion = r.IsDBNull(8) ? null : r.GetString(8),
        ResolvedBy = r.IsDBNull(9) ? null : r.GetString(9),
        ResolvedAt = r.IsDBNull(10) ? null : r.GetString(10),
        DevNote = r.IsDBNull(11) ? null : r.GetString(11),
        ClientCreatedAt = r.IsDBNull(12) ? null : r.GetString(12),
        Created = r.GetDateTime(13),
        CreatedBy = r.IsDBNull(14) ? null : r.GetString(14),
        Updated = r.IsDBNull(15) ? null : r.GetDateTime(15),
        UpdatedBy = r.IsDBNull(16) ? null : r.GetString(16),
    };

    private static PageCommentRevision MapRevision(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CommentId = r.GetString(1),
        Status = r.IsDBNull(2) ? null : r.GetString(2),
        Version = r.IsDBNull(3) ? null : r.GetString(3),
        Note = r.IsDBNull(4) ? null : r.GetString(4),
        UserName = r.IsDBNull(5) ? null : r.GetString(5),
        ClientCreatedAt = r.IsDBNull(6) ? null : r.GetString(6),
        Created = r.GetDateTime(7),
    };

    private static PageCommentImage MapImageMeta(SqlDataReader r) => new()
    {
        Id = r.GetString(0),
        CommentId = r.GetString(1),
        Mime = r.IsDBNull(2) ? null : r.GetString(2),
        Name = r.IsDBNull(3) ? null : r.GetString(3),
        ClientCreatedAt = r.IsDBNull(4) ? null : r.GetString(4),
        Created = r.GetDateTime(5),
    };

    private static PageCommentActivity MapActivity(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Action = r.GetString(1),
        ScreenKey = r.IsDBNull(2) ? null : r.GetString(2),
        Detail = r.IsDBNull(3) ? null : r.GetString(3),
        ClientCreatedAt = r.IsDBNull(4) ? null : r.GetString(4),
        Created = r.GetDateTime(5),
    };
}

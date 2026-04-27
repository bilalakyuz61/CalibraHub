using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlNoteRepository : INoteRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly INoteEncryptionService _encryption;
    private readonly string _notesTable;
    private readonly string _remindersTable;
    private readonly string _sharesTable;
    private readonly string _foldersTable;
    private readonly string _attachmentsTable;

    public SqlNoteRepository(
        SqlServerConnectionFactory connectionFactory,
        INoteEncryptionService encryption,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _encryption = encryption;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _notesTable = $"[{schema}].[notes]";
        _remindersTable = $"[{schema}].[note_reminders]";
        _sharesTable = $"[{schema}].[note_shares]";
        _foldersTable = $"[{schema}].[note_folders]";
        _attachmentsTable = $"[{schema}].[note_attachments]";
    }

    public async Task<IReadOnlyCollection<Note>> GetByUserAsync(int companyId, Guid userId, Guid? folderId, CancellationToken cancellationToken)
    {
        var notes = new List<Note>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (folderId.HasValue)
        {
            command.CommandText = $"""
                SELECT n.[id], n.[company_id], n.[user_id], n.[title], n.[content], n.[created_at], n.[updated_at], n.[folder_id], n.[is_pinned], n.[is_fully_encrypted], n.[encryption_hint]
                FROM {_notesTable} n
                WHERE n.[is_deleted] = 0
                  AND n.[company_id] = @CompanyId
                  AND n.[user_id] = @UserId
                  AND n.[folder_id] = @FolderId
                ORDER BY n.[updated_at] DESC;
                """;
            command.Parameters.Add(new SqlParameter("@FolderId", folderId.Value));
        }
        else
        {
            command.CommandText = $"""
                SELECT n.[id], n.[company_id], n.[user_id], n.[title], n.[content], n.[created_at], n.[updated_at], n.[folder_id], n.[is_pinned], n.[is_fully_encrypted], n.[encryption_hint]
                FROM {_notesTable} n
                WHERE n.[is_deleted] = 0
                  AND n.[company_id] = @CompanyId
                  AND (n.[user_id] = @UserId
                       OR EXISTS (SELECT 1 FROM {_sharesTable} s WHERE s.[note_id] = n.[id] AND s.[shared_with_user_id] = @UserId))
                ORDER BY n.[updated_at] DESC;
                """;
        }

        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            notes.Add(MapNote(reader));
        }

        return notes;
    }

    public async Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [user_id], [title], [content], [created_at], [updated_at], [folder_id], [is_pinned], [is_fully_encrypted], [encryption_hint]
            FROM {_notesTable}
            WHERE [id] = @Id AND [is_deleted] = 0;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return MapNote(reader);
    }

    public async Task SaveAsync(Note note, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_notesTable} WHERE [id] = @Id)
                UPDATE {_notesTable}
                SET [title] = @Title, [content] = @Content, [folder_id] = @FolderId,
                    [updated_at] = @UpdatedAt, [is_pinned] = @IsPinned,
                    [is_fully_encrypted] = @IsFullyEncrypted, [encryption_hint] = @EncryptionHint
                WHERE [id] = @Id;
            ELSE
                INSERT INTO {_notesTable}
                    ([id], [company_id], [user_id], [title], [content], [folder_id],
                     [created_at], [updated_at], [is_deleted], [is_pinned],
                     [is_fully_encrypted], [encryption_hint])
                VALUES
                    (@Id, @CompanyId, @UserId, @Title, @Content, @FolderId,
                     @CreatedAt, @UpdatedAt, 0, @IsPinned,
                     @IsFullyEncrypted, @EncryptionHint);
            """;
        command.Parameters.Add(new SqlParameter("@Id", note.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", note.CompanyId));
        command.Parameters.Add(new SqlParameter("@UserId", note.UserId));
        command.Parameters.Add(new SqlParameter("@Title", note.Title));
        // Content AES-Protect — DB'ye her zaman sifreli yazar (Katman 2 at-rest sifreleme).
        // Not: Mod 2 (E2E) durumunda icerik zaten client-side sifreli geliyor; uzerine bir kat
        // daha app-level AES uygulanir (defense-in-depth).
        var protectedContent = _encryption.Protect(note.Content);
        command.Parameters.Add(new SqlParameter("@Content", (object?)protectedContent ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@FolderId", (object?)note.FolderId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CreatedAt", note.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", note.UpdatedAt));
        command.Parameters.Add(new SqlParameter("@IsPinned", note.IsPinned));
        command.Parameters.Add(new SqlParameter("@IsFullyEncrypted", note.IsFullyEncrypted));
        command.Parameters.Add(new SqlParameter("@EncryptionHint", (object?)note.EncryptionHint ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TogglePinAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_notesTable}
            SET [is_pinned] = CASE WHEN [is_pinned] = 1 THEN 0 ELSE 1 END
            WHERE [id] = @Id AND [user_id] = @UserId;
            SELECT [is_pinned] FROM {_notesTable} WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_notesTable} SET [is_deleted] = 1, [updated_at] = @Now WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<NoteReminder>> GetRemindersAsync(Guid noteId, CancellationToken cancellationToken)
    {
        var reminders = new List<NoteReminder>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [note_id], [remind_at], [is_sent], [sent_at], [recurrence_type], [recurrence_data],
                   [delivery_channel], [target_user_id]
            FROM {_remindersTable}
            WHERE [note_id] = @NoteId
            ORDER BY [remind_at];
            """;
        command.Parameters.Add(new SqlParameter("@NoteId", noteId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            reminders.Add(MapReminder(reader));
        }

        return reminders;
    }

    public async Task AddReminderAsync(NoteReminder reminder, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_remindersTable}
                ([id], [note_id], [remind_at], [is_sent], [sent_at], [recurrence_type], [recurrence_data],
                 [delivery_channel], [target_user_id])
            VALUES (@Id, @NoteId, @RemindAt, 0, NULL, @RecurrenceType, @RecurrenceData,
                    @DeliveryChannel, @TargetUserId);
            """;
        command.Parameters.Add(new SqlParameter("@Id", reminder.Id));
        command.Parameters.Add(new SqlParameter("@NoteId", reminder.NoteId));
        command.Parameters.Add(new SqlParameter("@RemindAt", reminder.RemindAt));
        command.Parameters.Add(new SqlParameter("@RecurrenceType", (int)reminder.RecurrenceType));
        command.Parameters.Add(new SqlParameter("@RecurrenceData", (object?)reminder.RecurrenceData ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DeliveryChannel", (int)reminder.DeliveryChannel));
        command.Parameters.Add(new SqlParameter("@TargetUserId", (object?)reminder.TargetUserId ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteReminderAsync(Guid reminderId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_remindersTable} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", reminderId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetActiveReminderCountsAsync(
        IReadOnlyCollection<Guid> noteIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, int>();
        if (noteIds.Count == 0) return result;

        // SQL Server parametre limiti 2100 — not sayisi cok daha az olur, tek batch yeterli.
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var paramNames = noteIds
            .Select((_, i) => "@N" + i)
            .ToArray();
        command.CommandText = $@"
            SELECT [note_id], COUNT(*) AS [cnt]
            FROM {_remindersTable}
            WHERE [is_sent] = 0
              AND [note_id] IN ({string.Join(",", paramNames)})
            GROUP BY [note_id];";

        var idx = 0;
        foreach (var id in noteIds)
        {
            command.Parameters.Add(new SqlParameter(paramNames[idx++], id));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetGuid(0)] = reader.GetInt32(1);
        }
        return result;
    }

    public async Task<IReadOnlyCollection<NoteShare>> GetSharesAsync(Guid noteId, CancellationToken cancellationToken)
    {
        var shares = new List<NoteShare>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [note_id], [shared_with_user_id], [shared_at]
            FROM {_sharesTable}
            WHERE [note_id] = @NoteId
            ORDER BY [shared_at];
            """;
        command.Parameters.Add(new SqlParameter("@NoteId", noteId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            shares.Add(new NoteShare
            {
                Id = reader.GetGuid(0),
                NoteId = reader.GetGuid(1),
                SharedWithUserId = reader.GetGuid(2),
                SharedAt = reader.GetDateTime(3)
            });
        }

        return shares;
    }

    public async Task AddShareAsync(NoteShare share, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF NOT EXISTS (SELECT 1 FROM {_sharesTable} WHERE [note_id] = @NoteId AND [shared_with_user_id] = @SharedWithUserId)
                INSERT INTO {_sharesTable} ([id], [note_id], [shared_with_user_id], [shared_at])
                VALUES (@Id, @NoteId, @SharedWithUserId, @SharedAt);
            """;
        command.Parameters.Add(new SqlParameter("@Id", share.Id));
        command.Parameters.Add(new SqlParameter("@NoteId", share.NoteId));
        command.Parameters.Add(new SqlParameter("@SharedWithUserId", share.SharedWithUserId));
        command.Parameters.Add(new SqlParameter("@SharedAt", share.SharedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteShareAsync(Guid shareId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_sharesTable} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", shareId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<(NoteReminder Reminder, Note Note)>> GetUnsentDueRemindersAsync(CancellationToken cancellationToken)
    {
        var results = new List<(NoteReminder, Note)>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.[id], r.[note_id], r.[remind_at], r.[recurrence_type], r.[recurrence_data],
                   r.[delivery_channel], r.[target_user_id],
                   n.[id], n.[company_id], n.[user_id], n.[title], n.[content], n.[created_at], n.[updated_at], n.[folder_id]
            FROM {_remindersTable} r
            INNER JOIN {_notesTable} n ON r.[note_id] = n.[id]
            WHERE r.[is_sent] = 0
              AND r.[remind_at] <= @Now
              AND n.[is_deleted] = 0;
            """;
        command.Parameters.Add(new SqlParameter("@Now", DateTime.Now));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var reminder = new NoteReminder
            {
                Id = reader.GetGuid(0),
                NoteId = reader.GetGuid(1),
                RemindAt = reader.GetDateTime(2),
                RecurrenceType = (ReminderRecurrenceType)reader.GetInt32(3),
                RecurrenceData = reader.IsDBNull(4) ? null : reader.GetString(4),
                DeliveryChannel = (ReminderDeliveryChannel)reader.GetInt32(5),
                TargetUserId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
            };
            var rawReminderContent = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);
            var note = new Note
            {
                Id = reader.GetGuid(7),
                CompanyId = reader.GetInt32(8),
                UserId = reader.GetGuid(9),
                Title = reader.GetString(10),
                Content = _encryption.Unprotect(rawReminderContent) ?? string.Empty,
                CreatedAt = reader.GetDateTime(12),
                UpdatedAt = reader.GetDateTime(13),
                FolderId = reader.IsDBNull(14) ? null : reader.GetGuid(14)
            };
            results.Add((reminder, note));
        }

        return results;
    }

    public async Task MarkReminderSentAsync(Guid reminderId, DateTime sentAt, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_remindersTable} SET [is_sent] = 1, [sent_at] = @SentAt WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", reminderId));
        command.Parameters.Add(new SqlParameter("@SentAt", sentAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<NoteFolder>> GetFoldersAsync(int companyId, Guid userId, CancellationToken cancellationToken)
    {
        var folders = new List<NoteFolder>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [user_id], [name], [parent_folder_id], [created_at]
            FROM {_foldersTable}
            WHERE [company_id] = @CompanyId AND [user_id] = @UserId AND [is_deleted] = 0
            ORDER BY [name];
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            folders.Add(new NoteFolder
            {
                Id = reader.GetGuid(0),
                CompanyId = reader.GetInt32(1),
                UserId = reader.GetGuid(2),
                Name = reader.GetString(3),
                ParentFolderId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
                CreatedAt = reader.GetDateTime(5)
            });
        }

        return folders;
    }

    public async Task SaveFolderAsync(NoteFolder folder, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_foldersTable} WHERE [id] = @Id)
                UPDATE {_foldersTable} SET [name] = @Name WHERE [id] = @Id;
            ELSE
                INSERT INTO {_foldersTable} ([id], [company_id], [user_id], [name], [parent_folder_id], [created_at], [is_deleted])
                VALUES (@Id, @CompanyId, @UserId, @Name, @ParentFolderId, @CreatedAt, 0);
            """;
        command.Parameters.Add(new SqlParameter("@Id", folder.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", folder.CompanyId));
        command.Parameters.Add(new SqlParameter("@UserId", folder.UserId));
        command.Parameters.Add(new SqlParameter("@Name", folder.Name));
        command.Parameters.Add(new SqlParameter("@ParentFolderId", (object?)folder.ParentFolderId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CreatedAt", folder.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Note>> GetTrashedAsync(int companyId, Guid userId, CancellationToken cancellationToken)
    {
        var notes = new List<Note>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [user_id], [title], [content], [created_at], [updated_at], [folder_id], [is_pinned], [is_fully_encrypted], [encryption_hint]
            FROM {_notesTable}
            WHERE [is_deleted] = 1 AND [company_id] = @CompanyId AND [user_id] = @UserId
            ORDER BY [updated_at] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            notes.Add(MapNote(reader));
        return notes;
    }

    public async Task<int> GetTrashedCountAsync(int companyId, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {_notesTable} WHERE [is_deleted] = 1 AND [company_id] = @CompanyId AND [user_id] = @UserId;";
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task RestoreNoteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_notesTable} SET [is_deleted] = 0, [updated_at] = @Now WHERE [id] = @Id AND [user_id] = @UserId;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        command.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PermanentDeleteNoteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE r FROM {_remindersTable} r
            INNER JOIN {_notesTable} n ON r.[note_id] = n.[id]
            WHERE n.[id] = @Id AND n.[user_id] = @UserId;
            DELETE s FROM {_sharesTable} s
            INNER JOIN {_notesTable} n ON s.[note_id] = n.[id]
            WHERE n.[id] = @Id AND n.[user_id] = @UserId;
            DELETE FROM {_notesTable} WHERE [id] = @Id AND [user_id] = @UserId;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EmptyTrashAsync(int companyId, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE r FROM {_remindersTable} r
            INNER JOIN {_notesTable} n ON r.[note_id] = n.[id]
            WHERE n.[is_deleted] = 1 AND n.[company_id] = @CompanyId AND n.[user_id] = @UserId;
            DELETE s FROM {_sharesTable} s
            INNER JOIN {_notesTable} n ON s.[note_id] = n.[id]
            WHERE n.[is_deleted] = 1 AND n.[company_id] = @CompanyId AND n.[user_id] = @UserId;
            DELETE FROM {_notesTable}
            WHERE [is_deleted] = 1 AND [company_id] = @CompanyId AND [user_id] = @UserId;
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_foldersTable} SET [name] = @Name WHERE [id] = @Id";
        command.Parameters.Add(new SqlParameter("@Name", name));
        command.Parameters.Add(new SqlParameter("@Id", folderId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @ParentId UNIQUEIDENTIFIER = (SELECT [parent_folder_id] FROM {_foldersTable} WHERE [id] = @FolderId);
            UPDATE {_notesTable} SET [folder_id] = NULL WHERE [folder_id] = @FolderId;
            UPDATE {_foldersTable} SET [parent_folder_id] = @ParentId WHERE [parent_folder_id] = @FolderId;
            UPDATE {_foldersTable} SET [is_deleted] = 1 WHERE [id] = @FolderId;
            """;
        command.Parameters.Add(new SqlParameter("@FolderId", folderId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<NoteAttachment>> GetAttachmentsAsync(Guid noteId, CancellationToken cancellationToken)
    {
        var list = new List<NoteAttachment>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [note_id], [file_name], [stored_name], [content_type], [file_size], [uploaded_at], [description]
            FROM {_attachmentsTable}
            WHERE [note_id] = @NoteId
            ORDER BY [uploaded_at];
            """;
        command.Parameters.Add(new SqlParameter("@NoteId", noteId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(MapAttachment(reader));
        return list;
    }

    public async Task<NoteAttachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [note_id], [file_name], [stored_name], [content_type], [file_size], [uploaded_at], [description]
            FROM {_attachmentsTable}
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", attachmentId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapAttachment(reader) : null;
    }

    public async Task AddAttachmentAsync(NoteAttachment attachment, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_attachmentsTable}
                ([id], [note_id], [file_name], [stored_name], [content_type], [file_size], [uploaded_at], [description])
            VALUES
                (@Id, @NoteId, @FileName, @StoredName, @ContentType, @FileSize, @UploadedAt, @Description);
            """;
        command.Parameters.Add(new SqlParameter("@Id", attachment.Id));
        command.Parameters.Add(new SqlParameter("@NoteId", attachment.NoteId));
        command.Parameters.Add(new SqlParameter("@FileName", attachment.FileName));
        command.Parameters.Add(new SqlParameter("@StoredName", attachment.StoredName));
        command.Parameters.Add(new SqlParameter("@ContentType", (object?)attachment.ContentType ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@FileSize", attachment.FileSize));
        command.Parameters.Add(new SqlParameter("@UploadedAt", attachment.UploadedAt));
        command.Parameters.Add(new SqlParameter("@Description", (object?)attachment.Description ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_attachmentsTable} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", attachmentId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAllAttachmentsAsync(Guid noteId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_attachmentsTable} WHERE [note_id] = @NoteId;";
        command.Parameters.Add(new SqlParameter("@NoteId", noteId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NoteAttachment MapAttachment(SqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        NoteId = reader.GetGuid(1),
        FileName = reader.GetString(2),
        StoredName = reader.GetString(3),
        ContentType = reader.IsDBNull(4) ? null : reader.GetString(4),
        FileSize = reader.GetInt64(5),
        UploadedAt = reader.GetDateTime(6),
        Description = reader.IsDBNull(7) ? null : reader.GetString(7)
    };

    private Note MapNote(SqlDataReader reader)
    {
        var rawContent = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        return new Note
        {
            Id = reader.GetGuid(0),
            CompanyId = reader.GetInt32(1),
            UserId = reader.GetGuid(2),
            Title = reader.GetString(3),
            // At-rest sifrelenmis icerigi coz (eski duz metin kayitlarda aynen doner)
            Content = _encryption.Unprotect(rawContent) ?? string.Empty,
            CreatedAt = reader.GetDateTime(5),
            UpdatedAt = reader.GetDateTime(6),
            FolderId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            IsPinned = !reader.IsDBNull(8) && reader.GetBoolean(8),
            IsFullyEncrypted = reader.FieldCount > 9 && !reader.IsDBNull(9) && reader.GetBoolean(9),
            EncryptionHint = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : null
        };
    }

    private static NoteReminder MapReminder(SqlDataReader reader)
    {
        // Kolon sirasi: id, note_id, remind_at, is_sent, sent_at, recurrence_type, recurrence_data,
        //               delivery_channel, target_user_id
        var reminder = new NoteReminder
        {
            Id = reader.GetGuid(0),
            NoteId = reader.GetGuid(1),
            RemindAt = reader.GetDateTime(2),
            RecurrenceType = (ReminderRecurrenceType)reader.GetInt32(5),
            RecurrenceData = reader.IsDBNull(6) ? null : reader.GetString(6),
            DeliveryChannel = reader.FieldCount > 7 && !reader.IsDBNull(7)
                ? (ReminderDeliveryChannel)reader.GetInt32(7)
                : ReminderDeliveryChannel.InApp,
            TargetUserId = reader.FieldCount > 8 && !reader.IsDBNull(8)
                ? reader.GetGuid(8)
                : (Guid?)null,
        };
        if (reader.GetBoolean(3))
        {
            reminder.MarkSent(reader.IsDBNull(4) ? DateTime.Now : reader.GetDateTime(4));
        }
        return reminder;
    }
}

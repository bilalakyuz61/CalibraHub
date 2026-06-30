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
    private readonly string _reminderTargetsTable;

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
        _reminderTargetsTable = $"[{schema}].[note_reminder_targets]";
    }

    public async Task<IReadOnlyCollection<Note>> GetByUserAsync(int companyId, int userId, Guid? folderId, CancellationToken cancellationToken)
    {
        var notes = new List<Note>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (folderId.HasValue)
        {
            command.CommandText = $"""
                SELECT n.[Id], n.[CompanyId], n.[UserId], n.[Title], n.[Content], n.[Created], n.[Updated], n.[FolderId], n.[IsPinned], n.[IsFullyEncrypted], n.[EncryptionHint], n.[Tags], n.[linked_entity_type], n.[linked_entity_id], n.[linked_entity_label], n.[visibility], n.[share_token], n.[share_is_public], n.[share_include_attachments], n.[ocr_text]
                FROM {_notesTable} n
                WHERE n.[IsDeleted] = 0
                  AND n.[CompanyId] = @CompanyId
                  AND n.[UserId] = @UserId
                  AND n.[FolderId] = @FolderId
                ORDER BY n.[Updated] DESC;
                """;
            command.Parameters.Add(new SqlParameter("@FolderId", folderId.Value));
        }
        else
        {
            command.CommandText = $"""
                SELECT n.[Id], n.[CompanyId], n.[UserId], n.[Title], n.[Content], n.[Created], n.[Updated], n.[FolderId], n.[IsPinned], n.[IsFullyEncrypted], n.[EncryptionHint], n.[Tags], n.[linked_entity_type], n.[linked_entity_id], n.[linked_entity_label], n.[visibility], n.[share_token], n.[share_is_public], n.[share_include_attachments], n.[ocr_text]
                FROM {_notesTable} n
                WHERE n.[IsDeleted] = 0
                  AND n.[CompanyId] = @CompanyId
                  AND (n.[UserId] = @UserId
                       OR n.[visibility] = 1
                       OR EXISTS (SELECT 1 FROM {_sharesTable} s WHERE s.[note_id] = n.[Id] AND s.[shared_with_user_id] = @UserId))
                ORDER BY n.[Updated] DESC;
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

    public async Task<IReadOnlyCollection<Note>> GetListByUserAsync(int companyId, int userId, CancellationToken cancellationToken)
    {
        var notes = new List<Note>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT n.[Id], n.[CompanyId], n.[UserId], n.[Title], n.[Created], n.[Updated], n.[FolderId],
                   n.[IsPinned], n.[IsFullyEncrypted], n.[EncryptionHint], n.[Tags],
                   n.[linked_entity_type], n.[linked_entity_id], n.[linked_entity_label],
                   n.[visibility], n.[share_token], n.[share_is_public], n.[share_include_attachments],
                   n.[ocr_text]
            FROM {_notesTable} n
            WHERE n.[IsDeleted] = 0
              AND n.[CompanyId] = @CompanyId
              AND (n.[UserId] = @UserId
                   OR n.[visibility] = 1
                   OR EXISTS (SELECT 1 FROM {_sharesTable} s WHERE s.[note_id] = n.[Id] AND s.[shared_with_user_id] = @UserId))
            ORDER BY n.[Updated] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            notes.Add(MapNoteMetadata(reader));

        return notes;
    }

    public async Task<(string Content, string? OcrText)?> GetContentByIdAsync(Guid noteId, int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT n.[Content], n.[ocr_text]
            FROM {_notesTable} n
            WHERE n.[Id] = @Id AND n.[IsDeleted] = 0
              AND (n.[UserId] = @UserId
                   OR n.[visibility] = 1
                   OR EXISTS (SELECT 1 FROM {_sharesTable} s WHERE s.[note_id] = n.[Id] AND s.[shared_with_user_id] = @UserId));
            """;
        command.Parameters.Add(new SqlParameter("@Id", noteId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var rawContent = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var content = _encryption.Unprotect(rawContent) ?? string.Empty;
        var ocrText = reader.IsDBNull(1) ? null : reader.GetString(1);
        return (content, ocrText);
    }

    public async Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [UserId], [Title], [Content], [Created], [Updated], [FolderId], [IsPinned], [IsFullyEncrypted], [EncryptionHint], [Tags], [linked_entity_type], [linked_entity_id], [linked_entity_label], [visibility]
            FROM {_notesTable}
            WHERE [Id] = @Id AND [IsDeleted] = 0;
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
            IF EXISTS (SELECT 1 FROM {_notesTable} WHERE [Id] = @Id)
                UPDATE {_notesTable}
                SET [Title] = @Title, [Content] = @Content, [FolderId] = @FolderId,
                    [Updated] = @UpdatedAt, [IsPinned] = @IsPinned,
                    [IsFullyEncrypted] = @IsFullyEncrypted, [EncryptionHint] = @EncryptionHint,
                    [Tags] = @Tags,
                    [linked_entity_type] = @LinkedEntityType, [linked_entity_id] = @LinkedEntityId,
                    [linked_entity_label] = @LinkedEntityLabel, [visibility] = @Visibility,
                    [ocr_text] = @OcrText
                WHERE [Id] = @Id;
            ELSE
                INSERT INTO {_notesTable}
                    ([Id], [CompanyId], [UserId], [Title], [Content], [FolderId],
                     [Created], [Updated], [IsDeleted], [IsPinned],
                     [IsFullyEncrypted], [EncryptionHint], [Tags],
                     [linked_entity_type], [linked_entity_id], [linked_entity_label], [visibility],
                     [ocr_text])
                VALUES
                    (@Id, @CompanyId, @UserId, @Title, @Content, @FolderId,
                     @CreatedAt, @UpdatedAt, 0, @IsPinned,
                     @IsFullyEncrypted, @EncryptionHint, @Tags,
                     @LinkedEntityType, @LinkedEntityId, @LinkedEntityLabel, @Visibility,
                     @OcrText);
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
        command.Parameters.Add(new SqlParameter("@Tags", (object?)note.Tags ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LinkedEntityType", (object?)note.LinkedEntityType ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LinkedEntityId", (object?)note.LinkedEntityId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LinkedEntityLabel", (object?)note.LinkedEntityLabel ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Visibility", note.Visibility));
        command.Parameters.Add(new SqlParameter("@OcrText", (object?)note.OcrText ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TogglePinAsync(Guid id, int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_notesTable}
            SET [IsPinned] = CASE WHEN [IsPinned] = 1 THEN 0 ELSE 1 END
            WHERE [Id] = @Id AND [UserId] = @UserId;
            SELECT [IsPinned] FROM {_notesTable} WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_notesTable} SET [IsDeleted] = 1, [Updated] = @Now WHERE [Id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<NoteReminder>> GetRemindersAsync(Guid noteId, CancellationToken cancellationToken)
    {
        var reminders = new List<NoteReminder>();
        var targetMap = new Dictionary<Guid, List<int>>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // 1) Reminders
        await using (var command = connection.CreateCommand())
        {
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
        }

        // 2) Targets (many-to-many)
        if (reminders.Count > 0)
        {
            await using var tCmd = connection.CreateCommand();
            tCmd.CommandText = $"""
                SELECT t.[reminder_id], t.[user_id]
                FROM {_reminderTargetsTable} t
                INNER JOIN {_remindersTable} r ON r.[id] = t.[reminder_id]
                WHERE r.[note_id] = @NoteId;
                """;
            tCmd.Parameters.Add(new SqlParameter("@NoteId", noteId));
            await using var tReader = await tCmd.ExecuteReaderAsync(cancellationToken);
            while (await tReader.ReadAsync(cancellationToken))
            {
                var rid = tReader.GetGuid(0);
                var uid = tReader.GetInt32(1);
                if (!targetMap.TryGetValue(rid, out var list))
                {
                    list = new List<int>();
                    targetMap[rid] = list;
                }
                list.Add(uid);
            }
        }

        // 3) Merge targets into reminder entities
        for (var i = 0; i < reminders.Count; i++)
        {
            var r = reminders[i];
            if (targetMap.TryGetValue(r.Id, out var ids) && ids.Count > 0)
            {
                reminders[i] = CloneWithTargets(r, ids);
            }
        }

        return reminders;
    }

    private static NoteReminder CloneWithTargets(NoteReminder r, IReadOnlyCollection<int> targetIds)
    {
        var cloned = new NoteReminder
        {
            Id              = r.Id,
            NoteId          = r.NoteId,
            RemindAt        = r.RemindAt,
            RecurrenceType  = r.RecurrenceType,
            RecurrenceData  = r.RecurrenceData,
            DeliveryChannel = r.DeliveryChannel,
            TargetUserIds   = targetIds,
        };
        if (r.IsSent) cloned.MarkSent(r.SentAt ?? DateTime.Now);
        return cloned;
    }

    public async Task AddReminderAsync(NoteReminder reminder, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Reminder INSERT + targets INSERT'lerini tek transaction icinde yap
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            command.Transaction = tx;
            command.CommandText = $"""
                INSERT INTO {_remindersTable}
                    ([id], [note_id], [remind_at], [is_sent], [sent_at], [recurrence_type], [recurrence_data],
                     [delivery_channel], [target_user_id])
                VALUES (@Id, @NoteId, @RemindAt, 0, NULL, @RecurrenceType, @RecurrenceData,
                        @DeliveryChannel, NULL);
                """;
            command.Parameters.Add(new SqlParameter("@Id", reminder.Id));
            command.Parameters.Add(new SqlParameter("@NoteId", reminder.NoteId));
            command.Parameters.Add(new SqlParameter("@RemindAt", reminder.RemindAt));
            command.Parameters.Add(new SqlParameter("@RecurrenceType", (int)reminder.RecurrenceType));
            command.Parameters.Add(new SqlParameter("@RecurrenceData", (object?)reminder.RecurrenceData ?? DBNull.Value));
            command.Parameters.Add(new SqlParameter("@DeliveryChannel", (int)reminder.DeliveryChannel));
            await command.ExecuteNonQueryAsync(cancellationToken);

            foreach (var uid in reminder.TargetUserIds.Distinct())
            {
                await using var tCmd = connection.CreateCommand();
                tCmd.Transaction = tx;
                tCmd.CommandText = $"INSERT INTO {_reminderTargetsTable} ([id],[reminder_id],[user_id]) VALUES (@Id,@Rid,@Uid);";
                tCmd.Parameters.Add(new SqlParameter("@Id",  Guid.NewGuid()));
                tCmd.Parameters.Add(new SqlParameter("@Rid", reminder.Id));
                tCmd.Parameters.Add(new SqlParameter("@Uid", uid));
                await tCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
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
            SELECT [id], [note_id], [shared_with_user_id], [shared_at],
                   ISNULL([can_edit], 0) AS [can_edit]
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
                SharedWithUserId = reader.GetInt32(2),
                SharedAt = reader.GetDateTime(3),
                CanEdit = reader.GetBoolean(4)
            });
        }

        return shares;
    }

    public async Task AddShareAsync(NoteShare share, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Upsert: mevcut paylaşımın can_edit değerini güncelle, yoksa yeni satır ekle
        command.CommandText = $"""
            MERGE {_sharesTable} AS target
            USING (VALUES (@NoteId, @SharedWithUserId)) AS src ([note_id], [shared_with_user_id])
              ON target.[note_id] = src.[note_id] AND target.[shared_with_user_id] = src.[shared_with_user_id]
            WHEN MATCHED THEN
                UPDATE SET [can_edit] = @CanEdit, [shared_at] = @SharedAt
            WHEN NOT MATCHED THEN
                INSERT ([id], [note_id], [shared_with_user_id], [shared_at], [can_edit])
                VALUES (@Id, @NoteId, @SharedWithUserId, @SharedAt, @CanEdit);
            """;
        command.Parameters.Add(new SqlParameter("@Id", share.Id));
        command.Parameters.Add(new SqlParameter("@NoteId", share.NoteId));
        command.Parameters.Add(new SqlParameter("@SharedWithUserId", share.SharedWithUserId));
        command.Parameters.Add(new SqlParameter("@SharedAt", share.SharedAt));
        command.Parameters.Add(new SqlParameter("@CanEdit", share.CanEdit));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateSharePermissionAsync(Guid shareId, bool canEdit, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_sharesTable} SET [can_edit] = @CanEdit WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", shareId));
        command.Parameters.Add(new SqlParameter("@CanEdit", canEdit));
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
                   n.[Id], n.[CompanyId], n.[UserId], n.[Title], n.[Content], n.[Created], n.[Updated], n.[FolderId]
            FROM {_remindersTable} r
            INNER JOIN {_notesTable} n ON r.[note_id] = n.[Id]
            WHERE r.[is_sent] = 0
              AND r.[remind_at] <= @Now
              AND n.[IsDeleted] = 0;
            """;
        command.Parameters.Add(new SqlParameter("@Now", DateTime.Now));

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
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
                };
                var rawReminderContent = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);
                var note = new Note
                {
                    Id = reader.GetGuid(7),
                    CompanyId = reader.GetInt32(8),
                    UserId = reader.GetInt32(9),
                    Title = reader.GetString(10),
                    Content = _encryption.Unprotect(rawReminderContent) ?? string.Empty,
                    CreatedAt = reader.GetDateTime(12),
                    UpdatedAt = reader.GetDateTime(13),
                    FolderId = reader.IsDBNull(14) ? null : reader.GetGuid(14)
                };
                results.Add((reminder, note));
            }
        }

        // Due reminder'larin target listesini ekle — tek query, reminder_id ile IN
        if (results.Count > 0)
        {
            var dueIds = results.Select(x => x.Item1.Id).ToArray();
            var paramNames = dueIds.Select((_, i) => "@T" + i).ToArray();
            await using var tCmd = connection.CreateCommand();
            tCmd.CommandText = $"""
                SELECT [reminder_id], [user_id]
                FROM {_reminderTargetsTable}
                WHERE [reminder_id] IN ({string.Join(",", paramNames)});
                """;
            for (var i = 0; i < dueIds.Length; i++)
                tCmd.Parameters.Add(new SqlParameter(paramNames[i], dueIds[i]));

            var map = new Dictionary<Guid, List<int>>();
            await using var tR = await tCmd.ExecuteReaderAsync(cancellationToken);
            while (await tR.ReadAsync(cancellationToken))
            {
                var rid = tR.GetGuid(0);
                if (!map.TryGetValue(rid, out var list)) { list = new(); map[rid] = list; }
                list.Add(tR.GetInt32(1));
            }

            for (var i = 0; i < results.Count; i++)
            {
                var (rm, nt) = results[i];
                if (map.TryGetValue(rm.Id, out var ids) && ids.Count > 0)
                {
                    results[i] = (CloneWithTargets(rm, ids), nt);
                }
            }
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

    public async Task<IReadOnlyCollection<NoteFolder>> GetFoldersAsync(int companyId, int userId, CancellationToken cancellationToken)
    {
        var folders = new List<NoteFolder>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [company_id], [user_id], [name], [parent_folder_id], [Created]
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
                UserId = reader.GetInt32(2),
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
                INSERT INTO {_foldersTable} ([id], [company_id], [user_id], [name], [parent_folder_id], [Created], [is_deleted])
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

    public async Task<IReadOnlyCollection<Note>> GetTrashedAsync(int companyId, int userId, CancellationToken cancellationToken)
    {
        var notes = new List<Note>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [UserId], [Title], [Content], [Created], [Updated], [FolderId], [IsPinned], [IsFullyEncrypted], [EncryptionHint], [Tags], [linked_entity_type], [linked_entity_id], [linked_entity_label], [visibility]
            FROM {_notesTable}
            WHERE [IsDeleted] = 1 AND [CompanyId] = @CompanyId AND [UserId] = @UserId
            ORDER BY [Updated] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            notes.Add(MapNote(reader));
        return notes;
    }

    public async Task<int> GetTrashedCountAsync(int companyId, int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {_notesTable} WHERE [IsDeleted] = 1 AND [CompanyId] = @CompanyId AND [UserId] = @UserId;";
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task RestoreNoteAsync(Guid id, int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_notesTable} SET [IsDeleted] = 0, [Updated] = @Now WHERE [Id] = @Id AND [UserId] = @UserId;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        command.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PermanentDeleteNoteAsync(Guid id, int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE r FROM {_remindersTable} r
            INNER JOIN {_notesTable} n ON r.[note_id] = n.[Id]
            WHERE n.[Id] = @Id AND n.[UserId] = @UserId;
            DELETE s FROM {_sharesTable} s
            INNER JOIN {_notesTable} n ON s.[note_id] = n.[Id]
            WHERE n.[Id] = @Id AND n.[UserId] = @UserId;
            DELETE FROM {_notesTable} WHERE [Id] = @Id AND [UserId] = @UserId;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@UserId", userId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EmptyTrashAsync(int companyId, int userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE r FROM {_remindersTable} r
            INNER JOIN {_notesTable} n ON r.[note_id] = n.[Id]
            WHERE n.[IsDeleted] = 1 AND n.[CompanyId] = @CompanyId AND n.[UserId] = @UserId;
            DELETE s FROM {_sharesTable} s
            INNER JOIN {_notesTable} n ON s.[note_id] = n.[Id]
            WHERE n.[IsDeleted] = 1 AND n.[CompanyId] = @CompanyId AND n.[UserId] = @UserId;
            DELETE FROM {_notesTable}
            WHERE [IsDeleted] = 1 AND [CompanyId] = @CompanyId AND [UserId] = @UserId;
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
            UPDATE {_notesTable} SET [FolderId] = NULL WHERE [FolderId] = @FolderId;
            UPDATE {_foldersTable} SET [parent_folder_id] = @ParentId WHERE [parent_folder_id] = @FolderId;
            UPDATE {_foldersTable} SET [is_deleted] = 1 WHERE [id] = @FolderId;
            """;
        command.Parameters.Add(new SqlParameter("@FolderId", folderId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // GetListByUserAsync için — [Content] sütunu YOK; ordinal sırası [Id,CompanyId,UserId,Title,Created,Updated,...]
    private Note MapNoteMetadata(SqlDataReader reader)
    {
        return new Note
        {
            Id        = reader.GetGuid(0),
            CompanyId = reader.GetInt32(1),
            UserId    = reader.GetInt32(2),
            Title     = reader.GetString(3),
            Content   = string.Empty,  // Lazy-loaded via GetContentByIdAsync
            CreatedAt = reader.GetDateTime(4),
            UpdatedAt = reader.GetDateTime(5),
            FolderId  = reader.IsDBNull(6) ? null : reader.GetGuid(6),
            IsPinned  = !reader.IsDBNull(7) && reader.GetBoolean(7),
            IsFullyEncrypted = reader.FieldCount > 8  && !reader.IsDBNull(8)  && reader.GetBoolean(8),
            EncryptionHint   = reader.FieldCount > 9  && !reader.IsDBNull(9)  ? reader.GetString(9) : null,
            Tags             = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : null,
            LinkedEntityType  = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : null,
            LinkedEntityId    = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetInt32(12) : (int?)null,
            LinkedEntityLabel = reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetString(13) : null,
            Visibility        = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetByte(14) : 0,
            ShareToken               = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetString(15) : null,
            ShareIsPublic            = reader.FieldCount > 16 && !reader.IsDBNull(16) && reader.GetBoolean(16),
            ShareIncludeAttachments  = reader.FieldCount > 17 && !reader.IsDBNull(17) && reader.GetBoolean(17),
            OcrText                  = reader.FieldCount > 18 && !reader.IsDBNull(18) ? reader.GetString(18) : null,
        };
    }

    private Note MapNote(SqlDataReader reader)
    {
        var rawContent = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        return new Note
        {
            Id = reader.GetGuid(0),
            CompanyId = reader.GetInt32(1),
            UserId = reader.GetInt32(2),
            Title = reader.GetString(3),
            // At-rest sifrelenmis icerigi coz (eski duz metin kayitlarda aynen doner)
            Content = _encryption.Unprotect(rawContent) ?? string.Empty,
            CreatedAt = reader.GetDateTime(5),
            UpdatedAt = reader.GetDateTime(6),
            FolderId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            IsPinned = !reader.IsDBNull(8) && reader.GetBoolean(8),
            IsFullyEncrypted = reader.FieldCount > 9 && !reader.IsDBNull(9) && reader.GetBoolean(9),
            EncryptionHint = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : null,
            Tags = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : null,
            LinkedEntityType  = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : null,
            LinkedEntityId    = reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetInt32(13) : (int?)null,
            LinkedEntityLabel = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetString(14) : null,
            Visibility        = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetByte(15) : 0,
            ShareToken               = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetString(16) : null,
            ShareIsPublic            = reader.FieldCount > 17 && !reader.IsDBNull(17) && reader.GetBoolean(17),
            ShareIncludeAttachments  = reader.FieldCount > 18 && !reader.IsDBNull(18) && reader.GetBoolean(18),
            OcrText                  = reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetString(19) : null,
        };
    }

    public async Task<Note?> GetByShareTokenAsync(string token, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [UserId], [Title], [Content], [Created], [Updated], [FolderId],
                   [IsPinned], [IsFullyEncrypted], [EncryptionHint], [Tags],
                   [linked_entity_type], [linked_entity_id], [linked_entity_label], [visibility],
                   [share_token], [share_is_public], [share_include_attachments]
            FROM {_notesTable}
            WHERE [share_token] = @Token AND [share_is_public] = 1 AND [IsDeleted] = 0;
            """;
        command.Parameters.Add(new SqlParameter("@Token", token));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapNote(reader) : null;
    }

    public async Task SetSharePublicAsync(Guid noteId, bool isPublic, string? token, bool includeAttachments, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_notesTable}
            SET [share_token] = @Token, [share_is_public] = @IsPublic, [share_include_attachments] = @IncludeAttachments
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", noteId));
        command.Parameters.Add(new SqlParameter("@IsPublic", isPublic));
        command.Parameters.Add(new SqlParameter("@Token", (object?)token ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IncludeAttachments", includeAttachments));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NoteReminder MapReminder(SqlDataReader reader)
    {
        // Kolon sirasi: id, note_id, remind_at, is_sent, sent_at, recurrence_type, recurrence_data,
        //               delivery_channel, target_user_id (artik okunmuyor — many-to-many table var)
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
            TargetUserIds = Array.Empty<int>(),
        };
        if (reader.GetBoolean(3))
        {
            reminder.MarkSent(reader.IsDBNull(4) ? DateTime.Now : reader.GetDateTime(4));
        }
        return reminder;
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWaInboxRepository : IWaInboxRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _contactTable;

    private readonly string _groupTable;

    public SqlWaInboxRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table        = $"[{schema}].[wa_inbox]";
        _contactTable = $"[{schema}].[Contact]";
        _groupTable   = $"[{schema}].[WaGroup]";
    }

    public async Task<long?> InsertIfNotExistsAsync(WaInboxMessage m, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        // UNIQUE filtered index var ama NULL bridge_msg_id durumunda dedup yok (zaten Bridge bos id atmaz).
        // IF NOT EXISTS ile guvenli upsert.
        cmd.CommandText = $"""
            IF @BridgeMsgId IS NOT NULL
               AND EXISTS (SELECT 1 FROM {_table} WHERE [bridge_msg_id] = @BridgeMsgId)
            BEGIN
                SELECT NULL;
                RETURN;
            END;

            INSERT INTO {_table}
                ([bridge_msg_id],[direction],[contact_phone],[contact_id],[contact_name],
                 [body],[media_type],[has_media],[received_at],[Created],[read_at],
                 [media_path],[media_mime],[media_filename],[media_size],[is_lid],[wa_contact_id],
                 [group_jid],[sender_jid],[sender_name])
            OUTPUT INSERTED.[id]
            VALUES (@BridgeMsgId,@Direction,@Phone,@ContactId,@Name,
                    @Body,@MediaType,@HasMedia,@ReceivedAt,@CreatedAt,NULL,
                    @MediaPath,@MediaMime,@MediaFileName,@MediaSize,@IsLid,@WaContactId,
                    @GroupJid,@SenderJid,@SenderName);
            """;
        cmd.Parameters.Add(new SqlParameter("@BridgeMsgId",   (object?)m.BridgeMsgId    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Direction",     m.Direction));
        cmd.Parameters.Add(new SqlParameter("@Phone",         m.ContactPhone));
        cmd.Parameters.Add(new SqlParameter("@ContactId",     (object?)m.ContactId      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Name",          (object?)m.ContactName    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Body",          (object?)m.Body           ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MediaType",     (object?)m.MediaType      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@HasMedia",      m.HasMedia));
        cmd.Parameters.Add(new SqlParameter("@ReceivedAt",    m.ReceivedAt));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt",     m.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@MediaPath",     (object?)m.MediaPath      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MediaMime",     (object?)m.MediaMime      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MediaFileName", (object?)m.MediaFileName  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MediaSize",     (object?)m.MediaSize      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsLid",         m.IsLid));
        cmd.Parameters.Add(new SqlParameter("@WaContactId",   (object?)m.ContactId      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@GroupJid",      (object?)m.GroupJid       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SenderJid",     (object?)m.SenderJid      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SenderName",    (object?)m.SenderName     ?? DBNull.Value));

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull) return null;
        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<WaConversationSummary>> GetConversationsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        // Her telefon icin: son mesaj satiri (ROW_NUMBER trick) + okunmamis sayisi (subselect).
        // Contact join: WaPhone, Mobile veya Phone digit-bazli eslesme — SQL tarafinda karmasik,
        // basit yaklasim: WaPhone'a digit eslesmesi yap (CalibraHub'da number'lar genelde + ile yazilir).
        cmd.CommandText = $"""
            ;WITH last_msg AS (
                SELECT
                    [contact_phone],
                    [group_jid],
                    [body], [media_type], [direction], [received_at],
                    ISNULL([is_lid], 0) AS [is_lid],
                    ROW_NUMBER() OVER (PARTITION BY [contact_phone] ORDER BY [received_at] DESC, [id] DESC) AS rn
                FROM {_table}
            ),
            last_incoming_name AS (
                SELECT
                    [contact_phone],
                    [contact_name],
                    ROW_NUMBER() OVER (PARTITION BY [contact_phone] ORDER BY [received_at] DESC, [id] DESC) AS rn
                FROM {_table}
                WHERE [direction] = 0
                  AND [contact_name] IS NOT NULL
                  AND LEN(LTRIM(RTRIM([contact_name]))) > 0
            ),
            unread AS (
                SELECT [contact_phone], COUNT(1) AS unread_count
                FROM {_table}
                WHERE [direction] = 0 AND [read_at] IS NULL
                GROUP BY [contact_phone]
            )
            SELECT TOP (@N)
                lm.[contact_phone],
                c.[Id]             AS contact_id,
                lin.[contact_name] AS contact_name,
                c.[AccountTitle],
                c.[AccountCode],
                c.[WaName],
                lm.[body],
                lm.[media_type],
                lm.[direction],
                lm.[received_at],
                COALESCE(u.unread_count, 0) AS unread_count,
                lm.[is_lid],
                -- Faz 4: grup bilgileri
                CASE WHEN lm.[group_jid] IS NOT NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS is_group,
                lm.[group_jid],
                wg.[Subject]       AS group_subject,
                COALESCE(wg.[MemberCount], 0) AS member_count
            FROM last_msg lm
            LEFT JOIN last_incoming_name lin
                   ON lin.[contact_phone] = lm.[contact_phone] AND lin.rn = 1
            LEFT JOIN unread u ON u.[contact_phone] = lm.[contact_phone]
            LEFT JOIN {_groupTable} wg ON wg.[GroupJid] = lm.[group_jid]
            OUTER APPLY (
                SELECT TOP 1 [Id],[AccountTitle],[AccountCode],[WaName]
                FROM {_contactTable}
                WHERE [IsActive] = 1
                  AND lm.[group_jid] IS NULL   -- grup sohbetlerinde Contact join yapma
                  AND lm.[contact_phone] = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                              ISNULL([WaPhone], ''),
                                              ' ',''),'-',''),'(',''),')',''),'+','')
                ORDER BY [Id]
            ) c
            WHERE lm.rn = 1
            ORDER BY lm.[received_at] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@N", limit));

        var list = new List<WaConversationSummary>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new WaConversationSummary(
                ContactPhone:     r.GetString(0),
                ContactId:        r.IsDBNull(1)  ? null : r.GetInt32(1),
                ContactName:      r.IsDBNull(2)  ? null : r.GetString(2),
                AccountTitle:     r.IsDBNull(3)  ? null : r.GetString(3),
                AccountCode:      r.IsDBNull(4)  ? null : r.GetString(4),
                WaName:           r.IsDBNull(5)  ? null : r.GetString(5),
                LastBody:         r.IsDBNull(6)  ? null : r.GetString(6),
                LastMediaType:    r.IsDBNull(7)  ? null : r.GetString(7),
                LastFromMe:       r.GetByte(8) == 1,
                LastAt:           r.GetDateTime(9),
                UnreadCount:      r.GetInt32(10),
                IsLid:            !r.IsDBNull(11) && r.GetBoolean(11),
                IsGroup:          r.GetBoolean(12),
                GroupJid:         r.IsDBNull(13) ? null : r.GetString(13),
                GroupSubject:     r.IsDBNull(14) ? null : r.GetString(14),
                GroupMemberCount: r.GetInt32(15)));
        }
        return list;
    }

    public async Task<IReadOnlyList<WaInboxMessage>> GetMessagesByPhoneAsync(string contactPhone, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            ;WITH recent AS (
                SELECT TOP (@N)
                    [id],[bridge_msg_id],[direction],[contact_phone],[contact_id],[contact_name],
                    [body],[media_type],[has_media],[received_at],[Created],[read_at],
                    [media_path],[media_mime],[media_filename],[media_size],
                    ISNULL([is_deleted],0) AS [is_deleted],
                    [quoted_msg_id],[reaction_emoji],[delivery_status],
                    [group_jid],[sender_jid],[sender_name]
                FROM {_table}
                WHERE [contact_phone] = @Phone
                ORDER BY [received_at] DESC, [id] DESC
            )
            SELECT * FROM recent ORDER BY [received_at] ASC, [id] ASC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone", contactPhone));
        cmd.Parameters.Add(new SqlParameter("@N", limit));

        var list = new List<WaInboxMessage>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new WaInboxMessage
            {
                Id            = r.GetInt64(0),
                BridgeMsgId   = r.IsDBNull(1)  ? null : r.GetString(1),
                Direction     = r.GetByte(2),
                ContactPhone  = r.GetString(3),
                ContactId     = r.IsDBNull(4)  ? null : r.GetInt32(4),
                ContactName   = r.IsDBNull(5)  ? null : r.GetString(5),
                Body          = r.IsDBNull(6)  ? null : r.GetString(6),
                MediaType     = r.IsDBNull(7)  ? null : r.GetString(7),
                HasMedia      = r.GetBoolean(8),
                ReceivedAt    = r.GetDateTime(9),
                CreatedAt     = r.GetDateTime(10),
                ReadAt        = r.IsDBNull(11) ? null : r.GetDateTime(11),
                MediaPath     = r.IsDBNull(12) ? null : r.GetString(12),
                MediaMime     = r.IsDBNull(13) ? null : r.GetString(13),
                MediaFileName = r.IsDBNull(14) ? null : r.GetString(14),
                MediaSize     = r.IsDBNull(15) ? null : r.GetInt32(15),
                IsDeleted     = !r.IsDBNull(16) && r.GetBoolean(16),
                QuotedMsgId   = r.IsDBNull(17) ? null : r.GetString(17),
                ReactionEmoji = r.IsDBNull(18) ? null : r.GetString(18),
                DeliveryStatus= r.IsDBNull(19) ? null : r.GetString(19),
                GroupJid      = r.IsDBNull(20) ? null : r.GetString(20),
                SenderJid     = r.IsDBNull(21) ? null : r.GetString(21),
                SenderName    = r.IsDBNull(22) ? null : r.GetString(22),
            });
        }
        return list;
    }

    public async Task<int> MarkConversationReadAsync(string contactPhone, DateTime readAt, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [read_at] = @ReadAt
             WHERE [contact_phone] = @Phone
               AND [direction] = 0
               AND [read_at] IS NULL;
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone",  contactPhone));
        cmd.Parameters.Add(new SqlParameter("@ReadAt", readAt));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastReceivedAtAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT MAX([received_at]) FROM {_table};";
        var v = await cmd.ExecuteScalarAsync(cancellationToken);
        return v is null || v is DBNull ? null : (DateTime)v;
    }

    public async Task<int> DeleteConversationAsync(string contactPhone, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [contact_phone] = @Phone;";
        cmd.Parameters.Add(new SqlParameter("@Phone", contactPhone));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(long Id, string BridgeMsgId)>> GetMediaMessagesMissingFileAsync(int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@N) [id], [bridge_msg_id]
              FROM {_table}
             WHERE [has_media] = 1
               AND [media_path] IS NULL
               AND [bridge_msg_id] IS NOT NULL
             ORDER BY [received_at] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@N", limit));
        var list = new List<(long, string)>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
            list.Add((r.GetInt64(0), r.GetString(1)));
        return list;
    }

    public async Task<int> UpdateMediaPathAsync(long id, string mediaPath, string? mediaMime, string? mediaFileName, int? mediaSize, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [media_path]     = @Path,
                   [media_mime]     = @Mime,
                   [media_filename] = @FileName,
                   [media_size]     = @Size
             WHERE [id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Path",     mediaPath));
        cmd.Parameters.Add(new SqlParameter("@Mime",     (object?)mediaMime     ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@FileName", (object?)mediaFileName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Size",     (object?)mediaSize     ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Id",       id));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Faz 3 metodları ──────────────────────────────────────────────────

    public async Task<WaInboxMessage?> GetByBridgeMsgIdAsync(string bridgeMsgId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[bridge_msg_id],[direction],[contact_phone],[contact_id],[contact_name],
                   [body],[media_type],[has_media],[received_at],[Created],[read_at],
                   [media_path],[media_mime],[media_filename],[media_size],
                   ISNULL([is_deleted],0),[quoted_msg_id],[reaction_emoji],[delivery_status]
            FROM {_table}
            WHERE [bridge_msg_id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", bridgeMsgId));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        return new WaInboxMessage
        {
            Id            = r.GetInt64(0),
            BridgeMsgId   = r.IsDBNull(1)  ? null : r.GetString(1),
            Direction     = r.GetByte(2),
            ContactPhone  = r.GetString(3),
            ContactId     = r.IsDBNull(4)  ? null : r.GetInt32(4),
            ContactName   = r.IsDBNull(5)  ? null : r.GetString(5),
            Body          = r.IsDBNull(6)  ? null : r.GetString(6),
            MediaType     = r.IsDBNull(7)  ? null : r.GetString(7),
            HasMedia      = r.GetBoolean(8),
            ReceivedAt    = r.GetDateTime(9),
            CreatedAt     = r.GetDateTime(10),
            ReadAt        = r.IsDBNull(11) ? null : r.GetDateTime(11),
            MediaPath     = r.IsDBNull(12) ? null : r.GetString(12),
            MediaMime     = r.IsDBNull(13) ? null : r.GetString(13),
            MediaFileName = r.IsDBNull(14) ? null : r.GetString(14),
            MediaSize     = r.IsDBNull(15) ? null : r.GetInt32(15),
            IsDeleted     = !r.IsDBNull(16) && r.GetBoolean(16),
            QuotedMsgId   = r.IsDBNull(17) ? null : r.GetString(17),
            ReactionEmoji = r.IsDBNull(18) ? null : r.GetString(18),
            DeliveryStatus= r.IsDBNull(19) ? null : r.GetString(19),
        };
    }

    public async Task<int> MarkDeletedAsync(string bridgeMsgId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [is_deleted] = 1, [body] = NULL
             WHERE [bridge_msg_id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", bridgeMsgId));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> UpdateReactionAsync(string bridgeMsgId, string? emoji, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [reaction_emoji] = @Emoji
             WHERE [bridge_msg_id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Emoji", (object?)emoji ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Id",    bridgeMsgId));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> UpdateDeliveryStatusAsync(string bridgeMsgId, string status, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [delivery_status] = @Status
             WHERE [bridge_msg_id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Status", status));
        cmd.Parameters.Add(new SqlParameter("@Id",     bridgeMsgId));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WaInboxMessage>> SearchMessagesAsync(
        string contactPhone, string query, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@N)
                [id],[bridge_msg_id],[direction],[contact_phone],[contact_id],[contact_name],
                [body],[media_type],[has_media],[received_at],[Created],[read_at],
                [media_path],[media_mime],[media_filename],[media_size],
                ISNULL([is_deleted],0),[quoted_msg_id],[reaction_emoji],[delivery_status]
            FROM {_table}
            WHERE [contact_phone] = @Phone
              AND [body] LIKE @Query
              AND ISNULL([is_deleted],0) = 0
            ORDER BY [received_at] DESC, [id] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone", contactPhone));
        cmd.Parameters.Add(new SqlParameter("@Query", "%" + query.Replace("%","[%]").Replace("_","[_]") + "%"));
        cmd.Parameters.Add(new SqlParameter("@N",     limit));

        var list = new List<WaInboxMessage>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new WaInboxMessage
            {
                Id            = r.GetInt64(0),
                BridgeMsgId   = r.IsDBNull(1)  ? null : r.GetString(1),
                Direction     = r.GetByte(2),
                ContactPhone  = r.GetString(3),
                ContactId     = r.IsDBNull(4)  ? null : r.GetInt32(4),
                ContactName   = r.IsDBNull(5)  ? null : r.GetString(5),
                Body          = r.IsDBNull(6)  ? null : r.GetString(6),
                MediaType     = r.IsDBNull(7)  ? null : r.GetString(7),
                HasMedia      = r.GetBoolean(8),
                ReceivedAt    = r.GetDateTime(9),
                CreatedAt     = r.GetDateTime(10),
                ReadAt        = r.IsDBNull(11) ? null : r.GetDateTime(11),
                MediaPath     = r.IsDBNull(12) ? null : r.GetString(12),
                MediaMime     = r.IsDBNull(13) ? null : r.GetString(13),
                MediaFileName = r.IsDBNull(14) ? null : r.GetString(14),
                MediaSize     = r.IsDBNull(15) ? null : r.GetInt32(15),
                IsDeleted     = !r.IsDBNull(16) && r.GetBoolean(16),
                QuotedMsgId   = r.IsDBNull(17) ? null : r.GetString(17),
                ReactionEmoji = r.IsDBNull(18) ? null : r.GetString(18),
                DeliveryStatus= r.IsDBNull(19) ? null : r.GetString(19),
            });
        }
        return list;
    }

    public async Task<int> MarkUnreadAsync(string contactPhone, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        // Son gelen mesajın read_at'ını NULL yap
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [read_at] = NULL
             WHERE [id] = (
                 SELECT TOP 1 [id]
                   FROM {_table}
                  WHERE [contact_phone] = @Phone
                    AND [direction] = 0
                  ORDER BY [received_at] DESC, [id] DESC
             );
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone", contactPhone));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

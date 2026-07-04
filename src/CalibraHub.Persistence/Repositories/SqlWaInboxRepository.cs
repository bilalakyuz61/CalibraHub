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
        _table        = $"[{schema}].[WaInbox]";
        _contactTable = $"[{schema}].[Contact]";
        _groupTable   = $"[{schema}].[WaGroup]";
    }

    public async Task<long?> InsertIfNotExistsAsync(WaInboxMessage m, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        // UNIQUE filtered index var ama NULL BridgeMsgId durumunda dedup yok (zaten Bridge bos id atmaz).
        // IF NOT EXISTS ile guvenli upsert.
        cmd.CommandText = $"""
            IF @BridgeMsgId IS NOT NULL
               AND EXISTS (SELECT 1 FROM {_table} WHERE [BridgeMsgId] = @BridgeMsgId)
            BEGIN
                SELECT NULL;
                RETURN;
            END;

            INSERT INTO {_table}
                ([BridgeMsgId],[Direction],[ContactPhone],[ContactId],[ContactName],
                 [Body],[MediaType],[HasMedia],[ReceivedAt],[Created],[ReadAt],
                 [MediaPath],[MediaMime],[MediaFilename],[MediaSize],[is_lid],[wa_contact_id],
                 [group_jid],[sender_jid],[SenderName])
            OUTPUT INSERTED.[Id]
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
        // Contact join: Contact tablosunu tek seferinde CTE'de normalize edip LEFT JOIN ile esles;
        // per-row OUTER APPLY + her iki tarafta REPLACE chain kaldırıldı (index kör ediyordu).
        cmd.CommandText = $"""
            ;WITH contact_norm AS (
                -- Contact tablosunu tek seferinde normalize et — per-row OUTER APPLY yerine
                SELECT [Id], [AccountTitle], [AccountCode], [WaName],
                       REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL([WaPhone],''),' ',''),'-',''),'(',''),')',''),'+','') AS norm_phone
                FROM {_contactTable}
                WHERE [IsActive] = 1
                  AND [WaPhone] IS NOT NULL
                  AND LEN([WaPhone]) > 0
            ),
            has_incoming_cte AS (
                -- Gelen mesaji olan telefon numaralari — LID dedup icin (correlated subquery yerine)
                SELECT DISTINCT [ContactPhone]
                FROM {_table}
                WHERE [Direction] = 0
            ),
            last_msg AS (
                SELECT
                    [ContactPhone],
                    [group_jid],
                    [Body], [MediaType], [Direction], [ReceivedAt],
                    ISNULL([is_lid], 0) AS [is_lid],
                    ROW_NUMBER() OVER (PARTITION BY [ContactPhone] ORDER BY [ReceivedAt] DESC, [Id] DESC) AS rn
                FROM {_table}
            ),
            last_incoming_name AS (
                SELECT
                    [ContactPhone],
                    [ContactName],
                    ROW_NUMBER() OVER (PARTITION BY [ContactPhone] ORDER BY [ReceivedAt] DESC, [Id] DESC) AS rn
                FROM {_table}
                WHERE [Direction] = 0
                  AND [ContactName] IS NOT NULL
                  AND LEN(LTRIM(RTRIM([ContactName]))) > 0
            ),
            unread AS (
                SELECT [ContactPhone], COUNT(1) AS unread_count
                FROM {_table}
                WHERE [Direction] = 0 AND [ReadAt] IS NULL
                GROUP BY [ContactPhone]
            )
            SELECT TOP (@N)
                lm.[ContactPhone],
                cn.[Id]             AS contact_id,
                lin.[ContactName]   AS contact_name,
                cn.[AccountTitle],
                cn.[AccountCode],
                cn.[WaName],
                lm.[Body],
                lm.[MediaType],
                lm.[Direction],
                lm.[ReceivedAt],
                COALESCE(u.unread_count, 0) AS unread_count,
                lm.[is_lid],
                -- Faz 4: grup bilgileri
                CASE WHEN lm.[group_jid] IS NOT NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS is_group,
                lm.[group_jid],
                wg.[Subject]        AS group_subject,
                COALESCE(wg.[MemberCount], 0) AS member_count,
                -- LID dedup icin: bu konusmada hic gelen mesaj var mi?
                CAST(CASE WHEN hi.[ContactPhone] IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS has_incoming
            FROM last_msg lm
            LEFT JOIN last_incoming_name lin
                   ON lin.[ContactPhone] = lm.[ContactPhone] AND lin.rn = 1
            LEFT JOIN unread u ON u.[ContactPhone] = lm.[ContactPhone]
            LEFT JOIN {_groupTable} wg ON wg.[GroupJid] = lm.[group_jid]
            LEFT JOIN contact_norm cn
                   ON cn.norm_phone = lm.[ContactPhone]
                  AND lm.[group_jid] IS NULL
            LEFT JOIN has_incoming_cte hi ON hi.[ContactPhone] = lm.[ContactPhone]
            WHERE lm.rn = 1
            ORDER BY lm.[ReceivedAt] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@N", limit));

        var list = new List<WaConversationSummary>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var rawPhone = r.GetString(0);
            // JID'leri (@ içerenleri) olduğu gibi bırak; düz telefon numaralarını rakam-only'ye normalize et
            var normPhone = rawPhone.Contains('@') ? rawPhone
                : (new string(rawPhone.Where(char.IsDigit).ToArray()) is { Length: > 0 } d ? d : rawPhone);
            list.Add(new WaConversationSummary(
                ContactPhone:     normPhone,
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
                GroupMemberCount: r.GetInt32(15),
                HasIncoming:      !r.IsDBNull(16) && r.GetBoolean(16)));
        }

        // Dedup: aynı normalize telefon veya aynı ContactId olan satırlar birleştirilir.
        // ContactPhone artık normalize edilmiş (yukarıda); liste received_at DESC sıralı
        // → ilk karşılaşılan (en yeni) tutulur, sonrakiler atlanır.
        var seenPhones = new HashSet<string>(StringComparer.Ordinal);
        var seenContactIds = new HashSet<int>();
        var deduped = new List<WaConversationSummary>(list.Count);
        foreach (var conv in list)
        {
            if (!conv.IsGroup)
            {
                bool phoneDup = seenPhones.Contains(conv.ContactPhone);
                bool contactDup = conv.ContactId.HasValue && seenContactIds.Contains(conv.ContactId.Value);
                if (phoneDup || contactDup) continue;
                seenPhones.Add(conv.ContactPhone);
                if (conv.ContactId.HasValue) seenContactIds.Add(conv.ContactId.Value);
            }
            deduped.Add(conv);
        }

        // WhatsApp LID dedup: gelen mesajlar LID (15+ haneli uzun numara) ile, giden mesajlar
        // telefon numarasıyla saklanır → aynı kişi iki ayrı satır görünebilir.
        // Kural: kuyrukta gelen mesajı olan bir LID konuşması varken, yalnızca giden (HasIncoming=false)
        // olan düz telefon numarası sohbetlerini gizle.
        static bool IsLidPhone(string p) => !p.Contains('@') && p.Length >= 14;
        bool hasLidWithIncoming = deduped.Any(c => !c.IsGroup && c.HasIncoming && (c.IsLid || IsLidPhone(c.ContactPhone)));
        if (hasLidWithIncoming)
        {
            deduped = deduped
                .Where(c => c.IsGroup || c.IsLid || IsLidPhone(c.ContactPhone) || c.HasIncoming)
                .ToList();
        }

        return deduped;
    }

    public async Task<IReadOnlyList<WaInboxMessage>> GetMessagesByPhoneAsync(string contactPhone, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        // contact_phone INSERT tarafinda zaten normalize edilir (rakam-only veya JID olarak).
        // REPLACE chain kaldırıldı — kolondaki REPLACE index'i kör ediyordu; direkt esitlik kullan.
        cmd.CommandText = $"""
            ;WITH recent AS (
                SELECT TOP (@N)
                    [Id],[BridgeMsgId],[Direction],[ContactPhone],[ContactId],[ContactName],
                    [Body],[MediaType],[HasMedia],[ReceivedAt],[Created],[ReadAt],
                    [MediaPath],[MediaMime],[MediaFilename],[MediaSize],
                    ISNULL([is_deleted],0) AS [is_deleted],
                    [quoted_msg_id],[reaction_emoji],[delivery_status],
                    [group_jid],[sender_jid],[SenderName]
                FROM {_table}
                WHERE [ContactPhone] = @Phone
                ORDER BY [ReceivedAt] DESC, [Id] DESC
            )
            SELECT * FROM recent ORDER BY [ReceivedAt] ASC, [Id] ASC;
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
               SET [ReadAt] = @ReadAt
             WHERE [ContactPhone] = @Phone
               AND [Direction] = 0
               AND [ReadAt] IS NULL;
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone",  contactPhone));
        cmd.Parameters.Add(new SqlParameter("@ReadAt", readAt));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastReceivedAtAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT MAX([ReceivedAt]) FROM {_table};";
        var v = await cmd.ExecuteScalarAsync(cancellationToken);
        return v is null || v is DBNull ? null : (DateTime)v;
    }

    public async Task<int> DeleteConversationAsync(string contactPhone, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM {_table}
            WHERE [ContactPhone] = @Phone;
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone", contactPhone));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(long Id, string BridgeMsgId)>> GetMediaMessagesMissingFileAsync(int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@N) [Id], [BridgeMsgId]
              FROM {_table}
             WHERE [HasMedia] = 1
               AND [MediaPath] IS NULL
               AND [BridgeMsgId] IS NOT NULL
             ORDER BY [ReceivedAt] DESC;
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
               SET [MediaPath]     = @Path,
                   [MediaMime]     = @Mime,
                   [MediaFilename] = @FileName,
                   [MediaSize]     = @Size
             WHERE [Id] = @Id;
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
            SELECT [Id],[BridgeMsgId],[Direction],[ContactPhone],[ContactId],[ContactName],
                   [Body],[MediaType],[HasMedia],[ReceivedAt],[Created],[ReadAt],
                   [MediaPath],[MediaMime],[MediaFilename],[MediaSize],
                   ISNULL([is_deleted],0),[quoted_msg_id],[reaction_emoji],[delivery_status]
            FROM {_table}
            WHERE [BridgeMsgId] = @Id;
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
               SET [is_deleted] = 1, [Body] = NULL
             WHERE [BridgeMsgId] = @Id;
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
             WHERE [BridgeMsgId] = @Id;
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
             WHERE [BridgeMsgId] = @Id;
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
                [Id],[BridgeMsgId],[Direction],[ContactPhone],[ContactId],[ContactName],
                [Body],[MediaType],[HasMedia],[ReceivedAt],[Created],[ReadAt],
                [MediaPath],[MediaMime],[MediaFilename],[MediaSize],
                ISNULL([is_deleted],0),[quoted_msg_id],[reaction_emoji],[delivery_status]
            FROM {_table}
            WHERE [ContactPhone] = @Phone
              AND [Body] LIKE @Query
              AND ISNULL([is_deleted],0) = 0
            ORDER BY [ReceivedAt] DESC, [Id] DESC;
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
        // Son gelen mesajın ReadAt'ını NULL yap
        cmd.CommandText = $"""
            UPDATE {_table}
               SET [ReadAt] = NULL
             WHERE [Id] = (
                 SELECT TOP 1 [Id]
                   FROM {_table}
                  WHERE [ContactPhone] = @Phone
                    AND [Direction] = 0
                  ORDER BY [ReceivedAt] DESC, [Id] DESC
             );
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone", contactPhone));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

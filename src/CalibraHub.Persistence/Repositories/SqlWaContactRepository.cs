using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWaContactRepository : IWaContactRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _contactTable;
    private readonly string _jidTable;
    private readonly string _inboxTable;

    public SqlWaContactRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var s = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _schema       = $"[{s}]";
        _contactTable = $"[{s}].[WaContact]";
        _jidTable     = $"[{s}].[WaContactJid]";
        _inboxTable   = $"[{s}].[WaInbox]";
    }

    public async Task<WaContact?> FindByJidAsync(string jid, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id],[PrimaryPhone],[DisplayName],[ProfilePicUrl],[LastSeen],[PresenceStatus],
                   [LinkedContactId],[IsBlocked],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_contactTable} c
            JOIN {_jidTable} j ON j.[ContactId] = c.[Id]
            WHERE j.[Jid] = @Jid;
            """;
        cmd.Parameters.Add(new SqlParameter("@Jid", jid));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return MapContact(r);
    }

    public async Task<WaContact?> FindByPhoneAsync(string phone, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[PrimaryPhone],[DisplayName],[ProfilePicUrl],[LastSeen],[PresenceStatus],
                   [LinkedContactId],[IsBlocked],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_contactTable}
            WHERE [PrimaryPhone] = @Phone;
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone", phone));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return MapContact(r);
    }

    public async Task<int> CreateAsync(WaContact contact, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_contactTable}
                ([PrimaryPhone],[DisplayName],[ProfilePicUrl],[LastSeen],[PresenceStatus],
                 [LinkedContactId],[IsBlocked],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated])
            OUTPUT INSERTED.[Id]
            VALUES (@Phone,@DisplayName,@PicUrl,@LastSeen,@Presence,
                    @LinkedId,@Blocked,@Active,@CreatedById,@Created,@UpdatedById,@Updated);
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone",      (object?)contact.PrimaryPhone   ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DisplayName",(object?)contact.DisplayName    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PicUrl",     (object?)contact.ProfilePicUrl  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LastSeen",   (object?)contact.LastSeen       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Presence",   (object?)contact.PresenceStatus ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LinkedId",   (object?)contact.LinkedContactId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Blocked",    contact.IsBlocked));
        cmd.Parameters.Add(new SqlParameter("@Active",     contact.IsActive));
        cmd.Parameters.Add(new SqlParameter("@CreatedById",(object?)contact.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Created",    contact.Created));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById",(object?)contact.UpdatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Updated",    (object?)contact.Updated   ?? DBNull.Value));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(WaContact contact, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_contactTable}
               SET [PrimaryPhone]  = @Phone,
                   [DisplayName]   = @DisplayName,
                   [ProfilePicUrl] = @PicUrl,
                   [LastSeen]      = @LastSeen,
                   [PresenceStatus]= @Presence,
                   [LinkedContactId]=@LinkedId,
                   [IsBlocked]     = @Blocked,
                   [IsActive]      = @Active,
                   [UpdatedById]   = @UpdatedById,
                   [Updated]       = @Updated
             WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",         contact.Id));
        cmd.Parameters.Add(new SqlParameter("@Phone",      (object?)contact.PrimaryPhone   ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@DisplayName",(object?)contact.DisplayName    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@PicUrl",     (object?)contact.ProfilePicUrl  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LastSeen",   (object?)contact.LastSeen       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Presence",   (object?)contact.PresenceStatus ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@LinkedId",   (object?)contact.LinkedContactId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Blocked",    contact.IsBlocked));
        cmd.Parameters.Add(new SqlParameter("@Active",     contact.IsActive));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById",(object?)contact.UpdatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Updated",    (object?)contact.Updated   ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddJidAsync(WaContactJid jidEntry, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // UNIQUE (Jid) — çakışmada sessizce atla
        cmd.CommandText = $"""
            IF NOT EXISTS (SELECT 1 FROM {_jidTable} WHERE [Jid] = @Jid)
            BEGIN
                INSERT INTO {_jidTable} ([ContactId],[Jid],[JidType],[IsPrimary],[Created])
                VALUES (@ContactId,@Jid,@JidType,@IsPrimary,@Created);
            END;
            """;
        cmd.Parameters.Add(new SqlParameter("@ContactId",  jidEntry.ContactId));
        cmd.Parameters.Add(new SqlParameter("@Jid",        jidEntry.Jid));
        cmd.Parameters.Add(new SqlParameter("@JidType",    jidEntry.JidType));
        cmd.Parameters.Add(new SqlParameter("@IsPrimary",  jidEntry.IsPrimary));
        cmd.Parameters.Add(new SqlParameter("@Created",    jidEntry.Created));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task BackfillFromInboxAsync(CancellationToken ct)
    {
        // Her distinct contact_phone için WaContact yoksa oluştur
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DECLARE @phones TABLE (phone NVARCHAR(32), display_name NVARCHAR(200));
            INSERT INTO @phones
            SELECT DISTINCT i.[ContactPhone],
                   MAX(i.[ContactName])
            FROM {_inboxTable} i
            WHERE i.[ContactPhone] IS NOT NULL
            GROUP BY i.[ContactPhone];

            MERGE {_contactTable} AS tgt
            USING (
                SELECT p.phone, p.display_name
                FROM @phones p
                WHERE NOT EXISTS (
                    SELECT 1 FROM {_jidTable} j
                    WHERE j.[Jid] = p.phone + N'@s.whatsapp.net'
                       OR j.[Jid] = p.phone
                )
            ) AS src ON 1=0
            WHEN NOT MATCHED THEN
                INSERT ([PrimaryPhone],[DisplayName],[IsActive],[Created])
                VALUES (src.phone, src.display_name, 1, SYSUTCDATETIME());
            """;
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task LinkInboxContactIdsAsync(CancellationToken ct)
    {
        // WaInbox.ContactId = WaContact.Id (phone eşleşmesi üzerinden)
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE i
               SET i.[ContactId] = c.[Id]
            FROM {_inboxTable} i
            JOIN {_contactTable} c ON c.[PrimaryPhone] = i.[ContactPhone]
            WHERE i.[ContactId] IS NULL;
            """;
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static WaContact MapContact(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        PrimaryPhone    = r.IsDBNull(1)  ? null : r.GetString(1),
        DisplayName     = r.IsDBNull(2)  ? null : r.GetString(2),
        ProfilePicUrl   = r.IsDBNull(3)  ? null : r.GetString(3),
        LastSeen        = r.IsDBNull(4)  ? null : r.GetDateTime(4),
        PresenceStatus  = r.IsDBNull(5)  ? null : r.GetString(5),
        LinkedContactId = r.IsDBNull(6)  ? null : r.GetInt32(6),
        IsBlocked       = r.GetBoolean(7),
        IsActive        = r.GetBoolean(8),
        CreatedById     = r.IsDBNull(9)  ? null : r.GetInt32(9),
        Created         = r.GetDateTime(10),
        UpdatedById     = r.IsDBNull(11) ? null : r.GetInt32(11),
        Updated         = r.IsDBNull(12) ? null : r.GetDateTime(12),
    };
}

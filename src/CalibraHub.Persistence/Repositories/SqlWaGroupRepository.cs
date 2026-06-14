using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWaGroupRepository : IWaGroupRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _g;   // WaGroup table qualified
    private readonly string _gm;  // WaGroupMember table qualified

    public SqlWaGroupRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var s = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _g  = $"[{s}].[WaGroup]";
        _gm = $"[{s}].[WaGroupMember]";
    }

    public async Task<WaGroup> GetOrCreateAsync(string groupJid, string subject, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var sql = $"""
            MERGE {_g} AS t
            USING (SELECT @Jid AS Jid, @Subject AS Subject) AS s ON t.[GroupJid] = s.Jid
            WHEN NOT MATCHED THEN
                INSERT ([GroupJid],[Subject]) VALUES (s.Jid, s.Subject)
            WHEN MATCHED AND t.[Subject] <> s.Subject THEN
                UPDATE SET [Subject] = s.Subject, [Updated] = SYSUTCDATETIME()
            OUTPUT INSERTED.[Id], INSERTED.[GroupJid], INSERTED.[Subject],
                   INSERTED.[Description], INSERTED.[MemberCount], INSERTED.[IsActive],
                   INSERTED.[Created], INSERTED.[Updated];
            """;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Jid", groupJid);
        cmd.Parameters.AddWithValue("@Subject", subject);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct)) return ReadGroup(r);
        // Fallback: satır zaten var, Subject aynı → MERGE OUTPUT boş dönebilir
        r.Close();
        return await GetByJidAsync(groupJid, ct) ?? throw new InvalidOperationException($"WaGroup kayıt edilemedi: {groupJid}");
    }

    public async Task UpdateAsync(string groupJid, string subject, string? description, int memberCount, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var sql = $"""
            UPDATE {_g}
               SET [Subject]     = @Subject,
                   [Description] = @Desc,
                   [MemberCount] = @Cnt,
                   [Updated]     = SYSUTCDATETIME()
             WHERE [GroupJid] = @Jid;
            """;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Jid",     groupJid);
        cmd.Parameters.AddWithValue("@Subject",  subject);
        cmd.Parameters.AddWithValue("@Desc",     (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Cnt",      memberCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<WaGroup>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var sql = $"SELECT [Id],[GroupJid],[Subject],[Description],[MemberCount],[IsActive],[Created],[Updated] FROM {_g} WHERE [IsActive]=1 ORDER BY [Subject]";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<WaGroup>();
        while (await r.ReadAsync(ct)) list.Add(ReadGroup(r));
        return list;
    }

    public async Task<WaGroup?> GetByJidAsync(string groupJid, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var sql = $"SELECT [Id],[GroupJid],[Subject],[Description],[MemberCount],[IsActive],[Created],[Updated] FROM {_g} WHERE [GroupJid]=@Jid";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Jid", groupJid);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadGroup(r) : null;
    }

    public async Task UpsertMembersAsync(int groupId, IReadOnlyList<WaGroupMemberInput> members, CancellationToken ct)
    {
        if (members.Count == 0) return;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        foreach (var m in members)
        {
            var sql = $"""
                MERGE {_gm} AS t
                USING (SELECT @GroupId AS G, @Jid AS J) AS s ON t.[GroupId]=s.G AND t.[Jid]=s.J
                WHEN NOT MATCHED THEN
                    INSERT ([GroupId],[Jid],[Name],[Role]) VALUES (@GroupId,@Jid,@Name,@Role)
                WHEN MATCHED THEN
                    UPDATE SET [Name]=@Name, [Role]=@Role, [LeftAt]=NULL;
                """;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            cmd.Parameters.AddWithValue("@Jid",     m.Jid);
            cmd.Parameters.AddWithValue("@Name",    (object?)m.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Role",    m.Role);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        // MemberCount güncelle
        var upd = $"UPDATE {_g} SET [MemberCount]=@Cnt,[Updated]=SYSUTCDATETIME() WHERE [Id]=@Id";
        await using var updCmd = conn.CreateCommand();
        updCmd.CommandText = upd;
        updCmd.Parameters.AddWithValue("@Cnt", members.Count);
        updCmd.Parameters.AddWithValue("@Id",  groupId);
        await updCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddMembersAsync(int groupId, IReadOnlyList<string> jids, CancellationToken ct)
    {
        if (jids.Count == 0) return;
        var inputs = jids.Select(j => new WaGroupMemberInput(j, null)).ToList();
        await UpsertMembersAsync(groupId, inputs, ct);
    }

    public async Task RemoveMembersAsync(int groupId, IReadOnlyList<string> jids, CancellationToken ct)
    {
        if (jids.Count == 0) return;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        foreach (var jid in jids)
        {
            var sql = $"UPDATE {_gm} SET [LeftAt]=SYSUTCDATETIME() WHERE [GroupId]=@G AND [Jid]=@J AND [LeftAt] IS NULL";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@G", groupId);
            cmd.Parameters.AddWithValue("@J", jid);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<IReadOnlyList<WaGroupMember>> GetMembersAsync(int groupId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var sql = $"""
            SELECT [Id],[GroupId],[ContactId],[Jid],[Name],[Role],[JoinedAt],[LeftAt]
              FROM {_gm}
             WHERE [GroupId]=@G AND [LeftAt] IS NULL
             ORDER BY [Name],[Jid];
            """;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@G", groupId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<WaGroupMember>();
        while (await r.ReadAsync(ct))
            list.Add(new WaGroupMember
            {
                Id        = r.GetInt32(0),
                GroupId   = r.GetInt32(1),
                ContactId = r.IsDBNull(2) ? null : r.GetInt32(2),
                Jid       = r.GetString(3),
                Name      = r.IsDBNull(4) ? null : r.GetString(4),
                Role      = r.GetString(5),
                JoinedAt  = r.GetDateTime(6),
                LeftAt    = r.IsDBNull(7) ? null : r.GetDateTime(7),
            });
        return list;
    }

    private static WaGroup ReadGroup(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        GroupJid    = r.GetString(1),
        Subject     = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        MemberCount = r.GetInt32(4),
        IsActive    = r.GetBoolean(5),
        Created     = r.GetDateTime(6),
        Updated     = r.IsDBNull(7) ? null : r.GetDateTime(7),
    };
}

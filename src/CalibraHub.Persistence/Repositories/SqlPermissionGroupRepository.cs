using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Yetki grubu + üyelik persistence. Sistem DB (UserPermission ile aynı) —
/// OpenSystemConnectionAsync. Fiziksel grup silme yok; IsActive=0 deaktivasyon.
/// </summary>
public sealed class SqlPermissionGroupRepository : IPermissionGroupRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _groupTable;
    private readonly string _memberTable;
    private readonly string _usersTable;

    public SqlPermissionGroupRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _groupTable  = $"[{s}].[PermissionGroup]";
        _memberTable = $"[{s}].[UserPermissionGroup]";
        _usersTable  = $"[{s}].[Users]";
    }

    public async Task<IReadOnlyList<PermissionGroupDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT g.[Id], g.[Name], g.[Description], g.[IsActive],
                   (SELECT COUNT(*) FROM {_memberTable} m WHERE m.[GroupId] = g.[Id]) AS MemberCount
            FROM {_groupTable} g
            {(includeInactive ? "" : "WHERE g.[IsActive] = 1")}
            ORDER BY g.[Name];
            """;
        var list = new List<PermissionGroupDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new PermissionGroupDto(
                r.GetInt32(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetBoolean(3), r.GetInt32(4)));
        }
        return list;
    }

    public async Task<PermissionGroup?> GetAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[Name],[Description],[IsActive],[CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_groupTable} WHERE [Id]=@Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new PermissionGroup
        {
            Id          = r.GetInt32(0),
            Name        = r.GetString(1),
            Description = r.IsDBNull(2) ? null : r.GetString(2),
            IsActive    = r.GetBoolean(3),
            CreatedById = r.IsDBNull(4) ? null : r.GetInt32(4),
            Created     = r.GetDateTime(5),
            UpdatedById = r.IsDBNull(6) ? null : r.GetInt32(6),
            Updated     = r.IsDBNull(7) ? null : r.GetDateTime(7),
        };
    }

    public async Task<int> SaveAsync(PermissionGroup group, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);

        // Ad benzersizliği — kendisi hariç aynı isim var mı
        await using (var dupCmd = conn.CreateCommand())
        {
            dupCmd.CommandText = $"SELECT COUNT(*) FROM {_groupTable} WHERE [Name]=@Name AND [Id]!=@Id;";
            dupCmd.Parameters.AddWithValue("@Name", group.Name.Trim());
            dupCmd.Parameters.AddWithValue("@Id", group.Id);
            var dup = Convert.ToInt32(await dupCmd.ExecuteScalarAsync(ct));
            if (dup > 0)
                throw new InvalidOperationException($"Aynı isimde başka bir yetki grubu zaten tanımlı: '{group.Name.Trim()}'");
        }

        await using var cmd = conn.CreateCommand();
        if (group.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_groupTable} SET
                    [Name]=@Name, [Description]=@Desc, [IsActive]=@Active,
                    [UpdatedById]=@UserId, [Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", group.Id);
            cmd.Parameters.AddWithValue("@UserId", (object?)group.UpdatedById ?? DBNull.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_groupTable} ([Name],[Description],[IsActive],[CreatedById])
                VALUES (@Name,@Desc,@Active,@UserId);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@UserId", (object?)group.CreatedById ?? DBNull.Value);
        }
        cmd.Parameters.AddWithValue("@Name", group.Name.Trim());
        cmd.Parameters.AddWithValue("@Desc", (object?)group.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Active", group.IsActive);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<PermissionGroupMemberDto>> ListMembersAsync(int groupId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT m.[UserId], u.[FullName], u.[Email]
            FROM {_memberTable} m
            INNER JOIN {_usersTable} u ON u.[Id] = m.[UserId]
            WHERE m.[GroupId]=@G AND u.[IsActive]=1
            ORDER BY u.[FullName];
            """;
        cmd.Parameters.AddWithValue("@G", groupId);
        var list = new List<PermissionGroupMemberDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new PermissionGroupMemberDto(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2)));
        }
        return list;
    }

    public async Task ReplaceMembersAsync(int groupId, IReadOnlyList<int> userIds, int? createdById, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = $"DELETE FROM {_memberTable} WHERE [GroupId]=@G;";
                delCmd.Parameters.AddWithValue("@G", groupId);
                await delCmd.ExecuteNonQueryAsync(ct);
            }
            foreach (var uid in userIds.Distinct())
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_memberTable} ([UserId],[GroupId],[CreatedById])
                    SELECT @U, @G, @C WHERE EXISTS (SELECT 1 FROM {_usersTable} WHERE [Id]=@U);
                    """;
                insCmd.Parameters.AddWithValue("@U", uid);
                insCmd.Parameters.AddWithValue("@G", groupId);
                insCmd.Parameters.AddWithValue("@C", (object?)createdById ?? DBNull.Value);
                await insCmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            throw;
        }
    }

    public async Task<IReadOnlyList<PermissionGroupDto>> ListGroupsForUserAsync(int userId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT g.[Id], g.[Name], g.[Description], g.[IsActive],
                   (SELECT COUNT(*) FROM {_memberTable} m2 WHERE m2.[GroupId] = g.[Id]) AS MemberCount
            FROM {_memberTable} m
            INNER JOIN {_groupTable} g ON g.[Id] = m.[GroupId] AND g.[IsActive] = 1
            WHERE m.[UserId]=@U
            ORDER BY g.[Name];
            """;
        cmd.Parameters.AddWithValue("@U", userId);
        var list = new List<PermissionGroupDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new PermissionGroupDto(
                r.GetInt32(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetBoolean(3), r.GetInt32(4)));
        }
        return list;
    }
}

using System.Text;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlRptDefinitionRepository : IRptDefinitionRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _rptDef;
    private readonly string _rptDefRole;
    private readonly string _rptView;

    public SqlRptDefinitionRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var s = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
        _rptDef     = $"[{s}].[RptDef]";
        _rptDefRole = $"[{s}].[RptDefRole]";
        _rptView    = $"[{s}].[RptView]";
    }

    public async Task<IReadOnlyCollection<ReportDefinitionSummaryDto>> GetAccessibleAsync(
        int userId,
        IReadOnlyCollection<UserRole> roles,
        CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var roleParams = new StringBuilder();
        var i = 0;
        foreach (var role in roles)
        {
            if (i > 0) roleParams.Append(',');
            var pname = $"@r{i}";
            roleParams.Append(pname);
            cmd.Parameters.AddWithValue(pname, (byte)role);
            i++;
        }

        var hasRoles = i > 0;
        var roleClause = hasRoles
            ? $"EXISTS (SELECT 1 FROM {_rptDefRole} r WHERE r.[DefId] = d.[Id] AND r.[CanView] = 1 AND r.[Role] IN ({roleParams}))"
            : "0 = 1";
        var isAdmin = hasRoles && roles.Contains(UserRole.SystemAdmin);

        cmd.CommandText = $@"
            SELECT d.[Id], d.[Code], d.[Name], d.[ViewId], v.[Code] AS ViewCode,
                   d.[Category], d.[IsShared], d.[OwnerUserId], d.[UpdatedAt]
            FROM {_rptDef} d
            INNER JOIN {_rptView} v ON v.[Id] = d.[ViewId]
            WHERE d.[IsActive] = 1
              AND (
                    d.[OwnerUserId] = @UserId
                 OR @IsAdmin = 1
                 OR (d.[IsShared] = 1 AND ({roleClause}))
              )
            ORDER BY d.[UpdatedAt] DESC, d.[Name];";
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@IsAdmin", isAdmin);

        var list = new List<ReportDefinitionSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ReportDefinitionSummaryDto(
                Id: reader.GetInt32(0),
                Code: reader.GetString(1),
                Name: reader.GetString(2),
                ViewId: reader.GetInt32(3),
                ViewCode: reader.GetString(4),
                Category: (ReportCategory)reader.GetByte(5),
                IsShared: reader.GetBoolean(6),
                OwnerUserId: reader.GetInt32(7),
                UpdatedAt: reader.GetDateTime(8)));
        }
        return list;
    }

    public async Task<RptDefinition?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[ViewId],[Category],[ConfigJson],[OwnerUserId],
                   [IsShared],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_rptDef} WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<RptDefinition?> GetByCodeAsync(string code, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[ViewId],[Category],[ConfigJson],[OwnerUserId],
                   [IsShared],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_rptDef} WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code", code);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyCollection<RptDefinitionRole>> GetRolesAsync(int defId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[DefId],[Role],[CanView],[CanEdit],[CanDelete]
            FROM {_rptDefRole} WHERE [DefId] = @DefId ORDER BY [Role];";
        cmd.Parameters.AddWithValue("@DefId", defId);
        var list = new List<RptDefinitionRole>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RptDefinitionRole
            {
                Id = reader.GetInt32(0),
                DefId = reader.GetInt32(1),
                Role = (UserRole)reader.GetByte(2),
                CanView = reader.GetBoolean(3),
                CanEdit = reader.GetBoolean(4),
                CanDelete = reader.GetBoolean(5)
            });
        }
        return list;
    }

    public async Task<int> UpsertAsync(UpsertRptDefinitionRequest req, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            MERGE {_rptDef} AS T
            USING (SELECT @Code AS Code) AS S
            ON T.[Code] = S.Code
            WHEN MATCHED THEN
                UPDATE SET
                    [Name]        = @Name,
                    [ViewId]      = @ViewId,
                    [Category]    = @Category,
                    [ConfigJson]  = @ConfigJson,
                    [OwnerUserId] = @OwnerUserId,
                    [IsShared]    = @IsShared,
                    [IsActive]    = @IsActive,
                    [UpdatedAt]   = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([Code],[Name],[ViewId],[Category],[ConfigJson],[OwnerUserId],[IsShared],[IsActive])
                VALUES (@Code,@Name,@ViewId,@Category,@ConfigJson,@OwnerUserId,@IsShared,@IsActive);
            SELECT [Id] FROM {_rptDef} WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code", req.Code);
        cmd.Parameters.AddWithValue("@Name", req.Name);
        cmd.Parameters.AddWithValue("@ViewId", req.ViewId);
        cmd.Parameters.AddWithValue("@Category", (byte)req.Category);
        cmd.Parameters.AddWithValue("@ConfigJson", req.ConfigJson);
        cmd.Parameters.AddWithValue("@OwnerUserId", req.OwnerUserId);
        cmd.Parameters.AddWithValue("@IsShared", req.IsShared);
        cmd.Parameters.AddWithValue("@IsActive", req.IsActive);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null ? Convert.ToInt32(result) : 0;
    }

    public async Task ReplaceRolesAsync(int defId, IReadOnlyCollection<RptDefRoleDto> roles, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = $"DELETE FROM {_rptDefRole} WHERE [DefId] = @DefId;";
                delCmd.Parameters.AddWithValue("@DefId", defId);
                await delCmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var role in roles)
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $@"
                    INSERT INTO {_rptDefRole} ([DefId],[Role],[CanView],[CanEdit],[CanDelete])
                    VALUES (@DefId,@Role,@CanView,@CanEdit,@CanDelete);";
                insCmd.Parameters.AddWithValue("@DefId", defId);
                insCmd.Parameters.AddWithValue("@Role", (byte)role.Role);
                insCmd.Parameters.AddWithValue("@CanView", role.CanView);
                insCmd.Parameters.AddWithValue("@CanEdit", role.CanEdit);
                insCmd.Parameters.AddWithValue("@CanDelete", role.CanDelete);
                await insCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_rptDef} SET [IsActive] = 0, [UpdatedAt] = SYSUTCDATETIME() WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static RptDefinition Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        ViewId = r.GetInt32(3),
        Category = (ReportCategory)r.GetByte(4),
        ConfigJson = r.GetString(5),
        OwnerUserId = r.GetInt32(6),
        IsShared = r.GetBoolean(7),
        IsActive = r.GetBoolean(8),
        CreatedAt = r.GetDateTime(9),
        UpdatedAt = r.GetDateTime(10)
    };
}

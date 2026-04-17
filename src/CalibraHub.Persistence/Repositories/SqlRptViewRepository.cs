using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// RptView / RptViewCol / RptViewRole persistence.
/// ADO.NET + parametrize sorgular; bulk replace operasyonlari tek transaction icinde.
/// </summary>
public sealed class SqlRptViewRepository : IRptViewRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schemaName;
    private readonly string _rptView;
    private readonly string _rptViewCol;
    private readonly string _rptViewRole;

    public SqlRptViewRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schemaName = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schemaName.Replace("]", "]]");
        _rptView     = $"[{s}].[RptView]";
        _rptViewCol  = $"[{s}].[RptViewCol]";
        _rptViewRole = $"[{s}].[RptViewRole]";
    }

    public async Task<IReadOnlyCollection<RptView>> GetAllAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[SqlObjectName],[Description],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_rptView}
            WHERE (@IncludeInactive = 1 OR [IsActive] = 1)
            ORDER BY [Code];";
        cmd.Parameters.AddWithValue("@IncludeInactive", includeInactive);

        var list = new List<RptView>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapView(reader));
        return list;
    }

    public async Task<RptView?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[SqlObjectName],[Description],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_rptView} WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapView(reader) : null;
    }

    public async Task<RptView?> GetByCodeAsync(string code, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[SqlObjectName],[Description],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_rptView} WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code", code);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapView(reader) : null;
    }

    public async Task<IReadOnlyCollection<RptViewColumn>> GetColumnsAsync(int viewId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[ViewId],[ColName],[DisplayName],[DataType],[IsFilterable],[IsGroupable],
                   [IsAggregatable],[DefaultAggregate],[Ordinal],[ContextBinding]
            FROM {_rptViewCol}
            WHERE [ViewId] = @ViewId
            ORDER BY [Ordinal], [ColName];";
        cmd.Parameters.AddWithValue("@ViewId", viewId);

        var list = new List<RptViewColumn>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RptViewColumn
            {
                Id = reader.GetInt32(0),
                ViewId = reader.GetInt32(1),
                ColName = reader.GetString(2),
                DisplayName = reader.GetString(3),
                DataType = (ReportDataType)reader.GetByte(4),
                IsFilterable = reader.GetBoolean(5),
                IsGroupable = reader.GetBoolean(6),
                IsAggregatable = reader.GetBoolean(7),
                DefaultAggregate = reader.IsDBNull(8) ? null : (ReportAggregate)reader.GetByte(8),
                Ordinal = reader.GetInt32(9),
                ContextBinding = reader.IsDBNull(10) ? ReportContextBinding.None : (ReportContextBinding)reader.GetByte(10)
            });
        }
        return list;
    }

    public async Task<IReadOnlyCollection<RptViewRole>> GetRolesAsync(int viewId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[ViewId],[Role],[CanQuery],[CanDesign]
            FROM {_rptViewRole}
            WHERE [ViewId] = @ViewId
            ORDER BY [Role];";
        cmd.Parameters.AddWithValue("@ViewId", viewId);

        var list = new List<RptViewRole>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RptViewRole
            {
                Id = reader.GetInt32(0),
                ViewId = reader.GetInt32(1),
                Role = (UserRole)reader.GetByte(2),
                CanQuery = reader.GetBoolean(3),
                CanDesign = reader.GetBoolean(4)
            });
        }
        return list;
    }

    public async Task<int> UpsertViewAsync(UpsertRptViewRequest req, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            MERGE {_rptView} AS T
            USING (SELECT @Code AS Code) AS S
            ON T.[Code] = S.Code
            WHEN MATCHED THEN
                UPDATE SET
                    [Name]          = @Name,
                    [SqlObjectName] = @SqlObjectName,
                    [Description]   = @Description,
                    [IsActive]      = @IsActive,
                    [UpdatedAt]     = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([Code],[Name],[SqlObjectName],[Description],[IsActive])
                VALUES (@Code,@Name,@SqlObjectName,@Description,@IsActive);
            SELECT [Id] FROM {_rptView} WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code", req.Code);
        cmd.Parameters.AddWithValue("@Name", req.Name);
        cmd.Parameters.AddWithValue("@SqlObjectName", req.SqlObjectName);
        cmd.Parameters.AddWithValue("@Description", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsActive", req.IsActive);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null ? Convert.ToInt32(result) : 0;
    }

    public async Task ReplaceColumnsAsync(int viewId, IReadOnlyCollection<UpsertRptViewColumnRequest> cols, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = $"DELETE FROM {_rptViewCol} WHERE [ViewId] = @ViewId;";
                delCmd.Parameters.AddWithValue("@ViewId", viewId);
                await delCmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var col in cols)
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $@"
                    INSERT INTO {_rptViewCol}
                        ([ViewId],[ColName],[DisplayName],[DataType],[IsFilterable],[IsGroupable],
                         [IsAggregatable],[DefaultAggregate],[Ordinal],[ContextBinding])
                    VALUES
                        (@ViewId,@ColName,@DisplayName,@DataType,@IsFilterable,@IsGroupable,
                         @IsAggregatable,@DefaultAggregate,@Ordinal,@ContextBinding);";
                insCmd.Parameters.AddWithValue("@ViewId", viewId);
                insCmd.Parameters.AddWithValue("@ColName", col.ColName);
                insCmd.Parameters.AddWithValue("@DisplayName", col.DisplayName);
                insCmd.Parameters.AddWithValue("@DataType", (byte)col.DataType);
                insCmd.Parameters.AddWithValue("@IsFilterable", col.IsFilterable);
                insCmd.Parameters.AddWithValue("@IsGroupable", col.IsGroupable);
                insCmd.Parameters.AddWithValue("@IsAggregatable", col.IsAggregatable);
                insCmd.Parameters.AddWithValue("@DefaultAggregate",
                    col.DefaultAggregate.HasValue ? (object)(byte)col.DefaultAggregate.Value : DBNull.Value);
                insCmd.Parameters.AddWithValue("@Ordinal", col.Ordinal);
                insCmd.Parameters.AddWithValue("@ContextBinding", (byte)col.ContextBinding);
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

    public async Task ReplaceRolesAsync(int viewId, IReadOnlyCollection<UpsertRptViewRoleRequest> roles, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = $"DELETE FROM {_rptViewRole} WHERE [ViewId] = @ViewId;";
                delCmd.Parameters.AddWithValue("@ViewId", viewId);
                await delCmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var role in roles)
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $@"
                    INSERT INTO {_rptViewRole} ([ViewId],[Role],[CanQuery],[CanDesign])
                    VALUES (@ViewId,@Role,@CanQuery,@CanDesign);";
                insCmd.Parameters.AddWithValue("@ViewId", viewId);
                insCmd.Parameters.AddWithValue("@Role", (byte)role.Role);
                insCmd.Parameters.AddWithValue("@CanQuery", role.CanQuery);
                insCmd.Parameters.AddWithValue("@CanDesign", role.CanDesign);
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

    public async Task<IReadOnlyCollection<DiscoveredColumnDto>> DiscoverColumnsAsync(int viewId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        await using var nameCmd = conn.CreateCommand();
        nameCmd.CommandText = $"SELECT [SqlObjectName] FROM {_rptView} WHERE [Id] = @Id;";
        nameCmd.Parameters.AddWithValue("@Id", viewId);
        var sqlObjectName = await nameCmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(sqlObjectName))
            return Array.Empty<DiscoveredColumnDto>();

        await using var colCmd = conn.CreateCommand();
        colCmd.CommandText = @"
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS c
            INNER JOIN INFORMATION_SCHEMA.VIEWS v
                ON v.TABLE_SCHEMA = c.TABLE_SCHEMA AND v.TABLE_NAME = c.TABLE_NAME
            WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @ViewName
            ORDER BY c.ORDINAL_POSITION;";
        colCmd.Parameters.AddWithValue("@Schema", _schemaName);
        colCmd.Parameters.AddWithValue("@ViewName", sqlObjectName);

        var list = new List<DiscoveredColumnDto>();
        await using var reader = await colCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DiscoveredColumnDto(
                ColName: reader.GetString(0),
                SqlType: reader.GetString(1),
                IsNullable: string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }

    private static RptView MapView(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        SqlObjectName = r.GetString(3),
        Description = r.IsDBNull(4) ? null : r.GetString(4),
        IsActive = r.GetBoolean(5),
        CreatedAt = r.GetDateTime(6),
        UpdatedAt = r.GetDateTime(7)
    };
}

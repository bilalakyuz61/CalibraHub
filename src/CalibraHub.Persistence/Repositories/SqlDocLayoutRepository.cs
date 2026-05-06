using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDocLayoutRepository : IDocLayoutRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _layout;
    private readonly string _ds;

    public SqlDocLayoutRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var s = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
        _layout = $"[{s}].[DocLayout]";
        _ds     = $"[{s}].[DocLayoutDs]";
    }

    public async Task<IReadOnlyCollection<DocLayoutSummaryDto>> ListAsync(string? docType, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[DocType],[Description],[IsDefault],[OwnerUserId],[UpdatedAt]
            FROM {_layout}
            WHERE [IsActive] = 1
              AND (@DocType IS NULL OR [DocType] = @DocType)
            ORDER BY [IsDefault] DESC, [UpdatedAt] DESC, [Name];";
        cmd.Parameters.Add(new SqlParameter("@DocType", System.Data.SqlDbType.NVarChar, 60)
            { Value = (object?)docType ?? DBNull.Value });

        var list = new List<DocLayoutSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocLayoutSummaryDto(
                Id: reader.GetInt32(0),
                Code: reader.GetString(1),
                Name: reader.GetString(2),
                DocType: reader.GetString(3),
                Description: reader.IsDBNull(4) ? null : reader.GetString(4),
                IsDefault: reader.GetBoolean(5),
                OwnerUserId: reader.GetGuid(6),
                UpdatedAt: reader.GetDateTime(7)));
        }
        return list;
    }

    public async Task<DocLayout?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[DocType],[Description],[LayoutJson],
                   [PageW],[PageH],[MarginTop],[MarginBot],[MarginLeft],[MarginRight],
                   [OwnerUserId],[IsDefault],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_layout} WHERE [Id] = @Id AND [IsActive] = 1;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapLayout(reader) : null;
    }

    public async Task<IReadOnlyCollection<DocLayoutDs>> GetDataSourcesAsync(int layoutId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[LayoutId],[Alias],[Role],[ViewId],[AdHocSql],[JoinOn],[ParentAlias],[Ordinal]
            FROM {_ds} WHERE [LayoutId] = @LayoutId ORDER BY [Ordinal],[Id];";
        cmd.Parameters.AddWithValue("@LayoutId", layoutId);
        var list = new List<DocLayoutDs>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocLayoutDs
            {
                Id          = reader.GetInt32(0),
                LayoutId    = reader.GetInt32(1),
                Alias       = reader.GetString(2),
                Role        = reader.GetString(3),
                ViewId      = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                AdHocSql    = reader.IsDBNull(5) ? null : reader.GetString(5),
                JoinOn      = reader.IsDBNull(6) ? null : reader.GetString(6),
                ParentAlias = reader.IsDBNull(7) ? null : reader.GetString(7),
                Ordinal     = reader.GetInt32(8)
            });
        }
        return list;
    }

    public async Task<int> UpsertAsync(SaveDocLayoutRequest req, Guid ownerUserId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            MERGE {_layout} AS T
            USING (SELECT @Code AS Code) AS S ON T.[Code] = S.Code
            WHEN MATCHED THEN
                UPDATE SET
                    [Name]        = @Name,
                    [DocType]     = @DocType,
                    [Description] = @Description,
                    [LayoutJson]  = @LayoutJson,
                    [PageW]       = @PageW,
                    [PageH]       = @PageH,
                    [MarginTop]   = @MarginTop,
                    [MarginBot]   = @MarginBot,
                    [MarginLeft]  = @MarginLeft,
                    [MarginRight] = @MarginRight,
                    [IsDefault]   = @IsDefault,
                    [UpdatedAt]   = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([Code],[Name],[DocType],[Description],[LayoutJson],
                        [PageW],[PageH],[MarginTop],[MarginBot],[MarginLeft],[MarginRight],
                        [OwnerUserId],[IsDefault])
                VALUES (@Code,@Name,@DocType,@Description,@LayoutJson,
                        @PageW,@PageH,@MarginTop,@MarginBot,@MarginLeft,@MarginRight,
                        @OwnerUserId,@IsDefault);
            SELECT [Id] FROM {_layout} WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code",        req.Code);
        cmd.Parameters.AddWithValue("@Name",        req.Name);
        cmd.Parameters.AddWithValue("@DocType",     req.DocType);
        cmd.Parameters.Add(new SqlParameter("@Description", System.Data.SqlDbType.NVarChar, 500)
            { Value = (object?)req.Description ?? DBNull.Value });
        cmd.Parameters.AddWithValue("@LayoutJson",  req.LayoutJson);
        cmd.Parameters.AddWithValue("@PageW",       req.PageW);
        cmd.Parameters.AddWithValue("@PageH",       req.PageH);
        cmd.Parameters.AddWithValue("@MarginTop",   req.MarginTop);
        cmd.Parameters.AddWithValue("@MarginBot",   req.MarginBot);
        cmd.Parameters.AddWithValue("@MarginLeft",  req.MarginLeft);
        cmd.Parameters.AddWithValue("@MarginRight", req.MarginRight);
        cmd.Parameters.AddWithValue("@OwnerUserId", ownerUserId);
        cmd.Parameters.AddWithValue("@IsDefault",   req.IsDefault);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null ? Convert.ToInt32(result) : 0;
    }

    public async Task ReplaceDataSourcesAsync(int layoutId, IReadOnlyCollection<DocLayoutDsDto> sources, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = $"DELETE FROM {_ds} WHERE [LayoutId] = @LayoutId;";
                delCmd.Parameters.AddWithValue("@LayoutId", layoutId);
                await delCmd.ExecuteNonQueryAsync(ct);
            }

            var ordinal = 0;
            foreach (var src in sources)
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $@"
                    INSERT INTO {_ds} ([LayoutId],[Alias],[Role],[ViewId],[AdHocSql],[JoinOn],[ParentAlias],[Ordinal])
                    VALUES (@LayoutId,@Alias,@Role,@ViewId,@AdHocSql,@JoinOn,@ParentAlias,@Ordinal);";
                insCmd.Parameters.AddWithValue("@LayoutId", layoutId);
                insCmd.Parameters.AddWithValue("@Alias", src.Alias);
                insCmd.Parameters.AddWithValue("@Role", src.Role);
                insCmd.Parameters.Add(new SqlParameter("@ViewId", System.Data.SqlDbType.Int)
                    { Value = (object?)src.ViewId ?? DBNull.Value });
                insCmd.Parameters.Add(new SqlParameter("@AdHocSql", System.Data.SqlDbType.NVarChar, -1)
                    { Value = (object?)src.AdHocSql ?? DBNull.Value });
                insCmd.Parameters.Add(new SqlParameter("@JoinOn", System.Data.SqlDbType.NVarChar, 200)
                    { Value = (object?)src.JoinOn ?? DBNull.Value });
                insCmd.Parameters.Add(new SqlParameter("@ParentAlias", System.Data.SqlDbType.NVarChar, 60)
                    { Value = (object?)src.ParentAlias ?? DBNull.Value });
                insCmd.Parameters.AddWithValue("@Ordinal", ordinal++);
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
        cmd.CommandText = $"UPDATE {_layout} SET [IsActive]=0, [UpdatedAt]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DocLayout MapLayout(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        Code        = r.GetString(1),
        Name        = r.GetString(2),
        DocType     = r.GetString(3),
        Description = r.IsDBNull(4) ? null : r.GetString(4),
        LayoutJson  = r.GetString(5),
        PageW       = r.GetDecimal(6),
        PageH       = r.GetDecimal(7),
        MarginTop   = r.GetDecimal(8),
        MarginBot   = r.GetDecimal(9),
        MarginLeft  = r.GetDecimal(10),
        MarginRight = r.GetDecimal(11),
        OwnerUserId = r.GetGuid(12),
        IsDefault   = r.GetBoolean(13),
        IsActive    = r.GetBoolean(14),
        CreatedAt   = r.GetDateTime(15),
        UpdatedAt   = r.GetDateTime(16)
    };
}

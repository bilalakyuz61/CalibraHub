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
            SELECT [Id],[Code],[Name],[DocType],[Description],[IsDefault],[OwnerUserId],[UpdatedAt],
                   [DocumentTypeId],[OutputFormat],[DefaultSubject],[DefaultBody],
                   ISNULL([UseAsMailTemplate], 0) AS [UseAsMailTemplate]
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
                DocType: reader.IsDBNull(3) ? null : reader.GetString(3),
                Description: reader.IsDBNull(4) ? null : reader.GetString(4),
                IsDefault: reader.GetBoolean(5),
                OwnerUserId: reader.GetInt32(6),
                UpdatedAt: reader.GetDateTime(7),
                DocumentTypeId: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                OutputFormat: reader.IsDBNull(9) ? "pdf" : reader.GetString(9),
                DefaultSubject: reader.IsDBNull(10) ? null : reader.GetString(10),
                DefaultBody: reader.IsDBNull(11) ? null : reader.GetString(11),
                UseAsMailTemplate: !reader.IsDBNull(12) && reader.GetBoolean(12)));
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
                   [OwnerUserId],[IsDefault],[IsActive],[CreatedAt],[UpdatedAt],[DocumentTypeId],[OutputFormat],
                   [DefaultSubject],[DefaultBody],
                   [DefaultsViewName],[DefaultsSubjectColumn],[DefaultsBodyColumn],[DefaultsWhere],
                   ISNULL([UseAsMailTemplate], 0) AS [UseAsMailTemplate]
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

    public async Task<int> UpsertAsync(SaveDocLayoutRequest req, int ownerUserId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            MERGE {_layout} AS T
            USING (SELECT @Code AS Code) AS S ON T.[Code] = S.Code
            WHEN MATCHED THEN
                UPDATE SET
                    [Name]           = @Name,
                    [DocType]        = @DocType,
                    [DocumentTypeId] = @DocumentTypeId,
                    [Description]    = @Description,
                    [LayoutJson]     = @LayoutJson,
                    [PageW]          = @PageW,
                    [PageH]          = @PageH,
                    [MarginTop]      = @MarginTop,
                    [MarginBot]      = @MarginBot,
                    [MarginLeft]     = @MarginLeft,
                    [MarginRight]    = @MarginRight,
                    [IsDefault]      = @IsDefault,
                    [OutputFormat]   = @OutputFormat,
                    [DefaultSubject] = @DefaultSubject,
                    [DefaultBody]    = @DefaultBody,
                    [DefaultsViewName]      = @DefaultsViewName,
                    [DefaultsSubjectColumn] = @DefaultsSubjectColumn,
                    [DefaultsBodyColumn]    = @DefaultsBodyColumn,
                    [DefaultsWhere]         = @DefaultsWhere,
                    [UseAsMailTemplate]     = @UseAsMailTemplate,
                    [UpdatedAt]      = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([Code],[Name],[DocType],[DocumentTypeId],[Description],[LayoutJson],
                        [PageW],[PageH],[MarginTop],[MarginBot],[MarginLeft],[MarginRight],
                        [OwnerUserId],[IsDefault],[OutputFormat],[DefaultSubject],[DefaultBody],
                        [DefaultsViewName],[DefaultsSubjectColumn],[DefaultsBodyColumn],[DefaultsWhere],
                        [UseAsMailTemplate])
                VALUES (@Code,@Name,@DocType,@DocumentTypeId,@Description,@LayoutJson,
                        @PageW,@PageH,@MarginTop,@MarginBot,@MarginLeft,@MarginRight,
                        @OwnerUserId,@IsDefault,@OutputFormat,@DefaultSubject,@DefaultBody,
                        @DefaultsViewName,@DefaultsSubjectColumn,@DefaultsBodyColumn,@DefaultsWhere,
                        @UseAsMailTemplate);
            SELECT [Id] FROM {_layout} WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code",        req.Code);
        cmd.Parameters.AddWithValue("@Name",        req.Name);
        cmd.Parameters.Add(new SqlParameter("@DocType", System.Data.SqlDbType.NVarChar, 60)
            { Value = (object?)req.DocType ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@DocumentTypeId", System.Data.SqlDbType.Int)
            { Value = (object?)req.DocumentTypeId ?? DBNull.Value });
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
        cmd.Parameters.Add(new SqlParameter("@OutputFormat", System.Data.SqlDbType.NVarChar, 20)
            { Value = string.IsNullOrWhiteSpace(req.OutputFormat) ? "pdf" : req.OutputFormat });
        cmd.Parameters.Add(new SqlParameter("@DefaultSubject", System.Data.SqlDbType.NVarChar, 500)
            { Value = string.IsNullOrWhiteSpace(req.DefaultSubject) ? (object)DBNull.Value : req.DefaultSubject });
        cmd.Parameters.Add(new SqlParameter("@DefaultBody", System.Data.SqlDbType.NVarChar, -1)
            { Value = string.IsNullOrWhiteSpace(req.DefaultBody) ? (object)DBNull.Value : req.DefaultBody });
        cmd.Parameters.Add(new SqlParameter("@DefaultsViewName", System.Data.SqlDbType.NVarChar, 128)
            { Value = string.IsNullOrWhiteSpace(req.DefaultsViewName) ? (object)DBNull.Value : req.DefaultsViewName });
        cmd.Parameters.Add(new SqlParameter("@DefaultsSubjectColumn", System.Data.SqlDbType.NVarChar, 128)
            { Value = string.IsNullOrWhiteSpace(req.DefaultsSubjectColumn) ? (object)DBNull.Value : req.DefaultsSubjectColumn });
        cmd.Parameters.Add(new SqlParameter("@DefaultsBodyColumn", System.Data.SqlDbType.NVarChar, 128)
            { Value = string.IsNullOrWhiteSpace(req.DefaultsBodyColumn) ? (object)DBNull.Value : req.DefaultsBodyColumn });
        cmd.Parameters.Add(new SqlParameter("@DefaultsWhere", System.Data.SqlDbType.NVarChar, 2000)
            { Value = string.IsNullOrWhiteSpace(req.DefaultsWhere) ? (object)DBNull.Value : req.DefaultsWhere });
        cmd.Parameters.Add(new SqlParameter("@UseAsMailTemplate", System.Data.SqlDbType.Bit)
            { Value = req.UseAsMailTemplate });
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

    public async Task SetDefaultAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // DocumentTypeId varsa o uzerinden gruplandirir (ID-tabanli, tercih edilen).
        // Yoksa legacy DocType uzerinden — eski "custom" tasarimlar icin dahi calisir.
        cmd.CommandText = $@"
            DECLARE @DocType        nvarchar(60);
            DECLARE @DocumentTypeId int;
            SELECT @DocType = [DocType], @DocumentTypeId = [DocumentTypeId]
            FROM {_layout} WHERE [Id] = @Id AND [IsActive] = 1;
            IF @DocType IS NULL AND @DocumentTypeId IS NULL
                THROW 51001, 'Layout bulunamadi veya pasif.', 1;
            UPDATE {_layout}
            SET [IsDefault] = CASE WHEN [Id] = @Id THEN 1 ELSE 0 END,
                [UpdatedAt] = SYSUTCDATETIME()
            WHERE [IsActive] = 1
              AND (
                    (@DocumentTypeId IS NOT NULL AND [DocumentTypeId] = @DocumentTypeId)
                 OR (@DocumentTypeId IS NULL     AND [DocType] = @DocType)
              );";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DocLayout MapLayout(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        Code           = r.GetString(1),
        Name           = r.GetString(2),
        DocType        = r.IsDBNull(3) ? null : r.GetString(3),
        Description    = r.IsDBNull(4) ? null : r.GetString(4),
        LayoutJson     = r.GetString(5),
        PageW          = r.GetDecimal(6),
        PageH          = r.GetDecimal(7),
        MarginTop      = r.GetDecimal(8),
        MarginBot      = r.GetDecimal(9),
        MarginLeft     = r.GetDecimal(10),
        MarginRight    = r.GetDecimal(11),
        OwnerUserId    = r.GetInt32(12),
        IsDefault      = r.GetBoolean(13),
        IsActive       = r.GetBoolean(14),
        CreatedAt      = r.GetDateTime(15),
        UpdatedAt      = r.GetDateTime(16),
        DocumentTypeId = r.IsDBNull(17) ? null : r.GetInt32(17),
        OutputFormat   = r.IsDBNull(18) ? "pdf" : r.GetString(18),
        DefaultSubject = r.IsDBNull(19) ? null : r.GetString(19),
        DefaultBody    = r.IsDBNull(20) ? null : r.GetString(20),
        DefaultsViewName      = r.IsDBNull(21) ? null : r.GetString(21),
        DefaultsSubjectColumn = r.IsDBNull(22) ? null : r.GetString(22),
        DefaultsBodyColumn    = r.IsDBNull(23) ? null : r.GetString(23),
        DefaultsWhere         = r.IsDBNull(24) ? null : r.GetString(24),
        UseAsMailTemplate     = !r.IsDBNull(25) && r.GetBoolean(25),
    };
}

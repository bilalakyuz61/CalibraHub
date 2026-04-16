using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// FldSet tablosu persistence — form sabit alanlarinin rehber eslestirmesi.
/// SqlGuideRepository ile ayni ADO.NET pattern'ini kullanir.
/// </summary>
public sealed class SqlFieldSettingRepository : IFieldSettingRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schemaName;
    private readonly string _fldSetTable;

    public SqlFieldSettingRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schemaName = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _fldSetTable = $"[{_schemaName.Replace("]", "]]")}].[FldSet]";
    }

    public async Task<IReadOnlyCollection<FieldSetting>> GetByFormIdAsync(int formId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[FormId],[FieldKey],[FieldLabel],[GuideCode],[FilterJson],
                   [IsRequired],[FormatJson],[IsActive],[SortOrder],[CreatedAt],[UpdatedAt]
            FROM {_fldSetTable}
            WHERE [FormId] = @FormId
            ORDER BY [SortOrder], [FieldKey];";
        cmd.Parameters.AddWithValue("@FormId", formId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<IReadOnlyCollection<FieldSetting>> GetByGuideCodeAsync(string guideCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[FormId],[FieldKey],[FieldLabel],[GuideCode],[FilterJson],
                   [IsRequired],[FormatJson],[IsActive],[SortOrder],[CreatedAt],[UpdatedAt]
            FROM {_fldSetTable}
            WHERE [GuideCode] = @GuideCode AND [IsActive] = 1
            ORDER BY [FormId], [SortOrder], [FieldKey];";
        cmd.Parameters.AddWithValue("@GuideCode", guideCode);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<int> UpsertAsync(UpsertFieldSettingRequest request, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (request.Id <= 0)
        {
            cmd.CommandText = $@"
                INSERT INTO {_fldSetTable}
                    ([FormId],[FieldKey],[FieldLabel],[GuideCode],[FilterJson],
                     [IsRequired],[FormatJson],[IsActive],[SortOrder])
                VALUES
                    (@FormId,@FieldKey,@FieldLabel,@GuideCode,@FilterJson,
                     @IsRequired,@FormatJson,@IsActive,@SortOrder);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_fldSetTable}
                SET [FormId]=@FormId, [FieldKey]=@FieldKey, [FieldLabel]=@FieldLabel,
                    [GuideCode]=@GuideCode, [FilterJson]=@FilterJson,
                    [IsRequired]=@IsRequired, [FormatJson]=@FormatJson,
                    [IsActive]=@IsActive, [SortOrder]=@SortOrder,
                    [UpdatedAt]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", request.Id);
        }

        cmd.Parameters.AddWithValue("@FormId", request.FormId);
        cmd.Parameters.AddWithValue("@FieldKey", request.FieldKey);
        cmd.Parameters.AddWithValue("@FieldLabel", request.FieldLabel);
        cmd.Parameters.AddWithValue("@GuideCode", (object?)request.GuideCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FilterJson", (object?)request.FilterJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsRequired", request.IsRequired);
        cmd.Parameters.AddWithValue("@FormatJson", (object?)request.FormatJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsActive", request.IsActive);
        cmd.Parameters.AddWithValue("@SortOrder", request.SortOrder);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null ? Convert.ToInt32(result) : 0;
    }

    public async Task BulkMapGuideAsync(BulkMapGuideRequest request, CancellationToken ct)
    {
        if (request.Fields == null || request.Fields.Count == 0) return;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        foreach (var field in request.Fields)
        {
            await using var cmd = conn.CreateCommand();

            // MERGE: varsa guncelle, yoksa ekle
            cmd.CommandText = $@"
                MERGE {_fldSetTable} AS T
                USING (SELECT @FormId AS FormId, @FieldKey AS FieldKey) AS S
                ON T.[FormId] = S.FormId AND T.[FieldKey] = S.FieldKey
                WHEN MATCHED THEN
                    UPDATE SET
                        [FieldLabel]  = @FieldLabel,
                        [GuideCode]   = @GuideCode,
                        [FilterJson]  = @FilterJson,
                        [IsRequired]  = @IsRequired,
                        [IsActive]    = 1,
                        [UpdatedAt]   = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT ([FormId],[FieldKey],[FieldLabel],[GuideCode],[FilterJson],[IsRequired],[IsActive])
                    VALUES (@FormId, @FieldKey, @FieldLabel, @GuideCode, @FilterJson, @IsRequired, 1);";

            cmd.Parameters.AddWithValue("@FormId", request.FormId);
            cmd.Parameters.AddWithValue("@FieldKey", field.FieldKey);
            cmd.Parameters.AddWithValue("@FieldLabel", field.FieldLabel);
            cmd.Parameters.AddWithValue("@GuideCode",
                field.Mapped ? (object)request.GuideCode : DBNull.Value);
            cmd.Parameters.AddWithValue("@FilterJson",
                field.Mapped ? (object?)field.FilterJson ?? DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsRequired",
                field.Mapped && field.IsRequired);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<int> UpsertByFormCodeAsync(UpsertFieldSettingByFormCodeRequest request, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            DECLARE @FormId INT;
            SELECT @FormId = [Id] FROM [dbo].[Forms] WHERE [FormCode] = @FormCode;
            IF @FormId IS NULL RAISERROR('Form bulunamadi: %s', 16, 1, @FormCode);

            MERGE {_fldSetTable} AS T
            USING (SELECT @FormId AS FormId, @FieldKey AS FieldKey) AS S
            ON T.[FormId] = S.FormId AND T.[FieldKey] = S.FieldKey
            WHEN MATCHED THEN
                UPDATE SET [FieldLabel] = @FieldLabel, [GuideCode]  = @GuideCode,
                           [FilterJson] = @FilterJson, [IsRequired] = @IsRequired,
                           [FormatJson] = @FormatJson, [IsActive]   = 1,
                           [UpdatedAt]  = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([FormId],[FieldKey],[FieldLabel],[GuideCode],[FilterJson],[IsRequired],[FormatJson],[IsActive],[SortOrder])
                VALUES (@FormId,@FieldKey,@FieldLabel,@GuideCode,@FilterJson,@IsRequired,@FormatJson,1,10);

            SELECT [Id] FROM {_fldSetTable} WHERE [FormId] = @FormId AND [FieldKey] = @FieldKey;";

        cmd.Parameters.AddWithValue("@FormCode",   request.FormCode);
        cmd.Parameters.AddWithValue("@FieldKey",   request.FieldKey);
        cmd.Parameters.AddWithValue("@FieldLabel", request.FieldLabel);
        cmd.Parameters.AddWithValue("@GuideCode",  (object?)request.GuideCode  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FilterJson", (object?)request.FilterJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsRequired", request.IsRequired);
        cmd.Parameters.AddWithValue("@FormatJson", (object?)request.FormatJson ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_fldSetTable} WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<FieldGuideBindingDto>> GetGuideBindingsForFormAsync(
        string formCode, CancellationToken ct)
    {
        var s = _schemaName.Replace("]", "]]");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT fs.[FieldKey], fs.[FieldLabel], fs.[GuideCode], fs.[FilterJson], fs.[IsRequired], fs.[FormatJson]
            FROM [{s}].[FldSet] fs
            INNER JOIN [dbo].[Forms] f ON f.[Id] = fs.[FormId]
            WHERE f.[FormCode] = @FormCode
              AND fs.[GuideCode] IS NOT NULL
              AND fs.[IsActive] = 1
            ORDER BY fs.[SortOrder], fs.[FieldKey];";
        cmd.Parameters.AddWithValue("@FormCode", formCode);

        var result = new List<FieldGuideBindingDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new FieldGuideBindingDto(
                FieldKey:   reader.GetString(0),
                FieldLabel: reader.GetString(1),
                GuideCode:  reader.GetString(2),
                FilterJson: reader.IsDBNull(3) ? null : reader.GetString(3),
                IsRequired: reader.GetBoolean(4),
                FormatJson: reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return result;
    }

    public async Task<IReadOnlyCollection<string>> DiscoverFieldsAsync(int formId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) BaseTable'i bul
        await using var formCmd = conn.CreateCommand();
        formCmd.CommandText = "SELECT [BaseTable] FROM [dbo].[Forms] WHERE [Id] = @FormId;";
        formCmd.Parameters.AddWithValue("@FormId", formId);
        var baseTable = await formCmd.ExecuteScalarAsync(ct) as string;

        if (string.IsNullOrWhiteSpace(baseTable))
            return Array.Empty<string>();

        // 2) Schema ve tablo adini ayir
        var parts = baseTable.Split('.');
        string tableSchema, tableName;
        if (parts.Length >= 2)
        {
            tableSchema = parts[0].Trim('[', ']', ' ');
            tableName = parts[1].Trim('[', ']', ' ');
        }
        else
        {
            tableSchema = "dbo";
            tableName = parts[0].Trim('[', ']', ' ');
        }

        // 3) INFORMATION_SCHEMA + FldSet'te zaten tanimli olanlari haric tut
        var s = _schemaName.Replace("]", "]]");
        await using var colCmd = conn.CreateCommand();
        colCmd.CommandText = $@"
            SELECT c.COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = @TableSchema
              AND c.TABLE_NAME   = @TableName
              AND c.COLUMN_NAME NOT IN (
                  SELECT fs.[FieldKey]
                  FROM [{s}].[FldSet] fs
                  WHERE fs.[FormId] = @FormId
              )
            ORDER BY c.ORDINAL_POSITION;";
        colCmd.Parameters.AddWithValue("@TableSchema", tableSchema);
        colCmd.Parameters.AddWithValue("@TableName", tableName);
        colCmd.Parameters.AddWithValue("@FormId", formId);

        var columns = new List<string>();
        await using var reader = await colCmd.ExecuteReaderAsync(cancellationToken: ct);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    // ── Reader helper ──
    private static async Task<IReadOnlyCollection<FieldSetting>> ReadListAsync(SqlCommand cmd, CancellationToken ct)
    {
        var result = new List<FieldSetting>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new FieldSetting
            {
                Id = reader.GetInt32(0),
                FormId = reader.GetInt32(1),
                FieldKey = reader.GetString(2),
                FieldLabel = reader.GetString(3),
                GuideCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                FilterJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                IsRequired = reader.GetBoolean(6),
                FormatJson = reader.IsDBNull(7) ? null : reader.GetString(7),
                IsActive = reader.GetBoolean(8),
                SortOrder = reader.GetInt32(9),
                CreatedAt = reader.GetDateTime(10),
                UpdatedAt = reader.GetDateTime(11)
            });
        }
        return result;
    }
}

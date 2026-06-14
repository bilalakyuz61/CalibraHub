using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlBpmFormRepository(SqlServerConnectionFactory connectionFactory)
    : IBpmFormRepository
{
    // ── Definition ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BpmFormDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT fd.Id, fd.Name, fd.Code, fd.Description, fd.WorkflowDefinitionId,
                   wd.Name AS WorkflowName, fd.IsActive
            FROM   [BpmFormDefinition] fd
            LEFT JOIN [WorkflowDefinition] wd ON wd.Id = fd.WorkflowDefinitionId
            WHERE  fd.IsActive = 1
            ORDER  BY fd.Name;
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var result = new List<BpmFormDefinitionDto>();
        while (await r.ReadAsync(ct))
            result.Add(ReadDefinitionDto(r));
        return result;
    }

    public async Task<BpmFormDefinitionDetailDto?> GetDefinitionDetailAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT fd.Id, fd.Name, fd.Code, fd.Description, fd.WorkflowDefinitionId,
                   wd.Name AS WorkflowName, fd.IsActive
            FROM   [BpmFormDefinition] fd
            LEFT JOIN [WorkflowDefinition] wd ON wd.Id = fd.WorkflowDefinitionId
            WHERE  fd.Id = @Id;

            SELECT Id, FormDefinitionId, [Key], Label, FieldType, IsRequired,
                   SortOrder, OptionsJson, Placeholder, DefaultValue,
                   LayoutRow, LayoutCol, LayoutColSpan
            FROM   [BpmFormField]
            WHERE  FormDefinitionId = @Id
            ORDER  BY SortOrder, Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var def = ReadDefinitionDto(r);
        await r.NextResultAsync(ct);
        var fields = new List<BpmFormFieldDto>();
        while (await r.ReadAsync(ct))
            fields.Add(ReadFieldDto(r));
        return new BpmFormDefinitionDetailDto(def, fields);
    }

    public async Task<int> SaveDefinitionAsync(BpmFormDefinition def, int? actor, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        if (def.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO [BpmFormDefinition]
                    (Name, Code, Description, WorkflowDefinitionId, IsActive, CreatedById, Created)
                OUTPUT INSERTED.Id
                VALUES (@Name, @Code, @Desc, @WfId, @IsActive, @Actor, SYSUTCDATETIME());
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE [BpmFormDefinition]
                SET Name=@Name, Code=@Code, Description=@Desc,
                    WorkflowDefinitionId=@WfId, IsActive=@IsActive,
                    UpdatedById=@Actor, Updated=SYSUTCDATETIME()
                WHERE Id=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", def.Id);
        }
        cmd.Parameters.AddWithValue("@Name",     def.Name);
        cmd.Parameters.AddWithValue("@Code",     def.Code);
        cmd.Parameters.AddWithValue("@Desc",     (object?)def.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WfId",     (object?)def.WorkflowDefinitionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsActive",  def.IsActive);
        cmd.Parameters.AddWithValue("@Actor",     (object?)actor ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task DeleteDefinitionAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE [BpmFormDefinition] SET IsActive=0 WHERE Id=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> SaveFieldAsync(BpmFormField field, int? actor, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        if (field.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO [BpmFormField]
                    (FormDefinitionId, [Key], Label, FieldType, IsRequired,
                     SortOrder, OptionsJson, Placeholder, DefaultValue,
                     LayoutRow, LayoutCol, LayoutColSpan,
                     CreatedById, Created)
                OUTPUT INSERTED.Id
                VALUES (@FormId, @Key, @Label, @FieldType, @IsRequired,
                        @Sort, @Options, @Placeholder, @Default,
                        @LayoutRow, @LayoutCol, @LayoutColSpan,
                        @Actor, SYSUTCDATETIME());
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE [BpmFormField]
                SET [Key]=@Key, Label=@Label, FieldType=@FieldType, IsRequired=@IsRequired,
                    SortOrder=@Sort, OptionsJson=@Options, Placeholder=@Placeholder,
                    DefaultValue=@Default, LayoutRow=@LayoutRow, LayoutCol=@LayoutCol,
                    LayoutColSpan=@LayoutColSpan, UpdatedById=@Actor, Updated=SYSUTCDATETIME()
                WHERE Id=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", field.Id);
        }
        cmd.Parameters.AddWithValue("@FormId",        field.FormDefinitionId);
        cmd.Parameters.AddWithValue("@Key",            field.Key);
        cmd.Parameters.AddWithValue("@Label",          field.Label);
        cmd.Parameters.AddWithValue("@FieldType",      field.FieldType.ToString());
        cmd.Parameters.AddWithValue("@IsRequired",     field.IsRequired);
        cmd.Parameters.AddWithValue("@Sort",           field.SortOrder);
        cmd.Parameters.AddWithValue("@Options",        (object?)field.OptionsJson   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Placeholder",    (object?)field.Placeholder   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Default",        (object?)field.DefaultValue  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LayoutRow",      field.LayoutRow);
        cmd.Parameters.AddWithValue("@LayoutCol",      field.LayoutCol);
        cmd.Parameters.AddWithValue("@LayoutColSpan",  field.LayoutColSpan);
        cmd.Parameters.AddWithValue("@Actor",          (object?)actor               ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task DeleteFieldAsync(int fieldId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM [BpmFormField] WHERE Id=@Id;";
        cmd.Parameters.AddWithValue("@Id", fieldId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Submission ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BpmFormSubmissionDto>> GetSubmissionsByFormAsync(
        int formDefinitionId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.Id, s.FormDefinitionId, fd.Name, s.SubmittedBy,
                   s.SubmittedAt, s.Status, s.WorkflowInstanceId
            FROM   [BpmFormSubmission] s
            JOIN   [BpmFormDefinition] fd ON fd.Id = s.FormDefinitionId
            WHERE  s.FormDefinitionId = @FormId
            ORDER  BY s.SubmittedAt DESC;
            """;
        cmd.Parameters.AddWithValue("@FormId", formDefinitionId);
        return await ReadSubmissionListAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<BpmFormSubmissionDto>> GetMySubmissionsAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.Id, s.FormDefinitionId, fd.Name, s.SubmittedBy,
                   s.SubmittedAt, s.Status, s.WorkflowInstanceId
            FROM   [BpmFormSubmission] s
            JOIN   [BpmFormDefinition] fd ON fd.Id = s.FormDefinitionId
            WHERE  s.SubmittedBy = @UserId
            ORDER  BY s.SubmittedAt DESC;
            """;
        cmd.Parameters.AddWithValue("@UserId", userId);
        return await ReadSubmissionListAsync(cmd, ct);
    }

    public async Task<BpmFormSubmissionDetailDto?> GetSubmissionDetailAsync(
        int submissionId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.Id, s.FormDefinitionId, fd.Name, s.SubmittedBy,
                   s.SubmittedAt, s.Status, s.WorkflowInstanceId
            FROM   [BpmFormSubmission] s
            JOIN   [BpmFormDefinition] fd ON fd.Id = s.FormDefinitionId
            WHERE  s.Id = @Id;

            SELECT Id, FormDefinitionId, [Key], Label, FieldType, IsRequired,
                   SortOrder, OptionsJson, Placeholder, DefaultValue,
                   LayoutRow, LayoutCol, LayoutColSpan
            FROM   [BpmFormField]
            WHERE  FormDefinitionId = (SELECT FormDefinitionId FROM [BpmFormSubmission] WHERE Id = @Id)
            ORDER  BY SortOrder, Id;

            SELECT Id, SubmissionId, FieldKey, Value
            FROM   [BpmFormSubmissionValue]
            WHERE  SubmissionId = @Id;
            """;
        cmd.Parameters.AddWithValue("@Id", submissionId);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        if (!await r.ReadAsync(ct)) return null;
        var sub = ReadSubmissionDto(r);
        await r.NextResultAsync(ct);
        var fields = new List<BpmFormFieldDto>();
        while (await r.ReadAsync(ct)) fields.Add(ReadFieldDto(r));
        await r.NextResultAsync(ct);
        var values = new List<BpmSubmissionValueDto>();
        while (await r.ReadAsync(ct))
            values.Add(new BpmSubmissionValueDto(r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));

        // dummy definition for form detail
        var defDto = new BpmFormDefinitionDto(sub.FormDefinitionId, sub.FormName, "", null, null, null, true);
        var formDetail = new BpmFormDefinitionDetailDto(defDto, fields);
        return new BpmFormSubmissionDetailDto(sub, formDetail, values);
    }

    public async Task<int> CreateSubmissionAsync(BpmFormSubmission submission, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO [BpmFormSubmission]
                (FormDefinitionId, SubmittedBy, SubmittedAt, Status, WorkflowInstanceId, CreatedById, Created)
            OUTPUT INSERTED.Id
            VALUES (@FormId, @SubmittedBy, @SubmittedAt, @Status, @WfId, @CreatedById, SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@FormId",      submission.FormDefinitionId);
        cmd.Parameters.AddWithValue("@SubmittedBy",  (object?)submission.SubmittedBy  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SubmittedAt",  submission.SubmittedAt);
        cmd.Parameters.AddWithValue("@Status",       submission.Status);
        cmd.Parameters.AddWithValue("@WfId",         (object?)submission.WorkflowInstanceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedById",  (object?)submission.CreatedById ?? DBNull.Value);
        var submissionId = (int)(await cmd.ExecuteScalarAsync(ct))!;

        // Değerleri kaydet
        foreach (var v in submission.Values)
        {
            await using var vcmd = conn.CreateCommand();
            vcmd.CommandText = """
                INSERT INTO [BpmFormSubmissionValue] (SubmissionId, FieldKey, Value)
                VALUES (@SubId, @Key, @Val);
                """;
            vcmd.Parameters.AddWithValue("@SubId", submissionId);
            vcmd.Parameters.AddWithValue("@Key",    v.FieldKey);
            vcmd.Parameters.AddWithValue("@Val",    (object?)v.Value ?? DBNull.Value);
            await vcmd.ExecuteNonQueryAsync(ct);
        }
        return submissionId;
    }

    public async Task UpdateSubmissionStatusAsync(BpmFormSubmission submission, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE [BpmFormSubmission]
            SET Status=@Status, WorkflowInstanceId=@WfId, Updated=SYSUTCDATETIME()
            WHERE Id=@Id;
            """;
        cmd.Parameters.AddWithValue("@Id",     submission.Id);
        cmd.Parameters.AddWithValue("@Status", submission.Status);
        cmd.Parameters.AddWithValue("@WfId",   (object?)submission.WorkflowInstanceId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Readers ──────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<BpmFormSubmissionDto>> ReadSubmissionListAsync(
        Microsoft.Data.SqlClient.SqlCommand cmd, CancellationToken ct)
    {
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var result = new List<BpmFormSubmissionDto>();
        while (await r.ReadAsync(ct)) result.Add(ReadSubmissionDto(r));
        return result;
    }

    private static BpmFormDefinitionDto ReadDefinitionDto(Microsoft.Data.SqlClient.SqlDataReader r) =>
        new(r.GetInt32(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetInt32(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetBoolean(6));

    private static BpmFormFieldDto ReadFieldDto(Microsoft.Data.SqlClient.SqlDataReader r) =>
        new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
            Enum.Parse<BpmFieldType>(r.GetString(4)),
            r.GetBoolean(5), r.GetInt32(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9),
            r.IsDBNull(10) ? 0 : r.GetInt32(10),
            r.IsDBNull(11) ? 0 : r.GetInt32(11),
            r.IsDBNull(12) ? 12 : r.GetInt32(12));

    private static BpmFormSubmissionDto ReadSubmissionDto(Microsoft.Data.SqlClient.SqlDataReader r) =>
        new(r.GetInt32(0), r.GetInt32(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.GetDateTime(4), r.GetString(5),
            r.IsDBNull(6) ? null : r.GetInt32(6));
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDesignTemplateRepository : IDesignTemplateRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlDesignTemplateRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[design_templates]";
    }

    public async Task<IReadOnlyCollection<DesignTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<DesignTemplate>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [name], [type], [sub_type], [description], [html_content], [css_content], [gjs_data], [jsr_content], [is_active], [created_at], [updated_at]
            FROM {_table}
            ORDER BY [type], [sub_type], [name];
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<DesignTemplate>> GetByTypeAsync(string type, CancellationToken cancellationToken)
    {
        var list = new List<DesignTemplate>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [name], [type], [sub_type], [description], [html_content], [css_content], [gjs_data], [jsr_content], [is_active], [created_at], [updated_at]
            FROM {_table}
            WHERE [type] = @Type
            ORDER BY [sub_type], [name];
            """;
        command.Parameters.Add(new SqlParameter("@Type", type));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<DesignTemplate>> GetBySubTypeAsync(string subType, CancellationToken cancellationToken)
    {
        var list = new List<DesignTemplate>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [name], [type], [sub_type], [description], [html_content], [css_content], [gjs_data], [jsr_content], [is_active], [created_at], [updated_at]
            FROM {_table}
            WHERE [type] = 'document' AND [sub_type] = @SubType
            ORDER BY [name];
            """;
        command.Parameters.Add(new SqlParameter("@SubType", subType));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));
        return list;
    }

    public async Task<DesignTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [name], [type], [sub_type], [description], [html_content], [css_content], [gjs_data], [jsr_content], [is_active], [created_at], [updated_at]
            FROM {_table}
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task SaveAsync(DesignTemplate template, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        template.UpdatedAt = DateTime.Now;

        command.CommandText = $"""
            MERGE {_table} AS target
            USING (SELECT @Id AS [id]) AS source ON target.[id] = source.[id]
            WHEN MATCHED THEN
                UPDATE SET
                    [name]         = @Name,
                    [type]         = @Type,
                    [sub_type]     = @SubType,
                    [description]  = @Description,
                    [html_content] = @HtmlContent,
                    [css_content]  = @CssContent,
                    [gjs_data]     = @GjsData,
                    [jsr_content]  = @JsrContent,
                    [is_active]    = @IsActive,
                    [updated_at]   = @UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT ([id], [name], [type], [sub_type], [description], [html_content], [css_content], [gjs_data], [jsr_content], [is_active], [created_at], [updated_at])
                VALUES (@Id, @Name, @Type, @SubType, @Description, @HtmlContent, @CssContent, @GjsData, @JsrContent, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        command.Parameters.Add(new SqlParameter("@Id", template.Id));
        command.Parameters.Add(new SqlParameter("@Name", template.Name));
        command.Parameters.Add(new SqlParameter("@Type", template.Type));
        command.Parameters.Add(new SqlParameter("@SubType", (object?)template.SubType ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Description", (object?)template.Description ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@HtmlContent", (object?)template.HtmlContent ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CssContent", (object?)template.CssContent ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@GjsData", (object?)template.GjsData ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@JsrContent", (object?)template.JsrContent ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", template.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", template.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", template.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_table} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DesignTemplate Map(SqlDataReader r) => new()
    {
        Id          = r.GetGuid(0),
        Name        = r.GetString(1),
        Type        = r.GetString(2),
        SubType     = r.IsDBNull(3)  ? null : r.GetString(3),
        Description = r.IsDBNull(4)  ? null : r.GetString(4),
        HtmlContent = r.IsDBNull(5)  ? null : r.GetString(5),
        CssContent  = r.IsDBNull(6)  ? null : r.GetString(6),
        GjsData     = r.IsDBNull(7)  ? null : r.GetString(7),
        JsrContent  = r.IsDBNull(8)  ? null : r.GetString(8),
        IsActive    = r.GetBoolean(9),
        CreatedAt   = r.GetDateTime(10),
        UpdatedAt   = r.GetDateTime(11)
    };
}

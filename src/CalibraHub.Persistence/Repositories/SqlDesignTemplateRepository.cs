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
        _table = $"[{schema}].[DesignTemplate]";
    }

    public async Task<IReadOnlyCollection<DesignTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<DesignTemplate>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [Name], [Type], [SubType], [Description], [HtmlContent], [CssContent], [GjsData], [JsrContent], [IsActive], [Created], [Updated]
            FROM {_table}
            ORDER BY [Type], [SubType], [Name];
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
            SELECT [Id], [Name], [Type], [SubType], [Description], [HtmlContent], [CssContent], [GjsData], [JsrContent], [IsActive], [Created], [Updated]
            FROM {_table}
            WHERE [Type] = @Type
            ORDER BY [SubType], [Name];
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
            SELECT [Id], [Name], [Type], [SubType], [Description], [HtmlContent], [CssContent], [GjsData], [JsrContent], [IsActive], [Created], [Updated]
            FROM {_table}
            WHERE [Type] = 'document' AND [SubType] = @SubType
            ORDER BY [Name];
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
            SELECT [Id], [Name], [Type], [SubType], [Description], [HtmlContent], [CssContent], [GjsData], [JsrContent], [IsActive], [Created], [Updated]
            FROM {_table}
            WHERE [Id] = @Id;
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
            USING (SELECT @Id AS [Id]) AS source ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [Name]        = @Name,
                    [Type]        = @Type,
                    [SubType]     = @SubType,
                    [Description] = @Description,
                    [HtmlContent] = @HtmlContent,
                    [CssContent]  = @CssContent,
                    [GjsData]     = @GjsData,
                    [JsrContent]  = @JsrContent,
                    [IsActive]    = @IsActive,
                    [Updated]     = @UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT ([Id], [Name], [Type], [SubType], [Description], [HtmlContent], [CssContent], [GjsData], [JsrContent], [IsActive], [Created], [Updated])
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
        command.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id;";
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

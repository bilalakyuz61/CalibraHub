using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDocumentTypeRepository : IDocumentTypeRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlDocumentTypeRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[document_types]";
    }

    public async Task<IReadOnlyCollection<DocumentType>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<DocumentType>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id],[code],[name],[sql_view_name],[description],[is_active],[created_at],[updated_at]
            FROM {_table}
            ORDER BY [name];
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));
        return list;
    }

    public async Task<DocumentType?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id],[code],[name],[sql_view_name],[description],[is_active],[created_at],[updated_at]
            FROM {_table}
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<DocumentType?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id],[code],[name],[sql_view_name],[description],[is_active],[created_at],[updated_at]
            FROM {_table}
            WHERE [code] = @Code;
            """;
        command.Parameters.Add(new SqlParameter("@Code", code));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<int> SaveAsync(DocumentType entity, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (entity.Id > 0)
        {
            command.CommandText = $"""
                UPDATE {_table}
                    SET [code]=@Code,[name]=@Name,[sql_view_name]=@SqlViewName,
                        [description]=@Description,[is_active]=@IsActive,[updated_at]=GETDATE()
                    WHERE [id]=@Id;
                SELECT @Id;
                """;
            command.Parameters.Add(new SqlParameter("@Id", entity.Id));
        }
        else
        {
            command.CommandText = $"""
                INSERT INTO {_table} ([code],[name],[sql_view_name],[description],[is_active],[created_at],[updated_at])
                VALUES (@Code,@Name,@SqlViewName,@Description,@IsActive,GETDATE(),GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        }
        command.Parameters.Add(new SqlParameter("@Code", entity.Code));
        command.Parameters.Add(new SqlParameter("@Name", entity.Name));
        command.Parameters.Add(new SqlParameter("@SqlViewName", (object?)entity.SqlViewName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Description", (object?)entity.Description ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_table} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DocumentType Map(SqlDataReader r) => new()
    {
        Id           = r.GetInt32(0),
        Code         = r.GetString(1),
        Name         = r.GetString(2),
        SqlViewName  = r.IsDBNull(3) ? null : r.GetString(3),
        Description  = r.IsDBNull(4) ? null : r.GetString(4),
        IsActive     = r.GetBoolean(5),
        CreatedAt    = r.GetDateTime(6),
        UpdatedAt    = r.GetDateTime(7),
    };
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlReportTemplateRepository : IReportTemplateRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlReportTemplateRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[report_templates]";
    }

    private const string Columns = "[id],[name],[document_type_id],[frx_file_path],[description],[is_default],[is_active],[Created],[Updated],[frx_content],[sql_view_name],[key_column],[output_options_json],[order_column],[order_direction]";

    public async Task<IReadOnlyCollection<ReportTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<ReportTemplate>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {_table} ORDER BY [name];";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));
        return list;
    }

    public async Task<IReadOnlyCollection<ReportTemplate>> GetByDocumentTypeIdAsync(int documentTypeId, CancellationToken cancellationToken)
    {
        var list = new List<ReportTemplate>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {_table} WHERE [document_type_id] = @DocTypeId ORDER BY [name];";
        command.Parameters.Add(new SqlParameter("@DocTypeId", documentTypeId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));
        return list;
    }

    public async Task<ReportTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {_table} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<ReportTemplate?> GetDefaultByDocumentTypeIdAsync(int documentTypeId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TOP 1 {Columns} FROM {_table} WHERE [document_type_id] = @DocTypeId AND [is_default] = 1;";
        command.Parameters.Add(new SqlParameter("@DocTypeId", documentTypeId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<int> SaveAsync(ReportTemplate entity, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (entity.Id > 0)
        {
            command.CommandText = $"""
                UPDATE {_table}
                    SET [name]=@Name,[document_type_id]=@DocTypeId,[frx_file_path]=@FrxFilePath,[frx_content]=@FrxContent,
                        [description]=@Description,[is_default]=@IsDefault,[is_active]=@IsActive,
                        [sql_view_name]=@SqlViewName,[key_column]=@KeyColumn,[output_options_json]=@OutputOptionsJson,
                        [order_column]=@OrderColumn,[order_direction]=@OrderDirection,
                        [Updated]=GETDATE()
                    WHERE [id]=@Id;
                SELECT @Id;
                """;
            command.Parameters.Add(new SqlParameter("@Id", entity.Id));
        }
        else
        {
            command.CommandText = $"""
                INSERT INTO {_table} ([name],[document_type_id],[frx_file_path],[frx_content],[description],[is_default],[is_active],[sql_view_name],[key_column],[output_options_json],[order_column],[order_direction],[Created],[Updated])
                VALUES (@Name,@DocTypeId,@FrxFilePath,@FrxContent,@Description,@IsDefault,@IsActive,@SqlViewName,@KeyColumn,@OutputOptionsJson,@OrderColumn,@OrderDirection,GETDATE(),GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        }
        command.Parameters.Add(new SqlParameter("@Name", entity.Name));
        command.Parameters.Add(new SqlParameter("@DocTypeId", entity.DocumentTypeId));
        command.Parameters.Add(new SqlParameter("@FrxFilePath", (object?)entity.FrxFilePath ?? DBNull.Value));
        var frxContentParam = new SqlParameter("@FrxContent", System.Data.SqlDbType.VarBinary, -1)
        {
            Value = (object?)entity.FrxContent ?? DBNull.Value
        };
        command.Parameters.Add(frxContentParam);
        command.Parameters.Add(new SqlParameter("@Description", (object?)entity.Description ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsDefault", entity.IsDefault));
        command.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        command.Parameters.Add(new SqlParameter("@SqlViewName",
            string.IsNullOrWhiteSpace(entity.SqlViewName) ? (object)DBNull.Value : entity.SqlViewName.Trim()));
        command.Parameters.Add(new SqlParameter("@KeyColumn",
            string.IsNullOrWhiteSpace(entity.KeyColumn) ? (object)DBNull.Value : entity.KeyColumn.Trim()));
        command.Parameters.Add(new SqlParameter("@OutputOptionsJson",
            string.IsNullOrWhiteSpace(entity.OutputOptionsJson) ? (object)DBNull.Value : entity.OutputOptionsJson));
        command.Parameters.Add(new SqlParameter("@OrderColumn",
            string.IsNullOrWhiteSpace(entity.OrderColumn) ? (object)DBNull.Value : entity.OrderColumn.Trim()));
        command.Parameters.Add(new SqlParameter("@OrderDirection",
            string.IsNullOrWhiteSpace(entity.OrderDirection) ? (object)DBNull.Value : entity.OrderDirection.Trim().ToUpperInvariant()));
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

    private static ReportTemplate Map(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        Name           = r.GetString(1),
        DocumentTypeId = r.GetInt32(2),
        FrxFilePath    = r.IsDBNull(3) ? null : r.GetString(3),
        Description    = r.IsDBNull(4) ? null : r.GetString(4),
        IsDefault      = r.GetBoolean(5),
        IsActive       = r.GetBoolean(6),
        CreatedAt      = r.GetDateTime(7),
        UpdatedAt      = r.GetDateTime(8),
        FrxContent        = r.IsDBNull(9)  ? null : (byte[])r.GetValue(9),
        SqlViewName       = r.IsDBNull(10) ? null : r.GetString(10),
        KeyColumn         = r.IsDBNull(11) ? null : r.GetString(11),
        OutputOptionsJson = r.IsDBNull(12) ? null : r.GetString(12),
        OrderColumn       = r.IsDBNull(13) ? null : r.GetString(13),
        OrderDirection    = r.IsDBNull(14) ? null : r.GetString(14),
    };
}

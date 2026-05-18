using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlSalesRepresentativeRepository : ISalesRepresentativeRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlSalesRepresentativeRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[sales_representatives]";
    }

    public async Task<IReadOnlyCollection<SalesRepresentative>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<SalesRepresentative>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [id],[rep_name],[is_active],[Created],[Updated] FROM {_table} ORDER BY [rep_name];";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));
        return list;
    }

    public async Task<SalesRepresentative?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [id],[rep_name],[is_active],[Created],[Updated] FROM {_table} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<int> AddAsync(SalesRepresentative entity, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_table} ([rep_name],[is_active],[Created],[Updated])
            VALUES (@Name, @IsActive, GETDATE(), GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        command.Parameters.Add(new SqlParameter("@Name", entity.RepName));
        command.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(SalesRepresentative entity, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_table} SET [rep_name]=@Name, [is_active]=@IsActive, [Updated]=GETDATE()
            WHERE [id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", entity.Id));
        command.Parameters.Add(new SqlParameter("@Name", entity.RepName));
        command.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_table} WHERE [id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SalesRepresentative Map(SqlDataReader r) => new()
    {
        Id        = r.GetInt32(0),
        RepName   = r.GetString(1),
        IsActive  = r.GetBoolean(2),
        CreatedAt = r.GetDateTime(3),
        UpdatedAt = r.GetDateTime(4),
    };
}

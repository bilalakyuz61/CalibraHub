using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlCurrencyRepository : ICurrencyRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlCurrencyRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[currencies]";
    }

    public async Task<IReadOnlyCollection<Currency>> GetAllAsync(CancellationToken ct)
    {
        var list = new List<Currency>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [id],[code],[name],[symbol],[is_active],[Created],[Updated] FROM {_table} ORDER BY [code];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<Currency?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [id],[code],[name],[symbol],[is_active],[Created],[Updated] FROM {_table} WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<int> AddAsync(Currency entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table} ([code],[name],[symbol],[is_active],[Created],[Updated])
            VALUES (@Code, @Name, @Symbol, @IsActive, GETDATE(), GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", entity.Code));
        cmd.Parameters.Add(new SqlParameter("@Name", entity.Name));
        cmd.Parameters.Add(new SqlParameter("@Symbol", (object?)entity.Symbol ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task UpdateAsync(Currency entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [code]=@Code,[name]=@Name,[symbol]=@Symbol,[is_active]=@IsActive,[Updated]=GETDATE() WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", entity.Id));
        cmd.Parameters.Add(new SqlParameter("@Code", entity.Code));
        cmd.Parameters.Add(new SqlParameter("@Name", entity.Name));
        cmd.Parameters.Add(new SqlParameter("@Symbol", (object?)entity.Symbol ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive", entity.IsActive));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Currency Map(SqlDataReader r) => new()
    {
        Id        = r.GetInt32(0),
        Code      = r.GetString(1),
        Name      = r.GetString(2),
        Symbol    = r.IsDBNull(3) ? null : r.GetString(3),
        IsActive  = r.GetBoolean(4),
        CreatedAt = r.GetDateTime(5),
        UpdatedAt = r.GetDateTime(6),
    };
}

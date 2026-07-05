using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlIntegrationApiProfileRepository : IIntegrationApiProfileRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlIntegrationApiProfileRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[IntegrationApiProfile]";
    }

    public async Task<IReadOnlyCollection<IntegrationApiProfile>> GetByCompanyAsync(int companyId, CancellationToken ct)
    {
        var list = new List<IntegrationApiProfile>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[Name],[AuthType],[BaseUrl],[AuthConfigJson],[IsActive],[ProviderCode],[Created],[Updated]
            FROM {_table} WHERE [CompanyId] = @CompanyId ORDER BY [Name];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<IntegrationApiProfile?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[Name],[AuthType],[BaseUrl],[AuthConfigJson],[IsActive],[ProviderCode],[Created],[Updated]
            FROM {_table} WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task UpsertAsync(IntegrationApiProfile p, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [Id] = @Id)
                UPDATE {_table} SET [Name]=@Name, [AuthType]=@AuthType, [BaseUrl]=@BaseUrl,
                    [AuthConfigJson]=@AuthConfigJson, [IsActive]=@IsActive, [ProviderCode]=@ProviderCode, [Updated]=@UpdatedAt
                WHERE [Id]=@Id
            ELSE
                INSERT INTO {_table} ([Id],[CompanyId],[Name],[AuthType],[BaseUrl],[AuthConfigJson],[IsActive],[ProviderCode],[Created],[Updated])
                VALUES (@Id,@CompanyId,@Name,@AuthType,@BaseUrl,@AuthConfigJson,@IsActive,@ProviderCode,@CreatedAt,@UpdatedAt);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", p.Id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", p.CompanyId));
        cmd.Parameters.Add(new SqlParameter("@Name", p.Name));
        cmd.Parameters.Add(new SqlParameter("@AuthType", p.AuthType));
        cmd.Parameters.Add(new SqlParameter("@BaseUrl", p.BaseUrl));
        cmd.Parameters.Add(new SqlParameter("@AuthConfigJson", (object?)p.AuthConfigJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive", p.IsActive));
        cmd.Parameters.Add(new SqlParameter("@ProviderCode", (object?)p.ProviderCode ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", p.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", p.UpdatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static IntegrationApiProfile Map(SqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("Id")),
        CompanyId = r.GetInt32(r.GetOrdinal("CompanyId")),
        Name = r.GetString(r.GetOrdinal("Name")),
        AuthType = r.GetString(r.GetOrdinal("AuthType")),
        BaseUrl = r.GetString(r.GetOrdinal("BaseUrl")),
        AuthConfigJson = r.IsDBNull(r.GetOrdinal("AuthConfigJson")) ? null : r.GetString(r.GetOrdinal("AuthConfigJson")),
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
        ProviderCode = r.IsDBNull(r.GetOrdinal("ProviderCode")) ? null : r.GetString(r.GetOrdinal("ProviderCode")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("Created")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("Updated")),
    };
}

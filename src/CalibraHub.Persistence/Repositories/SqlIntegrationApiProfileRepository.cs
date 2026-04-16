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
        _table = $"[{schema}].[integration_api_profiles]";
    }

    public async Task<IReadOnlyCollection<IntegrationApiProfile>> GetByCompanyAsync(int companyId, CancellationToken ct)
    {
        var list = new List<IntegrationApiProfile>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[company_id],[name],[auth_type],[base_url],[auth_config_json],[is_active],[created_at],[updated_at]
            FROM {_table} WHERE [company_id] = @CompanyId ORDER BY [name];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        Console.WriteLine($"[GetByCompanyAsync] table={_table}, companyId={companyId}");

        // Tum satirlari da log'la (debug)
        await using (var allCmd = conn.CreateCommand())
        {
            allCmd.CommandText = $"SELECT [id],[company_id],[name] FROM {_table};";
            await using var allR = await allCmd.ExecuteReaderAsync(ct);
            while (await allR.ReadAsync(ct))
                Console.WriteLine($"[GetByCompanyAsync]   row: id={allR.GetGuid(0)}, company_id={allR[1]}, name={allR.GetString(2)}");
        }

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        Console.WriteLine($"[GetByCompanyAsync] result count={list.Count}");
        return list;
    }

    public async Task<IntegrationApiProfile?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[company_id],[name],[auth_type],[base_url],[auth_config_json],[is_active],[created_at],[updated_at]
            FROM {_table} WHERE [id] = @Id;
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
            IF EXISTS (SELECT 1 FROM {_table} WHERE [id] = @Id)
                UPDATE {_table} SET [name]=@Name, [auth_type]=@AuthType, [base_url]=@BaseUrl,
                    [auth_config_json]=@AuthConfigJson, [is_active]=@IsActive, [updated_at]=@UpdatedAt
                WHERE [id]=@Id
            ELSE
                INSERT INTO {_table} ([id],[company_id],[name],[auth_type],[base_url],[auth_config_json],[is_active],[created_at],[updated_at])
                VALUES (@Id,@CompanyId,@Name,@AuthType,@BaseUrl,@AuthConfigJson,@IsActive,@CreatedAt,@UpdatedAt);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", p.Id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", p.CompanyId));
        cmd.Parameters.Add(new SqlParameter("@Name", p.Name));
        cmd.Parameters.Add(new SqlParameter("@AuthType", p.AuthType));
        cmd.Parameters.Add(new SqlParameter("@BaseUrl", p.BaseUrl));
        cmd.Parameters.Add(new SqlParameter("@AuthConfigJson", (object?)p.AuthConfigJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsActive", p.IsActive));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt", p.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", p.UpdatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static IntegrationApiProfile Map(SqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        CompanyId = r.GetInt32(r.GetOrdinal("company_id")),
        Name = r.GetString(r.GetOrdinal("name")),
        AuthType = r.GetString(r.GetOrdinal("auth_type")),
        BaseUrl = r.GetString(r.GetOrdinal("base_url")),
        AuthConfigJson = r.IsDBNull(r.GetOrdinal("auth_config_json")) ? null : r.GetString(r.GetOrdinal("auth_config_json")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}

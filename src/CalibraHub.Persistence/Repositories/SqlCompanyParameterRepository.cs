using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// CompanyParameter tablosu persistence. Tum sorgular mevcut request'in
/// CompanyId'siyle filtrelenir (per-company izolasyon).
/// </summary>
public sealed class SqlCompanyParameterRepository : ICompanyParameterRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlCompanyParameterRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema.Replace("]", "]]")}].[CompanyParameter]";
    }

    public async Task<IReadOnlyCollection<CompanyParameter>> GetAllAsync(CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[CompanyId],[FormCode],[ParamKey],[ParamValue],[DataType],[Updated],[UpdatedById]
            FROM {_table}
            WHERE [CompanyId] = @CompanyId
            ORDER BY [FormCode], [ParamKey];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<IReadOnlyCollection<CompanyParameter>> GetByFormAsync(string formCode, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[CompanyId],[FormCode],[ParamKey],[ParamValue],[DataType],[Updated],[UpdatedById]
            FROM {_table}
            WHERE [CompanyId] = @CompanyId AND [FormCode] = @FormCode
            ORDER BY [ParamKey];";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@FormCode", formCode);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<CompanyParameter?> GetAsync(string formCode, string paramKey, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 1 [Id],[CompanyId],[FormCode],[ParamKey],[ParamValue],[DataType],[Updated],[UpdatedById]
            FROM {_table}
            WHERE [CompanyId] = @CompanyId AND [FormCode] = @FormCode AND [ParamKey] = @ParamKey;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@FormCode", formCode);
        cmd.Parameters.AddWithValue("@ParamKey", paramKey);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<int> UpsertAsync(string formCode, string paramKey, string? paramValue, CompanyParameterDataType dataType, int? updatedBy, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            MERGE {_table} WITH (HOLDLOCK) AS target
            USING (SELECT @CompanyId AS CompanyId, @FormCode AS FormCode, @ParamKey AS ParamKey) AS source
            ON target.[CompanyId] = source.CompanyId
               AND target.[FormCode] = source.FormCode
               AND target.[ParamKey] = source.ParamKey
            WHEN MATCHED THEN UPDATE SET
                [ParamValue] = @ParamValue,
                [DataType]   = @DataType,
                [Updated]    = SYSUTCDATETIME(),
                [UpdatedById] = @UpdatedBy
            WHEN NOT MATCHED THEN INSERT
                ([CompanyId],[FormCode],[ParamKey],[ParamValue],[DataType],[Updated],[UpdatedById])
                VALUES (@CompanyId,@FormCode,@ParamKey,@ParamValue,@DataType,SYSUTCDATETIME(),@UpdatedBy);

            SELECT [Id] FROM {_table}
            WHERE [CompanyId]=@CompanyId AND [FormCode]=@FormCode AND [ParamKey]=@ParamKey;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@FormCode", formCode);
        cmd.Parameters.AddWithValue("@ParamKey", paramKey);
        cmd.Parameters.AddWithValue("@ParamValue", (object?)paramValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DataType", (byte)dataType);
        cmd.Parameters.AddWithValue("@UpdatedBy", (object?)updatedBy ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(string formCode, string paramKey, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            DELETE FROM {_table}
            WHERE [CompanyId] = @CompanyId AND [FormCode] = @FormCode AND [ParamKey] = @ParamKey;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@FormCode", formCode);
        cmd.Parameters.AddWithValue("@ParamKey", paramKey);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyCollection<CompanyParameter>> ReadListAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<CompanyParameter>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(Map(reader));
        }
        return list;
    }

    private static CompanyParameter Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CompanyId = r.GetInt32(1),
        FormCode = r.GetString(2),
        ParamKey = r.GetString(3),
        ParamValue = r.IsDBNull(4) ? null : r.GetString(4),
        DataType = (CompanyParameterDataType)r.GetByte(5),
        UpdatedAt = r.IsDBNull(6) ? null : r.GetDateTime(6),
        UpdatedBy = r.IsDBNull(7) ? null : r.GetInt32(7),
    };
}

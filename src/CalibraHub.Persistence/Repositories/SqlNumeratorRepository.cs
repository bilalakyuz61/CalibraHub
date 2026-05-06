using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Numerator tablosu — atomik sayac. MERGE WITH (HOLDLOCK) ile concurrency-safe.
/// resetPolicy = "Yearly" → yil baslangicinda 1'e doner; "Never" → sifirlanmaz.
/// </summary>
public sealed class SqlNumeratorRepository : INumeratorRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlNumeratorRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema.Replace("]", "]]")}].[Numerator]";
    }

    public async Task<int> GetNextValueAsync(string entityType, string resetPolicy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("EntityType bos olamaz.", nameof(entityType));

        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var policy = string.IsNullOrWhiteSpace(resetPolicy) ? "Yearly" : resetPolicy.Trim();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            DECLARE @Out TABLE (NewValue INT);
            MERGE {_table} WITH (HOLDLOCK) AS target
            USING (SELECT @CompanyId AS CompanyId, @EntityType AS EntityType) AS source
            ON target.[CompanyId] = source.CompanyId AND target.[EntityType] = source.EntityType
            WHEN MATCHED THEN UPDATE SET
                [CurrentValue] = CASE
                    WHEN @ResetPolicy = N'Yearly'
                         AND YEAR(ISNULL(target.[LastResetAt], '1900-01-01')) < YEAR(SYSUTCDATETIME())
                    THEN 1
                    ELSE target.[CurrentValue] + 1
                END,
                [LastResetAt] = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT ([CompanyId],[EntityType],[CurrentValue],[LastResetAt])
                                  VALUES (@CompanyId, @EntityType, 1, SYSUTCDATETIME())
            OUTPUT INSERTED.[CurrentValue] INTO @Out (NewValue);

            SELECT TOP 1 NewValue FROM @Out;";
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@EntityType", entityType.Trim());
        cmd.Parameters.AddWithValue("@ResetPolicy", policy);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 1;
    }
}

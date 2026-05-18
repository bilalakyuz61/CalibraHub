using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// IItemCombinationResolver implementasyonu — ItemConfiguration tablosundan
/// RecordCode lookup. Per-company DB.
///
/// SQL: WHERE Id IN (@id0, @id1, ...) — her id parametre olarak verilir
/// (IN clause table-valued parameter veya STRING_SPLIT yerine basit @-paramlar
/// 100 kalem altinda yeterli; daha fazla icin TVP'ye gecilebilir).
/// </summary>
public sealed class SqlItemCombinationResolver : IItemCombinationResolver
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlItemCombinationResolver(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<string?> GetCombinationCodeAsync(int? combinationId, CancellationToken ct)
    {
        if (!combinationId.HasValue || combinationId.Value <= 0) return null;
        var map = await GetCombinationCodesAsync(new[] { combinationId.Value }, ct);
        return map.TryGetValue(combinationId.Value, out var code) ? code : null;
    }

    public async Task<IReadOnlyDictionary<int, string>> GetCombinationCodesAsync(
        IEnumerable<int> combinationIds, CancellationToken ct)
    {
        // Dedup + filter null/0
        var ids = combinationIds?.Where(i => i > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0) return new Dictionary<int, string>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Tablo var mi kontrol et — yoksa bos don
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT CASE WHEN OBJECT_ID(N'[{_schema}].[ItemConfiguration]', N'U') IS NOT NULL THEN 1 ELSE 0 END;";
            var exists = ((int)(await chk.ExecuteScalarAsync(ct) ?? 0)) == 1;
            if (!exists) return new Dictionary<int, string>();
        }

        // IN @id0, @id1, ... param'larla — SQL injection'a karsi guvenli
        var paramNames = ids.Select((_, i) => "@id" + i).ToArray();
        cmd.CommandText = $"""
            SELECT [Id], [RecordCode]
            FROM [{_schema}].[ItemConfiguration]
            WHERE [Id] IN ({string.Join(",", paramNames)})
              AND [IsActive] = 1;
            """;
        for (var i = 0; i < ids.Length; i++)
        {
            cmd.Parameters.Add(new SqlParameter(paramNames[i], ids[i]));
        }

        var result = new Dictionary<int, string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);
            var code = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!string.IsNullOrEmpty(code))
                result[id] = code;
        }
        return result;
    }
}

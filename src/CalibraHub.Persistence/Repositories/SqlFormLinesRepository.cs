using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// IFormLinesRepository implementasyonu — v_Flat_{linesFormCode} view'indan
/// parent FK ile filtreli kalem kayitlari ceker.
///
/// Per-company DB (SqlServerConnectionFactory). Identifier validation:
/// linesFormCode ve parentColumn dinamik SQL'e gomulur, bu yuzden katı regex
/// validasyon (^[A-Za-z_][A-Za-z0-9_]{0,63}$) yapilir.
/// </summary>
public sealed class SqlFormLinesRepository : IFormLinesRepository
{
    private static readonly Regex IdentifierRegex =
        new("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlFormLinesRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetLinesAsync(
        string linesFormCode, string parentColumn, string parentRecordId, CancellationToken ct)
    {
        // Identifier validation — dinamik SQL'e gomulecek
        if (!IdentifierRegex.IsMatch(linesFormCode)) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        if (!IdentifierRegex.IsMatch(parentColumn))  return Array.Empty<IReadOnlyDictionary<string, object?>>();
        if (string.IsNullOrWhiteSpace(parentRecordId)) return Array.Empty<IReadOnlyDictionary<string, object?>>();

        var viewName = "v_Flat_" + linesFormCode;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // View var mi kontrol — yoksa bos liste
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT CASE WHEN OBJECT_ID(N'[{_schema}].[{viewName}]', N'V') IS NOT NULL THEN 1 ELSE 0 END;";
            var exists = ((int)(await chk.ExecuteScalarAsync(ct) ?? 0)) == 1;
            if (!exists) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        // Parent column gercekten view'da var mi kontrol et — yoksa filtreyi atla
        bool parentColExists;
        await using (var colChk = conn.CreateCommand())
        {
            colChk.CommandText = $"""
                SELECT CASE WHEN COL_LENGTH(N'[{_schema}].[{viewName}]', N'{parentColumn}') IS NOT NULL THEN 1 ELSE 0 END;
                """;
            parentColExists = ((int)(await colChk.ExecuteScalarAsync(ct) ?? 0)) == 1;
        }

        await using var cmd = conn.CreateCommand();
        if (parentColExists)
        {
            cmd.CommandText = $"""
                SELECT * FROM [{_schema}].[{viewName}]
                WHERE CAST([{parentColumn}] AS NVARCHAR(100)) = @ParentId;
                """;
            cmd.Parameters.Add(new SqlParameter("@ParentId", parentRecordId));
        }
        else
        {
            // Parent FK kolonu view'da yok — filtre uygulanamaz, bos don
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        var list = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                dict[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            list.Add(dict);
        }
        return list;
    }
}

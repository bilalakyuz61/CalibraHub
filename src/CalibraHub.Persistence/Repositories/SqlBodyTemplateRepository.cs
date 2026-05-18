using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// BodyTemplate repository — endpoint body schema icin hazır JSON sablonlari.
/// Per-company DB uzerinde calisir. ADO.NET pattern (codebase tutarliligi).
///
/// Seed: <see cref="CalibraDatabaseInitializer"/> 5 baslangic sablonu olusturur
/// (Musteri Siparisi, Satis Faturasi, Alis Faturasi, Satis Irsaliyesi, Yeni Cari).
/// </summary>
public sealed class SqlBodyTemplateRepository : IBodyTemplateRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlBodyTemplateRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[BodyTemplate]";
    }

    public async Task<IReadOnlyCollection<BodyTemplate>> ListAsync(
        string? category, string? provider, string? search, CancellationToken ct)
    {
        var list = new List<BodyTemplate>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = "[IsActive] = 1";
        if (!string.IsNullOrWhiteSpace(category))
        {
            where += " AND [Category] = @cat";
            cmd.Parameters.Add(new SqlParameter("@cat", category.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(provider))
        {
            where += " AND [ProviderHint] = @prov";
            cmd.Parameters.Add(new SqlParameter("@prov", provider.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND ([Name] LIKE @s OR [Description] LIKE @s OR [Tags] LIKE @s OR [DocType] LIKE @s)";
            cmd.Parameters.Add(new SqlParameter("@s", "%" + search.Trim() + "%"));
        }

        cmd.CommandText = $"""
            SELECT [Id],[Category],[Name],[DocType],[ProviderHint],[UrlPattern],[HttpMethod],
                   [BodyJson],[Description],[Tags],[UsageCount],[IsBuiltIn],[IsActive],
                   [CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM {_table}
            WHERE {where}
            ORDER BY [UsageCount] DESC, [Category] ASC, [Name] ASC;
            """;

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(Map(rdr));
        }
        return list;
    }

    public async Task<BodyTemplate?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Category],[Name],[DocType],[ProviderHint],[UrlPattern],[HttpMethod],
                   [BodyJson],[Description],[Tags],[UsageCount],[IsBuiltIn],[IsActive],
                   [CreatedBy],[Created],[UpdatedBy],[Updated]
            FROM {_table}
            WHERE [Id] = @id;
            """;
        cmd.Parameters.Add(new SqlParameter("@id", id));
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct) ? Map(rdr) : null;
    }

    public async Task IncrementUsageAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [UsageCount] = [UsageCount] + 1 WHERE [Id] = @id;";
        cmd.Parameters.Add(new SqlParameter("@id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> AddAsync(BodyTemplate template, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
                ([Category],[Name],[DocType],[ProviderHint],[UrlPattern],[HttpMethod],
                 [BodyJson],[Description],[Tags],[UsageCount],[IsBuiltIn],[IsActive],
                 [CreatedBy])
            OUTPUT INSERTED.[Id]
            VALUES (@cat,@name,@doctype,@provider,@urlpattern,@httpmethod,
                    @body,@desc,@tags,0,0,1,@createdBy);
            """;
        cmd.Parameters.Add(new SqlParameter("@cat", template.Category));
        cmd.Parameters.Add(new SqlParameter("@name", template.Name));
        cmd.Parameters.Add(new SqlParameter("@doctype", (object?)template.DocType ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@provider", (object?)template.ProviderHint ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@urlpattern", (object?)template.UrlPattern ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@httpmethod", (object?)template.HttpMethod ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@body", template.BodyJson));
        cmd.Parameters.Add(new SqlParameter("@desc", (object?)template.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@tags", (object?)template.Tags ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@createdBy", (object?)template.CreatedBy ?? DBNull.Value));
        var idObj = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(idObj);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var t = await GetByIdAsync(id, ct);
        if (t is null) return;
        if (t.IsBuiltIn)
            throw new InvalidOperationException("Sistem (built-in) sablonlari silinemez. Sadece kendi yarattiginiz sablonlari silebilirsiniz.");

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @id AND [IsBuiltIn] = 0;";
        cmd.Parameters.Add(new SqlParameter("@id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static BodyTemplate Map(SqlDataReader r) => new()
    {
        Id           = r.GetInt32(0),
        Category     = r.GetString(1),
        Name         = r.GetString(2),
        DocType      = r.IsDBNull(3) ? null : r.GetString(3),
        ProviderHint = r.IsDBNull(4) ? null : r.GetString(4),
        UrlPattern   = r.IsDBNull(5) ? null : r.GetString(5),
        HttpMethod   = r.IsDBNull(6) ? null : r.GetString(6),
        BodyJson     = r.GetString(7),
        Description  = r.IsDBNull(8)  ? null : r.GetString(8),
        Tags         = r.IsDBNull(9)  ? null : r.GetString(9),
        UsageCount   = r.GetInt32(10),
        IsBuiltIn    = r.GetBoolean(11),
        IsActive     = r.GetBoolean(12),
        CreatedBy    = r.IsDBNull(13) ? null : r.GetString(13),
        Created      = r.GetDateTime(14),
        UpdatedBy    = r.IsDBNull(15) ? null : r.GetString(15),
        Updated      = r.IsDBNull(16) ? null : r.GetDateTime(16),
    };
}

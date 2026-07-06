using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — LocationSection (Bölüm) / LocationSubSection (Alt Bölüm) CRUD.
/// Per-company DB. Kod Name'den türetilir; ad benzersizliği hiyerarşik;
/// alt bölümü olan bölüm silinemez.
/// </summary>
public sealed class SqlLocationSectionRepository : ILocationSectionRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _section;
    private readonly string _sub;

    public SqlLocationSectionRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _section = $"[{s}].[LocationSection]";
        _sub     = $"[{s}].[LocationSubSection]";
    }

    private static string DeriveCode(string name, int max = 50)
    {
        var code = (name ?? string.Empty).Trim();
        return code.Length <= max ? code : code[..max];
    }

    private static string CleanName(string name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new InvalidOperationException("Ad boş olamaz.");
        return n;
    }

    // ── Bölüm ─────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<LocationSectionDto>> ListSectionsAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.[Id], s.[Code], s.[Name], s.[IsActive],
                   (SELECT COUNT(*) FROM {_sub} x WHERE x.[SectionId] = s.[Id]) AS SubCount
            FROM {_section} s
            WHERE s.[IsActive] = 1
            ORDER BY s.[Name];
            """;
        var list = new List<LocationSectionDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new LocationSectionDto(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4)));
        return list;
    }

    public async Task<LocationSectionDto?> GetSectionAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.[Id], s.[Code], s.[Name], s.[IsActive],
                   (SELECT COUNT(*) FROM {_sub} x WHERE x.[SectionId] = s.[Id]) AS SubCount
            FROM {_section} s WHERE s.[Id]=@Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new LocationSectionDto(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4))
            : null;
    }

    public async Task<int> SaveSectionAsync(int? id, string name, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        await using var conn = await _factory.OpenConnectionAsync(ct);

        await using (var dup = conn.CreateCommand())
        {
            dup.CommandText = $"SELECT COUNT(*) FROM {_section} WHERE [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
            dup.Parameters.AddWithValue("@N", n);
            dup.Parameters.AddWithValue("@Id", id ?? 0);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException($"Aynı isimde başka bir bölüm zaten tanımlı: '{n}'");
        }

        await using var cmd = conn.CreateCommand();
        if (id is > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_section} SET [Name]=@N, [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_section} ([Code],[Name],[CreatedById]) VALUES (@C,@N,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n));
        }
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@U", (object?)userId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteSectionAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT COUNT(*) FROM {_sub} WHERE [SectionId]=@Id;";
            chk.Parameters.AddWithValue("@Id", id);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException("Bu bölüme bağlı alt bölümler var — önce alt bölümleri silin.");
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_section} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Alt Bölüm ─────────────────────────────────────────────────────
    public async Task<IReadOnlyList<LocationSubSectionDto>> ListSubSectionsAsync(int sectionId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [SectionId], [Code], [Name], [IsActive]
            FROM {_sub}
            WHERE [SectionId]=@S AND [IsActive]=1
            ORDER BY [Name];
            """;
        cmd.Parameters.AddWithValue("@S", sectionId);
        var list = new List<LocationSubSectionDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new LocationSubSectionDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        return list;
    }

    public async Task<IReadOnlyList<LocationSubSectionListDto>> ListAllSubSectionsAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT x.[Id], x.[SectionId], s.[Name] AS SectionName, x.[Name]
            FROM {_sub} x
            INNER JOIN {_section} s ON s.[Id] = x.[SectionId]
            WHERE x.[IsActive] = 1
            ORDER BY s.[Name], x.[Name];
            """;
        var list = new List<LocationSubSectionListDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new LocationSubSectionListDto(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    public async Task<LocationSubSectionDto?> GetSubSectionAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[SectionId],[Code],[Name],[IsActive] FROM {_sub} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new LocationSubSectionDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4))
            : null;
    }

    public async Task<int> SaveSubSectionAsync(int? id, int sectionId, string name, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        if (sectionId <= 0) throw new InvalidOperationException("Bölüm seçilmelidir.");
        await using var conn = await _factory.OpenConnectionAsync(ct);

        await using (var dup = conn.CreateCommand())
        {
            dup.CommandText = $"SELECT COUNT(*) FROM {_sub} WHERE [SectionId]=@P AND [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
            dup.Parameters.AddWithValue("@P", sectionId);
            dup.Parameters.AddWithValue("@N", n);
            dup.Parameters.AddWithValue("@Id", id ?? 0);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException($"Bu bölümde aynı isimde başka bir alt bölüm zaten tanımlı: '{n}'");
        }

        await using var cmd = conn.CreateCommand();
        if (id is > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_sub} SET [Name]=@N, [SectionId]=@P, [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_sub} ([SectionId],[Code],[Name],[CreatedById]) VALUES (@P,@C,@N,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n));
        }
        cmd.Parameters.AddWithValue("@P", sectionId);
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@U", (object?)userId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteSubSectionAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_sub} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — Country/City/District CRUD (per-company DB).
/// Kurallar (CLAUDE.md): kullanıcı kod girmez → Code = Name'den türetilir;
/// ad benzersizliği hiyerarşik; silme çocuk kayıt varsa engellenir.
/// </summary>
public sealed class SqlAddressDefinitionRepository : IAddressDefinitionRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _country;
    private readonly string _city;
    private readonly string _district;

    public SqlAddressDefinitionRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _country  = $"[{s}].[Country]";
        _city     = $"[{s}].[City]";
        _district = $"[{s}].[District]";
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

    // ── Ülke ─────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<CountryDto>> ListCountriesAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[Code], c.[Name], c.[IsActive],
                   (SELECT COUNT(*) FROM {_city} s WHERE s.[CountryId] = c.[Id]) AS CityCount
            FROM {_country} c
            WHERE c.[IsActive] = 1
            ORDER BY CASE WHEN c.[Name] = N'Türkiye' THEN 0 ELSE 1 END, c.[Name];
            """;
        var list = new List<CountryDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CountryDto(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4)));
        return list;
    }

    public async Task<int> SaveCountryAsync(int? id, string name, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        await using var conn = await _factory.OpenConnectionAsync(ct);

        await using (var dup = conn.CreateCommand())
        {
            dup.CommandText = $"SELECT COUNT(*) FROM {_country} WHERE [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
            dup.Parameters.AddWithValue("@N", n);
            dup.Parameters.AddWithValue("@Id", id ?? 0);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException($"Aynı isimde başka bir ülke zaten tanımlı: '{n}'");
        }

        await using var cmd = conn.CreateCommand();
        if (id is > 0)
        {
            // Update: mevcut Code korunur (eski referanslar bozulmasın)
            cmd.CommandText = $"""
                UPDATE {_country} SET [Name]=@N, [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_country} ([Code],[Name],[CreatedById]) VALUES (@C,@N,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n, 10));
        }
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@U", (object?)userId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteCountryAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT COUNT(*) FROM {_city} WHERE [CountryId]=@Id;";
            chk.Parameters.AddWithValue("@Id", id);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException("Bu ülkeye bağlı şehirler var — önce şehirleri silin.");
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_country} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Şehir ────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<CityDto>> ListCitiesAsync(int countryId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[CountryId], c.[Code], c.[Name], c.[IsActive],
                   (SELECT COUNT(*) FROM {_district} d WHERE d.[CityId] = c.[Id]) AS DistrictCount
            FROM {_city} c
            WHERE c.[CountryId]=@CountryId AND c.[IsActive] = 1
            ORDER BY c.[Name];
            """;
        cmd.Parameters.AddWithValue("@CountryId", countryId);
        var list = new List<CityDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CityDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetInt32(5)));
        return list;
    }

    public async Task<int> SaveCityAsync(int? id, int countryId, string name, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        if (countryId <= 0) throw new InvalidOperationException("Ülke seçilmelidir.");
        await using var conn = await _factory.OpenConnectionAsync(ct);

        await using (var dup = conn.CreateCommand())
        {
            dup.CommandText = $"SELECT COUNT(*) FROM {_city} WHERE [CountryId]=@P AND [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
            dup.Parameters.AddWithValue("@P", countryId);
            dup.Parameters.AddWithValue("@N", n);
            dup.Parameters.AddWithValue("@Id", id ?? 0);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException($"Bu ülkede aynı isimde başka bir şehir zaten tanımlı: '{n}'");
        }

        await using var cmd = conn.CreateCommand();
        if (id is > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_city} SET [Name]=@N, [CountryId]=@P, [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_city} ([CountryId],[Code],[Name],[CreatedById]) VALUES (@P,@C,@N,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n));
        }
        cmd.Parameters.AddWithValue("@P", countryId);
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@U", (object?)userId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteCityAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT COUNT(*) FROM {_district} WHERE [CityId]=@Id;";
            chk.Parameters.AddWithValue("@Id", id);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException("Bu şehre bağlı ilçeler var — önce ilçeleri silin.");
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_city} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── İlçe ─────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<DistrictDto>> ListDistrictsAsync(int cityId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [CityId], [Code], [Name], [IsActive]
            FROM {_district}
            WHERE [CityId]=@CityId AND [IsActive] = 1
            ORDER BY [Name];
            """;
        cmd.Parameters.AddWithValue("@CityId", cityId);
        var list = new List<DistrictDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new DistrictDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        return list;
    }

    public async Task<int> SaveDistrictAsync(int? id, int cityId, string name, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        if (cityId <= 0) throw new InvalidOperationException("Şehir seçilmelidir.");
        await using var conn = await _factory.OpenConnectionAsync(ct);

        await using (var dup = conn.CreateCommand())
        {
            dup.CommandText = $"SELECT COUNT(*) FROM {_district} WHERE [CityId]=@P AND [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
            dup.Parameters.AddWithValue("@P", cityId);
            dup.Parameters.AddWithValue("@N", n);
            dup.Parameters.AddWithValue("@Id", id ?? 0);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException($"Bu şehirde aynı isimde başka bir ilçe zaten tanımlı: '{n}'");
        }

        await using var cmd = conn.CreateCommand();
        if (id is > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_district} SET [Name]=@N, [CityId]=@P, [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_district} ([CityId],[Code],[Name],[CreatedById]) VALUES (@P,@C,@N,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n));
        }
        cmd.Parameters.AddWithValue("@P", cityId);
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@U", (object?)userId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteDistrictAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_district} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── SmartBoard listeleri + edit tekil fetch'leri ─────────────────────
    public async Task<CountryDto?> GetCountryAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[Code], c.[Name], c.[IsActive],
                   (SELECT COUNT(*) FROM {_city} s WHERE s.[CountryId] = c.[Id]) AS CityCount
            FROM {_country} c WHERE c.[Id]=@Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new CountryDto(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4))
            : null;
    }

    public async Task<CityDto?> GetCityAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[CountryId], c.[Code], c.[Name], c.[IsActive],
                   (SELECT COUNT(*) FROM {_district} d WHERE d.[CityId] = c.[Id]) AS DistrictCount
            FROM {_city} c WHERE c.[Id]=@Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new CityDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetInt32(5))
            : null;
    }

    public async Task<DistrictDto?> GetDistrictAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[CityId],[Code],[Name],[IsActive] FROM {_district} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new DistrictDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4))
            : null;
    }

    public async Task<IReadOnlyList<CityListDto>> ListAllCitiesAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[CountryId], co.[Name] AS CountryName, c.[Name],
                   (SELECT COUNT(*) FROM {_district} d WHERE d.[CityId] = c.[Id]) AS DistrictCount
            FROM {_city} c
            INNER JOIN {_country} co ON co.[Id] = c.[CountryId]
            WHERE c.[IsActive] = 1
            ORDER BY co.[Name], c.[Name];
            """;
        var list = new List<CityListDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CityListDto(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4)));
        return list;
    }

    public async Task<IReadOnlyList<DistrictListDto>> ListAllDistrictsAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT d.[Id], d.[CityId], c.[Name] AS CityName, co.[Name] AS CountryName, d.[Name]
            FROM {_district} d
            INNER JOIN {_city} c ON c.[Id] = d.[CityId]
            INNER JOIN {_country} co ON co.[Id] = c.[CountryId]
            WHERE d.[IsActive] = 1
            ORDER BY co.[Name], c.[Name], d.[Name];
            """;
        var list = new List<DistrictListDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new DistrictListDto(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }
}

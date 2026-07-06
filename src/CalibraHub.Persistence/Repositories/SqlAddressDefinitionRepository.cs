using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — Country/City/District/Neighborhood/Village CRUD (per-company DB).
/// Kurallar: ülke kodu kullanıcı girişli (bilinçli istisna); diğer kodlar Name'den
/// türetilir. Ad benzersizliği hiyerarşik; silme çocuk kayıt varsa engellenir.
/// Köy save: neighborhoodId doluysa DistrictId mahalleden türetilir (tutarlılık).
/// </summary>
public sealed class SqlAddressDefinitionRepository : IAddressDefinitionRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _country;
    private readonly string _city;
    private readonly string _district;
    private readonly string _neighborhood;
    private readonly string _village;
    private readonly string _currency;

    public SqlAddressDefinitionRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _country      = $"[{s}].[Country]";
        _city         = $"[{s}].[City]";
        _district     = $"[{s}].[District]";
        _neighborhood = $"[{s}].[Neighborhood]";
        _village      = $"[{s}].[Village]";
        _currency     = $"[{s}].[Currency]";
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

    // ══════════════════════════════════════════════════════════════════
    // Ülke
    // ══════════════════════════════════════════════════════════════════
    private const string CountrySelect = """
        SELECT c.[Id], c.[Code], c.[Name], c.[ForeignName], c.[CurrencyId],
               cur.[code] AS CurrencyCode, cur.[name] AS CurrencyName,
               c.[IsActive],
               (SELECT COUNT(*) FROM {CITY} s WHERE s.[CountryId] = c.[Id]) AS CityCount
        FROM {COUNTRY} c
        LEFT JOIN {CURRENCY} cur ON cur.[Id] = c.[CurrencyId]
        """;

    private string CountrySql(string where) =>
        CountrySelect.Replace("{CITY}", _city).Replace("{COUNTRY}", _country).Replace("{CURRENCY}", _currency) + "\n" + where;

    private static CountryDto MapCountry(SqlDataReader r) => new(
        r.GetInt32(0),
        r.IsDBNull(1) ? null : r.GetString(1),
        r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetInt32(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.GetBoolean(7),
        r.GetInt32(8));

    public async Task<IReadOnlyList<CountryDto>> ListCountriesAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = CountrySql("WHERE c.[IsActive] = 1 ORDER BY CASE WHEN c.[Name] = N'Türkiye' THEN 0 ELSE 1 END, c.[Name];");
        var list = new List<CountryDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapCountry(r));
        return list;
    }

    public async Task<CountryDto?> GetCountryAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = CountrySql("WHERE c.[Id]=@Id;");
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapCountry(r) : null;
    }

    public async Task<int> SaveCountryAsync(int? id, string name, string? code, int? currencyId, string? foreignName, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        var c = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
        var fn = string.IsNullOrWhiteSpace(foreignName) ? null : foreignName.Trim();
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
            cmd.CommandText = $"""
                UPDATE {_country} SET [Name]=@N, [Code]=@C, [ForeignName]=@F, [CurrencyId]=@Cur,
                       [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_country} ([Code],[Name],[ForeignName],[CurrencyId],[CreatedById])
                VALUES (@C,@N,@F,@Cur,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        }
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@C", (object?)c ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@F", (object?)fn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Cur", (object?)currencyId ?? DBNull.Value);
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

    // ══════════════════════════════════════════════════════════════════
    // Şehir
    // ══════════════════════════════════════════════════════════════════
    public async Task<IReadOnlyList<CityDto>> ListCitiesAsync(int countryId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[CountryId], c.[Code], c.[Name], c.[PlateCode], c.[IsActive],
                   (SELECT COUNT(*) FROM {_district} d WHERE d.[CityId] = c.[Id]) AS DistrictCount
            FROM {_city} c
            WHERE c.[CountryId]=@CountryId AND c.[IsActive] = 1
            ORDER BY c.[Name];
            """;
        cmd.Parameters.AddWithValue("@CountryId", countryId);
        var list = new List<CityDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CityDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetBoolean(5), r.GetInt32(6)));
        return list;
    }

    public async Task<IReadOnlyList<CityListDto>> ListAllCitiesAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[CountryId], co.[Name] AS CountryName, c.[Name], c.[PlateCode],
                   (SELECT COUNT(*) FROM {_district} d WHERE d.[CityId] = c.[Id]) AS DistrictCount
            FROM {_city} c
            INNER JOIN {_country} co ON co.[Id] = c.[CountryId]
            WHERE c.[IsActive] = 1
            ORDER BY co.[Name], c.[Name];
            """;
        var list = new List<CityListDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CityListDto(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5)));
        return list;
    }

    public async Task<CityDto?> GetCityAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.[Id], c.[CountryId], c.[Code], c.[Name], c.[PlateCode], c.[IsActive],
                   (SELECT COUNT(*) FROM {_district} d WHERE d.[CityId] = c.[Id]) AS DistrictCount
            FROM {_city} c WHERE c.[Id]=@Id;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new CityDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetBoolean(5), r.GetInt32(6))
            : null;
    }

    public async Task<int> SaveCityAsync(int? id, int countryId, string name, string? plateCode, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        if (countryId <= 0) throw new InvalidOperationException("Ülke seçilmelidir.");
        var plate = string.IsNullOrWhiteSpace(plateCode) ? null : plateCode.Trim();
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
                UPDATE {_city} SET [Name]=@N, [CountryId]=@P, [PlateCode]=@Plate,
                       [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_city} ([CountryId],[Code],[Name],[PlateCode],[CreatedById])
                VALUES (@P,@C,@N,@Plate,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n));
        }
        cmd.Parameters.AddWithValue("@P", countryId);
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@Plate", (object?)plate ?? DBNull.Value);
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

    // ══════════════════════════════════════════════════════════════════
    // İlçe
    // ══════════════════════════════════════════════════════════════════
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
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"""
                SELECT (SELECT COUNT(*) FROM {_neighborhood} WHERE [DistrictId]=@Id)
                     + (SELECT COUNT(*) FROM {_village} WHERE [DistrictId]=@Id);
                """;
            chk.Parameters.AddWithValue("@Id", id);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException("Bu ilçeye bağlı mahalle/köy kayıtları var — önce onları silin.");
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_district} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ══════════════════════════════════════════════════════════════════
    // Mahalle
    // ══════════════════════════════════════════════════════════════════
    public async Task<IReadOnlyList<NeighborhoodDto>> ListNeighborhoodsAsync(int districtId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [DistrictId], [Code], [Name], [IsActive]
            FROM {_neighborhood}
            WHERE [DistrictId]=@D AND [IsActive]=1
            ORDER BY [Name];
            """;
        cmd.Parameters.AddWithValue("@D", districtId);
        var list = new List<NeighborhoodDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new NeighborhoodDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        return list;
    }

    public async Task<IReadOnlyList<NeighborhoodListDto>> ListAllNeighborhoodsAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT n.[Id], n.[DistrictId], d.[Name] AS DistrictName, c.[Name] AS CityName, n.[Name],
                   (SELECT COUNT(*) FROM {_village} v WHERE v.[NeighborhoodId] = n.[Id]) AS VillageCount
            FROM {_neighborhood} n
            INNER JOIN {_district} d ON d.[Id] = n.[DistrictId]
            INNER JOIN {_city} c ON c.[Id] = d.[CityId]
            WHERE n.[IsActive] = 1
            ORDER BY c.[Name], d.[Name], n.[Name];
            """;
        var list = new List<NeighborhoodListDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new NeighborhoodListDto(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5)));
        return list;
    }

    public async Task<NeighborhoodDto?> GetNeighborhoodAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[DistrictId],[Code],[Name],[IsActive] FROM {_neighborhood} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new NeighborhoodDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4))
            : null;
    }

    public async Task<int> SaveNeighborhoodAsync(int? id, int districtId, string name, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        if (districtId <= 0) throw new InvalidOperationException("İlçe seçilmelidir.");
        await using var conn = await _factory.OpenConnectionAsync(ct);

        await using (var dup = conn.CreateCommand())
        {
            dup.CommandText = $"SELECT COUNT(*) FROM {_neighborhood} WHERE [DistrictId]=@P AND [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
            dup.Parameters.AddWithValue("@P", districtId);
            dup.Parameters.AddWithValue("@N", n);
            dup.Parameters.AddWithValue("@Id", id ?? 0);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException($"Bu ilçede aynı isimde başka bir mahalle zaten tanımlı: '{n}'");
        }

        await using var cmd = conn.CreateCommand();
        if (id is > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_neighborhood} SET [Name]=@N, [DistrictId]=@P, [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_neighborhood} ([DistrictId],[Code],[Name],[CreatedById]) VALUES (@P,@C,@N,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n));
        }
        cmd.Parameters.AddWithValue("@P", districtId);
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@U", (object?)userId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteNeighborhoodAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT COUNT(*) FROM {_village} WHERE [NeighborhoodId]=@Id;";
            chk.Parameters.AddWithValue("@Id", id);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException("Bu mahalleye bağlı köyler var — önce köyleri silin.");
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_neighborhood} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ══════════════════════════════════════════════════════════════════
    // Köy
    // ══════════════════════════════════════════════════════════════════
    public async Task<IReadOnlyList<VillageListDto>> ListAllVillagesAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT v.[Id], v.[DistrictId], v.[NeighborhoodId], n.[Name] AS NeighborhoodName,
                   d.[Name] AS DistrictName, c.[Name] AS CityName, v.[Name]
            FROM {_village} v
            INNER JOIN {_district} d ON d.[Id] = v.[DistrictId]
            INNER JOIN {_city} c ON c.[Id] = d.[CityId]
            LEFT JOIN {_neighborhood} n ON n.[Id] = v.[NeighborhoodId]
            WHERE v.[IsActive] = 1
            ORDER BY c.[Name], d.[Name], v.[Name];
            """;
        var list = new List<VillageListDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new VillageListDto(
                r.GetInt32(0), r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetString(6)));
        return list;
    }

    public async Task<VillageDto?> GetVillageAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Id],[DistrictId],[NeighborhoodId],[Code],[Name],[IsActive] FROM {_village} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new VillageDto(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetBoolean(5))
            : null;
    }

    public async Task<int> SaveVillageAsync(int? id, int districtId, int? neighborhoodId, string name, int? userId, CancellationToken ct)
    {
        var n = CleanName(name);
        await using var conn = await _factory.OpenConnectionAsync(ct);

        // Mahalle seçildiyse ilçe mahalleden türetilir (tutarlılık — kullanıcı
        // ilçe/mahalle çelişkisi gönderemez); mahalle yoksa districtId zorunlu.
        if (neighborhoodId is > 0)
        {
            await using var ngCmd = conn.CreateCommand();
            ngCmd.CommandText = $"SELECT [DistrictId] FROM {_neighborhood} WHERE [Id]=@Ng;";
            ngCmd.Parameters.AddWithValue("@Ng", neighborhoodId.Value);
            var res = await ngCmd.ExecuteScalarAsync(ct);
            if (res is null) throw new InvalidOperationException("Seçilen mahalle bulunamadı.");
            districtId = Convert.ToInt32(res);
        }
        else if (districtId <= 0)
        {
            throw new InvalidOperationException("İlçe veya mahalle seçilmelidir.");
        }

        await using (var dup = conn.CreateCommand())
        {
            if (neighborhoodId is > 0)
            {
                dup.CommandText = $"SELECT COUNT(*) FROM {_village} WHERE [NeighborhoodId]=@Ng AND [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
                dup.Parameters.AddWithValue("@Ng", neighborhoodId.Value);
            }
            else
            {
                dup.CommandText = $"SELECT COUNT(*) FROM {_village} WHERE [DistrictId]=@P AND [NeighborhoodId] IS NULL AND [Name]=@N AND [Id]!=@Id AND [IsActive]=1;";
                dup.Parameters.AddWithValue("@P", districtId);
            }
            dup.Parameters.AddWithValue("@N", n);
            dup.Parameters.AddWithValue("@Id", id ?? 0);
            if (Convert.ToInt32(await dup.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException($"Aynı seviyede aynı isimde başka bir köy zaten tanımlı: '{n}'");
        }

        await using var cmd = conn.CreateCommand();
        if (id is > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_village} SET [Name]=@N, [DistrictId]=@P, [NeighborhoodId]=@Ng,
                       [UpdatedById]=@U, [Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;
                SELECT @Id;
                """;
            cmd.Parameters.AddWithValue("@Id", id.Value);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_village} ([DistrictId],[NeighborhoodId],[Code],[Name],[CreatedById])
                VALUES (@P,@Ng,@C,@N,@U);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.AddWithValue("@C", DeriveCode(n));
        }
        cmd.Parameters.AddWithValue("@P", districtId);
        cmd.Parameters.AddWithValue("@Ng", (object?)neighborhoodId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@N", n);
        cmd.Parameters.AddWithValue("@U", (object?)userId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteVillageAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_village} WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ══════════════════════════════════════════════════════════════════
    // Para birimi lookup (Country edit)
    // ══════════════════════════════════════════════════════════════════
    public async Task<IReadOnlyList<CurrencyLookupDto>> ListCurrenciesLookupAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Currency legacy kolon adları küçük harf ([code],[name]) — rename migration kapsamı dışı.
        cmd.CommandText = $"SELECT [Id], [code], [name] FROM {_currency} WHERE [IsActive] = 1 ORDER BY [code];";
        var list = new List<CurrencyLookupDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CurrencyLookupDto(r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2)));
        return list;
    }
}

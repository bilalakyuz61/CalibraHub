using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlAddressRepository : IAddressRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _postalTable;   // PostalLocality
    private readonly string _addressTable;  // ContactAddress

    public SqlAddressRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _postalTable  = $"[{schema}].[PostalLocality]";
        _addressTable = $"[{schema}].[ContactAddress]";
    }

    // ════════════════════════════════════════════════════════════
    // PostalLocality — PTT verisi cascade dropdown'lari icin
    // ════════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<string>> GetCitiesAsync(string? countryCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT DISTINCT [CityName] FROM {_postalTable} WHERE [CountryCode] = @Cc ORDER BY [CityName];";
        cmd.Parameters.Add(new SqlParameter("@Cc", countryCode ?? "TR"));
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetString(0));
        return list;
    }

    public async Task<IReadOnlyCollection<string>> GetDistrictsAsync(string? countryCode, string cityName, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT DISTINCT [DistrictName] FROM {_postalTable} WHERE [CountryCode] = @Cc AND [CityName] = @City ORDER BY [DistrictName];";
        cmd.Parameters.Add(new SqlParameter("@Cc", countryCode ?? "TR"));
        cmd.Parameters.Add(new SqlParameter("@City", cityName));
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetString(0));
        return list;
    }

    public async Task<IReadOnlyCollection<PostalLocality>> GetNeighborhoodsAsync(string? countryCode, string cityName, string districtName, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CountryCode],[CityCode],[CityName],[DistrictName],[NeighborhoodName],[PostalCode]
            FROM {_postalTable}
            WHERE [CountryCode] = @Cc AND [CityName] = @City AND [DistrictName] = @Dist
            ORDER BY [NeighborhoodName];
            """;
        cmd.Parameters.Add(new SqlParameter("@Cc", countryCode ?? "TR"));
        cmd.Parameters.Add(new SqlParameter("@City", cityName));
        cmd.Parameters.Add(new SqlParameter("@Dist", districtName));
        var list = new List<PostalLocality>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapLocality(r));
        return list;
    }

    public async Task<PostalLocality?> FindByPostalCodeAsync(string postalCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[CountryCode],[CityCode],[CityName],[DistrictName],[NeighborhoodName],[PostalCode]
            FROM {_postalTable} WHERE [PostalCode] = @Pk;
            """;
        cmd.Parameters.Add(new SqlParameter("@Pk", postalCode));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapLocality(r) : null;
    }

    public async Task<int> GetPostalLocalityCountAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {_postalTable};";
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task BulkInsertPostalLocalitiesAsync(IReadOnlyCollection<PostalLocality> rows, bool clearExisting, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        if (clearExisting)
        {
            await using var clr = conn.CreateCommand();
            clr.CommandText = $"TRUNCATE TABLE {_postalTable};";
            await clr.ExecuteNonQueryAsync(ct);
        }

        // SqlBulkCopy kullan — 50k satir icin en hizli yol
        var dt = new System.Data.DataTable();
        dt.Columns.Add("CountryCode",      typeof(string));
        dt.Columns.Add("CityCode",         typeof(string));
        dt.Columns.Add("CityName",         typeof(string));
        dt.Columns.Add("DistrictName",     typeof(string));
        dt.Columns.Add("NeighborhoodName", typeof(string));
        dt.Columns.Add("PostalCode",       typeof(string));
        foreach (var row in rows)
        {
            dt.Rows.Add(
                row.CountryCode ?? "TR",
                (object?)row.CityCode ?? DBNull.Value,
                row.CityName,
                row.DistrictName,
                row.NeighborhoodName,
                (object?)row.PostalCode ?? DBNull.Value);
        }

        using var bulk = new SqlBulkCopy(conn) { DestinationTableName = _postalTable, BatchSize = 5000 };
        bulk.ColumnMappings.Add("CountryCode",      "CountryCode");
        bulk.ColumnMappings.Add("CityCode",         "CityCode");
        bulk.ColumnMappings.Add("CityName",         "CityName");
        bulk.ColumnMappings.Add("DistrictName",     "DistrictName");
        bulk.ColumnMappings.Add("NeighborhoodName", "NeighborhoodName");
        bulk.ColumnMappings.Add("PostalCode",       "PostalCode");
        await bulk.WriteToServerAsync(dt, ct);
    }

    private static PostalLocality MapLocality(SqlDataReader r) => new()
    {
        Id               = r.GetInt32(0),
        CountryCode      = r.IsDBNull(1) ? "TR" : r.GetString(1),
        CityCode         = r.IsDBNull(2) ? null : r.GetString(2),
        CityName         = r.GetString(3),
        DistrictName     = r.GetString(4),
        NeighborhoodName = r.GetString(5),
        PostalCode       = r.IsDBNull(6) ? null : r.GetString(6),
    };

    // ════════════════════════════════════════════════════════════
    // ContactAddress — per-cari teslim adresleri
    // ════════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<ContactAddress>> GetAddressesByContactAsync(int contactId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ContactId],[Name],[CountryCode],[CityName],[DistrictName],[NeighborhoodName],[PostalCode],[AddressLine],[IsDefault],[Created]
            FROM {_addressTable}
            WHERE [ContactId] = @ContactId
            ORDER BY [IsDefault] DESC, [Created];
            """;
        cmd.Parameters.Add(new SqlParameter("@ContactId", contactId));
        var list = new List<ContactAddress>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapAddress(r));
        return list;
    }

    public async Task<ContactAddress?> GetAddressByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ContactId],[Name],[CountryCode],[CityName],[DistrictName],[NeighborhoodName],[PostalCode],[AddressLine],[IsDefault],[Created]
            FROM {_addressTable} WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapAddress(r) : null;
    }

    public async Task<int> AddAddressAsync(ContactAddress a, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_addressTable}
                ([ContactId],[Name],[CountryCode],[CityName],[DistrictName],[NeighborhoodName],[PostalCode],[AddressLine],[IsDefault],[Created])
            VALUES
                (@ContactId,@Name,@Cc,@City,@Dist,@Ng,@Pk,@Addr,@Def,@At);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        AddAddressParams(cmd, a);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateAddressAsync(ContactAddress a, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_addressTable}
            SET [Name]             = @Name,
                [CountryCode]      = @Cc,
                [CityName]         = @City,
                [DistrictName]     = @Dist,
                [NeighborhoodName] = @Ng,
                [PostalCode]       = @Pk,
                [AddressLine]      = @Addr,
                [IsDefault]        = @Def
            WHERE [Id] = @Id;
            """;
        AddAddressParams(cmd, a);
        cmd.Parameters.Add(new SqlParameter("@Id", a.Id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAddressAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_addressTable} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetDefaultAddressAsync(int contactId, int addressId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_addressTable} SET [IsDefault] = 0 WHERE [ContactId] = @Cid;
            UPDATE {_addressTable} SET [IsDefault] = 1 WHERE [Id] = @Aid AND [ContactId] = @Cid;
            """;
        cmd.Parameters.Add(new SqlParameter("@Cid", contactId));
        cmd.Parameters.Add(new SqlParameter("@Aid", addressId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddAddressParams(SqlCommand cmd, ContactAddress a)
    {
        cmd.Parameters.Add(new SqlParameter("@ContactId", a.ContactId));
        cmd.Parameters.Add(new SqlParameter("@Name",  a.Name));
        cmd.Parameters.Add(new SqlParameter("@Cc",    (object?)a.CountryCode      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@City",  (object?)a.CityName         ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Dist",  (object?)a.DistrictName     ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Ng",    (object?)a.NeighborhoodName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Pk",    (object?)a.PostalCode       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Addr",  (object?)a.AddressLine      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Def",   a.IsDefault));
        cmd.Parameters.Add(new SqlParameter("@At",    a.CreatedAt == default ? DateTime.Now : a.CreatedAt));
    }

    private static ContactAddress MapAddress(SqlDataReader r) => new()
    {
        Id               = r.GetInt32(0),
        ContactId        = r.GetInt32(1),
        Name             = r.GetString(2),
        CountryCode      = r.IsDBNull(3) ? null : r.GetString(3),
        CityName         = r.IsDBNull(4) ? null : r.GetString(4),
        DistrictName     = r.IsDBNull(5) ? null : r.GetString(5),
        NeighborhoodName = r.IsDBNull(6) ? null : r.GetString(6),
        PostalCode       = r.IsDBNull(7) ? null : r.GetString(7),
        AddressLine      = r.IsDBNull(8) ? null : r.GetString(8),
        IsDefault        = r.GetBoolean(9),
        CreatedAt        = r.GetDateTime(10),
    };
}

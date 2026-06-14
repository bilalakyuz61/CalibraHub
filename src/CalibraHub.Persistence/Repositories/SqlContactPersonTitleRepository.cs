using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// ContactPersonTitle lookup repository — ADO.NET, schema-aware. Seed kayitlari
/// (IsSystem=1) burada filtrelenmez; sadece IsActive=1 doner.
/// </summary>
public sealed class SqlContactPersonTitleRepository : IContactPersonTitleRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlContactPersonTitleRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _table = $"[{s}].[ContactPersonTitle]";
    }

    public async Task<IReadOnlyList<ContactPersonTitle>> GetAllActiveAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[SortOrder],[IsSystem],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            WHERE [IsActive] = 1
            ORDER BY [SortOrder] ASC, [Name] ASC;
            """;
        var list = new List<ContactPersonTitle>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(Map(r));
        return list;
    }

    public async Task<ContactPersonTitle?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[SortOrder],[IsSystem],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<ContactPersonTitle?> GetByNameAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (1) [Id],[Name],[SortOrder],[IsSystem],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_table}
            WHERE LOWER(LTRIM(RTRIM([Name]))) = LOWER(LTRIM(RTRIM(@Name)))
              AND [IsActive] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@Name", name));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<int> AddAsync(ContactPersonTitle entity, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table} ([Name],[SortOrder],[IsSystem],[IsActive],[Created],[CreatedById])
            VALUES (@Name,@SortOrder,@IsSystem,@IsActive,SYSUTCDATETIME(),@CreatedById);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@Name",        entity.Name ?? string.Empty));
        cmd.Parameters.Add(new SqlParameter("@SortOrder",   entity.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsSystem",    entity.IsSystem));
        cmd.Parameters.Add(new SqlParameter("@IsActive",    entity.IsActive));
        cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value));
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task DeleteAsync(int id, int? updatedById, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET [IsActive] = 0,
                [Updated]    = SYSUTCDATETIME(),
                [UpdatedById] = @UpdatedById
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",          id));
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)updatedById ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> GetUsageCountAsync(int titleId, CancellationToken ct)
    {
        // ContactPerson tablosunda bu TitleId'yi kullanan AKTIF kac kayit var.
        // Schema'yi ayni connection factory'den okuyamiyoruz; tablo adi sabit "ContactPerson"
        // ayni schema'da. _table'dan schema prefix'i cikartalim.
        var schemaPart = _table.Split('.')[0]; // "[dbo]"
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {schemaPart}.[ContactPerson] WHERE [TitleId] = @Id AND [IsActive] = 1;";
        cmd.Parameters.Add(new SqlParameter("@Id", titleId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int i ? i : Convert.ToInt32(result);
    }

    private static ContactPersonTitle Map(SqlDataReader r) => new()
    {
        Id           = r.GetInt32(0),
        Name         = r.GetString(1),
        SortOrder    = r.GetInt32(2),
        IsSystem     = r.GetBoolean(3),
        IsActive     = r.GetBoolean(4),
        Created      = r.GetDateTime(5),
        Updated      = r.IsDBNull(6) ? null : r.GetDateTime(6),
        CreatedById  = r.IsDBNull(7) ? null : r.GetInt32(7),
        UpdatedById  = r.IsDBNull(8) ? null : r.GetInt32(8),
    };
}

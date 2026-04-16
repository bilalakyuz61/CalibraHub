using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlPriceListRepository : IPriceListRepository
{
    private readonly SqlServerConnectionFactory _cf;
    private readonly string _tblGroups;
    private readonly string _tblEntries;

    public SqlPriceListRepository(SqlServerConnectionFactory cf, CalibraDatabaseOptions options)
    {
        _cf = cf;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tblGroups  = $"[{schema}].[price_groups]";
        _tblEntries = $"[{schema}].[price_list_entries]";
    }

    // ── Fiyat Gruplari ────────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<PriceGroup>> GetAllGroupsAsync(CancellationToken ct)
    {
        var list = new List<PriceGroup>();
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT [id],[group_code],[group_name],[description],[is_active],[created_at],[updated_at] FROM {_tblGroups} ORDER BY [group_code];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapGroup(r));
        return list;
    }

    public async Task<PriceGroup?> GetGroupByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT [id],[group_code],[group_name],[description],[is_active],[created_at],[updated_at] FROM {_tblGroups} WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapGroup(r) : null;
    }

    public async Task<int> AddGroupAsync(PriceGroup g, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_tblGroups} ([group_code],[group_name],[description],[is_active],[created_at],[updated_at])
            VALUES (@Code,@Name,@Desc,@Active,GETDATE(),GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.Add(new SqlParameter("@Code",   g.GroupCode));
        cmd.Parameters.Add(new SqlParameter("@Name",   g.GroupName));
        cmd.Parameters.Add(new SqlParameter("@Desc",   (object?)g.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Active", g.IsActive));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task UpdateGroupAsync(PriceGroup g, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_tblGroups} SET [group_code]=@Code,[group_name]=@Name,[description]=@Desc,[is_active]=@Active,[updated_at]=GETDATE()
            WHERE [id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",     g.Id));
        cmd.Parameters.Add(new SqlParameter("@Code",   g.GroupCode));
        cmd.Parameters.Add(new SqlParameter("@Name",   g.GroupName));
        cmd.Parameters.Add(new SqlParameter("@Desc",   (object?)g.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Active", g.IsActive));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteGroupAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tblEntries} WHERE [price_group_id]=@Id; DELETE FROM {_tblGroups} WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Fiyat Kalemleri ──────────────────────────────────────────────────────

    private const string EntryCols =
        "[id],[price_group_id],[stock_card_id],[material_code],[material_name]," +
        "[currency],[buying_price],[selling_price],[valid_from],[valid_to],[is_active],[created_at],[updated_at]";

    public async Task<IReadOnlyCollection<PriceListEntry>> GetEntriesByGroupAsync(int groupId, CancellationToken ct)
    {
        var list = new List<PriceListEntry>();
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {EntryCols} FROM {_tblEntries} WHERE [price_group_id]=@GroupId ORDER BY [material_code],[valid_from];";
        cmd.Parameters.Add(new SqlParameter("@GroupId", groupId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapEntry(r));
        return list;
    }

    public async Task<PriceListEntry?> GetEntryByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {EntryCols} FROM {_tblEntries} WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapEntry(r) : null;
    }

    public async Task<int> AddEntryAsync(PriceListEntry e, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_tblEntries}
                ([price_group_id],[stock_card_id],[material_code],[material_name],[currency],[buying_price],[selling_price],[valid_from],[valid_to],[is_active],[created_at],[updated_at])
            VALUES (@GroupId,@SCardId,@MatCode,@MatName,@Currency,@BuyPrice,@SellPrice,@ValidFrom,@ValidTo,@Active,GETDATE(),GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        AddEntryParams(cmd, e);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task AddBulkEntriesAsync(IReadOnlyCollection<PriceListEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        await using var conn = await _cf.OpenConnectionAsync(ct);

        foreach (var e in entries)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_tblEntries}
                    ([price_group_id],[stock_card_id],[material_code],[material_name],[currency],[buying_price],[selling_price],[valid_from],[valid_to],[is_active],[created_at],[updated_at])
                VALUES (@GroupId,@SCardId,@MatCode,@MatName,@Currency,@BuyPrice,@SellPrice,@ValidFrom,@ValidTo,@Active,GETDATE(),GETDATE());
                """;
            AddEntryParams(cmd, e);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task UpdateEntryAsync(PriceListEntry e, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_tblEntries} SET
                [material_code]=@MatCode,[material_name]=@MatName,
                [currency]=@Currency,[buying_price]=@BuyPrice,[selling_price]=@SellPrice,
                [valid_from]=@ValidFrom,[valid_to]=@ValidTo,
                [is_active]=@Active,[updated_at]=GETDATE()
            WHERE [id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", e.Id));
        cmd.Parameters.Add(new SqlParameter("@MatCode",   e.MaterialCode));
        cmd.Parameters.Add(new SqlParameter("@MatName",   (object?)e.MaterialName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Currency",  e.Currency));
        cmd.Parameters.Add(new SqlParameter("@BuyPrice",  e.BuyingPrice));
        cmd.Parameters.Add(new SqlParameter("@SellPrice", e.SellingPrice));
        cmd.Parameters.Add(new SqlParameter("@ValidFrom", e.ValidFrom));
        cmd.Parameters.Add(new SqlParameter("@ValidTo",   (object?)e.ValidTo ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Active",    e.IsActive));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteEntryAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tblEntries} WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddEntryParams(SqlCommand cmd, PriceListEntry e)
    {
        cmd.Parameters.Add(new SqlParameter("@GroupId",   e.PriceGroupId));
        cmd.Parameters.Add(new SqlParameter("@SCardId",   (object?)e.StockCardId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MatCode",   e.MaterialCode));
        cmd.Parameters.Add(new SqlParameter("@MatName",   (object?)e.MaterialName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Currency",  e.Currency));
        cmd.Parameters.Add(new SqlParameter("@BuyPrice",  e.BuyingPrice));
        cmd.Parameters.Add(new SqlParameter("@SellPrice", e.SellingPrice));
        cmd.Parameters.Add(new SqlParameter("@ValidFrom", e.ValidFrom));
        cmd.Parameters.Add(new SqlParameter("@ValidTo",   (object?)e.ValidTo ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Active",    e.IsActive));
    }

    private static PriceGroup MapGroup(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        GroupCode   = r.GetString(1),
        GroupName   = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        IsActive    = r.GetBoolean(4),
        CreatedAt   = r.GetDateTime(5),
        UpdatedAt   = r.GetDateTime(6)
    };

    private static PriceListEntry MapEntry(SqlDataReader r) => new()
    {
        Id           = r.GetInt32(0),
        PriceGroupId = r.GetInt32(1),
        StockCardId  = r.IsDBNull(2) ? null : r.GetInt32(2),
        MaterialCode = r.GetString(3),
        MaterialName = r.IsDBNull(4) ? null : r.GetString(4),
        Currency     = r.GetString(5),
        BuyingPrice  = r.GetDecimal(6),
        SellingPrice = r.GetDecimal(7),
        ValidFrom    = r.GetDateTime(8),
        ValidTo      = r.IsDBNull(9) ? null : r.GetDateTime(9),
        IsActive     = r.GetBoolean(10),
        CreatedAt    = r.GetDateTime(11),
        UpdatedAt    = r.GetDateTime(12)
    };
}

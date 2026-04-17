using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
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
        _tblGroups  = $"[{schema}].[PriceGroup]";
        _tblEntries = $"[{schema}].[PriceList]";
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
        "[id],[price_group_id],[item_id],[material_code],[material_name]," +
        "[combination_code],[combination_name]," +
        "[currency],[buying_price],[selling_price],[valid_from],[valid_to],[is_active],[created_at],[updated_at]";

    public async Task<IReadOnlyCollection<PriceList>> GetEntriesByGroupAsync(int groupId, CancellationToken ct)
    {
        var list = new List<PriceList>();
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {EntryCols} FROM {_tblEntries} WHERE [price_group_id]=@GroupId ORDER BY [material_code],[combination_code],[valid_from];";
        cmd.Parameters.Add(new SqlParameter("@GroupId", groupId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapEntry(r));
        return list;
    }

    public async Task<PriceList?> GetEntryByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {EntryCols} FROM {_tblEntries} WHERE [id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapEntry(r) : null;
    }

    public async Task<int> AddEntryAsync(PriceList e, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_tblEntries}
                ([price_group_id],[item_id],[material_code],[material_name],[combination_code],[combination_name],[currency],[buying_price],[selling_price],[valid_from],[valid_to],[is_active],[created_at],[updated_at])
            VALUES (@GroupId,@SCardId,@MatCode,@MatName,@ComboCode,@ComboName,@Currency,@BuyPrice,@SellPrice,@ValidFrom,@ValidTo,@Active,GETDATE(),GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        AddEntryParams(cmd, e);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task AddBulkEntriesAsync(IReadOnlyCollection<PriceList> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        await using var conn = await _cf.OpenConnectionAsync(ct);

        foreach (var e in entries)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_tblEntries}
                    ([price_group_id],[item_id],[material_code],[material_name],[combination_code],[combination_name],[currency],[buying_price],[selling_price],[valid_from],[valid_to],[is_active],[created_at],[updated_at])
                VALUES (@GroupId,@SCardId,@MatCode,@MatName,@ComboCode,@ComboName,@Currency,@BuyPrice,@SellPrice,@ValidFrom,@ValidTo,@Active,GETDATE(),GETDATE());
                """;
            AddEntryParams(cmd, e);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task UpdateEntryAsync(PriceList e, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_tblEntries} SET
                [material_code]=@MatCode,[material_name]=@MatName,
                [combination_code]=@ComboCode,[combination_name]=@ComboName,
                [currency]=@Currency,[buying_price]=@BuyPrice,[selling_price]=@SellPrice,
                [valid_from]=@ValidFrom,[valid_to]=@ValidTo,
                [is_active]=@Active,[updated_at]=GETDATE()
            WHERE [id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", e.Id));
        cmd.Parameters.Add(new SqlParameter("@MatCode",   e.MaterialCode));
        cmd.Parameters.Add(new SqlParameter("@MatName",   (object?)e.MaterialName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ComboCode", (object?)e.CombinationCode ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ComboName", (object?)e.CombinationName ?? DBNull.Value));
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

    // ── Upsert (bulk) ────────────────────────────────────────────────────────

    public async Task<BulkUpsertResult> UpsertBulkEntriesAsync(
        IReadOnlyCollection<PriceList> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return new BulkUpsertResult(0, 0);
        var inserted = 0;
        var updated  = 0;

        await using var conn = await _cf.OpenConnectionAsync(ct);
        foreach (var e in entries)
        {
            // MERGE pattern — per-row (toplu satir sayisi dusuk oldugu icin yeterli)
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                MERGE {_tblEntries} AS tgt
                USING (SELECT
                        @GroupId   AS price_group_id,
                        @SCardId   AS item_id,
                        @MatCode   AS material_code,
                        @ComboCode AS combination_code,
                        @Currency  AS currency,
                        @ValidFrom AS valid_from
                      ) AS src
                ON tgt.[price_group_id] = src.price_group_id
                   AND ISNULL(tgt.[item_id], -1) = ISNULL(src.item_id, -1)
                   AND tgt.[material_code] = src.material_code
                   AND ISNULL(tgt.[combination_code], N'') = ISNULL(src.combination_code, N'')
                   AND tgt.[currency] = src.currency
                   AND tgt.[valid_from] = src.valid_from
                WHEN MATCHED THEN
                    UPDATE SET
                        [material_name]    = @MatName,
                        [combination_name] = @ComboName,
                        [buying_price]     = @BuyPrice,
                        [selling_price]    = @SellPrice,
                        [valid_to]         = @ValidTo,
                        [is_active]        = @Active,
                        [updated_at]       = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT ([price_group_id],[item_id],[material_code],[material_name],
                            [combination_code],[combination_name],[currency],
                            [buying_price],[selling_price],[valid_from],[valid_to],
                            [is_active],[created_at],[updated_at])
                    VALUES (@GroupId,@SCardId,@MatCode,@MatName,
                            @ComboCode,@ComboName,@Currency,
                            @BuyPrice,@SellPrice,@ValidFrom,@ValidTo,
                            @Active,GETDATE(),GETDATE())
                OUTPUT $action AS Act;
                """;
            AddEntryParams(cmd, e);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                var act = r.GetString(0);
                if (string.Equals(act, "INSERT", StringComparison.OrdinalIgnoreCase)) inserted++;
                else if (string.Equals(act, "UPDATE", StringComparison.OrdinalIgnoreCase)) updated++;
            }
        }

        return new BulkUpsertResult(inserted, updated);
    }

    // ── Mevcut Fiyat Sorgusu ─────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<ExistingPriceRow>> GetExistingPricesAsync(
        int priceGroupId, string currency, DateTime validFrom,
        IReadOnlyCollection<PriceEntryKey> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return Array.Empty<ExistingPriceRow>();

        var list = new List<ExistingPriceRow>();
        await using var conn = await _cf.OpenConnectionAsync(ct);

        // Basit yaklasim: her key icin tek sorgu (anahtarlar genelde az sayida)
        foreach (var k in keys)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT TOP(1) [item_id],[material_code],[combination_code],[buying_price],[selling_price]
                FROM {_tblEntries}
                WHERE [price_group_id] = @GroupId
                  AND [material_code]  = @MatCode
                  AND ISNULL([combination_code], N'') = ISNULL(@ComboCode, N'')
                  AND [currency]       = @Currency
                  AND [is_active]      = 1
                ORDER BY [valid_from] DESC;
                """;
            cmd.Parameters.Add(new SqlParameter("@GroupId",   priceGroupId));
            cmd.Parameters.Add(new SqlParameter("@MatCode",   k.MaterialCode));
            cmd.Parameters.Add(new SqlParameter("@ComboCode", (object?)k.CombinationCode ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Currency",  currency));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                list.Add(new ExistingPriceRow(
                    ItemId:     r.IsDBNull(0) ? null : r.GetInt32(0),
                    MaterialCode:    r.GetString(1),
                    CombinationCode: r.IsDBNull(2) ? null : r.GetString(2),
                    BuyingPrice:     r.GetDecimal(3),
                    SellingPrice:    r.GetDecimal(4)));
            }
        }

        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddEntryParams(SqlCommand cmd, PriceList e)
    {
        cmd.Parameters.Add(new SqlParameter("@GroupId",   e.PriceGroupId));
        cmd.Parameters.Add(new SqlParameter("@SCardId",   (object?)e.ItemId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MatCode",   e.MaterialCode));
        cmd.Parameters.Add(new SqlParameter("@MatName",   (object?)e.MaterialName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ComboCode", (object?)e.CombinationCode ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ComboName", (object?)e.CombinationName ?? DBNull.Value));
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

    private static PriceList MapEntry(SqlDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        PriceGroupId    = r.GetInt32(1),
        ItemId     = r.IsDBNull(2) ? null : r.GetInt32(2),
        MaterialCode    = r.GetString(3),
        MaterialName    = r.IsDBNull(4) ? null : r.GetString(4),
        CombinationCode = r.IsDBNull(5) ? null : r.GetString(5),
        CombinationName = r.IsDBNull(6) ? null : r.GetString(6),
        Currency        = r.GetString(7),
        BuyingPrice     = r.GetDecimal(8),
        SellingPrice    = r.GetDecimal(9),
        ValidFrom       = r.GetDateTime(10),
        ValidTo         = r.IsDBNull(11) ? null : r.GetDateTime(11),
        IsActive        = r.GetBoolean(12),
        CreatedAt       = r.GetDateTime(13),
        UpdatedAt       = r.GetDateTime(14)
    };
}

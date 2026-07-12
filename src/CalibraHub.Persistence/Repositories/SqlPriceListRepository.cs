using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// PriceList repository — her DB satiri TEK bir fiyat tasir (PriceType: 'b'=alis,
/// 's'=satis; ileride 'm'=maliyet eklenebilir). UI tarafi her satiri ayri olarak
/// gosterir; tabloda "Tip" sutunu Alis/Satis kayitlarini ayri satirlarda gosterir.
/// </summary>
public sealed class SqlPriceListRepository : IPriceListRepository
{
    private readonly SqlServerConnectionFactory _cf;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly string _schema;
    private readonly string _tblGroups;
    private readonly string _tblEntries;

    public SqlPriceListRepository(
        SqlServerConnectionFactory cf,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor,
        IDataVisibilityFilter dvFilter)
    {
        _cf = cf;
        _httpContextAccessor = httpContextAccessor;
        _dvFilter = dvFilter;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tblGroups  = $"[{_schema}].[PriceGroup]";
        _tblEntries = $"[{_schema}].[PriceList]";
    }

    private int GetCurrentCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var raw = httpContext.User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var id))
                return id;
        }
        return 0;
    }

    // ── Fiyat Gruplari ────────────────────────────────────────────────────────

    private const string GroupCols =
        "[Id],[CompanyId],[Code],[Name],[Description],[IsActive]," +
        "[AllowsBuying],[AllowsSelling],[AllowsCost],[Created],[Updated],[IsDefault]";

    public async Task<IReadOnlyCollection<PriceGroup>> GetAllGroupsAsync(CancellationToken ct)
    {
        var list = new List<PriceGroup>();
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        // Satır görünürlük kuralları (row-level security) — tek tablo, alias 'pg'.
        var dv = await _dvFilter.BuildAsync(FormCodes.PriceList, "pg", "Id", ct);
        cmd.CommandText = $"""
            SELECT {GroupCols}
            FROM {_tblGroups} pg
            WHERE [CompanyId]=@CompanyId
            {dv.Sql}
            ORDER BY [Code];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        foreach (var prm in dv.Parameters) cmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapGroup(r));
        return list;
    }

    public async Task<PriceGroup?> GetGroupByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {GroupCols}
            FROM {_tblGroups}
            WHERE [CompanyId]=@CompanyId AND [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapGroup(r) : null;
    }

    public async Task<int> AddGroupAsync(PriceGroup g, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_tblGroups} ([CompanyId],[Code],[Name],[Description],[IsActive],[AllowsBuying],[AllowsSelling],[AllowsCost],[IsDefault],[Created],[Updated])
            VALUES (@CompanyId,@Code,@Name,@Desc,@Active,@AllowsBuying,@AllowsSelling,@AllowsCost,@IsDefault,GETDATE(),GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        var effectiveCompanyId = g.CompanyId > 0
            ? g.CompanyId
            : (GetCurrentCompanyId() is int cid && cid > 0 ? cid : 1);
        cmd.Parameters.Add(new SqlParameter("@CompanyId",     effectiveCompanyId));
        cmd.Parameters.Add(new SqlParameter("@Code",          g.Code));
        cmd.Parameters.Add(new SqlParameter("@Name",          g.Name));
        cmd.Parameters.Add(new SqlParameter("@Desc",          (object?)g.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Active",        g.IsActive));
        cmd.Parameters.Add(new SqlParameter("@AllowsBuying",  g.AllowsBuying));
        cmd.Parameters.Add(new SqlParameter("@AllowsSelling", g.AllowsSelling));
        cmd.Parameters.Add(new SqlParameter("@AllowsCost",    g.AllowsCost));
        cmd.Parameters.Add(new SqlParameter("@IsDefault",     g.IsDefault));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task UpdateGroupAsync(PriceGroup g, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_tblGroups}
            SET [Code]=@Code,[Name]=@Name,[Description]=@Desc,[IsActive]=@Active,
                [AllowsBuying]=@AllowsBuying,[AllowsSelling]=@AllowsSelling,[AllowsCost]=@AllowsCost,
                [Updated]=GETDATE()
            WHERE [CompanyId]=@CompanyId AND [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId",     GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id",            g.Id));
        cmd.Parameters.Add(new SqlParameter("@Code",          g.Code));
        cmd.Parameters.Add(new SqlParameter("@Name",          g.Name));
        cmd.Parameters.Add(new SqlParameter("@Desc",          (object?)g.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Active",        g.IsActive));
        cmd.Parameters.Add(new SqlParameter("@AllowsBuying",  g.AllowsBuying));
        cmd.Parameters.Add(new SqlParameter("@AllowsSelling", g.AllowsSelling));
        cmd.Parameters.Add(new SqlParameter("@AllowsCost",    g.AllowsCost));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteGroupAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE pl FROM {_tblEntries} pl
                INNER JOIN {_tblGroups} g ON g.[Id] = pl.[GroupId]
                WHERE g.[CompanyId]=@CompanyId AND g.[Id]=@Id;
            DELETE FROM {_tblGroups}
                WHERE [CompanyId]=@CompanyId AND [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id",        id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // "Genel Liste" (fallback) grubu — CompanyId basina tek IsDefault=1 kayit.
    public async Task<int?> GetDefaultGroupIdAsync(CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP(1) [Id]
            FROM {_tblGroups}
            WHERE [CompanyId]=@CompanyId AND [IsDefault]=1 AND [IsActive]=1
            ORDER BY [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        var val = await cmd.ExecuteScalarAsync(ct);
        return val is null || val == DBNull.Value ? null : Convert.ToInt32(val);
    }

    // Verilen grubu default yap; ayni company'deki diger tum gruplarin default'unu kaldir.
    // Tek UPDATE statement + CASE WHEN → filtered unique index (tek default) ihlal edilmeden atomik.
    public async Task SetDefaultGroupAsync(int groupId, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_tblGroups}
            SET [IsDefault] = CASE WHEN [Id]=@Id THEN 1 ELSE 0 END,
                [Updated]   = GETDATE()
            WHERE [CompanyId]=@CompanyId;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", GetCurrentCompanyId()));
        cmd.Parameters.Add(new SqlParameter("@Id",        groupId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Fiyat Kalemleri ──────────────────────────────────────────────────────

    private const string EntryCols =
        "[Id],[GroupId],[ItemId],[ConfigId],[CurrencyId]," +
        "[PriceType],[Price],[ValidFrom],[ValidTo],[IsActive],[Created],[Updated]";

    private string TblItems          => $"[{_schema}].[Items]";
    private string TblConfigurations => $"[{_schema}].[ItemConfiguration]";
    private string TblCurrencies     => $"[{_schema}].[Currency]";

    /// <summary>
    /// Grup bazli liste — her DB satiri tek bir DTO'ya map'lenir (PriceType + Price).
    /// Ayni urun icin Buying ve Selling iki ayri satir olarak donulur; UI tabloda
    /// Tip sutunuyla ayri ayri gosterir.
    /// </summary>
    public async Task<PagedPriceListResult> GetEntriesByGroupAsync(
        int groupId, PriceListFilter filter, CancellationToken ct)
    {
        var page     = filter.Page < 1 ? 1 : filter.Page;
        // Normal liste icin frontend 25-250 arasi kullanir; Excel export endpoint
        // 100000 gonderebilir (tum filtrelenen kayitlar tek sayfada). Ust limit 100k
        // pratik olarak sinirsiz ama DDoS / OOM korumasi icin clamped.
        var pageSize = filter.PageSize switch
        {
            < 1     => 50,
            > 100000 => 100000,
            _       => filter.PageSize
        };
        var offset   = (page - 1) * pageSize;

        var list  = new List<PriceListDto>();
        var total = 0;

        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();

        // Defansif JOIN: orphan FK olan satırlar (Items/currencies eşleşmeyen) düşmesin —
        // LEFT JOIN + null fallback. Server-side filter + OFFSET/FETCH pagination.
        // COUNT(*) OVER() ile total tek query'de.
        //
        // ──── ActiveOn (Baz Tarih) DAVRANIŞI ────
        // Set ise: AYNI (Group+Item+Config+Currency+PriceType) anahtarı için MAX(ValidFrom <= aon)
        // satırı seçilir; daha eski ValidFrom'lu kayıtlar listede görünmez (sonraki kayıt
        // tarafından invalide edilmiş kabul edilir). ValidTo dolu ve aon'u geçtiyse de düşer.
        // Tek satır CTE + ROW_NUMBER() ile partition.
        var hasActiveOn = filter.ActiveOn.HasValue;

        var sb = new System.Text.StringBuilder();
        if (hasActiveOn)
        {
            sb.AppendLine("WITH AsOf AS (");
            sb.AppendLine($"  SELECT p.*, ROW_NUMBER() OVER (");
            sb.AppendLine("    PARTITION BY p.[GroupId], p.[ItemId], p.[ConfigId], p.[CurrencyId], p.[PriceType]");
            sb.AppendLine("    ORDER BY p.[ValidFrom] DESC, p.[Id] DESC");
            sb.AppendLine("  ) AS rn");
            sb.AppendLine($"  FROM {_tblEntries} p");
            sb.AppendLine("  WHERE p.[GroupId] = @GroupId");
            sb.AppendLine("    AND p.[IsActive] = 1");
            sb.AppendLine("    AND p.[ValidFrom] <= @ActiveOn");
            sb.AppendLine("    AND (p.[ValidTo] IS NULL OR p.[ValidTo] >= @ActiveOn)");
            sb.AppendLine(")");
            sb.AppendLine("SELECT p.[Id], p.[GroupId], p.[ItemId], i.[Code], i.[Name],");
            sb.AppendLine("       p.[ConfigId], cfg.[RecordCode],");
            sb.AppendLine("       p.[CurrencyId], cur.[code],");
            sb.AppendLine("       p.[PriceType], p.[Price],");
            sb.AppendLine("       p.[ValidFrom], p.[ValidTo], p.[IsActive],");
            sb.AppendLine("       p.[Created], p.[Updated],");
            sb.AppendLine("       COUNT(*) OVER() AS TotalCount");
            sb.AppendLine("FROM AsOf p");
            sb.AppendLine($"LEFT JOIN {TblItems} i ON i.[Id] = p.[ItemId]");
            sb.AppendLine($"LEFT JOIN {TblConfigurations} cfg ON cfg.[Id] = p.[ConfigId]");
            sb.AppendLine($"LEFT JOIN {TblCurrencies} cur ON cur.[Id] = p.[CurrencyId]");
            sb.AppendLine("WHERE p.rn = 1");
            cmd.Parameters.Add(new SqlParameter("@ActiveOn", filter.ActiveOn!.Value.Date));
        }
        else
        {
            sb.AppendLine($"SELECT p.[Id], p.[GroupId], p.[ItemId], i.[Code], i.[Name],");
            sb.AppendLine($"       p.[ConfigId], cfg.[RecordCode],");
            sb.AppendLine($"       p.[CurrencyId], cur.[code],");
            sb.AppendLine($"       p.[PriceType], p.[Price],");
            sb.AppendLine($"       p.[ValidFrom], p.[ValidTo], p.[IsActive],");
            sb.AppendLine($"       p.[Created], p.[Updated],");
            sb.AppendLine($"       COUNT(*) OVER() AS TotalCount");
            sb.AppendLine($"FROM {_tblEntries} p");
            sb.AppendLine($"LEFT JOIN {TblItems} i ON i.[Id] = p.[ItemId]");
            sb.AppendLine($"LEFT JOIN {TblConfigurations} cfg ON cfg.[Id] = p.[ConfigId]");
            sb.AppendLine($"LEFT JOIN {TblCurrencies} cur ON cur.[Id] = p.[CurrencyId]");
            sb.AppendLine($"WHERE p.[GroupId] = @GroupId");
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            sb.AppendLine("  AND (i.[Code] LIKE @Search OR i.[Name] LIKE @Search)");
            cmd.Parameters.Add(new SqlParameter("@Search", "%" + filter.Search.Trim() + "%"));
        }
        if (filter.CurrencyId is int cid && cid > 0)
        {
            sb.AppendLine("  AND p.[CurrencyId] = @CurrencyId");
            cmd.Parameters.Add(new SqlParameter("@CurrencyId", cid));
        }
        if (!string.IsNullOrWhiteSpace(filter.PriceType))
        {
            sb.AppendLine("  AND p.[PriceType] = @PriceType");
            cmd.Parameters.Add(new SqlParameter("@PriceType", filter.PriceType));
        }
        if (filter.ValidFromMin is DateTime vfm)
        {
            sb.AppendLine("  AND p.[ValidFrom] >= @ValidFromMin");
            cmd.Parameters.Add(new SqlParameter("@ValidFromMin", vfm.Date));
        }
        if (filter.ValidToMax is DateTime vtm)
        {
            sb.AppendLine("  AND (p.[ValidTo] IS NULL OR p.[ValidTo] <= @ValidToMax)");
            cmd.Parameters.Add(new SqlParameter("@ValidToMax", vtm.Date));
        }

        sb.AppendLine("ORDER BY i.[Code], cfg.[RecordCode], p.[ValidFrom],");
        sb.AppendLine("         CASE p.[PriceType] WHEN N'b' THEN 0 ELSE 1 END");
        sb.AppendLine("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;");

        cmd.Parameters.Add(new SqlParameter("@GroupId",  groupId));
        cmd.Parameters.Add(new SqlParameter("@Offset",   offset));
        cmd.Parameters.Add(new SqlParameter("@PageSize", pageSize));
        cmd.CommandText = sb.ToString();

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (total == 0 && !r.IsDBNull(16)) total = r.GetInt32(16);
            list.Add(new PriceListDto(
                Id:           r.GetInt32(0),
                GroupId:      r.GetInt32(1),
                ItemId:       r.IsDBNull(2)  ? 0  : r.GetInt32(2),
                ItemCode:     r.IsDBNull(3)  ? "?" : r.GetString(3),
                ItemName:     r.IsDBNull(4)  ? ""  : r.GetString(4),
                ConfigId:     r.IsDBNull(5)  ? null : r.GetInt32(5),
                ConfigCode:   r.IsDBNull(6)  ? null : r.GetString(6),
                CurrencyId:   r.IsDBNull(7)  ? 0  : r.GetInt32(7),
                CurrencyCode: r.IsDBNull(8)  ? "?" : r.GetString(8),
                PriceType:    r.IsDBNull(9)  ? ""  : r.GetString(9),
                Price:        r.IsDBNull(10) ? 0m  : r.GetDecimal(10),
                ValidFrom:    r.IsDBNull(11) ? DateTime.MinValue : r.GetDateTime(11),
                ValidTo:      r.IsDBNull(12) ? null : r.GetDateTime(12),
                IsActive:     !r.IsDBNull(13) && r.GetBoolean(13),
                CreatedAt:    r.IsDBNull(14) ? DateTime.MinValue : r.GetDateTime(14),
                UpdatedAt:    r.IsDBNull(15) ? DateTime.MinValue : r.GetDateTime(15)));
        }

        // Eger sayfa bos ama filtre uyusan kayit varsa total yine de COUNT'tan gelir;
        // ama window function bos resultset'te satir uretmedigi icin total=0 olur.
        // Bu kullanici icin tutarli cunku "0 kayit" gosterilir.

        return new PagedPriceListResult(list, total, page, pageSize);
    }

    public async Task<PriceList?> GetEntryByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {EntryCols} FROM {_tblEntries} WHERE [Id]=@Id;";
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
                ([GroupId],[ItemId],[ConfigId],[CurrencyId],[PriceType],[Price],[ValidFrom],[ValidTo],[IsActive],[Created],[Updated])
            VALUES (@GroupId,@ItemId,@ConfigId,@CurrencyId,@PriceType,@Price,@ValidFrom,@ValidTo,@Active,GETDATE(),GETDATE());
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
                    ([GroupId],[ItemId],[ConfigId],[CurrencyId],[PriceType],[Price],[ValidFrom],[ValidTo],[IsActive],[Created],[Updated])
                VALUES (@GroupId,@ItemId,@ConfigId,@CurrencyId,@PriceType,@Price,@ValidFrom,@ValidTo,@Active,GETDATE(),GETDATE());
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
                [ItemId]=@ItemId,[ConfigId]=@ConfigId,[CurrencyId]=@CurrencyId,
                [PriceType]=@PriceType,[Price]=@Price,
                [ValidFrom]=@ValidFrom,[ValidTo]=@ValidTo,
                [IsActive]=@Active,[Updated]=GETDATE()
            WHERE [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", e.Id));
        AddEntryParams(cmd, e);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteEntryAsync(int id, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tblEntries} WHERE [Id]=@Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PriceList?> FindActiveDuplicateAsync(
        int groupId, int itemId, int? configId, int currencyId,
        string priceType, DateTime validFrom,
        int excludeId, CancellationToken ct)
    {
        await using var conn = await _cf.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        var configClause = configId.HasValue ? "[ConfigId] = @ConfigId" : "[ConfigId] IS NULL";
        cmd.CommandText = $@"
            SELECT TOP 1 {EntryCols}
            FROM {_tblEntries}
            WHERE [GroupId]    = @GroupId
              AND [ItemId]     = @ItemId
              AND {configClause}
              AND [CurrencyId] = @CurrencyId
              AND [PriceType]  = @PriceType
              AND [ValidFrom]  = @ValidFrom
              AND [IsActive]   = 1
              AND [Id]        <> @ExcludeId;";
        cmd.Parameters.Add(new SqlParameter("@GroupId",    groupId));
        cmd.Parameters.Add(new SqlParameter("@ItemId",     itemId));
        if (configId.HasValue)
            cmd.Parameters.Add(new SqlParameter("@ConfigId", configId.Value));
        cmd.Parameters.Add(new SqlParameter("@CurrencyId", currencyId));
        cmd.Parameters.Add(new SqlParameter("@PriceType",  priceType));
        cmd.Parameters.Add(new SqlParameter("@ValidFrom",  validFrom));
        cmd.Parameters.Add(new SqlParameter("@ExcludeId",  excludeId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapEntry(r) : null;
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
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                MERGE {_tblEntries} AS tgt
                USING (SELECT
                        @GroupId    AS GroupId,
                        @ItemId     AS ItemId,
                        @ConfigId   AS ConfigId,
                        @CurrencyId AS CurrencyId,
                        @PriceType  AS PriceType,
                        @ValidFrom  AS ValidFrom
                      ) AS src
                ON tgt.[GroupId] = src.GroupId
                   AND tgt.[ItemId] = src.ItemId
                   AND ISNULL(tgt.[ConfigId], -1) = ISNULL(src.ConfigId, -1)
                   AND tgt.[CurrencyId] = src.CurrencyId
                   AND tgt.[PriceType] = src.PriceType
                   AND tgt.[ValidFrom] = src.ValidFrom
                WHEN MATCHED THEN
                    UPDATE SET
                        [Price]      = @Price,
                        [ValidTo]    = @ValidTo,
                        [IsActive]   = @Active,
                        [Updated] = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT ([GroupId],[ItemId],[ConfigId],[CurrencyId],[PriceType],[Price],
                            [ValidFrom],[ValidTo],[IsActive],[Created],[Updated])
                    VALUES (@GroupId,@ItemId,@ConfigId,@CurrencyId,@PriceType,@Price,
                            @ValidFrom,@ValidTo,@Active,GETDATE(),GETDATE())
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

    // ── Mevcut Fiyat Sorgusu (tek tip — wizard'da secilen PriceType) ─────────

    public async Task<IReadOnlyCollection<ExistingPriceRow>> GetExistingPricesAsync(
        int groupId, int currencyId, string priceType, DateTime validFrom,
        IReadOnlyCollection<PriceEntryKey> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return Array.Empty<ExistingPriceRow>();

        var list = new List<ExistingPriceRow>();
        await using var conn = await _cf.OpenConnectionAsync(ct);

        // DEBUG: maliyet sorunu teshisi icin (gecici) — params + her key icin sonuc
        Console.WriteLine($"[PriceLookup] GroupId={groupId} CurrencyId={currencyId} PriceType='{priceType}' ValidFrom={validFrom:yyyy-MM-dd} keys={keys.Count}");

        foreach (var k in keys)
        {
            await using var cmd = conn.CreateCommand();
            // BUG fix: eskisi ORDER BY ValidFrom DESC LIMIT 1 → daima en son
            // girilen fiyat doniiyordu; belge tarihi yok sayiliyordu. Dogru mantik:
            // verilen tarihte (validFrom parametresi) yururlukte olan fiyati bul —
            // ValidFrom <= @ValidFrom AND (ValidTo IS NULL OR ValidTo >= @ValidFrom),
            // sonra en yeni ValidFrom'lu olani dondur ("en yakın geçmiş" geçerli fiyat).
            cmd.CommandText = $"""
                SELECT TOP(1) [ItemId], [ConfigId], [Price]
                FROM {_tblEntries}
                WHERE [GroupId]    = @GroupId
                  AND [ItemId]     = @ItemId
                  AND ISNULL([ConfigId], -1) = ISNULL(@ConfigId, -1)
                  AND [CurrencyId] = @CurrencyId
                  AND [PriceType]  = @PriceType
                  AND [IsActive]   = 1
                  AND [ValidFrom] <= @ValidFrom
                  AND ([ValidTo] IS NULL OR [ValidTo] >= @ValidFrom)
                ORDER BY [ValidFrom] DESC;
                """;
            cmd.Parameters.Add(new SqlParameter("@GroupId",    groupId));
            cmd.Parameters.Add(new SqlParameter("@ItemId",     k.ItemId));
            cmd.Parameters.Add(new SqlParameter("@ConfigId",   (object?)k.ConfigId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@CurrencyId", currencyId));
            cmd.Parameters.Add(new SqlParameter("@PriceType",  priceType));
            cmd.Parameters.Add(new SqlParameter("@ValidFrom",  validFrom));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                var row = new ExistingPriceRow(
                    ItemId:   r.GetInt32(0),
                    ConfigId: r.IsDBNull(1) ? null : r.GetInt32(1),
                    Price:    r.GetDecimal(2));
                list.Add(row);
                Console.WriteLine($"[PriceLookup]   ItemId={k.ItemId} ConfigId={(k.ConfigId?.ToString() ?? "NULL")} → Price={row.Price}");
            }
            else
            {
                Console.WriteLine($"[PriceLookup]   ItemId={k.ItemId} ConfigId={(k.ConfigId?.ToString() ?? "NULL")} → NO ROW");
            }
        }

        return list;
    }

    // ── Fallback'li Fiyat Cozumu (cari grubu → Genel Liste) ──────────────────
    // Her key icin cari grubu + default grubu TEK sorguda arar; oncelik ORDER BY:
    //   (a) cari grup + exact config, (b) cari grup + base(null config),
    //   (c) genel grup + exact config, (d) genel grup + base.
    // Tarih-etkinlik ve ConfigId semantigi GetExistingPricesAsync ile birebir ayni.
    public async Task<IReadOnlyCollection<ResolvedPriceRow>> ResolveExistingPricesAsync(
        int? contactGroupId, int defaultGroupId,
        int currencyId, string priceType, DateTime date,
        IReadOnlyCollection<PriceEntryKey> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return Array.Empty<ResolvedPriceRow>();

        var list = new List<ResolvedPriceRow>();
        await using var conn = await _cf.OpenConnectionAsync(ct);

        foreach (var k in keys)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT TOP(1) [Price], [GroupId],
                       CASE WHEN [ConfigId] IS NULL THEN 0 ELSE 1 END AS ConfigExact
                FROM {_tblEntries}
                WHERE [GroupId] IN (@ContactGroupId, @DefaultGroupId)
                  AND [ItemId]     = @ItemId
                  AND [CurrencyId]  = @CurrencyId
                  AND [PriceType]  = @PriceType
                  AND [IsActive]   = 1
                  AND [ValidFrom] <= @Date
                  AND ([ValidTo] IS NULL OR [ValidTo] >= @Date)
                  AND ([ConfigId] = @ConfigId OR [ConfigId] IS NULL)
                ORDER BY
                  CASE WHEN [GroupId] = @ContactGroupId THEN 0 ELSE 1 END,
                  CASE WHEN [ConfigId] = @ConfigId THEN 0 ELSE 1 END,
                  [ValidFrom] DESC;
                """;
            cmd.Parameters.Add(new SqlParameter("@ContactGroupId", (object?)contactGroupId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@DefaultGroupId", defaultGroupId));
            cmd.Parameters.Add(new SqlParameter("@ItemId",     k.ItemId));
            cmd.Parameters.Add(new SqlParameter("@ConfigId",   (object?)k.ConfigId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@CurrencyId", currencyId));
            cmd.Parameters.Add(new SqlParameter("@PriceType",  priceType));
            cmd.Parameters.Add(new SqlParameter("@Date",       date));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                var price       = r.GetDecimal(0);
                var groupId     = r.GetInt32(1);
                var configExact = r.GetInt32(2) == 1;
                var source = (contactGroupId.HasValue && groupId == contactGroupId.Value, configExact) switch
                {
                    (true,  true)  => PriceSource.ContactExact,
                    (true,  false) => PriceSource.ContactBase,
                    (false, true)  => PriceSource.DefaultExact,
                    (false, false) => PriceSource.DefaultBase,
                };
                list.Add(new ResolvedPriceRow(k.ItemId, k.ConfigId, price, groupId, source));
            }
        }
        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddEntryParams(SqlCommand cmd, PriceList e)
    {
        cmd.Parameters.Add(new SqlParameter("@GroupId",    e.GroupId));
        cmd.Parameters.Add(new SqlParameter("@ItemId",     e.ItemId));
        cmd.Parameters.Add(new SqlParameter("@ConfigId",   (object?)e.ConfigId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@CurrencyId", e.CurrencyId));
        cmd.Parameters.Add(new SqlParameter("@PriceType",  e.PriceType));
        cmd.Parameters.Add(new SqlParameter("@Price",      e.Price));
        cmd.Parameters.Add(new SqlParameter("@ValidFrom",  e.ValidFrom));
        cmd.Parameters.Add(new SqlParameter("@ValidTo",    (object?)e.ValidTo ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Active",     e.IsActive));
    }

    private static PriceGroup MapGroup(SqlDataReader r) => new()
    {
        Id            = r.GetInt32(0),
        CompanyId     = r.GetInt32(1),
        Code          = r.GetString(2),
        Name          = r.GetString(3),
        Description   = r.IsDBNull(4) ? null : r.GetString(4),
        IsActive      = r.GetBoolean(5),
        AllowsBuying  = r.GetBoolean(6),
        AllowsSelling = r.GetBoolean(7),
        AllowsCost    = r.GetBoolean(8),
        CreatedAt     = r.GetDateTime(9),
        UpdatedAt     = r.IsDBNull(10) ? DateTime.MinValue : r.GetDateTime(10),
        IsDefault     = !r.IsDBNull(11) && r.GetBoolean(11)
    };

    // Defansif map: orphan/migration kaynakli NULL kolonlar (ItemId, CurrencyId vs.)
    // PriceList tablosunda olabilir — InvalidCastException atarak update endpoint'lerini
    // 500'e dusurmesin.
    private static PriceList MapEntry(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        GroupId     = r.IsDBNull(1) ? 0 : r.GetInt32(1),
        ItemId      = r.IsDBNull(2) ? 0 : r.GetInt32(2),
        ConfigId    = r.IsDBNull(3) ? null : r.GetInt32(3),
        CurrencyId  = r.IsDBNull(4) ? 0 : r.GetInt32(4),
        PriceType   = r.IsDBNull(5) ? string.Empty : r.GetString(5),
        Price       = r.IsDBNull(6) ? 0m : r.GetDecimal(6),
        ValidFrom   = r.IsDBNull(7) ? DateTime.MinValue : r.GetDateTime(7),
        ValidTo     = r.IsDBNull(8) ? null : r.GetDateTime(8),
        IsActive    = !r.IsDBNull(9) && r.GetBoolean(9),
        CreatedAt   = r.IsDBNull(10) ? DateTime.MinValue : r.GetDateTime(10),
        UpdatedAt   = r.IsDBNull(11) ? DateTime.MinValue : r.GetDateTime(11)
    };
}

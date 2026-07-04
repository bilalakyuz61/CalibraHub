using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlExchangeRateRepository : IExchangeRateRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _currenciesTable;

    public SqlExchangeRateRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[Exchange]";
        _currenciesTable = $"[{schema}].[Currency]";
    }

    // SELECT kolonlari — CurrencyId FK ile Currency JOIN'lenir, Code+Name otomatik dolar
    private const string SelectCols =
        "r.[Id], r.[CurrencyId], c.[Code], r.[Date], r.[BuyingRate], r.[SellingRate], " +
        "ISNULL(r.[EffectiveBuyingRate],0), ISNULL(r.[EffectiveSellingRate],0), " +
        "r.[Source], r.[Created], c.[Name]";

    public async Task<IReadOnlyCollection<ExchangeRate>> GetLatestRatesAsync(CancellationToken ct)
    {
        var list = new List<ExchangeRate>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {_table} r
            INNER JOIN {_currenciesTable} c ON c.[Id] = r.[CurrencyId]
            INNER JOIN (
                SELECT [CurrencyId], MAX([Date]) AS [max_date]
                FROM {_table}
                GROUP BY [CurrencyId]
            ) latest ON r.[CurrencyId] = latest.[CurrencyId] AND r.[Date] = latest.[max_date]
            ORDER BY c.[Code];
            """;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) list.Add(Map(rd));
        return list;
    }

    public async Task<IReadOnlyCollection<ExchangeRate>> GetRatesForDateAsync(DateTime date, CancellationToken ct)
    {
        var list = new List<ExchangeRate>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // O tarihte kur varsa onu getir, yoksa en yakin onceki tarihteki kuru getir (hafta sonu icin cuma)
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {_table} r
            INNER JOIN {_currenciesTable} c ON c.[Id] = r.[CurrencyId]
            INNER JOIN (
                SELECT [CurrencyId], MAX([Date]) AS [best_date]
                FROM {_table}
                WHERE [Date] <= @Date
                GROUP BY [CurrencyId]
            ) best ON r.[CurrencyId] = best.[CurrencyId] AND r.[Date] = best.[best_date]
            ORDER BY c.[Code];
            """;
        cmd.Parameters.Add(new SqlParameter("@Date", date.Date));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) list.Add(Map(rd));
        return list;
    }

    public async Task<ExchangeRate?> GetRateAsync(string currencyCode, DateTime date, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 {SelectCols}
            FROM {_table} r
            INNER JOIN {_currenciesTable} c ON c.[Id] = r.[CurrencyId]
            WHERE c.[Code] = @Code AND r.[Date] <= @Date
            ORDER BY r.[Date] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", currencyCode));
        cmd.Parameters.Add(new SqlParameter("@Date", date.Date));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    public async Task SaveRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken ct)
    {
        if (rates.Count == 0) return;
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);

        // Caller'lar (TCMB worker dahil) genellikle CurrencyId yerine sadece CurrencyCode tasiyor.
        // Tek seferlik code → id lookup tablosu hazirla; CurrencyId zaten set edilmisse atla.
        Dictionary<string, int>? codeIdMap = null;
        if (rates.Any(r => r.CurrencyId == 0 && !string.IsNullOrWhiteSpace(r.CurrencyCode)))
        {
            codeIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            await using var lookupCmd = conn.CreateCommand();
            lookupCmd.CommandText = $"SELECT [Id], [Code] FROM {_currenciesTable};";
            await using var lookupRd = await lookupCmd.ExecuteReaderAsync(ct);
            while (await lookupRd.ReadAsync(ct))
                codeIdMap[lookupRd.GetString(1)] = lookupRd.GetInt32(0);
        }

        foreach (var rate in rates)
        {
            var currencyId = rate.CurrencyId;
            if (currencyId == 0 && codeIdMap is not null && codeIdMap.TryGetValue(rate.CurrencyCode, out var resolvedId))
                currencyId = resolvedId;
            if (currencyId == 0) continue; // currencies tablosunda kod yoksa atla — sessizce

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                MERGE {_table} AS tgt
                USING (SELECT @CurrencyId AS [cid], @Date AS [d]) AS src
                    ON tgt.[CurrencyId] = src.[cid] AND tgt.[Date] = src.[d]
                WHEN MATCHED THEN
                    UPDATE SET [BuyingRate]=@Buying, [SellingRate]=@Selling,
                               [EffectiveBuyingRate]=@EffBuying, [EffectiveSellingRate]=@EffSelling,
                               [Source]=@Source
                WHEN NOT MATCHED THEN
                    INSERT ([CurrencyId],[Date],[BuyingRate],[SellingRate],[EffectiveBuyingRate],[EffectiveSellingRate],[Source],[Created])
                    VALUES (@CurrencyId, @Date, @Buying, @Selling, @EffBuying, @EffSelling, @Source, GETDATE());
                """;
            cmd.Parameters.Add(new SqlParameter("@CurrencyId", currencyId));
            cmd.Parameters.Add(new SqlParameter("@Date", rate.Date.Date));
            cmd.Parameters.Add(new SqlParameter("@Buying", rate.BuyingRate));
            cmd.Parameters.Add(new SqlParameter("@Selling", rate.SellingRate));
            cmd.Parameters.Add(new SqlParameter("@EffBuying", rate.EffectiveBuyingRate));
            cmd.Parameters.Add(new SqlParameter("@EffSelling", rate.EffectiveSellingRate));
            cmd.Parameters.Add(new SqlParameter("@Source", rate.Source));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<IReadOnlyCollection<ExchangeRate>> GetRatesInRangeAsync(string currencyCode, DateTime from, DateTime to, CancellationToken ct)
    {
        var list = new List<ExchangeRate>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {_table} r
            INNER JOIN {_currenciesTable} c ON c.[Id] = r.[CurrencyId]
            WHERE c.[Code] = @Code AND r.[Date] BETWEEN @From AND @To
            ORDER BY r.[Date] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", currencyCode));
        cmd.Parameters.Add(new SqlParameter("@From", from.Date));
        cmd.Parameters.Add(new SqlParameter("@To", to.Date));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) list.Add(Map(rd));
        return list;
    }

    public async Task<IReadOnlyCollection<ExchangeRate>> GetAllRatesInRangeAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var list = new List<ExchangeRate>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectCols}
            FROM {_table} r
            INNER JOIN {_currenciesTable} c ON c.[Id] = r.[CurrencyId]
            WHERE r.[Date] BETWEEN @From AND @To
            ORDER BY r.[Date] DESC, c.[Code];
            """;
        cmd.Parameters.Add(new SqlParameter("@From", from.Date));
        cmd.Parameters.Add(new SqlParameter("@To", to.Date));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) list.Add(Map(rd));
        return list;
    }

    public async Task DeleteRateAsync(string currencyCode, DateTime date, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE r FROM {_table} r
            INNER JOIN {_currenciesTable} c ON c.[Id] = r.[CurrencyId]
            WHERE c.[Code] = @Code AND r.[Date] = @Date;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", currencyCode));
        cmd.Parameters.Add(new SqlParameter("@Date", date.Date));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ExchangeRate Map(SqlDataReader r) => new()
    {
        Id                   = r.GetInt32(0),
        CurrencyId           = r.GetInt32(1),
        CurrencyCode         = r.GetString(2),
        Date                 = r.GetDateTime(3),
        BuyingRate           = r.GetDecimal(4),
        SellingRate          = r.GetDecimal(5),
        EffectiveBuyingRate  = r.GetDecimal(6),
        EffectiveSellingRate = r.GetDecimal(7),
        Source               = r.GetString(8),
        CreatedAt            = r.GetDateTime(9),
        CurrencyName         = r.IsDBNull(10) ? null : r.GetString(10),
    };
}

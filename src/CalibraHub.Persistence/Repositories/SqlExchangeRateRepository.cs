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

    public SqlExchangeRateRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[exchange_rates]";
    }

    public async Task<IReadOnlyCollection<ExchangeRate>> GetLatestRatesAsync(CancellationToken ct)
    {
        var list = new List<ExchangeRate>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT r.[id], r.[currency_code], r.[rate_date], r.[buying_rate], r.[selling_rate], ISNULL(r.[effective_buying_rate],0), ISNULL(r.[effective_selling_rate],0), r.[source], r.[created_at]
            FROM {_table} r
            INNER JOIN (
                SELECT [currency_code], MAX([rate_date]) AS [max_date]
                FROM {_table}
                GROUP BY [currency_code]
            ) latest ON r.[currency_code] = latest.[currency_code] AND r.[rate_date] = latest.[max_date]
            ORDER BY r.[currency_code];
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyCollection<ExchangeRate>> GetRatesForDateAsync(DateTime date, CancellationToken ct)
    {
        var list = new List<ExchangeRate>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // O tarihte kur varsa onu getir, yoksa en yakin onceki tarihteki kuru getir (hafta sonu icin cuma kuru)
        cmd.CommandText = $"""
            SELECT r.[id], r.[currency_code], r.[rate_date], r.[buying_rate], r.[selling_rate], ISNULL(r.[effective_buying_rate],0), ISNULL(r.[effective_selling_rate],0), r.[source], r.[created_at]
            FROM {_table} r
            INNER JOIN (
                SELECT [currency_code], MAX([rate_date]) AS [best_date]
                FROM {_table}
                WHERE [rate_date] <= @Date
                GROUP BY [currency_code]
            ) best ON r.[currency_code] = best.[currency_code] AND r.[rate_date] = best.[best_date]
            ORDER BY r.[currency_code];
            """;
        cmd.Parameters.Add(new SqlParameter("@Date", date.Date));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<ExchangeRate?> GetRateAsync(string currencyCode, DateTime date, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [id],[currency_code],[rate_date],[buying_rate],[selling_rate],ISNULL([effective_buying_rate],0),ISNULL([effective_selling_rate],0),[source],[created_at]
            FROM {_table} WHERE [currency_code] = @Code AND [rate_date] <= @Date
            ORDER BY [rate_date] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", currencyCode));
        cmd.Parameters.Add(new SqlParameter("@Date", date.Date));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task SaveRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken ct)
    {
        if (rates.Count == 0) return;
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        foreach (var rate in rates)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                MERGE {_table} AS tgt
                USING (SELECT @Code AS [cc], @Date AS [rd]) AS src ON tgt.[currency_code] = src.[cc] AND tgt.[rate_date] = src.[rd]
                WHEN MATCHED THEN UPDATE SET [buying_rate]=@Buying, [selling_rate]=@Selling, [effective_buying_rate]=@EffBuying, [effective_selling_rate]=@EffSelling, [source]=@Source
                WHEN NOT MATCHED THEN INSERT ([currency_code],[rate_date],[buying_rate],[selling_rate],[effective_buying_rate],[effective_selling_rate],[source],[created_at])
                    VALUES (@Code, @Date, @Buying, @Selling, @EffBuying, @EffSelling, @Source, GETDATE());
                """;
            cmd.Parameters.Add(new SqlParameter("@Code", rate.CurrencyCode));
            cmd.Parameters.Add(new SqlParameter("@Date", rate.RateDate.Date));
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
            SELECT r.[id], r.[currency_code], r.[rate_date], r.[buying_rate], r.[selling_rate], ISNULL(r.[effective_buying_rate],0), ISNULL(r.[effective_selling_rate],0), r.[source], r.[created_at]
            FROM {_table} r
            WHERE r.[currency_code] = @Code AND r.[rate_date] BETWEEN @From AND @To
            ORDER BY r.[rate_date] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", currencyCode));
        cmd.Parameters.Add(new SqlParameter("@From", from.Date));
        cmd.Parameters.Add(new SqlParameter("@To", to.Date));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyCollection<ExchangeRate>> GetAllRatesInRangeAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var list = new List<ExchangeRate>();
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT r.[id], r.[currency_code], r.[rate_date], r.[buying_rate], r.[selling_rate], ISNULL(r.[effective_buying_rate],0), ISNULL(r.[effective_selling_rate],0), r.[source], r.[created_at]
            FROM {_table} r
            WHERE r.[rate_date] BETWEEN @From AND @To
            ORDER BY r.[rate_date] DESC, r.[currency_code];
            """;
        cmd.Parameters.Add(new SqlParameter("@From", from.Date));
        cmd.Parameters.Add(new SqlParameter("@To", to.Date));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task DeleteRateAsync(string currencyCode, DateTime date, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenSystemConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [currency_code]=@Code AND [rate_date]=@Date;";
        cmd.Parameters.Add(new SqlParameter("@Code", currencyCode));
        cmd.Parameters.Add(new SqlParameter("@Date", date.Date));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ExchangeRate Map(SqlDataReader r) => new()
    {
        Id                   = r.GetInt32(0),
        CurrencyCode         = r.GetString(1),
        RateDate             = r.GetDateTime(2),
        BuyingRate           = r.GetDecimal(3),
        SellingRate          = r.GetDecimal(4),
        EffectiveBuyingRate  = r.GetDecimal(5),
        EffectiveSellingRate = r.GetDecimal(6),
        Source               = r.GetString(7),
        CreatedAt            = r.GetDateTime(8),
    };
}

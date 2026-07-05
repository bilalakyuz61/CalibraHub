using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — DecimalSetting CRUD. Per-company DB (SqlServerConnectionFactory tenant
/// routing) + ek CompanyId filtresi: şirket yalnızca kendi satırlarını görür.
/// </summary>
public sealed class SqlDecimalSettingRepository : IDecimalSettingRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlDecimalSettingRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[DecimalSetting]";
    }

    public async Task<IReadOnlyList<DecimalSetting>> GetAllAsync(int companyId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[FormCode],[QuantityDecimals],[UnitPriceDecimals],
                   [AmountDecimals],[RateDecimals],[ExchangeRateDecimals],[IsActive],
                   [CreatedById],[Created],[UpdatedById],[Updated],[FxUnitPriceDecimals]
            FROM {_table}
            WHERE [CompanyId]=@CompanyId AND [IsActive]=1
            ORDER BY [FormCode];
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        var list = new List<DecimalSetting>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list;
    }

    public async Task<DecimalSetting?> GetAsync(int companyId, string formCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[CompanyId],[FormCode],[QuantityDecimals],[UnitPriceDecimals],
                   [AmountDecimals],[RateDecimals],[ExchangeRateDecimals],[IsActive],
                   [CreatedById],[Created],[UpdatedById],[Updated],[FxUnitPriceDecimals]
            FROM {_table}
            WHERE [CompanyId]=@CompanyId AND [FormCode]=@FormCode AND [IsActive]=1;
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpsertAsync(DecimalSetting setting, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            MERGE {_table} AS t
            USING (SELECT @CompanyId AS CompanyId, @FormCode AS FormCode) AS src
                ON t.[CompanyId]=src.CompanyId AND t.[FormCode]=src.FormCode
            WHEN MATCHED THEN UPDATE SET
                [QuantityDecimals]=@Qty, [UnitPriceDecimals]=@Price, [FxUnitPriceDecimals]=@FxPrice,
                [AmountDecimals]=@Amount, [RateDecimals]=@Rate, [ExchangeRateDecimals]=@Fx, [IsActive]=1,
                [UpdatedById]=@UserId, [Updated]=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT
                ([CompanyId],[FormCode],[QuantityDecimals],[UnitPriceDecimals],[FxUnitPriceDecimals],
                 [AmountDecimals],[RateDecimals],[ExchangeRateDecimals],[IsActive],[CreatedById])
                VALUES (@CompanyId,@FormCode,@Qty,@Price,@FxPrice,@Amount,@Rate,@Fx,1,@UserId);
            """;
        cmd.Parameters.Add(new SqlParameter("@CompanyId", setting.CompanyId));
        cmd.Parameters.Add(new SqlParameter("@FormCode", setting.FormCode));
        cmd.Parameters.Add(new SqlParameter("@Qty", setting.QuantityDecimals));
        cmd.Parameters.Add(new SqlParameter("@Price", setting.UnitPriceDecimals));
        cmd.Parameters.Add(new SqlParameter("@FxPrice", setting.FxUnitPriceDecimals));
        cmd.Parameters.Add(new SqlParameter("@Amount", setting.AmountDecimals));
        cmd.Parameters.Add(new SqlParameter("@Rate", setting.RateDecimals));
        cmd.Parameters.Add(new SqlParameter("@Fx", setting.ExchangeRateDecimals));
        cmd.Parameters.Add(new SqlParameter("@UserId", (object?)(setting.UpdatedById ?? setting.CreatedById) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int companyId, string formCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [CompanyId]=@CompanyId AND [FormCode]=@FormCode;";
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DecimalSetting Map(SqlDataReader r) => new()
    {
        Id                   = r.GetInt32(0),
        CompanyId            = r.GetInt32(1),
        FormCode             = r.GetString(2),
        QuantityDecimals     = r.GetInt32(3),
        UnitPriceDecimals    = r.GetInt32(4),
        AmountDecimals       = r.GetInt32(5),
        RateDecimals         = r.GetInt32(6),
        ExchangeRateDecimals = r.GetInt32(7),
        IsActive             = r.GetBoolean(8),
        CreatedById          = r.IsDBNull(9) ? null : r.GetInt32(9),
        Created              = r.GetDateTime(10),
        UpdatedById          = r.IsDBNull(11) ? null : r.GetInt32(11),
        Updated              = r.IsDBNull(12) ? null : r.GetDateTime(12),
        FxUnitPriceDecimals  = r.GetInt32(13),
    };
}

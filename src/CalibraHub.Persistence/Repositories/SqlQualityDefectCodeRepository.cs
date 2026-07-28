using System.ComponentModel;
using System.Reflection;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>Hata kodu kataloğu veri erişimi (SqlActivityReasonRepository deseni). Per-company DB.</summary>
public sealed class SqlQualityDefectCodeRepository : IQualityDefectCodeRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlQualityDefectCodeRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema.Replace("]", "]]")}].[QualityDefectCode]";
    }

    public async Task<IReadOnlyList<QualityDefectCodeDto>> ListAsync(byte? category, bool includeInactive, CancellationToken ct)
    {
        var where = "1=1";
        if (!includeInactive) where += " AND [IsActive] = 1";
        if (category.HasValue) where += " AND [Category] = @Cat";
        var sql = $"""
            SELECT [Id],[Category],[Code],[Name],[Description],[ColorHex],[SortOrder],[IsActive]
            FROM {_table} WHERE {where} ORDER BY [Category], [SortOrder], [Name];
            """;
        var list = new List<QualityDefectCodeDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (category.HasValue) cmd.Parameters.Add(new SqlParameter("@Cat", category.Value));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Read(r));
        return list;
    }

    public async Task<QualityDefectCodeDto?> GetAsync(int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT [Id],[Category],[Code],[Name],[Description],[ColorHex],[SortOrder],[IsActive]
            FROM {_table} WHERE [Id] = @Id;
            """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Read(r) : null;
    }

    public async Task<int> SaveAsync(QualityDefectCode e, CancellationToken ct)
    {
        var sql = e.Id > 0
            ? $"""
                UPDATE {_table} SET [Category]=@Cat,[Code]=@Code,[Name]=@Name,[Description]=@Desc,
                    [ColorHex]=@Color,[SortOrder]=@Sort,[IsActive]=@Active,[UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                SELECT @Id;
                """
            : $"""
                INSERT INTO {_table} ([Category],[Code],[Name],[Description],[ColorHex],[SortOrder],[IsActive],[CreatedById],[Created])
                VALUES (@Cat,@Code,@Name,@Desc,@Color,@Sort,@Active,@Cre,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", e.Id));
        cmd.Parameters.Add(new SqlParameter("@Cat", (byte)e.Category));
        cmd.Parameters.Add(new SqlParameter("@Code", e.Code));
        cmd.Parameters.Add(new SqlParameter("@Name", e.Name));
        cmd.Parameters.Add(new SqlParameter("@Desc", (object?)e.Description ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Color", (object?)e.ColorHex ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Sort", e.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@Active", e.IsActive));
        cmd.Parameters.Add(new SqlParameter("@Cre", (object?)e.CreatedById ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)e.UpdatedById ?? DBNull.Value));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task SoftDeleteAsync(int id, int? userId, CancellationToken ct)
    {
        var sql = $"UPDATE {_table} SET [IsActive]=0,[UpdatedById]=@Upd,[Updated]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.Parameters.Add(new SqlParameter("@Upd", (object?)userId ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static QualityDefectCodeDto Read(SqlDataReader r)
    {
        var cat = r.GetByte(1);
        return new QualityDefectCodeDto(
            Id: r.GetInt32(0),
            Category: cat,
            CategoryLabel: Describe((DefectCategory)cat),
            Code: r.GetString(2),
            Name: r.GetString(3),
            Description: r.IsDBNull(4) ? null : r.GetString(4),
            ColorHex: r.IsDBNull(5) ? null : r.GetString(5),
            SortOrder: r.GetInt32(6),
            IsActive: r.GetBoolean(7));
    }

    internal static string Describe(Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString();
    }
}

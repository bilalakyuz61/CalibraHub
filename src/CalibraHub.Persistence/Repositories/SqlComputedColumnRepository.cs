using System.Diagnostics;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Hesaplanan Kolon deposu.
///
/// GÜVENLİK TEMELİ: view ve kolon adları sorguya ancak <see cref="ResolveAsync"/> ile
/// <c>sys.views</c> / <c>sys.columns</c>'a karşı doğrulandıktan SONRA girer — ve girerken de
/// kataloğun döndürdüğü GERÇEK ad kullanılır, kullanıcının yazdığı metin değil. Böylece
/// tanım tablosuna elle bozuk bir ad yazılsa bile sorguya geçemez.
/// </summary>
public sealed class SqlComputedColumnRepository : IComputedColumnRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _table;

    public SqlComputedColumnRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{_schema.Replace("]", "]]")}].[BoardComputedColumn]";
    }

    // ── CRUD ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ComputedColumnDto>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await ReadListAsync(conn, "ORDER BY [SortOrder], [Label]", null, ct);
    }

    public async Task<IReadOnlyList<ComputedColumnDto>> GetForBoardAsync(
        string entityKind, string boardKey, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var all = await ReadListAsync(conn,
            "WHERE [IsActive] = 1 AND [EntityKind] = @Entity ORDER BY [SortOrder], [Label]",
            cmd => cmd.Parameters.AddWithValue("@Entity", entityKind), ct);

        // BoardKeys boş = varlığın TÜM listeleri. Dolu olduğunda virgüllü listede aranır.
        return all.Where(c =>
            string.IsNullOrWhiteSpace(c.BoardKeys) ||
            c.BoardKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Any(k => string.Equals(k, boardKey, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<int> SaveAsync(SaveComputedColumnRequest r, int? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Label)) throw new ArgumentException("Başlık zorunludur.");

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // Kaydetmeden önce tanımlayıcılar DOĞRULANIR — bozuk tanım hiç kaydedilmesin.
        var resolved = await ResolveAsync(conn, r.ViewName, r.KeyColumn, r.ValueColumn, r.UnitColumn, ct);
        if (resolved.Error is not null) throw new ArgumentException(resolved.Error);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = r.Id > 0
            ? $@"UPDATE {_table} SET
                    [Label] = @Label, [EntityKind] = @Entity, [ViewName] = @View,
                    [KeyColumn] = @Key, [ValueColumn] = @Val, [UnitColumn] = @Unit,
                    [DataType] = @Type, [FormatJson] = @Fmt, [NullDisplay] = @Null,
                    [BoardKeys] = @Boards, [TimeoutSec] = @Timeout, [SortOrder] = @Sort,
                    [IsActive] = @Active, [UpdatedById] = @User, [Updated] = SYSUTCDATETIME()
                 WHERE [Id] = @Id;
                 SELECT @Id;"
            : $@"INSERT INTO {_table}
                    ([Label],[EntityKind],[ViewName],[KeyColumn],[ValueColumn],[UnitColumn],
                     [DataType],[FormatJson],[NullDisplay],[BoardKeys],[TimeoutSec],[SortOrder],
                     [IsActive],[CreatedById],[Created])
                 VALUES
                    (@Label,@Entity,@View,@Key,@Val,@Unit,@Type,@Fmt,@Null,@Boards,@Timeout,@Sort,
                     @Active,@User,SYSUTCDATETIME());
                 SELECT CAST(SCOPE_IDENTITY() AS INT);";

        cmd.Parameters.AddWithValue("@Id", r.Id);
        cmd.Parameters.AddWithValue("@Label", r.Label.Trim());
        cmd.Parameters.AddWithValue("@Entity", (r.EntityKind ?? "Item").Trim());
        // Kataloğun döndürdüğü gerçek adlar yazılır (kullanıcının yazdığı büyük/küçük harf değil).
        cmd.Parameters.AddWithValue("@View", resolved.ViewName!);
        cmd.Parameters.AddWithValue("@Key", resolved.KeyColumn!);
        cmd.Parameters.AddWithValue("@Val", resolved.ValueColumn!);
        cmd.Parameters.AddWithValue("@Unit", (object?)resolved.UnitColumn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Type", (r.DataType ?? "number").Trim());
        cmd.Parameters.AddWithValue("@Fmt", (object?)r.FormatJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Null", (r.NullDisplay ?? "dash").Trim());
        cmd.Parameters.AddWithValue("@Boards", (object?)r.BoardKeys ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Timeout", Math.Clamp(r.TimeoutSec <= 0 ? 3 : r.TimeoutSec, 1, 30));
        cmd.Parameters.AddWithValue("@Sort", r.SortOrder);
        cmd.Parameters.AddWithValue("@Active", r.IsActive);
        cmd.Parameters.AddWithValue("@User", (object?)userId ?? DBNull.Value);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Kaynak keşfi ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ComputedColumnSourceDto>> GetSourcesAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // YALNIZ view — tablo değil. Tablo seçilebilseydi filtreleme/toplama gerekirdi,
        // o da tanıma SQL sokmak demekti.
        cmd.CommandText = """
            SELECT v.[name] AS ViewName, c.[name] AS ColumnName, t.[name] AS SqlType
              FROM sys.views v
              JOIN sys.columns c ON c.object_id = v.object_id
              JOIN sys.types   t ON t.user_type_id = c.user_type_id
             WHERE v.is_ms_shipped = 0
             ORDER BY v.[name], c.column_id;
            """;

        var map = new Dictionary<string, List<ComputedColumnSourceFieldDto>>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var view = r.GetString(0);
            if (!map.TryGetValue(view, out var cols))
            {
                cols = new List<ComputedColumnSourceFieldDto>();
                map[view] = cols;
            }
            cols.Add(new ComputedColumnSourceFieldDto(r.GetString(1), r.GetString(2)));
        }
        return map.Select(kv => new ComputedColumnSourceDto(kv.Key, kv.Value)).ToList();
    }

    // ── Önizleme ────────────────────────────────────────────────────────────

    public async Task<ComputedColumnPreviewDto> PreviewAsync(
        SaveComputedColumnRequest r, int sampleSize, CancellationToken ct)
    {
        var take = Math.Clamp(sampleSize, 1, 20);
        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            var res = await ResolveAsync(conn, r.ViewName, r.KeyColumn, r.ValueColumn, r.UnitColumn, ct);
            if (res.Error is not null)
                return new ComputedColumnPreviewDto(false, res.Error, 0, Array.Empty<ComputedColumnPreviewRowDto>());

            var sw = Stopwatch.StartNew();
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = Math.Clamp(r.TimeoutSec <= 0 ? 3 : r.TimeoutSec, 1, 30);
            cmd.CommandText = $"""
                SELECT TOP (@Take) [{res.KeyColumn}] AS K, [{res.ValueColumn}] AS V
                       {(res.UnitColumn is null ? ", CAST(NULL AS NVARCHAR(40)) AS U" : $", [{res.UnitColumn}] AS U")}
                  FROM [{res.Schema}].[{res.ViewName}];
                """;
            cmd.Parameters.AddWithValue("@Take", take);

            var rows = new List<ComputedColumnPreviewRowDto>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                rows.Add(new ComputedColumnPreviewRowDto(
                    rd.IsDBNull(0) ? "" : rd.GetValue(0)?.ToString() ?? "",
                    rd.IsDBNull(1) ? null : rd.GetValue(1)?.ToString(),
                    rd.IsDBNull(2) ? null : rd.GetValue(2)?.ToString()));
            }
            sw.Stop();
            return new ComputedColumnPreviewDto(true, null, (int)sw.ElapsedMilliseconds, rows);
        }
        catch (Exception ex)
        {
            // Önizlemenin görevi hatayı GÖSTERMEK — yutmak değil. Zaman aşımı da buraya düşer
            // ve kullanıcı view'ının yavaş olduğunu kaydetmeden önce öğrenir.
            return new ComputedColumnPreviewDto(false, ex.Message, 0, Array.Empty<ComputedColumnPreviewRowDto>());
        }
    }

    // ── Değer okuma (liste sayfası) ─────────────────────────────────────────

    public async Task<(IReadOnlyDictionary<int, ComputedCellValue> Values, string? Error)> ReadValuesAsync(
        ComputedColumnDto column, IReadOnlyCollection<int> keys, CancellationToken ct)
    {
        var empty = (IReadOnlyDictionary<int, ComputedCellValue>)new Dictionary<int, ComputedCellValue>();
        if (keys.Count == 0) return (empty, null);

        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            var res = await ResolveAsync(conn, column.ViewName, column.KeyColumn, column.ValueColumn, column.UnitColumn, ct);
            if (res.Error is not null) return (empty, res.Error);

            // Anahtarlar SAYFADAKİ satırlarla sınırlı — tüm view'ı taramak yerine IN listesi.
            // Değerler parametreli gider; kimlik numaraları da olsa string birleştirme yapılmaz.
            var paramNames = keys.Select((_, i) => "@k" + i).ToList();
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = Math.Clamp(column.TimeoutSec <= 0 ? 3 : column.TimeoutSec, 1, 30);
            cmd.CommandText = $"""
                SELECT [{res.KeyColumn}] AS K, [{res.ValueColumn}] AS V
                       {(res.UnitColumn is null ? ", CAST(NULL AS NVARCHAR(40)) AS U" : $", [{res.UnitColumn}] AS U")}
                  FROM [{res.Schema}].[{res.ViewName}]
                 WHERE [{res.KeyColumn}] IN ({string.Join(",", paramNames)});
                """;
            var i2 = 0;
            foreach (var k in keys) cmd.Parameters.AddWithValue(paramNames[i2++], k);

            var map = new Dictionary<int, ComputedCellValue>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                if (rd.IsDBNull(0)) continue;
                if (!int.TryParse(rd.GetValue(0)?.ToString(), out var key)) continue;
                map[key] = new ComputedCellValue(
                    rd.IsDBNull(1) ? null : rd.GetValue(1)?.ToString(),
                    rd.IsDBNull(2) ? null : rd.GetValue(2)?.ToString());
            }
            return (map, null);
        }
        catch (Exception ex)
        {
            // Bozuk/yavaş tanım YALNIZ kendi kolonunu düşürür. Exception yukarı taşınsaydı
            // tek bir kolon yüzünden tüm liste ekranı boş dönerdi.
            return (empty, ex.Message);
        }
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<ComputedColumnDto>> ReadListAsync(
        SqlConnection conn, string tail, Action<SqlCommand>? bind, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Label],[EntityKind],[ViewName],[KeyColumn],[ValueColumn],[UnitColumn],
                   [DataType],[FormatJson],[NullDisplay],[BoardKeys],[TimeoutSec],[SortOrder],[IsActive]
              FROM {_table} {tail};
            """;
        bind?.Invoke(cmd);

        var list = new List<ComputedColumnDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ComputedColumnDto(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8), r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                r.GetInt32(11), r.GetInt32(12), r.GetBoolean(13)));
        }
        return list;
    }

    /// <summary>
    /// View ve kolon adlarını <c>sys</c> kataloğuna karşı çözer. Dönen adlar KATALOĞUN
    /// yazdığı adlardır — çağıran onları sorguya koyar, kullanıcının girdiği metni değil.
    /// Bulunamayan ad sorguya HİÇ girmez; hata metniyle geri döner.
    /// </summary>
    private async Task<(string? Schema, string? ViewName, string? KeyColumn, string? ValueColumn, string? UnitColumn, string? Error)>
        ResolveAsync(SqlConnection conn, string? view, string? key, string? val, string? unit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(view)) return (null, null, null, null, null, "Kaynak view seçilmedi.");
        if (string.IsNullOrWhiteSpace(key)) return (null, null, null, null, null, "Anahtar kolon seçilmedi.");
        if (string.IsNullOrWhiteSpace(val)) return (null, null, null, null, null, "Değer kolonu seçilmedi.");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.[name] AS SchemaName, v.[name] AS ViewName, c.[name] AS ColumnName
              FROM sys.views v
              JOIN sys.schemas s ON s.schema_id = v.schema_id
              JOIN sys.columns c ON c.object_id = v.object_id
             WHERE v.is_ms_shipped = 0 AND v.[name] = @View;
            """;
        cmd.Parameters.AddWithValue("@View", view.Trim());

        string? schemaName = null, viewName = null;
        var cols = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                schemaName ??= r.GetString(0);
                viewName ??= r.GetString(1);
                cols.Add(r.GetString(2));
            }
        }
        if (viewName is null)
            return (null, null, null, null, null, $"View bulunamadı: '{view}'. Silinmiş ya da yeniden adlandırılmış olabilir.");

        string? Match(string? name) => name is null
            ? null
            : cols.FirstOrDefault(c => string.Equals(c, name.Trim(), StringComparison.OrdinalIgnoreCase));

        var k = Match(key);
        if (k is null) return (null, null, null, null, null, $"'{viewName}' view'ında '{key}' kolonu yok.");
        var v = Match(val);
        if (v is null) return (null, null, null, null, null, $"'{viewName}' view'ında '{val}' kolonu yok.");

        string? u = null;
        if (!string.IsNullOrWhiteSpace(unit))
        {
            u = Match(unit);
            if (u is null) return (null, null, null, null, null, $"'{viewName}' view'ında '{unit}' birim kolonu yok.");
        }

        return (schemaName, viewName, k, v, u, null);
    }
}

using System.Text.RegularExpressions;
using CalibraHub.Persistence.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// DatabaseMetadataController — fiziksel tablo ve kolon listeleme API.
///
/// Endpoint'ler:
///   GET /api/database/tables                        → Şemadaki fiziksel tabloları listele
///   GET /api/database/tables/{tableName}/columns    → Tablonun kolon adlarını listele
///   GET /api/database/map                           → Tablo ilişki grafiği (Veritabanı Haritası)
///
/// SQL Injection koruması: tableName parametresi identifier regex'ten geçer.
/// </summary>
[Authorize]
[ApiController]
[Route("api/database")]
[IgnoreAntiforgeryToken]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.SetupDefinitions)]
public sealed class DatabaseMetadataController : ControllerBase
{
    private static readonly Regex IdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseMetadataController> _logger;

    public DatabaseMetadataController(
        SqlServerConnectionFactory connectionFactory,
        ILogger<DatabaseMetadataController> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Veritabanı Haritası grafiği: tablolar (düğüm) + aralarındaki ilişkiler (kenar).
    ///
    /// İki KENAR TÜRÜ döner ve ikisi AYRI işaretlenir — çünkü güvenilirlikleri farklı:
    ///   · "fk"       → gerçek FOREIGN KEY kısıtı (sys.foreign_keys). Kesin bilgi.
    ///   · "inferred" → kısıt YOK, ilişki kolon adından çıkarıldı ("ItemId" → "Item").
    ///
    /// Çıkarım BİLEREK dar tutuldu: yalnız "<Tablo>Id" veya "<Tablo>sId" birebir eşleşmesi
    /// kabul edilir. "ParentDocumentId" gibi önek alan kolonlar TAHMİN EDİLMEZ — uydurma bir
    /// ilişki çizmek, eksik çizmekten kötüdür; kullanıcı haritaya bakıp yanlış sonuç çıkarır.
    /// Eşleşmeyen "*Id" kolonlarının sayısı <c>unmatchedIdColumns</c> ile AÇIKÇA bildirilir:
    /// harita "her ilişkiyi gösteriyorum" iddiasında bulunmaz.
    ///
    /// Düğüm ölçüleri gerçek veriden gelir (uydurma modül/grup yok):
    ///   · rowCount    → tablo büyüklüğü (sys.dm_db_partition_stats; yaklaşık, kilitsiz)
    ///   · columnCount → alan sayısı
    ///   · degree      → bağlantı sayısı (istemcide hesaplanır; merkez/uydu ayrımı buna göre)
    /// </summary>
    [HttpGet("map")]
    public async Task<IActionResult> GetMap(CancellationToken ct)
    {
        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

            // ── 1) Tablolar ──────────────────────────────────────────────────
            var tables = new List<TableNode>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT  t.[name] AS TableName,
                            (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount,
                            ISNULL((SELECT SUM(p.[rows]) FROM sys.partitions p
                                     WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)), 0) AS RowCount
                      FROM sys.tables t
                     WHERE t.is_ms_shipped = 0
                       AND t.[name] NOT IN ('__EFMigrationsHistory', 'sysdiagrams')
                     ORDER BY t.[name]
                    """;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    tables.Add(new TableNode(
                        reader.GetString(0),
                        reader.GetInt32(1),
                        reader.GetInt64(2)));
                }
            }

            var tableByName = tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

            // ── 2) Gerçek FK kenarları ───────────────────────────────────────
            var edges = new List<EdgeDto>();
            var fkColumnKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT  OBJECT_NAME(fk.parent_object_id)     AS FromTable,
                            pc.[name]                            AS FromColumn,
                            OBJECT_NAME(fk.referenced_object_id) AS ToTable,
                            rc.[name]                            AS ToColumn,
                            fk.[name]                            AS ConstraintName
                      FROM sys.foreign_keys fk
                      JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                      JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id
                                         AND pc.column_id = fkc.parent_column_id
                      JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id
                                         AND rc.column_id = fkc.referenced_column_id
                     ORDER BY FromTable, ToTable
                    """;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var fromTable = reader.GetString(0);
                    var fromColumn = reader.GetString(1);
                    edges.Add(new EdgeDto(fromTable, fromColumn, reader.GetString(2), reader.GetString(3), "fk", reader.GetString(4)));
                    fkColumnKeys.Add(fromTable + "." + fromColumn);
                }
            }

            // ── 3) Kısıtı olmayan "*Id" kolonlarından çıkarım ────────────────
            var unmatched = 0;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT  t.[name] AS TableName, c.[name] AS ColumnName
                      FROM sys.columns c
                      JOIN sys.tables  t ON t.object_id = c.object_id
                     WHERE t.is_ms_shipped = 0
                       AND c.[name] LIKE '%Id'
                       AND c.[name] <> 'Id'
                     ORDER BY t.[name], c.[name]
                    """;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var tableName = reader.GetString(0);
                    var columnName = reader.GetString(1);
                    if (fkColumnKeys.Contains(tableName + "." + columnName)) continue;   // zaten gerçek FK

                    var target = columnName[..^2];                                        // "ItemId" → "Item"
                    if (target.Length == 0) continue;

                    string? hit = null;
                    if (tableByName.TryGetValue(target, out var direct)) hit = direct.Name;
                    else if (tableByName.TryGetValue(target + "s", out var plural)) hit = plural.Name;

                    if (hit is null) { unmatched++; continue; }
                    edges.Add(new EdgeDto(tableName, columnName, hit, "Id", "inferred", null));
                }
            }

            return Ok(new
            {
                ok = true,
                tables = tables.Select(t => new
                {
                    name = t.Name,
                    columnCount = t.ColumnCount,
                    rowCount = t.RowCount,
                }),
                edges = edges.Select(e => new
                {
                    from = e.FromTable,
                    fromColumn = e.FromColumn,
                    to = e.ToTable,
                    toColumn = e.ToColumn,
                    kind = e.Kind,
                    name = e.ConstraintName,
                }),
                // Haritanın kapsamı hakkında dürüst özet — istemci bunu ekranda gösterir.
                summary = new
                {
                    tableCount = tables.Count,
                    fkCount = edges.Count(e => e.Kind == "fk"),
                    inferredCount = edges.Count(e => e.Kind == "inferred"),
                    unmatchedIdColumns = unmatched,
                },
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Veritabanı haritası oluşturulurken hata");
            return StatusCode(500, new { ok = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    private sealed record TableNode(string Name, int ColumnCount, long RowCount);

    private sealed record EdgeDto(
        string FromTable, string FromColumn, string ToTable, string ToColumn,
        string Kind, string? ConstraintName);

    // GET /api/database/views
    // Şemadaki view'ları döner.
    [HttpGet("views")]
    public async Task<IActionResult> GetViews(CancellationToken ct)
    {
        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT TABLE_SCHEMA, TABLE_NAME
                  FROM INFORMATION_SCHEMA.TABLES
                 WHERE TABLE_TYPE = 'VIEW'
                   AND TABLE_SCHEMA NOT IN ('sys')
                 ORDER BY TABLE_SCHEMA, TABLE_NAME
                """;
            var views = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                views.Add(new
                {
                    schema   = reader.GetString(0),
                    name     = reader.GetString(1),
                    fullName = reader.GetString(0) + "." + reader.GetString(1),
                });
            }
            return Ok(views);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "View listesi alınırken hata");
            return StatusCode(500, new { success = false, message = "View listesi alınamadı: " + "İşlem sırasında bir hata oluştu." });
        }
    }

    // GET /api/database/tables
    // Şemadaki fiziksel tabloları döner (sistem tabloları ve view'lar hariç).
    [HttpGet("tables")]
    public async Task<IActionResult> GetTables(CancellationToken ct)
    {
        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT TABLE_SCHEMA, TABLE_NAME
                  FROM INFORMATION_SCHEMA.TABLES
                 WHERE TABLE_TYPE = 'BASE TABLE'
                   AND TABLE_NAME NOT IN ('__EFMigrationsHistory', 'sysdiagrams')
                   AND TABLE_SCHEMA NOT IN ('sys')
                 ORDER BY TABLE_SCHEMA, TABLE_NAME
                """;

            var tables = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tables.Add(new
                {
                    schema = reader.GetString(0),
                    tableName = reader.GetString(1),
                    fullName = reader.GetString(0) + "." + reader.GetString(1)
                });
            }
            return Ok(tables);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tablo listesi alınırken hata");
            return StatusCode(500, new { success = false, message = "Tablo listesi alınamadı: " + "İşlem sırasında bir hata oluştu." });
        }
    }

    // GET /api/database/tables/{tableName}/columns
    // Belirtilen tablonun kolon adlarını döner.
    // tableName parametresi identifier regex'ten geçmeli.
    [HttpGet("tables/{tableName}/columns")]
    public async Task<IActionResult> GetColumns(string tableName, CancellationToken ct)
    {
        // tableName "schema.table" veya "table" formatında gelebilir.
        // Her parçayı ayrı ayrı doğrula.
        var parts = tableName.Split('.');
        foreach (var part in parts)
        {
            if (!IdentifierRegex.IsMatch(part))
            {
                return BadRequest(new { success = false, message = "Geçersiz tablo adı formatı." });
            }
        }

        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();

            if (parts.Length == 2)
            {
                cmd.CommandText = """
                    SELECT COLUMN_NAME
                      FROM INFORMATION_SCHEMA.COLUMNS
                     WHERE TABLE_SCHEMA = @Schema
                       AND TABLE_NAME   = @TableName
                     ORDER BY ORDINAL_POSITION
                    """;
                cmd.Parameters.Add(new SqlParameter("@Schema", parts[0]));
                cmd.Parameters.Add(new SqlParameter("@TableName", parts[1]));
            }
            else
            {
                cmd.CommandText = """
                    SELECT COLUMN_NAME
                      FROM INFORMATION_SCHEMA.COLUMNS
                     WHERE TABLE_NAME = @TableName
                     ORDER BY ORDINAL_POSITION
                    """;
                cmd.Parameters.Add(new SqlParameter("@TableName", parts[0]));
            }

            var columns = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                columns.Add(reader.GetString(0));
            }
            return Ok(columns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kolon listesi alınırken hata: {TableName}", tableName);
            return StatusCode(500, new { success = false, message = "Kolon listesi alınamadı: " + "İşlem sırasında bir hata oluştu." });
        }
    }
}

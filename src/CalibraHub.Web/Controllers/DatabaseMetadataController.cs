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

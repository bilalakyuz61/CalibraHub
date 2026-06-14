using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models.Diagnostics;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Services;

/// <summary>
/// 2026-05-26 — Health Check "schema probe" (Aşamalı A+C yaklaşımı, Faz 1).
/// Bir ekrana karşılık gelen ana tablo için INSERT...ROLLBACK testi yapar.
/// Sadece şema uyumunu test eder (kolon adları, type'lar, NOT NULL constraint'leri):
///   - SQL hata vermezse 'ok'
///   - SqlException 207 (Invalid column name), 208 (Invalid object name), 515 (NULL into NOT NULL) → 'error'
///   - Diğer exception → 'exception'
///
/// Veri kalıntı bırakmaz: BEGIN TRAN __hcp ... ROLLBACK her durumda devreye girer.
/// Service/controller mantığını test ETMEZ — sadece şema seviyesinde probe.
/// İlerleyen aşamada (Faz 2) gerçek save+delete E2E testi de eklenebilir.
/// </summary>
public sealed class SchemaProbeService
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _schema;

    public SchemaProbeService(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema;
    }

    public async Task<SchemaProbeResult> ProbeAsync(SchemaProbeDefinition def, CancellationToken ct)
    {
        var schema = _schema.Replace("]", "]]");
        var cols = string.Join(", ", def.Columns.Select(c => $"[{c.Column}]"));
        var vals = string.Join(", ", def.Columns.Select(c => c.SqlValue));

        var sql = $@"
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRAN __hcp;
    INSERT INTO [{schema}].[{def.Table}] ({cols}) VALUES ({vals});
    IF @@TRANCOUNT > 0 ROLLBACK TRAN __hcp;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN __hcp;
    THROW;
END CATCH;";

        try
        {
            await using var conn = await _factory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 10;
            await cmd.ExecuteNonQueryAsync(ct);
            return new SchemaProbeResult
            {
                Status = "ok",
                Table = def.Table,
            };
        }
        catch (SqlException ex)
        {
            return new SchemaProbeResult
            {
                Status = "error",
                Table = def.Table,
                ErrorNumber = ex.Number,
                ErrorMessage = TrimMessage(ex.Message),
            };
        }
        catch (Exception ex)
        {
            return new SchemaProbeResult
            {
                Status = "exception",
                Table = def.Table,
                ErrorMessage = TrimMessage(ex.Message),
            };
        }
    }

    private static string TrimMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return string.Empty;
        msg = msg.Replace("\r", " ").Replace("\n", " ").Trim();
        return msg.Length > 250 ? msg.Substring(0, 250) + "..." : msg;
    }
}

public sealed class SchemaProbeResult
{
    public string Status { get; set; } = "skip";    // ok / error / exception / skip
    public string Table { get; set; } = "";
    public int? ErrorNumber { get; set; }
    public string? ErrorMessage { get; set; }
}

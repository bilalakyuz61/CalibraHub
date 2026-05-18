using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// IIntegrationQueueService implementasyonu.
///
/// Strateji:
///   1. Integration → SourceFormCode → Form.BaseTable + Form.BaseRecordKey
///   2. Base table'da hangi display kolonlari var? (INFORMATION_SCHEMA ile dinamik kesif)
///      - Code (varsa) → DisplayCode
///      - Name (varsa) → DisplayName
///      - Yoksa Id'yi her ikisine de koy.
///   3. SELECT base_table LEFT JOIN IntegrationRecordStatus ON RecordId = id
///      WHERE filter ORDER BY id DESC OFFSET/FETCH
///
/// Per-company DB (SqlServerConnectionFactory). Salt-okunur (yazimlar repo'da).
/// </summary>
public sealed class SqlIntegrationQueueService : IIntegrationQueueService
{
    private static readonly Regex IdentRegex =
        new("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlIntegrationQueueService(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string S => _schema.Replace("]", "]]");

    // ── 1) Sol menu — Manual tetikleyici olan aktif entegrasyonlar ──────────
    public async Task<IReadOnlyList<QueueIntegrationDto>> ListManualIntegrationsAsync(CancellationToken ct)
    {
        var list = new List<QueueIntegrationDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT i.[Id], i.[Name], i.[SourceFormCode],
                   f.[FormName], f.[Icon], f.[IconColor],
                   ISNULL(SUM(CASE WHEN s.[Status]='Pending' THEN 1 ELSE 0 END),0) AS PendingCnt,
                   ISNULL(SUM(CASE WHEN s.[Status]='Failed'  THEN 1 ELSE 0 END),0) AS FailedCnt,
                   ISNULL(SUM(CASE WHEN s.[Status]='Skipped' THEN 1 ELSE 0 END),0) AS SkippedCnt
            FROM [{S}].[Integration] i
            INNER JOIN [{S}].[IntegrationTrigger] t
                    ON t.[IntegrationId] = i.[Id]
                   AND t.[TriggerType]   = 'Manual'
                   AND t.[IsActive]      = 1
            LEFT JOIN [{S}].[Forms] f
                   ON f.[FormCode] = i.[SourceFormCode]
            LEFT JOIN [{S}].[IntegrationRecordStatus] s
                   ON s.[IntegrationId] = i.[Id]
                  AND s.[IsActive]      = 1
            WHERE i.[IsActive] = 1
            GROUP BY i.[Id], i.[Name], i.[SourceFormCode], f.[FormName], f.[Icon], f.[IconColor]
            ORDER BY i.[Name];
            """;
        try
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new QueueIntegrationDto(
                    IntegrationId: r.GetInt32(0),
                    Name:          r.GetString(1),
                    FormCode:      r.IsDBNull(2) ? "" : r.GetString(2),
                    FormName:      r.IsDBNull(3) ? null : r.GetString(3),
                    Icon:          r.IsDBNull(4) ? null : r.GetString(4),
                    IconColor:     r.IsDBNull(5) ? null : r.GetString(5),
                    PendingCount:  r.GetInt32(6),
                    FailedCount:   r.GetInt32(7),
                    SkippedCount:  r.GetInt32(8)));
            }
        }
        catch (SqlException ex) when (ex.Number is 208 or 207)
        {
            // Tablo veya kolon yok — bos liste don (V1 setup, henuz hic entegrasyon yok)
            return Array.Empty<QueueIntegrationDto>();
        }
        return list;
    }

    // ── 2) Kuyruk satirlari ─────────────────────────────────────────────────
    public async Task<QueueListResult> ListAsync(
        int integrationId,
        QueueFilter filter,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page     < 1)   page     = 1;
        if (pageSize < 1)   pageSize = 25;
        if (pageSize > 200) pageSize = 200;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 2a) Integration → SourceFormCode → Form.BaseTable / BaseRecordKey
        string? sourceFormCode = null;
        await using (var integCmd = conn.CreateCommand())
        {
            integCmd.CommandText = $"SELECT [SourceFormCode] FROM [{S}].[Integration] WHERE [Id] = @Id;";
            integCmd.Parameters.Add(new SqlParameter("@Id", integrationId));
            var v = await integCmd.ExecuteScalarAsync(ct);
            sourceFormCode = v as string;
        }
        if (string.IsNullOrWhiteSpace(sourceFormCode))
            return EmptyResult();

        string? baseTable = null, baseRecordKey = null;
        await using (var formCmd = conn.CreateCommand())
        {
            formCmd.CommandText = $"SELECT [BaseTable], [BaseRecordKey] FROM [{S}].[Forms] WHERE [FormCode] = @Code;";
            formCmd.Parameters.Add(new SqlParameter("@Code", sourceFormCode));
            await using var r = await formCmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                baseTable     = r.IsDBNull(0) ? null : r.GetString(0);
                baseRecordKey = r.IsDBNull(1) ? null : r.GetString(1);
            }
        }
        if (string.IsNullOrWhiteSpace(baseTable) || string.IsNullOrWhiteSpace(baseRecordKey))
            return EmptyResult();
        if (!IdentRegex.IsMatch(baseTable) || !IdentRegex.IsMatch(baseRecordKey))
            return EmptyResult();

        // 2b) Base table'da Code / Name kolonlari var mi? (INFORMATION_SCHEMA)
        var hasCode = await ColumnExistsAsync(conn, baseTable, "Code", ct)
                      || await ColumnExistsAsync(conn, baseTable, "code", ct);
        var hasName = await ColumnExistsAsync(conn, baseTable, "Name", ct)
                      || await ColumnExistsAsync(conn, baseTable, "name", ct);
        // Case-sensitive isim yakala (gercek kolon adi):
        var codeCol = hasCode ? await ResolveColumnNameAsync(conn, baseTable, "Code", ct) : null;
        var nameCol = hasName ? await ResolveColumnNameAsync(conn, baseTable, "Name", ct) : null;

        var selectCode = codeCol is null ? "NULL" : $"b.[{codeCol}]";
        var selectName = nameCol is null ? "NULL" : $"b.[{nameCol}]";

        // 2c) Status filter SQL
        string statusWhere = filter switch
        {
            QueueFilter.Active  => "(s.[Status] IS NULL OR s.[Status] IN ('Pending','Failed'))",
            QueueFilter.Pending => "(s.[Status] IS NULL OR s.[Status] = 'Pending')",
            QueueFilter.Failed  => "s.[Status] = 'Failed'",
            QueueFilter.Sent    => "s.[Status] = 'Sent'",
            QueueFilter.Skipped => "s.[Status] = 'Skipped'",
            QueueFilter.All     => "1 = 1",
            _                   => "(s.[Status] IS NULL OR s.[Status] IN ('Pending','Failed'))",
        };

        // 2d) Search filter — code/name LIKE
        string searchWhere = "1 = 1";
        if (!string.IsNullOrWhiteSpace(search) && (hasCode || hasName))
        {
            var bits = new List<string>();
            if (hasCode) bits.Add($"b.[{codeCol}] LIKE @Search");
            if (hasName) bits.Add($"b.[{nameCol}] LIKE @Search");
            searchWhere = "(" + string.Join(" OR ", bits) + ")";
        }

        // 2e) Toplam say + sayfali satirlar (UNION'lu count tek tur trip — basitlik icin 2 sorgu)
        int totalCount = 0;
        var rows = new List<QueueRowDto>();

        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"""
                SELECT COUNT(*)
                FROM [{S}].[{baseTable}] b
                LEFT JOIN [{S}].[IntegrationRecordStatus] s
                       ON s.[IntegrationId] = @IntegrationId
                      AND s.[RecordId]      = CAST(b.[{baseRecordKey}] AS NVARCHAR(100))
                      AND s.[IsActive]      = 1
                WHERE {statusWhere} AND {searchWhere};
                """;
            countCmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
            if (!string.IsNullOrWhiteSpace(search))
                countCmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
            totalCount = (int)(await countCmd.ExecuteScalarAsync(ct) ?? 0);
        }

        await using (var listCmd = conn.CreateCommand())
        {
            listCmd.CommandText = $"""
                SELECT CAST(b.[{baseRecordKey}] AS NVARCHAR(100)) AS RecordId,
                       {selectCode} AS DisplayCode,
                       {selectName} AS DisplayName,
                       ISNULL(s.[Status], 'Pending')       AS [Status],
                       s.[LastSentAt],
                       s.[LastError],
                       ISNULL(s.[AttemptCount], 0)         AS AttemptCount,
                       s.[LastRunId],
                       s.[SkippedBy],
                       s.[SkipReason],
                       s.[SkippedAt]
                FROM [{S}].[{baseTable}] b
                LEFT JOIN [{S}].[IntegrationRecordStatus] s
                       ON s.[IntegrationId] = @IntegrationId
                      AND s.[RecordId]      = CAST(b.[{baseRecordKey}] AS NVARCHAR(100))
                      AND s.[IsActive]      = 1
                WHERE {statusWhere} AND {searchWhere}
                ORDER BY b.[{baseRecordKey}] DESC
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
                """;
            listCmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
            listCmd.Parameters.Add(new SqlParameter("@Skip", (page - 1) * pageSize));
            listCmd.Parameters.Add(new SqlParameter("@Take", pageSize));
            if (!string.IsNullOrWhiteSpace(search))
                listCmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));

            await using var r = await listCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add(new QueueRowDto(
                    RecordId:     r.GetString(0),
                    Code:         r.IsDBNull(1) ? null : Convert.ToString(r.GetValue(1)),
                    Name:         r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2)),
                    Status:       r.GetString(3),
                    LastSentAt:   r.IsDBNull(4) ? null : r.GetDateTime(4),
                    LastError:    r.IsDBNull(5) ? null : r.GetString(5),
                    AttemptCount: r.GetInt32(6),
                    LastRunId:    r.IsDBNull(7) ? null : r.GetInt64(7),
                    SkippedBy:    r.IsDBNull(8) ? null : r.GetString(8),
                    SkipReason:   r.IsDBNull(9) ? null : r.GetString(9),
                    SkippedAt:    r.IsDBNull(10) ? null : r.GetDateTime(10)));
            }
        }

        // 2f) Toplam status sayilari (sayfa baginsiz)
        int pending = 0, failed = 0, sent = 0, skipped = 0;
        await using (var sumCmd = conn.CreateCommand())
        {
            sumCmd.CommandText = $"""
                SELECT [Status], COUNT(*) AS Cnt
                FROM [{S}].[IntegrationRecordStatus]
                WHERE [IntegrationId] = @IntegrationId AND [IsActive] = 1
                GROUP BY [Status];
                """;
            sumCmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
            await using var r = await sumCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var st  = r.GetString(0);
                var cnt = r.GetInt32(1);
                switch (st)
                {
                    case "Pending": pending = cnt; break;
                    case "Failed":  failed  = cnt; break;
                    case "Sent":    sent    = cnt; break;
                    case "Skipped": skipped = cnt; break;
                }
            }
        }

        return new QueueListResult(rows, totalCount, pending, failed, sent, skipped);
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static QueueListResult EmptyResult() =>
        new(Array.Empty<QueueRowDto>(), 0, 0, 0, 0, 0);

    private async Task<bool> ColumnExistsAsync(SqlConnection conn, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table AND COLUMN_NAME = @Col;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", _schema));
        cmd.Parameters.Add(new SqlParameter("@Table", table));
        cmd.Parameters.Add(new SqlParameter("@Col", column));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private async Task<string?> ResolveColumnNameAsync(SqlConnection conn, string table, string columnLike, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table AND COLUMN_NAME = @Col;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", _schema));
        cmd.Parameters.Add(new SqlParameter("@Table", table));
        cmd.Parameters.Add(new SqlParameter("@Col", columnLike));
        return (await cmd.ExecuteScalarAsync(ct)) as string;
    }
}

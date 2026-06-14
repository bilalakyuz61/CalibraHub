using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services.Integration;
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
    private readonly IntegrationFilterEngine _filterEngine;

    public SqlIntegrationQueueService(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IntegrationFilterEngine filterEngine)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _filterEngine = filterEngine;
    }

    private string S => _schema.Replace("]", "]]");

    // ── 1) Sol menu — Manual tetikleyici olan aktif entegrasyonlar ──────────
    public async Task<IReadOnlyList<QueueIntegrationDto>> ListManualIntegrationsAsync(CancellationToken ct)
    {
        // 2026-05-21: Iki adim — once entegrasyonlari listele, sonra her biri icin
        // base table COUNT al ve Pending = base_count - (failed+sent+skipped) hesapla.
        // Tek-sorgu SUM yaklasimi yanlis sayardi cunku henuz IRS'ye girmemis kayitlar
        // (gercek bekleyenler) tabloda yok.
        var list = new List<QueueIntegrationDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // ── A) Adim 1: entegrasyonlar + IRS-bazli sayilar (Failed/Sent/Skipped + Pending-in-IRS)
        var rows = new List<(int Id, string Name, string FormCode, string? FormName, string? Icon, string? IconColor,
                             int PendingIrs, int Failed, int Sent, int Skipped)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT i.[Id], i.[Name], i.[SourceFormCode],
                       f.[FormName], f.[Icon], f.[IconColor],
                       ISNULL(SUM(CASE WHEN s.[Status]='Pending' THEN 1 ELSE 0 END),0) AS PendingIrs,
                       ISNULL(SUM(CASE WHEN s.[Status]='Failed'  THEN 1 ELSE 0 END),0) AS FailedCnt,
                       ISNULL(SUM(CASE WHEN s.[Status]='Sent'    THEN 1 ELSE 0 END),0) AS SentCnt,
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
                    rows.Add((
                        r.GetInt32(0),
                        r.GetString(1),
                        r.IsDBNull(2) ? "" : r.GetString(2),
                        r.IsDBNull(3) ? null : r.GetString(3),
                        r.IsDBNull(4) ? null : r.GetString(4),
                        r.IsDBNull(5) ? null : r.GetString(5),
                        r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9)));
                }
            }
            catch (SqlException ex) when (ex.Number is 208 or 207)
            {
                return Array.Empty<QueueIntegrationDto>();
            }
        }

        // ── B) Adim 2: her entegrasyon icin base table COUNT (BaseTableFilter ile)
        // Form basina cache yaparak ayni base table'i tekrar saymaktan kacin.
        var baseTotalByForm = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.FormCode)) { list.Add(BuildDto(row, irsPending: row.PendingIrs)); continue; }
            int baseTotal = await ResolveBaseTableCountAsync(conn, row.FormCode, baseTotalByForm, ct);
            int implicitPending = Math.Max(0, baseTotal - row.Failed - row.Sent - row.Skipped);
            list.Add(BuildDto(row, irsPending: implicitPending));
        }
        return list;

        static QueueIntegrationDto BuildDto(
            (int Id, string Name, string FormCode, string? FormName, string? Icon, string? IconColor,
             int PendingIrs, int Failed, int Sent, int Skipped) r,
            int irsPending)
            => new(
                IntegrationId: r.Id,
                Name:          r.Name,
                FormCode:      r.FormCode,
                FormName:      r.FormName,
                Icon:          r.Icon,
                IconColor:     r.IconColor,
                PendingCount:  irsPending,
                FailedCount:   r.Failed,
                SkippedCount:  r.Skipped);
    }

    /// <summary>
    /// FormCode → base table COUNT (BaseTableFilter dikkate alinir). Cache key = formCode.
    /// </summary>
    private async Task<int> ResolveBaseTableCountAsync(SqlConnection conn, string formCode,
        Dictionary<string, int> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(formCode, out var cached)) return cached;

        string? baseTable = null, baseRecordKey = null, baseTableFilter = null;
        await using (var formCmd = conn.CreateCommand())
        {
            formCmd.CommandText = $"""
                SELECT [BaseTable],
                       [BaseRecordKey],
                       CASE WHEN COL_LENGTH(N'[{S}].[Forms]', N'BaseTableFilter') IS NULL
                            THEN NULL ELSE [BaseTableFilter] END AS BaseTableFilter
                FROM [{S}].[Forms] WHERE [FormCode] = @Code;
                """;
            formCmd.Parameters.Add(new SqlParameter("@Code", formCode));
            try
            {
                await using var r = await formCmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    baseTable       = r.IsDBNull(0) ? null : r.GetString(0);
                    baseRecordKey   = r.IsDBNull(1) ? null : r.GetString(1);
                    baseTableFilter = r.IsDBNull(2) ? null : r.GetString(2);
                }
            }
            catch (SqlException) { cache[formCode] = 0; return 0; }
        }
        if (string.IsNullOrWhiteSpace(baseTable)) { cache[formCode] = 0; return 0; }

        var rawTable = baseTable.Replace("[", "").Replace("]", "");
        var dotIdx   = rawTable.IndexOf('.');
        var tableSchema = dotIdx > 0 ? rawTable.Substring(0, dotIdx) : _schema;
        var tableName   = dotIdx > 0 ? rawTable.Substring(dotIdx + 1) : rawTable;
        if (!IdentRegex.IsMatch(tableSchema) || !IdentRegex.IsMatch(tableName)) { cache[formCode] = 0; return 0; }
        var ts = tableSchema.Replace("]", "]]");
        var tn = tableName.Replace("]", "]]");
        var filterClause = string.IsNullOrWhiteSpace(baseTableFilter) ? "1 = 1" : "(" + baseTableFilter + ")";

        await using var cntCmd = conn.CreateCommand();
        cntCmd.CommandText = $"SELECT COUNT(*) FROM [{ts}].[{tn}] WHERE {filterClause};";
        cntCmd.CommandTimeout = 60;
        try
        {
            var cnt = (int)(await cntCmd.ExecuteScalarAsync(ct) ?? 0);
            cache[formCode] = cnt;
            return cnt;
        }
        catch (SqlException) { cache[formCode] = 0; return 0; }
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

        // 2a) Integration → SourceFormCode + SourceFilterJson
        string? sourceFormCode = null;
        string? sourceFilterJson = null;
        await using (var integCmd = conn.CreateCommand())
        {
            // SourceFilterJson kolonu eski sema'da olmayabilir — COL_LENGTH ile null-safe
            integCmd.CommandText = $"""
                SELECT [SourceFormCode],
                       CASE WHEN COL_LENGTH(N'[{S}].[Integration]', N'SourceFilterJson') IS NULL
                            THEN NULL ELSE [SourceFilterJson] END AS SourceFilterJson
                FROM [{S}].[Integration] WHERE [Id] = @Id;
                """;
            integCmd.Parameters.Add(new SqlParameter("@Id", integrationId));
            try
            {
                await using var r = await integCmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    sourceFormCode   = r.IsDBNull(0) ? null : r.GetString(0);
                    sourceFilterJson = r.IsDBNull(1) ? null : r.GetString(1);
                }
            }
            catch (SqlException) { /* fallback yok — sema problemli, EmptyResult döner */ }
        }
        if (string.IsNullOrWhiteSpace(sourceFormCode))
            return EmptyResult();

        string? baseTable = null, baseRecordKey = null, baseTableFilter = null;
        await using (var formCmd = conn.CreateCommand())
        {
            // 2026-05-21: BaseTableFilter kolonu eklendi — eski sema'da olmayabilir
            // (COL_LENGTH check ile null-safe okuruz).
            formCmd.CommandText = $"""
                SELECT [BaseTable],
                       [BaseRecordKey],
                       CASE WHEN COL_LENGTH(N'[{S}].[Forms]', N'BaseTableFilter') IS NULL
                            THEN NULL ELSE [BaseTableFilter] END AS BaseTableFilter
                FROM [{S}].[Forms] WHERE [FormCode] = @Code;
                """;
            formCmd.Parameters.Add(new SqlParameter("@Code", sourceFormCode));
            try
            {
                await using var r = await formCmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    baseTable       = r.IsDBNull(0) ? null : r.GetString(0);
                    baseRecordKey   = r.IsDBNull(1) ? null : r.GetString(1);
                    baseTableFilter = r.IsDBNull(2) ? null : r.GetString(2);
                }
            }
            catch (SqlException)
            {
                // COL_LENGTH yine de invalid column hatasi verirse — kolon yok say
                formCmd.CommandText = $"SELECT [BaseTable], [BaseRecordKey] FROM [{S}].[Forms] WHERE [FormCode] = @Code;";
                await using var r2 = await formCmd.ExecuteReaderAsync(ct);
                if (await r2.ReadAsync(ct))
                {
                    baseTable     = r2.IsDBNull(0) ? null : r2.GetString(0);
                    baseRecordKey = r2.IsDBNull(1) ? null : r2.GetString(1);
                }
            }
        }
        if (string.IsNullOrWhiteSpace(baseTable) || string.IsNullOrWhiteSpace(baseRecordKey))
            return EmptyResult();

        // 2026-05-21 BUG FIX: Forms.BaseTable hem "Document" hem "dbo.Document" formatında
        // gelebilir (admin migration eski stili kullanır). Eski kod IdentRegex'i dot içermediği
        // için 'dbo.Document'i reddediyor + EmptyResult dönüyordu — sol pane'de "Hatalı: 2"
        // dursa bile sağda "Bu filtrede kayıt yok" görünmesinin sebebi buydu.
        // Çözüm: schema.tableName ayrıştır (RebuildSingleFlatViewAsync ile aynı pattern).
        var rawTable = baseTable.Replace("[", "").Replace("]", "");
        var dotIdx   = rawTable.IndexOf('.');
        var tableSchema = dotIdx > 0 ? rawTable.Substring(0, dotIdx) : _schema;
        var tableName   = dotIdx > 0 ? rawTable.Substring(dotIdx + 1) : rawTable;
        if (!IdentRegex.IsMatch(tableSchema) || !IdentRegex.IsMatch(tableName) || !IdentRegex.IsMatch(baseRecordKey))
            return EmptyResult();
        var ts = tableSchema.Replace("]", "]]");
        var tn = tableName.Replace("]", "]]");

        // 2b) Base table'da Code / Name kolonlari var mi? (INFORMATION_SCHEMA — admin'in
        //     belirttiği schema kullanılır; ".dbo.Document" gibi tam yol da desteklensin)
        var hasCode = await ColumnExistsAsync(conn, tableSchema, tableName, "Code", ct)
                      || await ColumnExistsAsync(conn, tableSchema, tableName, "code", ct);
        var hasName = await ColumnExistsAsync(conn, tableSchema, tableName, "Name", ct)
                      || await ColumnExistsAsync(conn, tableSchema, tableName, "name", ct);
        // Case-sensitive isim yakala (gercek kolon adi):
        var codeCol = hasCode ? await ResolveColumnNameAsync(conn, tableSchema, tableName, "Code", ct) : null;
        var nameCol = hasName ? await ResolveColumnNameAsync(conn, tableSchema, tableName, "Name", ct) : null;

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

        // 2c+) BaseTableFilter — Form-level scope (orn. SALES_QUOTE_EDIT → satis_teklifi tipindeki Document'lar).
        // Admin bu alani UI'dan girebilir; biz sadece "AND (filter)" olarak parantez icinde ekleriz.
        // Predicate icindeki kolon referanslari [Document]'a aittir; b. alias'i opsiyonel.
        string baseFilterClause = string.IsNullOrWhiteSpace(baseTableFilter)
            ? "1 = 1"
            : "(" + baseTableFilter + ")";

        // 2d) Search filter — code/name LIKE
        string searchWhere = "1 = 1";
        if (!string.IsNullOrWhiteSpace(search) && (hasCode || hasName))
        {
            var bits = new List<string>();
            if (hasCode) bits.Add($"b.[{codeCol}] LIKE @Search");
            if (hasName) bits.Add($"b.[{nameCol}] LIKE @Search");
            searchWhere = "(" + string.Join(" OR ", bits) + ")";
        }

        // 2d+) 2026-05-22 Pre-flight Filter — Integration.SourceFilterJson kuralları.
        // Filter engine WHERE fragment + (widget: kuralları için) LEFT JOIN üretir.
        // Tüm tetikleyici yollarla TEK NOKTA — bu helper hem queue listesi hem Runner'da kullanılır.
        var preflight = _filterEngine.BuildSqlWhere(
            filterJson:       sourceFilterJson,
            baseTableAlias:   "b",
            schema:           tableSchema,
            baseTable:        tableName,
            baseRecordKeyCol: baseRecordKey,
            formCode:         sourceFormCode);
        string preflightWhere = preflight.IsEmpty ? "1 = 1" : preflight.WhereClause;
        string preflightJoin  = preflight.JoinClauses;

        // 2e) Toplam say + sayfali satirlar (UNION'lu count tek tur trip — basitlik icin 2 sorgu)
        int totalCount = 0;
        var rows = new List<QueueRowDto>();

        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"""
                SELECT COUNT(*)
                FROM [{ts}].[{tn}] b
                {preflightJoin}
                LEFT JOIN [{S}].[IntegrationRecordStatus] s
                       ON s.[IntegrationId] = @IntegrationId
                      AND s.[RecordId]      = CAST(b.[{baseRecordKey}] AS NVARCHAR(100))
                      AND s.[IsActive]      = 1
                WHERE {statusWhere} AND {searchWhere} AND {baseFilterClause} AND {preflightWhere};
                """;
            countCmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
            countCmd.CommandTimeout = 60;
            if (!string.IsNullOrWhiteSpace(search))
                countCmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
            foreach (var p in preflight.Parameters) countCmd.Parameters.Add(CloneParam(p));
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
                FROM [{ts}].[{tn}] b
                {preflightJoin}
                LEFT JOIN [{S}].[IntegrationRecordStatus] s
                       ON s.[IntegrationId] = @IntegrationId
                      AND s.[RecordId]      = CAST(b.[{baseRecordKey}] AS NVARCHAR(100))
                      AND s.[IsActive]      = 1
                WHERE {statusWhere} AND {searchWhere} AND {baseFilterClause} AND {preflightWhere}
                ORDER BY b.[{baseRecordKey}] DESC
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
                """;
            listCmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
            listCmd.Parameters.Add(new SqlParameter("@Skip", (page - 1) * pageSize));
            listCmd.Parameters.Add(new SqlParameter("@Take", pageSize));
            if (!string.IsNullOrWhiteSpace(search))
                listCmd.Parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
            foreach (var p in preflight.Parameters) listCmd.Parameters.Add(CloneParam(p));

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
        // 2026-05-21 FIX: Pending = base_table_total - (failed + sent + skipped).
        // Eski kod sadece IntegrationRecordStatus tablosundaki Pending satirlarini sayardi;
        // henuz hic islem gormemis kayitlar IRS tablosunda OLMADIGI icin Pending=0 gorunurdu.
        // Artik base table COUNT'undan Failed/Sent/Skipped'i cikariyoruz.
        int failed = 0, sent = 0, skipped = 0;
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
                    case "Failed":  failed  = cnt; break;
                    case "Sent":    sent    = cnt; break;
                    case "Skipped": skipped = cnt; break;
                }
            }
        }

        // Base table'in toplam satir sayisi (BaseTableFilter ile filtrelenmis).
        int baseTotal = 0;
        await using (var baseCntCmd = conn.CreateCommand())
        {
            baseCntCmd.CommandText = $"SELECT COUNT(*) FROM [{ts}].[{tn}] b WHERE {baseFilterClause};";
            baseCntCmd.CommandTimeout = 60;
            baseTotal = (int)(await baseCntCmd.ExecuteScalarAsync(ct) ?? 0);
        }
        var pending = Math.Max(0, baseTotal - failed - sent - skipped);

        return new QueueListResult(rows, totalCount, pending, failed, sent, skipped);
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static QueueListResult EmptyResult() =>
        new(Array.Empty<QueueRowDto>(), 0, 0, 0, 0, 0);

    // 2026-05-21: Schema parametresi eklendi — admin BaseTable'da "dbo.Document" gibi
    // tam yol vermisse o schema'da arama yapilir, yoksa _schema (tenant default) kullanilir.
    private static async Task<bool> ColumnExistsAsync(SqlConnection conn, string schema, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table AND COLUMN_NAME = @Col;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", schema));
        cmd.Parameters.Add(new SqlParameter("@Table", table));
        cmd.Parameters.Add(new SqlParameter("@Col", column));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task<string?> ResolveColumnNameAsync(SqlConnection conn, string schema, string table, string columnLike, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table AND COLUMN_NAME = @Col;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", schema));
        cmd.Parameters.Add(new SqlParameter("@Table", table));
        cmd.Parameters.Add(new SqlParameter("@Col", columnLike));
        return (await cmd.ExecuteScalarAsync(ct)) as string;
    }

    /// <summary>
    /// SqlParameter aynı SqlCommand'a ikinci kez eklenemez — clone üret.
    /// FilterEngine.BuildSqlWhere parametre listesini iki query'ye (count + list) inject ederken kullanılır.
    /// </summary>
    private static SqlParameter CloneParam(SqlParameter p) => new()
    {
        ParameterName = p.ParameterName,
        Value         = p.Value,
        SqlDbType     = p.SqlDbType,
        Size          = p.Size,
        Direction     = p.Direction,
    };
}

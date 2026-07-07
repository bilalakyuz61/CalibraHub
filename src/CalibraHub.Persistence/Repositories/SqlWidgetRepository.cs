using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SqlWidgetRepository — EAV widget sisteminin ADO.NET persistence katmani.
/// Tablolar: dbo.Forms (mevcut, read-only), dbo.WidgetMas (yeni), dbo.WidgetTra (yeni).
///
/// SqlServerConnectionFactory ile per-company connection yonetimi korunur.
/// SqlDynamicFieldValueRepository pattern'inin birebir uyarlanmis halidir.
///
/// CompanyId izolasyonu: IHttpContextAccessor araciligiyla mevcut kullanicinin
/// sirket kimligini ceker ve tum WidgetMas sorgularina filtre olarak ekler.
/// Guid.Empty (startup/no-context) durumunda filtre uygulanmaz.
/// </summary>
public sealed class SqlWidgetRepository : IWidgetRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _formsTable;
    private readonly string _widgetMasTable;
    private readonly string _widgetTraTable;
    private readonly string _widgetTraLogTable;

    // 2026-06-08 — IsPermissionControlled kolonu DB'de var mı? İlk çağrıda kontrol edilir,
    // sonrasında cache'lenir. Yoksa SELECT/INSERT/UPDATE'lerde bu kolon atlanır — bu sayede
    // ALTER TABLE çalıştırılmadan da uygulama hata vermez (graceful degradation).
    private static bool? _hasPermCtlColumn;
    private static readonly object _hasPermCtlLock = new();

    // 2026-07-06 — Alan bazli audit altyapisi (WidgetTra.CreatedBy + WidgetTraLog) DB'de
    // var mi? Ayni graceful-degradation pattern'i: migration calismadiysa save'ler audit'siz
    // ama hatasiz devam eder.
    private static bool? _hasValueAudit;
    private static readonly object _hasValueAuditLock = new();

    public SqlWidgetRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IHttpContextAccessor httpContextAccessor)
    {
        _connectionFactory   = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _formsTable     = $"[{schema}].[Forms]";
        _widgetMasTable = $"[{schema}].[WidgetMas]";
        _widgetTraTable = $"[{schema}].[WidgetTra]";
        _widgetTraLogTable = $"[{schema}].[WidgetTraLog]";
    }

    /// <summary>
    /// 2026-06-08 — IsPermissionControlled kolonu DB'de var mı? Process-lifetime cache.
    /// Hot path'i kirletmesin diye sadece bir kere sorgulanır; sonuç bool? olarak saklanır.
    /// </summary>
    private async Task<bool> HasPermControlColumnAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_hasPermCtlColumn.HasValue) return _hasPermCtlColumn.Value;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT CASE WHEN COL_LENGTH(N'{_widgetMasTable}', N'IsPermissionControlled') IS NULL THEN 0 ELSE 1 END;";
        var result = await cmd.ExecuteScalarAsync(ct);
        var exists = result != null && Convert.ToInt32(result) == 1;
        lock (_hasPermCtlLock) { _hasPermCtlColumn = exists; }
        return exists;
    }

    /// <summary>
    /// HTTP context'ten mevcut kullanicinin sirket kimligini ceker.
    /// Kimlik dogrulamasi yoksa veya claim eksikse Guid.Empty doner.
    /// </summary>
    private int GetCurrentCompanyId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var raw = httpContext.User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var id))
                return id;
        }
        return 0;
    }

    /// <summary>
    /// Audit icin mevcut kullanici adi (Identity.Name, yoksa email claim).
    /// Background/integration cagrilarinda (HTTP context yok) null doner —
    /// audit satirinda ChangedBy bos kalir, kayit yine yazilir.
    /// </summary>
    private string? GetCurrentUserName()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        var name = user.Identity!.Name;
        if (!string.IsNullOrWhiteSpace(name)) return name.Length > 120 ? name[..120] : name;
        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email)) return null;
        return email.Length > 120 ? email[..120] : email;
    }

    /// <summary>
    /// 2026-07-06 — Audit altyapisi hazir mi? (WidgetTraLog tablosu + WidgetTra.CreatedBy
    /// kolonu). Process-lifetime cache; initializer startup'ta calistigi icin normalde true.
    /// </summary>
    private async Task<bool> HasValueAuditAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_hasValueAudit.HasValue) return _hasValueAudit.Value;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT CASE WHEN OBJECT_ID(N'{_widgetTraLogTable}', N'U') IS NOT NULL
                         AND COL_LENGTH(N'{_widgetTraTable}', N'CreatedBy') IS NOT NULL
                        THEN 1 ELSE 0 END;
            """;
        var result = await cmd.ExecuteScalarAsync(ct);
        var exists = result != null && Convert.ToInt32(result) == 1;
        lock (_hasValueAuditLock) { _hasValueAudit = exists; }
        return exists;
    }

    // ══════════════════════════════════════════════════════════
    // dbo.Forms — mevcut katalog (read-only bu servis acisindan)
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<FormDefinition>> GetFormsAsync(CancellationToken ct)
    {
        var list = new List<FormDefinition>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[FormCode],[FormName],[Module],[SubModule],[SortOrder],[IsActive],[BaseTable],[BaseRecordKey],[Icon],[IconColor],[IsMenuItem],[MenuKey],[MenuLabel],[MenuLabelEn],[MenuGroupKey],[MenuGroupName],[MenuGroupIcon],[MenuGroupSortOrder],[MenuSortOrder],[MenuMatchPath],[AdminOnly],[IsWidgetForm]
            FROM {_formsTable}
            WHERE [IsActive] = 1
            ORDER BY [SortOrder], [FormName];
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapForm(r));
        return list;
    }

    public async Task<FormDefinition?> GetFormByIdAsync(int formId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (1) [Id],[FormCode],[FormName],[Module],[SubModule],[SortOrder],[IsActive],[BaseTable],[BaseRecordKey],[Icon],[IconColor],[IsMenuItem],[MenuKey],[MenuLabel],[MenuLabelEn],[MenuGroupKey],[MenuGroupName],[MenuGroupIcon],[MenuGroupSortOrder],[MenuSortOrder],[MenuMatchPath],[AdminOnly]
            FROM {_formsTable}
            WHERE [Id] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", formId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
            return MapForm(r);
        return null;
    }

    public async Task<FormDefinition?> GetFormByCodeAsync(string formCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formCode)) return null;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (1) [Id],[FormCode],[FormName],[Module],[SubModule],[SortOrder],[IsActive],[BaseTable],[BaseRecordKey],[Icon],[IconColor],[IsMenuItem],[MenuKey],[MenuLabel],[MenuLabelEn],[MenuGroupKey],[MenuGroupName],[MenuGroupIcon],[MenuGroupSortOrder],[MenuSortOrder],[MenuMatchPath],[AdminOnly]
            FROM {_formsTable}
            WHERE [FormCode] = @Code;
            """;
        cmd.Parameters.Add(new SqlParameter("@Code", formCode.Trim()));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
            return MapForm(r);
        return null;
    }

    // ══════════════════════════════════════════════════════════
    // WidgetMas — widget tanimlari (master)
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<WidgetDefinition>> GetWidgetsByFormAsync(int formId, CancellationToken ct, bool includeInactive = false)
    {
        var companyId = GetCurrentCompanyId();
        var list = new List<WidgetDefinition>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var hasPerm = await HasPermControlColumnAsync(conn, ct);
        var permCol = hasPerm ? ",[IsPermissionControlled]" : string.Empty;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[CompanyId],[FormId],[ParentId],[WidgetCode],[Label],[DataType],
                   [MaxLength],[MinLength],[ExpectedLength],[MinValue],[MaxValue],[SortOrder],[OptionsJSON],[RulesJSON],[IsPlainField],[IsRequired],[IsActive],
                   [ColorType],[ColorValue],[ColSpan],[LabelStyle],[IsSystemField],[EntityColumn]{permCol},[CreatedAt],[UpdatedAt]
            FROM {_widgetMasTable}
            WHERE [FormId] = @FormId
              AND (@IncludeInactive = 1 OR [IsActive] = 1)
              AND (@CompanyId = 0 OR [CompanyId] = @CompanyId)
            ORDER BY [SortOrder], [Label];
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        cmd.Parameters.Add(new SqlParameter("@IncludeInactive", includeInactive ? 1 : 0));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapWidget(r));
        return list;
    }

    public async Task<WidgetDefinition?> GetWidgetByIdAsync(int widgetId, CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var hasPerm = await HasPermControlColumnAsync(conn, ct);
        var permCol = hasPerm ? ",[IsPermissionControlled]" : string.Empty;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (1) [Id],[CompanyId],[FormId],[ParentId],[WidgetCode],[Label],[DataType],
                   [MaxLength],[MinLength],[ExpectedLength],[MinValue],[MaxValue],[SortOrder],[OptionsJSON],[RulesJSON],[IsPlainField],[IsRequired],[IsActive],
                   [ColorType],[ColorValue],[ColSpan],[LabelStyle],[IsSystemField],[EntityColumn]{permCol},[CreatedAt],[UpdatedAt]
            FROM {_widgetMasTable}
            WHERE [Id] = @Id
              AND (@CompanyId = 0 OR [CompanyId] = @CompanyId);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", widgetId));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
            return MapWidget(r);
        return null;
    }

    public async Task<int> UpsertWidgetAsync(WidgetDefinition widget, CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var hasPerm = await HasPermControlColumnAsync(conn, ct);
        var permSetClause = hasPerm ? ",\n                    [IsPermissionControlled] = @IsPermissionControlled" : string.Empty;
        var permInsCol    = hasPerm ? ",[IsPermissionControlled]" : string.Empty;
        var permInsVal    = hasPerm ? ", @IsPermissionControlled" : string.Empty;

        if (widget.Id > 0)
        {
            // UPDATE — CompanyId degismez; sirket dogrulamasi WHERE ile yapilir
            await using var upd = conn.CreateCommand();
            upd.CommandText = $"""
                UPDATE {_widgetMasTable}
                SET [FormId]       = @FormId,
                    [ParentId]     = @ParentId,
                    [WidgetCode]   = @WidgetCode,
                    [Label]        = @Label,
                    [DataType]     = @DataType,
                    [MaxLength]      = @MaxLength,
                    [MinLength]      = @MinLength,
                    [ExpectedLength] = @ExpectedLength,
                    [MinValue]       = @MinValue,
                    [MaxValue]       = @MaxValue,
                    [SortOrder]    = @SortOrder,
                    [OptionsJSON]  = @OptionsJson,
                    [RulesJSON]    = @RulesJson,
                    [IsPlainField] = @IsPlainField,
                    [IsRequired]   = @IsRequired,
                    [IsActive]     = @IsActive,
                    [ColorType]    = @ColorType,
                    [ColorValue]   = @ColorValue,
                    [ColSpan]      = @ColSpan,
                    [LabelStyle]   = @LabelStyle,
                    [IsSystemField] = @IsSystemField,
                    [EntityColumn]  = @EntityColumn{permSetClause},
                    [UpdatedAt]    = @UpdatedAt
                WHERE [Id] = @Id
                  AND (@CompanyId = 0 OR [CompanyId] = @CompanyId);
                """;
            BindWidgetParams(upd, widget, hasPerm);
            upd.Parameters.Add(new SqlParameter("@Id", widget.Id));
            upd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await upd.ExecuteNonQueryAsync(ct);
            return widget.Id;
        }

        // INSERT — CompanyId mevcut kullanicinin sirketinden alinir
        await using var ins = conn.CreateCommand();
        ins.CommandText = $"""
            INSERT INTO {_widgetMasTable}
                ([CompanyId],[FormId],[ParentId],[WidgetCode],[Label],[DataType],[MaxLength],[MinLength],[ExpectedLength],[MinValue],[MaxValue],
                 [SortOrder],[OptionsJSON],[RulesJSON],[IsPlainField],[IsRequired],[IsActive],[ColorType],[ColorValue],[ColSpan],[LabelStyle],[IsSystemField],[EntityColumn]{permInsCol},[CreatedAt],[UpdatedAt])
            OUTPUT INSERTED.[Id]
            VALUES
                (@CompanyId, @FormId, @ParentId, @WidgetCode, @Label, @DataType, @MaxLength, @MinLength, @ExpectedLength, @MinValue, @MaxValue,
                 @SortOrder, @OptionsJson, @RulesJson, @IsPlainField, @IsRequired, @IsActive, @ColorType, @ColorValue, @ColSpan, @LabelStyle, @IsSystemField, @EntityColumn{permInsVal}, @CreatedAt, @UpdatedAt);
            """;
        BindWidgetParams(ins, widget, hasPerm);
        ins.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        ins.Parameters.Add(new SqlParameter("@CreatedAt", widget.CreatedAt));
        var newId = await ins.ExecuteScalarAsync(ct);
        return Convert.ToInt32(newId);
    }

    public async Task<int> CountChildrenByParentIdAsync(int parentId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {_widgetMasTable} WHERE [ParentId] = @Pid;";
        cmd.Parameters.Add(new SqlParameter("@Pid", parentId));
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar == null || scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
    }

    public async Task DeleteWidgetAsync(int widgetId, CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Once degerleri sil (FK constraint) — sirket dogrulamasi WidgetMas JOIN ile
            await using (var delVal = conn.CreateCommand())
            {
                delVal.Transaction = tx;
                delVal.CommandText = $"""
                    DELETE FROM {_widgetTraTable}
                    WHERE [WidgetId] = @Id
                      AND EXISTS (
                          SELECT 1 FROM {_widgetMasTable}
                          WHERE [Id] = @Id
                            AND (@CompanyId = 0 OR [CompanyId] = @CompanyId)
                      );
                    """;
                delVal.Parameters.Add(new SqlParameter("@Id", widgetId));
                delVal.Parameters.Add(new SqlParameter("@CompanyId", companyId));
                await delVal.ExecuteNonQueryAsync(ct);
            }
            // Sonra widget'i sil — sadece kendi sirketinin widget'ini silebilir
            await using (var delW = conn.CreateCommand())
            {
                delW.Transaction = tx;
                delW.CommandText = $"""
                    DELETE FROM {_widgetMasTable}
                    WHERE [Id] = @Id
                      AND (@CompanyId = 0 OR [CompanyId] = @CompanyId);
                    """;
                delW.Parameters.Add(new SqlParameter("@Id", widgetId));
                delW.Parameters.Add(new SqlParameter("@CompanyId", companyId));
                await delW.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ══════════════════════════════════════════════════════════
    // WidgetTra — widget degerleri (transaction)
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<WidgetValue>> GetValuesAsync(int formId, string recordId, CancellationToken ct)
    {
        var list = new List<WidgetValue>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT t.[Id], t.[WidgetId], t.[RecordId], t.[ParentRecordId], t.[Value], t.[CreatedAt], t.[UpdatedAt]
            FROM {_widgetTraTable} t
            INNER JOIN {_widgetMasTable} m ON m.[Id] = t.[WidgetId]
            WHERE m.[FormId] = @FormId AND t.[RecordId] = @RecordId;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));
        cmd.Parameters.Add(new SqlParameter("@RecordId", recordId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapValue(r));
        return list;
    }

    public async Task<ILookup<string, WidgetValue>> GetValuesBatchAsync(
        int formId,
        IReadOnlyCollection<string> recordIds,
        CancellationToken ct)
    {
        if (recordIds.Count == 0)
            return Enumerable.Empty<WidgetValue>().ToLookup(v => v.RecordId);

        var pairs = new List<(string RecordId, WidgetValue Value)>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Parameterized IN clause — her RecordId icin ayri parametre
        var paramNames = new List<string>(recordIds.Count);
        int idx = 0;
        foreach (var rid in recordIds)
        {
            var pName = $"@rid{idx++}";
            paramNames.Add(pName);
            cmd.Parameters.Add(new SqlParameter(pName, rid));
        }

        cmd.CommandText = $"""
            SELECT t.[Id], t.[WidgetId], t.[RecordId], t.[ParentRecordId], t.[Value], t.[CreatedAt], t.[UpdatedAt]
            FROM {_widgetTraTable} t
            INNER JOIN {_widgetMasTable} m ON m.[Id] = t.[WidgetId]
            WHERE m.[FormId] = @FormId
              AND t.[RecordId] IN ({string.Join(",", paramNames)})
              AND t.[ParentRecordId] IS NULL;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var v = MapValue(r);
            pairs.Add((v.RecordId, v));
        }

        return pairs.ToLookup(p => p.RecordId, p => p.Value);
    }

    public async Task UpsertValuesAsync(
        int formId,
        string recordId,
        IReadOnlyDictionary<int, string?> valuesByWidgetId,
        CancellationToken ct,
        string? parentRecordId = null)
    {
        if (valuesByWidgetId.Count == 0) return;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // Audit kontrolu transaction ACILMADAN once — pending tx varken transaction'siz
        // komut calistirmak SqlClient'ta InvalidOperationException uretir.
        var hasAudit = await HasValueAuditAsync(conn, ct);
        var userName = hasAudit ? GetCurrentUserName() : null;

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Audit: DELETE oncesi eski degerlerin fotografi + WidgetCode haritasi.
            // (WidgetCode log satirlarina snapshot yazilir — widget silinse de log okunur.)
            var oldValues = new Dictionary<int, string?>();
            var codeById  = new Dictionary<int, string>();
            if (hasAudit)
            {
                await using var snap = conn.CreateCommand();
                snap.Transaction = tx;
                snap.CommandText = $"""
                    SELECT m.[Id], m.[WidgetCode], t.[Value]
                    FROM {_widgetMasTable} m
                    LEFT JOIN {_widgetTraTable} t
                           ON t.[WidgetId] = m.[Id] AND t.[RecordId] = @RecordId
                    WHERE m.[FormId] = @FormId;
                    """;
                snap.Parameters.Add(new SqlParameter("@FormId", formId));
                snap.Parameters.Add(new SqlParameter("@RecordId", recordId));
                await using var r = await snap.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var wid = r.GetInt32(0);
                    codeById[wid] = r.GetString(1);
                    if (!r.IsDBNull(2)) oldValues[wid] = r.GetString(2);
                }
            }

            // Scope: sadece bu RecordId (master) veya (RecordId + ParentRecordId) (child).
            // DELETE + INSERT. Kardes child satirlarina dokunulmaz cunku RecordId tek kayda ait.
            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"""
                    DELETE FROM {_widgetTraTable}
                    WHERE [RecordId] = @RecordId
                      AND [WidgetId] IN (SELECT [Id] FROM {_widgetMasTable} WHERE [FormId] = @FormId);
                    """;
                del.Parameters.Add(new SqlParameter("@FormId", formId));
                del.Parameters.Add(new SqlParameter("@RecordId", recordId));
                await del.ExecuteNonQueryAsync(ct);
            }

            // Yeni degerleri ekle (null olmayanlar)
            var now = DateTime.Now;
            var userCols = hasAudit ? ",[CreatedBy],[UpdatedBy]" : string.Empty;
            var userVals = hasAudit ? ", @UserName, @UserName" : string.Empty;
            foreach (var kv in valuesByWidgetId)
            {
                if (kv.Value == null) continue; // null = silindi sayilir
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_widgetTraTable}
                        ([WidgetId],[RecordId],[ParentRecordId],[Value],[CreatedAt],[UpdatedAt]{userCols})
                    VALUES
                        (@WidgetId, @RecordId, @ParentRecordId, @Value, @CreatedAt, @UpdatedAt{userVals});
                    """;
                ins.Parameters.Add(new SqlParameter("@WidgetId", kv.Key));
                ins.Parameters.Add(new SqlParameter("@RecordId", recordId));
                ins.Parameters.Add(new SqlParameter("@ParentRecordId", (object?)parentRecordId ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Value", (object?)kv.Value ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@CreatedAt", now));
                ins.Parameters.Add(new SqlParameter("@UpdatedAt", now));
                if (hasAudit)
                    ins.Parameters.Add(new SqlParameter("@UserName", (object?)userName ?? DBNull.Value));
                await ins.ExecuteNonQueryAsync(ct);
            }

            // Audit: diff → yalnizca GERCEKTEN degisen degerler icin log satiri.
            // Uc durum: eski var + yeni farkli (degisti), eski var + yeni yok/null
            // (silindi), eski yok + yeni var (eklendi). Esit degerler loglanmaz —
            // "kaydet"e her basista gurultu olusmaz.
            if (hasAudit)
            {
                var changed = new List<(int WidgetId, string? Old, string? New)>();
                var seen = new HashSet<int>();
                foreach (var kv in valuesByWidgetId)
                {
                    seen.Add(kv.Key);
                    oldValues.TryGetValue(kv.Key, out var oldVal);
                    if (!string.Equals(oldVal, kv.Value, StringComparison.Ordinal))
                        changed.Add((kv.Key, oldVal, kv.Value));
                }
                // Incoming dict'te hic olmayan ama eski degeri olan widget'lar:
                // DELETE ile veri kayboldu → old → null olarak logla.
                foreach (var old in oldValues)
                {
                    if (!seen.Contains(old.Key) && old.Value != null)
                        changed.Add((old.Key, old.Value, null));
                }

                foreach (var c in changed)
                {
                    await using var log = conn.CreateCommand();
                    log.Transaction = tx;
                    log.CommandText = $"""
                        INSERT INTO {_widgetTraLogTable}
                            ([FormId],[WidgetId],[WidgetCode],[RecordId],[ParentRecordId],[OldValue],[NewValue],[ChangedBy],[ChangedAt])
                        VALUES
                            (@FormId, @WidgetId, @WidgetCode, @RecordId, @ParentRecordId, @OldValue, @NewValue, @ChangedBy, @ChangedAt);
                        """;
                    log.Parameters.Add(new SqlParameter("@FormId", formId));
                    log.Parameters.Add(new SqlParameter("@WidgetId", c.WidgetId));
                    log.Parameters.Add(new SqlParameter("@WidgetCode",
                        codeById.TryGetValue(c.WidgetId, out var code) ? code : $"#{c.WidgetId}"));
                    log.Parameters.Add(new SqlParameter("@RecordId", recordId));
                    log.Parameters.Add(new SqlParameter("@ParentRecordId", (object?)parentRecordId ?? DBNull.Value));
                    log.Parameters.Add(new SqlParameter("@OldValue", (object?)c.Old ?? DBNull.Value));
                    log.Parameters.Add(new SqlParameter("@NewValue", (object?)c.New ?? DBNull.Value));
                    log.Parameters.Add(new SqlParameter("@ChangedBy", (object?)userName ?? DBNull.Value));
                    log.Parameters.Add(new SqlParameter("@ChangedAt", now));
                    await log.ExecuteNonQueryAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Widget degerlerini bir RecordId'den diger RecordId'ye kopyalar (atomic
    /// INSERT ... SELECT). Kalem revizyonu gibi senaryolarda kullanilir —
    /// kullanici "Revize Et" dediginde eski satirin ek alanlari yeni satira
    /// oldugu gibi tasinir. Sadece top-level (ParentRecordId IS NULL) satirlar
    /// kopyalanir; grid child satirlarini atlanir — yeni revize kendi grid
    /// veri setiyle baslar.
    /// </summary>
    public async Task CopyValuesAsync(
        int formId,
        string sourceRecordId,
        string targetRecordId,
        CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var hasAudit = await HasValueAuditAsync(conn, ct);
        var userCols = hasAudit ? ", [CreatedBy], [UpdatedBy]" : string.Empty;
        var userVals = hasAudit ? ", @UserName, @UserName" : string.Empty;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_widgetTraTable} ([WidgetId], [RecordId], [ParentRecordId], [Value], [CreatedAt], [UpdatedAt]{userCols})
            SELECT t.[WidgetId], @Target, NULL, t.[Value], @Now, @Now{userVals}
            FROM {_widgetTraTable} t
            INNER JOIN {_widgetMasTable} m ON m.[Id] = t.[WidgetId]
            WHERE m.[FormId] = @FormId
              AND t.[RecordId] = @Source
              AND t.[ParentRecordId] IS NULL;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));
        cmd.Parameters.Add(new SqlParameter("@Source", sourceRecordId));
        cmd.Parameters.Add(new SqlParameter("@Target", targetRecordId));
        cmd.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        if (hasAudit)
            cmd.Parameters.Add(new SqlParameter("@UserName", (object?)GetCurrentUserName() ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Alan bazli degisiklik gecmisi — yeni→eski sirali. Master kaydin kendi
    /// loglari (FormId + RecordId) ile grid child satirlarinin loglari
    /// (ParentRecordId = recordId) birlikte doner. Audit tablosu heniz yoksa
    /// (migration calismamis eski DB) bos liste — cagiran taraf hata gormez.
    /// </summary>
    public async Task<IReadOnlyCollection<WidgetValueLog>> GetValueLogsAsync(
        int formId,
        string recordId,
        int top,
        CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        if (!await HasValueAuditAsync(conn, ct)) return Array.Empty<WidgetValueLog>();

        if (top <= 0) top = 200;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Top)
                   [Id],[FormId],[WidgetId],[WidgetCode],[RecordId],[ParentRecordId],
                   [OldValue],[NewValue],[ChangedBy],[ChangedAt]
            FROM {_widgetTraLogTable}
            WHERE ([FormId] = @FormId AND [RecordId] = @RecordId)
               OR [ParentRecordId] = @RecordId
            ORDER BY [ChangedAt] DESC, [Id] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Top", top));
        cmd.Parameters.Add(new SqlParameter("@FormId", formId));
        cmd.Parameters.Add(new SqlParameter("@RecordId", recordId));

        var list = new List<WidgetValueLog>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new WidgetValueLog
            {
                Id             = r.GetInt64(0),
                FormId         = r.GetInt32(1),
                WidgetId       = r.GetInt32(2),
                WidgetCode     = r.GetString(3),
                RecordId       = r.GetString(4),
                ParentRecordId = r.IsDBNull(5) ? null : r.GetString(5),
                OldValue       = r.IsDBNull(6) ? null : r.GetString(6),
                NewValue       = r.IsDBNull(7) ? null : r.GetString(7),
                ChangedBy      = r.IsDBNull(8) ? null : r.GetString(8),
                ChangedAt      = r.GetDateTime(9),
            });
        }
        return list;
    }

    // ══════════════════════════════════════════════════════════
    // Master-Detail (Faz E — grid widget)
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<string>> GetChildRecordIdsAsync(
        int childFormId,
        string parentRecordId,
        CancellationToken ct)
    {
        var list = new List<string>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT t.[RecordId]
            FROM {_widgetTraTable} t
            INNER JOIN {_widgetMasTable} m ON m.[Id] = t.[WidgetId]
            WHERE m.[FormId] = @FormId AND t.[ParentRecordId] = @ParentId;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormId", childFormId));
        cmd.Parameters.Add(new SqlParameter("@ParentId", parentRecordId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(r.GetString(0));
        return list;
    }

    public async Task DeleteChildRecordsAsync(
        int childFormId,
        IReadOnlyCollection<string> childRecordIds,
        CancellationToken ct)
    {
        if (childRecordIds.Count == 0) return;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Her child RecordId icin ayri DELETE — parametre listesi esnek kalsin
            foreach (var rid in childRecordIds)
            {
                await using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = $"""
                    DELETE FROM {_widgetTraTable}
                    WHERE [RecordId] = @RecordId
                      AND [WidgetId] IN (SELECT [Id] FROM {_widgetMasTable} WHERE [FormId] = @FormId);
                    """;
                del.Parameters.Add(new SqlParameter("@FormId", childFormId));
                del.Parameters.Add(new SqlParameter("@RecordId", rid));
                await del.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ══════════════════════════════════════════════════════════
    // Mapping helpers
    // ══════════════════════════════════════════════════════════

    private static FormDefinition MapForm(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        FormCode = r.GetString(r.GetOrdinal("FormCode")),
        FormName = r.GetString(r.GetOrdinal("FormName")),
        Module = r.GetString(r.GetOrdinal("Module")),
        SubModule = r.IsDBNull(r.GetOrdinal("SubModule")) ? null : r.GetString(r.GetOrdinal("SubModule")),
        SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
        BaseTable = r.IsDBNull(r.GetOrdinal("BaseTable")) ? null : r.GetString(r.GetOrdinal("BaseTable")),
        BaseRecordKey = r.IsDBNull(r.GetOrdinal("BaseRecordKey")) ? null : r.GetString(r.GetOrdinal("BaseRecordKey")),
        Icon = HasColumn(r, "Icon") && !r.IsDBNull(r.GetOrdinal("Icon")) ? r.GetString(r.GetOrdinal("Icon")) : null,
        IconColor = HasColumn(r, "IconColor") && !r.IsDBNull(r.GetOrdinal("IconColor")) ? r.GetString(r.GetOrdinal("IconColor")) : null,
        IsMenuItem = HasColumn(r, "IsMenuItem") && !r.IsDBNull(r.GetOrdinal("IsMenuItem")) && r.GetBoolean(r.GetOrdinal("IsMenuItem")),
        MenuKey = HasColumn(r, "MenuKey") && !r.IsDBNull(r.GetOrdinal("MenuKey")) ? r.GetString(r.GetOrdinal("MenuKey")) : null,
        MenuLabel = HasColumn(r, "MenuLabel") && !r.IsDBNull(r.GetOrdinal("MenuLabel")) ? r.GetString(r.GetOrdinal("MenuLabel")) : null,
        MenuLabelEn = HasColumn(r, "MenuLabelEn") && !r.IsDBNull(r.GetOrdinal("MenuLabelEn")) ? r.GetString(r.GetOrdinal("MenuLabelEn")) : null,
        MenuGroupKey = HasColumn(r, "MenuGroupKey") && !r.IsDBNull(r.GetOrdinal("MenuGroupKey")) ? r.GetString(r.GetOrdinal("MenuGroupKey")) : null,
        MenuGroupName = HasColumn(r, "MenuGroupName") && !r.IsDBNull(r.GetOrdinal("MenuGroupName")) ? r.GetString(r.GetOrdinal("MenuGroupName")) : null,
        MenuGroupIcon = HasColumn(r, "MenuGroupIcon") && !r.IsDBNull(r.GetOrdinal("MenuGroupIcon")) ? r.GetString(r.GetOrdinal("MenuGroupIcon")) : null,
        MenuGroupSortOrder = HasColumn(r, "MenuGroupSortOrder") && !r.IsDBNull(r.GetOrdinal("MenuGroupSortOrder")) ? r.GetInt32(r.GetOrdinal("MenuGroupSortOrder")) : null,
        MenuSortOrder = HasColumn(r, "MenuSortOrder") && !r.IsDBNull(r.GetOrdinal("MenuSortOrder")) ? r.GetInt32(r.GetOrdinal("MenuSortOrder")) : null,
        MenuMatchPath = HasColumn(r, "MenuMatchPath") && !r.IsDBNull(r.GetOrdinal("MenuMatchPath")) ? r.GetString(r.GetOrdinal("MenuMatchPath")) : null,
        AdminOnly = HasColumn(r, "AdminOnly") && !r.IsDBNull(r.GetOrdinal("AdminOnly")) && r.GetBoolean(r.GetOrdinal("AdminOnly")),
        IsWidgetForm = !HasColumn(r, "IsWidgetForm") || r.IsDBNull(r.GetOrdinal("IsWidgetForm")) || r.GetBoolean(r.GetOrdinal("IsWidgetForm")),
    };

    private static WidgetDefinition MapWidget(SqlDataReader r)
    {
        // IsSystemField + EntityColumn — yeni Sprint 1 kolonlari; eski DB'lerde
        // olmayabilir (migration sirasinda eklenir). HasColumn ile guvenli oku.
        bool hasSysField = HasColumn(r, "IsSystemField");
        bool hasEntCol   = HasColumn(r, "EntityColumn");
        bool hasPermCtl  = HasColumn(r, "IsPermissionControlled");
        return new()
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            CompanyId = r.IsDBNull(r.GetOrdinal("CompanyId")) ? 0 : r.GetInt32(r.GetOrdinal("CompanyId")),
            FormId = r.GetInt32(r.GetOrdinal("FormId")),
            ParentId = r.IsDBNull(r.GetOrdinal("ParentId")) ? null : r.GetInt32(r.GetOrdinal("ParentId")),
            WidgetCode = r.GetString(r.GetOrdinal("WidgetCode")),
            Label = r.GetString(r.GetOrdinal("Label")),
            DataType = r.GetString(r.GetOrdinal("DataType")),
            MaxLength      = r.IsDBNull(r.GetOrdinal("MaxLength"))      ? null : r.GetInt32(r.GetOrdinal("MaxLength")),
            MinLength      = r.IsDBNull(r.GetOrdinal("MinLength"))      ? null : r.GetInt32(r.GetOrdinal("MinLength")),
            ExpectedLength = r.IsDBNull(r.GetOrdinal("ExpectedLength")) ? null : r.GetInt32(r.GetOrdinal("ExpectedLength")),
            MinValue       = r.IsDBNull(r.GetOrdinal("MinValue"))       ? null : r.GetDecimal(r.GetOrdinal("MinValue")),
            MaxValue       = r.IsDBNull(r.GetOrdinal("MaxValue"))       ? null : r.GetDecimal(r.GetOrdinal("MaxValue")),
            SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
            OptionsJson = r.IsDBNull(r.GetOrdinal("OptionsJSON")) ? null : r.GetString(r.GetOrdinal("OptionsJSON")),
            RulesJson = r.IsDBNull(r.GetOrdinal("RulesJSON")) ? null : r.GetString(r.GetOrdinal("RulesJSON")),
            IsPlainField = !r.IsDBNull(r.GetOrdinal("IsPlainField")) && r.GetBoolean(r.GetOrdinal("IsPlainField")),
            IsRequired = !r.IsDBNull(r.GetOrdinal("IsRequired")) && r.GetBoolean(r.GetOrdinal("IsRequired")),
            IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
            ColorType  = r.IsDBNull(r.GetOrdinal("ColorType"))  ? 0    : r.GetInt32(r.GetOrdinal("ColorType")),
            ColorValue = r.IsDBNull(r.GetOrdinal("ColorValue")) ? null : r.GetString(r.GetOrdinal("ColorValue")),
            ColSpan    = r.IsDBNull(r.GetOrdinal("ColSpan"))    ? 6    : r.GetInt32(r.GetOrdinal("ColSpan")),
            LabelStyle = r.IsDBNull(r.GetOrdinal("LabelStyle")) ? "standard" : r.GetString(r.GetOrdinal("LabelStyle")),
            IsSystemField = hasSysField && !r.IsDBNull(r.GetOrdinal("IsSystemField")) && r.GetBoolean(r.GetOrdinal("IsSystemField")),
            EntityColumn  = (hasEntCol && !r.IsDBNull(r.GetOrdinal("EntityColumn"))) ? r.GetString(r.GetOrdinal("EntityColumn")) : null,
            IsPermissionControlled = hasPermCtl && !r.IsDBNull(r.GetOrdinal("IsPermissionControlled")) && r.GetBoolean(r.GetOrdinal("IsPermissionControlled")),
            CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
            UpdatedAt = r.GetDateTime(r.GetOrdinal("UpdatedAt")),
        };
    }

    /// <summary>Reader'da kolonu var mi? (yeni eklenen optional kolonlar icin guvenli check)</summary>
    private static bool HasColumn(SqlDataReader r, string name)
    {
        for (int i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static WidgetValue MapValue(SqlDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        WidgetId = r.GetInt32(r.GetOrdinal("WidgetId")),
        RecordId = r.GetString(r.GetOrdinal("RecordId")),
        ParentRecordId = r.IsDBNull(r.GetOrdinal("ParentRecordId")) ? null : r.GetString(r.GetOrdinal("ParentRecordId")),
        Value = r.IsDBNull(r.GetOrdinal("Value")) ? null : r.GetString(r.GetOrdinal("Value")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("UpdatedAt")),
    };

    private static void BindWidgetParams(SqlCommand cmd, WidgetDefinition w, bool includePermControlled = true)
    {
        cmd.Parameters.Add(new SqlParameter("@FormId", w.FormId));
        cmd.Parameters.Add(new SqlParameter("@ParentId", (object?)w.ParentId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@WidgetCode", w.WidgetCode));
        cmd.Parameters.Add(new SqlParameter("@Label", w.Label));
        cmd.Parameters.Add(new SqlParameter("@DataType", w.DataType));
        cmd.Parameters.Add(new SqlParameter("@MaxLength",      (object?)w.MaxLength      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MinLength",      (object?)w.MinLength      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ExpectedLength", (object?)w.ExpectedLength ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MinValue",       (object?)w.MinValue       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MaxValue",       (object?)w.MaxValue       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", w.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@OptionsJson", (object?)w.OptionsJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@RulesJson", (object?)w.RulesJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IsPlainField", w.IsPlainField));
        cmd.Parameters.Add(new SqlParameter("@IsRequired", w.IsRequired));
        cmd.Parameters.Add(new SqlParameter("@IsActive", w.IsActive));
        cmd.Parameters.Add(new SqlParameter("@ColorType",  w.ColorType));
        cmd.Parameters.Add(new SqlParameter("@ColorValue", (object?)w.ColorValue ?? DBNull.Value));
        // ColSpan — 1-24 arasi clamp; disinda bir deger gelirse varsayilan 12.
        var safeCol = (w.ColSpan >= 1 && w.ColSpan <= 24) ? w.ColSpan : 12;
        cmd.Parameters.Add(new SqlParameter("@ColSpan", safeCol));
        // LabelStyle — whitelist: "standard", "modern" veya "inline". Diger degerler standart kabul.
        // Not: "inline" eski IsPlainField=true davranisinin yerine gecer (Sprint 0A migration).
        var safeStyle = (w.LabelStyle == "modern" || w.LabelStyle == "inline") ? w.LabelStyle : "standard";
        cmd.Parameters.Add(new SqlParameter("@LabelStyle", safeStyle));
        // Sprint 1 — Universal Form Engine. EntityColumn null olabilir.
        cmd.Parameters.Add(new SqlParameter("@IsSystemField", w.IsSystemField));
        cmd.Parameters.Add(new SqlParameter("@EntityColumn", (object?)w.EntityColumn ?? DBNull.Value));
        // 2026-06-08 — Yetkilendirilebilir alan flag'i. Sadece DB'de IsPermissionControlled
        // kolonu varsa bağlanır (graceful: ALTER TABLE çalıştırılmadığında SQL atılmaz).
        if (includePermControlled)
            cmd.Parameters.Add(new SqlParameter("@IsPermissionControlled", w.IsPermissionControlled));
        cmd.Parameters.Add(new SqlParameter("@UpdatedAt", w.UpdatedAt));
    }

    // ══════════════════════════════════════════════════════════
    // Faz H — Flattened View otomasyonu
    // ══════════════════════════════════════════════════════════

    // Guvenli SQL identifier regex'leri (Faz F allowlist pattern ile ayni)
    private static readonly Regex FormCodeRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex SimpleIdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex QualifiedTableRegex =
        new(@"^(\[?[A-Za-z_][A-Za-z0-9_]{0,63}\]?\.)?\[?[A-Za-z_][A-Za-z0-9_]{0,63}\]?$", RegexOptions.Compiled);

    public async Task RegenerateFlattenedViewAsync(
        FormDefinition form,
        IReadOnlyCollection<WidgetDefinition> widgets,
        CancellationToken ct)
    {
        if (form == null) throw new ArgumentNullException(nameof(form));
        if (string.IsNullOrWhiteSpace(form.BaseTable) || string.IsNullOrWhiteSpace(form.BaseRecordKey))
            return;  // Base table tanimli degil — view uretilmez, sessiz no-op

        // 1) Identifier dogrulama — regex allowlist + bracket escape
        if (!FormCodeRegex.IsMatch(form.FormCode))
            throw new ArgumentException($"Gecersiz FormCode: '{form.FormCode}'");
        var viewName = "v_Flat_" + form.FormCode;   // orn. v_Flat_CONTACTS

        // BaseTable icin iki format destekli: "dbo.Contacts" veya "Contacts"
        var (baseSchema, baseTableName) = ParseQualifiedTable(form.BaseTable);
        if (!SimpleIdentifierRegex.IsMatch(baseSchema))
            throw new ArgumentException($"Gecersiz BaseTable schema: '{baseSchema}'");
        if (!SimpleIdentifierRegex.IsMatch(baseTableName))
            throw new ArgumentException($"Gecersiz BaseTable name: '{baseTableName}'");
        if (!SimpleIdentifierRegex.IsMatch(form.BaseRecordKey))
            throw new ArgumentException($"Gecersiz BaseRecordKey: '{form.BaseRecordKey}'");

        // 2) Widget listesi — sadece aktif, grup ve grid disinda olanlar pivot'a dahil
        var pivotWidgets = widgets
            .Where(w => w.IsActive)
            .Where(w => !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase))
            .Where(w => !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.SortOrder)
            .ToArray();

        // Her widget code'u regex filtresinden gec — widget upsert sirasinda da
        // dogrulaniyor ama double-check (defensive programming)
        foreach (var w in pivotWidgets)
        {
            if (!SimpleIdentifierRegex.IsMatch(w.WidgetCode))
                throw new ArgumentException($"Gecersiz WidgetCode: '{w.WidgetCode}'");
        }

        // 3) Base tablodan kolon listesini cek — INFORMATION_SCHEMA tabanli
        // SELECT base.* ile hepsini alabiliriz ama GROUP BY icin tek tek listelememiz
        // gerekiyor. Ayrica base kolonlar ile widget kolonlari cakisirsa cakismaya karsi
        // uyari verelim (pivot kolonlari skip edilir).
        var baseColumns = await GetTableColumnsAsync(baseSchema, baseTableName, ct);
        if (baseColumns.Count == 0)
            throw new ArgumentException($"Base tablo bos veya erisilemez: {baseSchema}.{baseTableName}");

        // Widget kolonlariyla cakismalari tespit et (skip et ve log — sessiz yol)
        var baseColumnSet = new HashSet<string>(baseColumns, StringComparer.OrdinalIgnoreCase);
        var finalPivot = pivotWidgets
            .Where(w => !baseColumnSet.Contains(w.WidgetCode))
            .ToArray();

        // 4) Dinamik SQL insa et
        var schemaEsc = baseSchema.Replace("]", "]]");
        var tableEsc  = baseTableName.Replace("]", "]]");
        var recordKey = form.BaseRecordKey;

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE OR ALTER VIEW [{schemaEsc}].[{viewName}] AS");
        sb.AppendLine("SELECT");

        // base.* kolonlari tek tek (GROUP BY icin)
        var baseSelectParts = baseColumns.Select(c => $"    base.[{c.Replace("]", "]]")}]").ToList();

        // Pivot widget kolonlari
        var pivotSelectParts = new List<string>();
        foreach (var w in finalPivot)
        {
            var col = w.WidgetCode.Replace("]", "]]");
            var wc  = w.WidgetCode.Replace("'", "''");   // string literal escape
            var dt = (w.DataType ?? "text").ToLowerInvariant();
            string castExpr = dt switch
            {
                "numeric" => $"TRY_CAST(MAX(CASE WHEN m.[WidgetCode] = N'{wc}' THEN t.[Value] END) AS DECIMAL(18,4))",
                "date"    => $"TRY_CAST(MAX(CASE WHEN m.[WidgetCode] = N'{wc}' THEN t.[Value] END) AS DATE)",
                "boolean" => $"CAST(CASE WHEN MAX(CASE WHEN m.[WidgetCode] = N'{wc}' THEN t.[Value] END) = N'true' THEN 1 " +
                             $"WHEN MAX(CASE WHEN m.[WidgetCode] = N'{wc}' THEN t.[Value] END) = N'false' THEN 0 ELSE NULL END AS BIT)",
                // text, dropdown, link, lookup, multi-select (JSON), ve bilinmeyen tipler → NVARCHAR
                _ => $"CAST(MAX(CASE WHEN m.[WidgetCode] = N'{wc}' THEN t.[Value] END) AS NVARCHAR(500))"
            };
            pivotSelectParts.Add($"    {castExpr} AS [{col}]");
        }

        // SELECT clause birlestir
        var allSelect = new List<string>(baseSelectParts);
        allSelect.AddRange(pivotSelectParts);
        sb.AppendLine(string.Join(",\n", allSelect));

        // FROM + JOIN (WidgetTra + WidgetMas)
        sb.AppendLine($"FROM [{schemaEsc}].[{tableEsc}] base");
        sb.AppendLine($"LEFT JOIN {_widgetMasTable} m ON m.[FormId] = {form.Id}");
        sb.AppendLine($"LEFT JOIN {_widgetTraTable} t ON t.[WidgetId] = m.[Id]");
        sb.AppendLine($"    AND t.[RecordId] = CAST(base.[{recordKey.Replace("]", "]]")}] AS NVARCHAR(60))");

        // GROUP BY — base kolonlarinin tamami (pivot widget kolonlari aggregate icinde)
        sb.AppendLine("GROUP BY");
        sb.AppendLine(string.Join(",\n", baseColumns.Select(c => $"    base.[{c.Replace("]", "]]")}]")));
        sb.Append(';');

        var viewSql = sb.ToString();

        // 5) Execute — CREATE OR ALTER tek batch
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = viewSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RegenerateAllFlattenedViewsAsync(CancellationToken ct)
    {
        var forms = await GetFormsAsync(ct);
        foreach (var f in forms.Where(f => !string.IsNullOrWhiteSpace(f.BaseTable) && !string.IsNullOrWhiteSpace(f.BaseRecordKey)))
        {
            try
            {
                var widgets = await GetWidgetsByFormAsync(f.Id, ct);
                await RegenerateFlattenedViewAsync(f, widgets, ct);
            }
            catch (Exception ex)
            {
                // Tek form hatasi digerlerini etkilemesin — log ve devam
                Console.Error.WriteLine($"[FlatView] Regen failed for form '{f.FormCode}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// "dbo.Contacts" veya "Contacts" formatindan (schema, table)
    /// cifti uretir. Schema verilmemisse 'dbo' default. Koseli parantezler soyulur.
    /// </summary>
    private static (string Schema, string Table) ParseQualifiedTable(string raw)
    {
        var clean = raw.Trim().Replace("[", "").Replace("]", "");
        var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return ("dbo", parts[0]);
        if (parts.Length == 2) return (parts[0], parts[1]);
        throw new ArgumentException($"Gecersiz tablo adi: '{raw}'");
    }

    /// <summary>
    /// INFORMATION_SCHEMA.COLUMNS sorgusuyla verilen tablonun tum kolon isimlerini
    /// dondurur. View SQL'inin GROUP BY clause'unda base.* yerine tek tek kolonlari
    /// isimlendirmek icin kullanilir.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetTableColumnsAsync(string schema, string tableName, CancellationToken ct)
    {
        var cols = new List<string>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [COLUMN_NAME]
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @Table
            ORDER BY [ORDINAL_POSITION];";
        cmd.Parameters.Add(new SqlParameter("@Schema", schema));
        cmd.Parameters.Add(new SqlParameter("@Table", tableName));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            cols.Add(r.GetString(0));
        return cols;
    }
}

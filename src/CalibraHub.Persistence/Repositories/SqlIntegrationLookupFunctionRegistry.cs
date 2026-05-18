using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// IIntegrationLookupFunctionRegistry — DB-tabanli implementasyon.
/// Liste/sorgulamayi `IntegrationLookupFunctionDefinitionRepository` uzerinden yapar,
/// IMemoryCache ile 5dk TTL'li okur. Admin paneli kayit eklediginde cache invalidate
/// edilmeli (admin service `Bust` cagrisi ile).
///
/// Resolve calistirildiginda DB'deki view + key kolonu ile satir bulur ve istenen
/// kolonu doner. Identifier dogrulamasi SafeIdentifierRegex ile SQL injection korur.
/// </summary>
public sealed class SqlIntegrationLookupFunctionRegistry : IIntegrationLookupFunctionRegistry
{
    private const string CacheKey = "integration.lookup-functions.v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly Regex SafeIdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);
    // SqlFunctionName: optional "schema." + name. Quotes / spaces / semicolon yok.
    private static readonly Regex SqlFunctionNameRegex =
        new(@"^([A-Za-z_][A-Za-z0-9_]{0,127}\.)?[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

    private readonly IIntegrationLookupFunctionDefinitionRepository _defRepo;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IMemoryCache _cache;
    private readonly string _schema;

    public SqlIntegrationLookupFunctionRegistry(
        IIntegrationLookupFunctionDefinitionRepository defRepo,
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IMemoryCache cache)
    {
        _defRepo = defRepo;
        _connectionFactory = connectionFactory;
        _cache = cache;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    /// <summary>Cache'i geçersizle — admin kayıt ekleyince/değiştirince çağrılır.</summary>
    public static void Invalidate(IMemoryCache cache) => cache.Remove(CacheKey);

    private async Task<IReadOnlyList<CachedFunction>> GetCachedAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<IReadOnlyList<CachedFunction>>(CacheKey, out var cached) && cached is not null)
            return cached;

        var defs = await _defRepo.GetAllAsync(includeInactive: false, ct);
        var list = defs.Select(d => new CachedFunction(
            Id: d.Code,
            Label: d.Label,
            Description: d.Description ?? string.Empty,
            ViewName: d.ViewName,
            KeyColumn: d.KeyColumn,
            SqlSnippet: d.SqlSnippet,
            SqlFunctionName: d.SqlFunctionName,
            ReturnColumns: d.Columns
                .OrderBy(c => c.SortOrder)
                .Select(c => new IntegrationLookupFunctionColumn(c.Column, c.Label))
                .ToList())).ToList();

        _cache.Set(CacheKey, (IReadOnlyList<CachedFunction>)list, CacheTtl);
        return list;
    }

    public IReadOnlyList<IntegrationLookupFunctionDto> List()
    {
        // Sync interface — cache hit'te bekleme yok. Cache miss'te GetAwaiter ile bloklayici.
        var cached = GetCachedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return cached.Select(ToDto).ToList();
    }

    public IntegrationLookupFunctionDto? Get(string functionId)
    {
        var cached = GetCachedAsync(CancellationToken.None).GetAwaiter().GetResult();
        var f = cached.FirstOrDefault(x =>
            string.Equals(x.Id, functionId, StringComparison.OrdinalIgnoreCase));
        return f is null ? null : ToDto(f);
    }

    private static IntegrationLookupFunctionDto ToDto(CachedFunction f) => new(
        Id: f.Id,
        Label: f.Label,
        Description: f.Description,
        ReturnColumns: f.ReturnColumns,
        Kind: !string.IsNullOrWhiteSpace(f.SqlFunctionName) ? "sqlfn"
            : !string.IsNullOrWhiteSpace(f.SqlSnippet)      ? "snippet"
            : "view");

    public Task<object?> ResolveWithParamsAsync(
        string functionId,
        string? formCode,
        string? keyValue,
        string? manualParam,
        string? returnColumn,
        CancellationToken ct)
        => ResolveInternalAsync(functionId, formCode, keyValue, manualParam, returnColumn, ct);

    public async Task<object?> ExecuteDbFunctionAsync(
        string functionFullName,
        string? formCode,
        string? keyValue,
        string? manualParam,
        CancellationToken ct)
    {
        // Bu yol wrapper tablosunu (IntegrationLookupFunction) by-pass eder — direkt DB function call.
        // Wizard'in "Fonksiyon" source dropdown'undan secilen sema.fn ile cagrilir.
        var virtualSpec = new CachedFunction(
            Id: functionFullName,
            Label: functionFullName,
            Description: string.Empty,
            ViewName: null,
            KeyColumn: null,
            SqlSnippet: null,
            SqlFunctionName: functionFullName,
            ReturnColumns: Array.Empty<IntegrationLookupFunctionColumn>());
        return await ResolveSqlFunctionAsync(virtualSpec, formCode, keyValue, manualParam, ct);
    }

    public async Task<IReadOnlyList<AvailableDbFunctionDto>> ListDbFunctionsAsync(CancellationToken ct)
    {
        return await _defRepo.ListAvailableFunctionsAsync(ct);
    }

    public Task<object?> ResolveAsync(
        string functionId, string? keyValue, string? returnColumn, CancellationToken ct)
        => ResolveInternalAsync(functionId, formCode: null, keyValue, manualParam: null, returnColumn, ct);

    private async Task<object?> ResolveInternalAsync(
        string functionId, string? formCode, string? keyValue, string? manualParam,
        string? returnColumn, CancellationToken ct)
    {
        var cached = await GetCachedAsync(ct);
        var spec = cached.FirstOrDefault(x =>
            string.Equals(x.Id, functionId, StringComparison.OrdinalIgnoreCase));
        if (spec is null) return null;

        // ── Mode 0: SqlFunctionName (3-param standart imza) — YENI ────────
        if (!string.IsNullOrWhiteSpace(spec.SqlFunctionName))
            return await ResolveSqlFunctionAsync(spec, formCode, keyValue, manualParam, ct);

        // ── Mode 1: SqlSnippet (serbest SQL) — LEGACY ─────────────────────
        if (!string.IsNullOrWhiteSpace(spec.SqlSnippet))
            return await ResolveSqlSnippetAsync(spec, keyValue, returnColumn, ct);

        // ── Mode 2: View + Key (klasik lookup) ────────────────────────────
        if (string.IsNullOrWhiteSpace(keyValue)) return null;
        if (string.IsNullOrWhiteSpace(spec.ViewName) || string.IsNullOrWhiteSpace(spec.KeyColumn))
            return null;
        if (spec.ReturnColumns.Count == 0) return null;

        var col = string.IsNullOrWhiteSpace(returnColumn)
            ? spec.ReturnColumns[0].Column
            : returnColumn;
        var hasCol = spec.ReturnColumns.Any(c =>
            string.Equals(c.Column, col, StringComparison.OrdinalIgnoreCase));
        if (!hasCol) col = spec.ReturnColumns[0].Column;

        if (!SafeIdentifierRegex.IsMatch(spec.ViewName)) return null;
        if (!SafeIdentifierRegex.IsMatch(spec.KeyColumn)) return null;
        if (!SafeIdentifierRegex.IsMatch(col)) return null;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText =
                $"SELECT CASE WHEN OBJECT_ID(N'[{_schema}].[{spec.ViewName}]', N'V') IS NOT NULL THEN 1 ELSE 0 END;";
            var exists = ((int)(await chk.ExecuteScalarAsync(ct) ?? 0)) == 1;
            if (!exists) return null;
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 1 [{col}] FROM [{_schema}].[{spec.ViewName}]
            WHERE CAST([{spec.KeyColumn}] AS NVARCHAR(100)) = @Key;";
        cmd.Parameters.Add(new SqlParameter("@Key", keyValue));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result == DBNull.Value ? null : result;
    }

    /// <summary>
    /// Mode 0: DB'de tanimli scalar function'i 3 param ile calistirir.
    /// Cagri: SELECT [schema].[fnName](@P1, @P2, @P3)
    ///   @P1 = formCode (mapping engine'in saglayacagi standart "form bilgisi")
    ///   @P2 = keyValue (mapping satirinin LookupSourceField alanindan)
    ///   @P3 = manualParam (mapping satirinin LookupParam alanindan, kullanici serbest yazar)
    /// Hatalar (gecersiz fonksiyon adi, runtime exception, vb.) sessizce null doner —
    /// integration runner ErrorBehavior'a gore (Skip/Retry/Manual) tepki verir.
    /// </summary>
    private async Task<object?> ResolveSqlFunctionAsync(
        CachedFunction spec, string? formCode, string? keyValue, string? manualParam, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spec.SqlFunctionName)) return null;
        if (!SqlFunctionNameRegex.IsMatch(spec.SqlFunctionName!)) return null;

        // Schema.fn formatini parcala — quoted identifier'la wrap et
        string quoted;
        var trimmed = spec.SqlFunctionName!.Trim();
        var dotIx = trimmed.IndexOf('.');
        if (dotIx > 0 && dotIx < trimmed.Length - 1)
        {
            var sch = trimmed.Substring(0, dotIx);
            var fnm = trimmed.Substring(dotIx + 1);
            quoted = $"[{sch}].[{fnm}]";
        }
        else
        {
            quoted = $"[{_schema}].[{trimmed}]";
        }

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"SELECT {quoted}(@P1, @P2, @P3);";
        cmd.Parameters.Add(new SqlParameter("@P1", (object?)formCode    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@P2", (object?)keyValue    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@P3", (object?)manualParam ?? DBNull.Value));
        cmd.CommandTimeout = 10;

        try
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            return result == DBNull.Value ? null : result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serbest SQL snippet calistirir. @Key parametresi binding ile gecirilir.
    /// returnColumn = SELECT'in dondurdugu kolon adi (case-insensitive). Bos veya
    /// bulunamazsa ilk kolon kullanilir.
    /// </summary>
    private async Task<object?> ResolveSqlSnippetAsync(
        CachedFunction spec, string? keyValue, string? returnColumn, CancellationToken ct)
    {
        // Guvenlik: sadece SELECT yazilmis olmali; UPDATE/DELETE/DROP/EXEC vs. reddet
        var secError = CalibraHub.Application.Services.Integration.IntegrationSqlSecurity
            .ValidateSelectOnly(spec.SqlSnippet);
        if (secError is not null) return null;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = spec.SqlSnippet!;
        cmd.Parameters.Add(new SqlParameter("@Key", (object?)keyValue ?? DBNull.Value));
        cmd.CommandTimeout = 10;

        try
        {
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct)) return null;

            // returnColumn varsa o kolonu doner; yoksa ilk kolon
            if (!string.IsNullOrWhiteSpace(returnColumn))
            {
                for (int i = 0; i < rd.FieldCount; i++)
                {
                    if (string.Equals(rd.GetName(i), returnColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        var v = rd.GetValue(i);
                        return v == DBNull.Value ? null : v;
                    }
                }
            }
            var first = rd.GetValue(0);
            return first == DBNull.Value ? null : first;
        }
        catch
        {
            return null;
        }
    }

    private sealed record CachedFunction(
        string Id,
        string Label,
        string Description,
        string? ViewName,
        string? KeyColumn,
        string? SqlSnippet,
        string? SqlFunctionName,
        IReadOnlyList<IntegrationLookupFunctionColumn> ReturnColumns);
}

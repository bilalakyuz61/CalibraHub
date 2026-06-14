using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL View tabanli jenerik Rehber (Lookup) persistence katmani.
///
/// Proje mevcut raw ADO.NET pattern'ini koruyor; yalnizca bu repository icin
/// Dapper kullaniliyor — dinamik kolonlu sorgu satirlarini dynamic dictionary
/// olarak almayi kolaylastiriyor, GetAll / Resolve icin de strongly-typed
/// mapping'i tek satirda veriyor.
///
/// ═══ SQL INJECTION SAVUNMA KATMANI ═══
///   - ViewName, ValueColumn, DisplayColumn, GridColumnsJson icindeki tum
///     kolonlar, sortColumn — hepsi IdentifierRegex allowlist'ten geciyor.
///   - sortDirection yalnizca 'ASC' veya 'DESC'.
///   - search parametresi @Search olarak Dapper DynamicParameters ile
///     parametreli, LIKE karakterleri (% _ [) escape ediliyor.
///   - ViewName cok parcali olabilir ('schema.view') — parcalar ayri ayri
///     validate edilip [bracket] ile birlestiriliyor.
/// </summary>
public sealed class SqlGuideRepository : IGuideRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schemaName;
    private readonly string _guideMasTable;

    // Identifier allowlist: harfle basla, harf/rakam/altcizgi; max 64 karakter.
    private static readonly Regex IdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    public SqlGuideRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schemaName = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _guideMasTable = $"[{_schemaName.Replace("]", "]]")}].[GuideMas]";
    }

    // ══════════════════════════════════════════════════════════
    // GuideMas runtime resolve API
    // (PR 3: GetAllAsync admin metodu kaldirildi — UI artik /api/guides/views
    //  uzerinden fiziksel view'lari listeliyor; GuideMas kaydi sadece runtime
    //  resolve sirasinda GetByCodeAsync ile teker teker okunuyor.)
    // ══════════════════════════════════════════════════════════

    public async Task<GuideDefinition?> GetByCodeAsync(string guideCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(guideCode)) return null;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Hem GuideCode hem ViewName ile eslestirme: PR 2+'da raw view'lar (cbv_Guide_*)
        // direkt secilebiliyor — bu durumda guideCode yerine viewName geliyor. SQL Server
        // default CI collation case-insensitive eslestirmeyi otomatik halleder.
        // Duplikat ViewName varsa (legacy data) ORDER BY Id DESC ile son giren kazanir
        // (kullanici feedback'i: "en alttakinin icerigi dogru").
        cmd.CommandText = $@"
            SELECT TOP(1) [Id],[GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],
                          [GridColumnsJson],[DefaultSortColumn],[DefaultFilterJson],
                          [IsActive],[CreatedAt],[UpdatedAt]
            FROM {_guideMasTable}
            WHERE ([GuideCode] = @Code OR [ViewName] = @Code) AND [IsActive] = 1
            ORDER BY [Id] DESC;";
        cmd.Parameters.AddWithValue("@Code", guideCode);

        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
        {
            if (await reader.ReadAsync(ct))
            {
                return new GuideDefinition
                {
                    Id = reader.GetInt32(0),
                    GuideCode = reader.GetString(1),
                    GuideLabel = reader.GetString(2),
                    ViewName = reader.GetString(3),
                    ValueColumn = reader.GetString(4),
                    DisplayColumn = reader.GetString(5),
                    GridColumnsJson = reader.GetString(6),
                    DefaultSortColumn = reader.IsDBNull(7) ? null : reader.GetString(7),
                    DefaultFilterJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                    IsActive = reader.GetBoolean(9),
                    CreatedAt = reader.GetDateTime(10),
                    UpdatedAt = reader.GetDateTime(11)
                };
            }
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════
    // Arama — dinamik SQL (guvenli)
    // ══════════════════════════════════════════════════════════

    public async Task<GuideSearchResultDto> SearchAsync(
        GuideDefinition guide,
        string? search,
        int page,
        int pageSize,
        string? sortColumn,
        string? sortDirection,
        CancellationToken ct,
        IReadOnlyCollection<GuideConstraintDto>? constraints = null)
    {
        // 1) Input clamp
        if (page < 1) page = 1;
        if (pageSize < 10) pageSize = 10;
        if (pageSize > 200) pageSize = 200;

        // 2) Kolon listesi parse + allowlist
        var rawColumns = (JsonSerializer.Deserialize<string[]>(guide.GridColumnsJson) ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => ValidateIdentifier(c.Trim(), "Column"))
            .ToArray();
        if (rawColumns.Length == 0)
            throw new ArgumentException($"Guide '{guide.GuideCode}' icin GridColumnsJson bos.");

        var valueCol = ValidateIdentifier(guide.ValueColumn, "ValueColumn");
        var displayCol = ValidateIdentifier(guide.DisplayColumn, "DisplayColumn");

        // 2b) Defensive: GuideMas.GridColumnsJson view ile drift edebilir (stale seed,
        // view kolon rename/drop). View'in gercek kolonlarini INFORMATION_SCHEMA'dan
        // cek, kesisimi al — boylece olmayan kolon SELECT'e girmez (207 hatasi yok).
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var actualCols = await GetActualViewColumnsAsync(conn, guide.ViewName, ct);
        if (actualCols.Count == 0)
            throw new ArgumentException(
                $"Guide '{guide.GuideCode}' icin view '{guide.ViewName}' DB'de bulunamadi.");

        var columns = rawColumns.Where(c => actualCols.Contains(c)).ToArray();
        if (columns.Length == 0)
            throw new ArgumentException(
                $"Guide '{guide.GuideCode}' GridColumnsJson view '{guide.ViewName}' kolonlariyla eslesmiyor. " +
                $"View kolonlari: [{string.Join(", ", actualCols)}], beklenen: [{string.Join(", ", rawColumns)}].");

        if (!actualCols.Contains(valueCol))
            throw new ArgumentException(
                $"Guide '{guide.GuideCode}' ValueColumn '{valueCol}' view '{guide.ViewName}' kolonlarinda yok.");
        if (!actualCols.Contains(displayCol))
            throw new ArgumentException(
                $"Guide '{guide.GuideCode}' DisplayColumn '{displayCol}' view '{guide.ViewName}' kolonlarinda yok.");

        // 3) sortColumn allowlist: sadece view kolonlari + Value/Display
        var allSortables = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase)
            { valueCol, displayCol };
        var actualSort = string.IsNullOrWhiteSpace(sortColumn)
            ? (guide.DefaultSortColumn ?? valueCol)
            : sortColumn;
        if (!allSortables.Contains(actualSort))
            actualSort = guide.DefaultSortColumn ?? valueCol;
        actualSort = ValidateIdentifier(actualSort, "SortColumn");

        var direction = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";

        // 4) SELECT kolon listesi — Value + Display + grid kolonlari (benzersiz)
        var selectColumns = new List<string>(columns.Length + 2);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in new[] { valueCol, displayCol }.Concat(columns))
        {
            if (seen.Add(c))
                selectColumns.Add($"[{c}]");
        }
        var selectClause = string.Join(", ", selectColumns);

        var view = BracketView(guide.ViewName);

        // 5) WHERE — tokenized arama: her kelime ayri ayri tum kolonlarda aranir (AND)
        var whereParts = new List<string>();
        var searchTerms = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            searchTerms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var t = 0; t < searchTerms.Length; t++)
            {
                var paramName = $"@Search{t}";
                // CAST AS NVARCHAR: INT/DECIMAL/DATETIME gibi string olmayan kolonlarda da
                // LIKE + COLLATE Turkish_CI_AI guvenli calisir.
                var likes = columns.Select(c =>
                    $"CAST([{c}] AS NVARCHAR(400)) COLLATE Turkish_CI_AI LIKE {paramName} ESCAPE '^'"
                ).ToArray();
                whereParts.Add("(" + string.Join(" OR ", likes) + ")");
            }
        }

        // 5b) Dinamik kisitlar (constraints) — SQL Injection korumalı
        // Her kisit AND veya OR ile birlestirilir (Logic property).
        // Gruplama: ardisik AND'ler parantez icinde, OR ile ayrilir.
        //
        // GuideMas.DefaultFilterJson — rehber bazli varsayilan WHERE fragment.
        // FldSet field-level filtresinden BAGIMSIZ olarak her arama cagrisinda
        // otomatik prepend edilir (AND ile birlesir). Boylece bir rehbere bir
        // kez verilen kisit (orn. cbv_Guide_Items: TYPID IN (2,3)) bu rehberin
        // kullanildigi tum form alanlarinda — BOM mamul, is emri mamul, satir
        // grid'i, vs. — otomatik gecerli olur. Token desteklenmiyor (global, form-bagimsiz).
        var effectiveConstraints = new List<GuideConstraintDto>();
        if (!string.IsNullOrWhiteSpace(guide.DefaultFilterJson))
        {
            effectiveConstraints.Add(new GuideConstraintDto(
                Field: null, Operator: null, Value: null,
                Logic: "and",
                RawSql: guide.DefaultFilterJson.Trim()));
        }
        if (constraints is { Count: > 0 })
        {
            effectiveConstraints.AddRange(constraints);
        }
        var constraintParams = new List<(string ParamName, object Value)>();
        var constraintSqlParts = new List<(string Sql, string Logic)>(); // (SQL fragment, logic)
        if (effectiveConstraints.Count > 0)
        {
            constraints = effectiveConstraints;
            var pIdx = 0;
            foreach (var c in constraints)
            {
                // Logic: yalnizca "and" veya "or" — diger degerler "and" olarak davranir (raw SQL icin de gecerli)
                var rawLogic = (c.Logic ?? "and").Trim().ToLowerInvariant();
                if (rawLogic != "or") rawLogic = "and";

                // RawSql modu: admin Alan Ayarlari'nda serbest SQL fragment yazdiysa,
                // direkt WHERE'a append. Token (formdaki {#fieldId}) frontend'de
                // resolve edilmis olarak gelir. Column allowlist YOK (admin sorumlu).
                if (!string.IsNullOrWhiteSpace(c.RawSql))
                {
                    var trimmedRaw = c.RawSql.Trim();
                    constraintSqlParts.Add(($"({trimmedRaw})", rawLogic));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(c.Field) || string.IsNullOrWhiteSpace(c.Value)) continue;

                // Güvenlik 1: kolon adi allowlist'ten gecmeli
                var safeField = ValidateIdentifier(c.Field.Trim(), "ConstraintField");

                // Logic: yalnizca "and" veya "or" — diger degerler "and" olarak davranir
                var logic = (c.Logic ?? "and").Trim().ToLowerInvariant();
                if (logic != "or") logic = "and";

                // Güvenlik 2: operatör switch-case ile — asla kullanicidan gelen string SQL'e yazilmaz
                string? sqlFragment = null;
                var op = (c.Operator ?? "eq").Trim().ToLowerInvariant();
                switch (op)
                {
                    case "eq":
                        sqlFragment = $"[{safeField}] = @cp{pIdx}";
                        constraintParams.Add(($"@cp{pIdx}", c.Value.Trim()));
                        pIdx++;
                        break;
                    case "neq":
                        sqlFragment = $"[{safeField}] <> @cp{pIdx}";
                        constraintParams.Add(($"@cp{pIdx}", c.Value.Trim()));
                        pIdx++;
                        break;
                    case "gt":
                        sqlFragment = $"[{safeField}] > @cp{pIdx}";
                        constraintParams.Add(($"@cp{pIdx}", c.Value.Trim()));
                        pIdx++;
                        break;
                    case "lt":
                        sqlFragment = $"[{safeField}] < @cp{pIdx}";
                        constraintParams.Add(($"@cp{pIdx}", c.Value.Trim()));
                        pIdx++;
                        break;
                    case "like":
                        var likeVal = c.Value.Trim()
                            .Replace("^", "^^").Replace("%", "^%").Replace("_", "^_").Replace("[", "^[");
                        sqlFragment = $"[{safeField}] COLLATE Turkish_CI_AI LIKE @cp{pIdx} ESCAPE '^'";
                        constraintParams.Add(($"@cp{pIdx}", "%" + likeVal + "%"));
                        pIdx++;
                        break;
                    case "in":
                        // IN operatörü: virgülle ayrılmış değerler → ayrı parametreler
                        var inVals = c.Value.Split(',')
                            .Select(v => v.Trim())
                            .Where(v => v.Length > 0)
                            .ToArray();
                        if (inVals.Length > 0)
                        {
                            var inParams = new List<string>();
                            foreach (var iv in inVals)
                            {
                                inParams.Add($"@cp{pIdx}");
                                constraintParams.Add(($"@cp{pIdx}", iv));
                                pIdx++;
                            }
                            sqlFragment = $"[{safeField}] IN ({string.Join(",", inParams)})";
                        }
                        break;
                    default:
                        // Bilinmeyen operatör sessizce atlanır — güvenlik
                        break;
                }

                if (sqlFragment != null)
                    constraintSqlParts.Add((sqlFragment, logic));
            }

            // Constraint parcalarini VE/VEYA mantigi ile birlestir
            if (constraintSqlParts.Count > 0)
            {
                var constraintClause = constraintSqlParts[0].Sql;
                for (int i = 1; i < constraintSqlParts.Count; i++)
                {
                    var joiner = constraintSqlParts[i].Logic == "or" ? " OR " : " AND ";
                    constraintClause += joiner + constraintSqlParts[i].Sql;
                }
                // Tum constraint blogu parantez icinde whereParts'a eklenir
                whereParts.Add("(" + constraintClause + ")");
            }
        }

        var whereClause = whereParts.Count > 0
            ? "WHERE " + string.Join(" AND ", whereParts)
            : string.Empty;

        // 6) OFFSET / FETCH — hasMore hesabi icin pageSize + 1 satir cek
        var offset = (page - 1) * pageSize;
        var sql = $@"
            SELECT {selectClause}
            FROM {view}
            {whereClause}
            ORDER BY [{actualSort}] {direction}
            OFFSET @Offset ROWS FETCH NEXT @Fetch ROWS ONLY;";

        // 7) Execute — raw ADO.NET SqlDataReader (conn step 2b'de acildi)
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Offset", offset);
        cmd.Parameters.AddWithValue("@Fetch", pageSize + 1);

        for (var t = 0; t < searchTerms.Length; t++)
        {
            var escaped = searchTerms[t]
                .Replace("^", "^^")
                .Replace("%", "^%")
                .Replace("_", "^_")
                .Replace("[", "^[");
            cmd.Parameters.AddWithValue($"@Search{t}", "%" + escaped + "%");
        }

        // Constraint parametreleri — her biri güvenli parametrize
        foreach (var (paramName, paramValue) in constraintParams)
        {
            cmd.Parameters.AddWithValue(paramName, paramValue);
        }

        var rows = new List<GuideRowDto>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var cells = new Dictionary<string, object?>(columns.Length, StringComparer.OrdinalIgnoreCase);
                var vObj = reader[valueCol];
                var dObj = reader[displayCol];

                foreach (var col in columns)
                {
                    var ordinal = reader.GetOrdinal(col);
                    cells[col] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
                }

                rows.Add(new GuideRowDto(
                    Value: vObj == DBNull.Value ? string.Empty : vObj?.ToString() ?? string.Empty,
                    Display: dObj == DBNull.Value ? string.Empty : dObj?.ToString() ?? string.Empty,
                    Cells: cells));
            }
        }

        // 8) hasMore — fazla satir varsa son satiri at
        var hasMore = rows.Count > pageSize;
        if (hasMore && rows.Count > 0)
            rows.RemoveAt(rows.Count - 1);

        return new GuideSearchResultDto(rows, columns.AsReadOnly(), page, pageSize, hasMore);
    }

    // ══════════════════════════════════════════════════════════
    // Tek value → display cozumleme
    // ══════════════════════════════════════════════════════════

    public async Task<GuideResolveDto?> ResolveAsync(GuideDefinition guide, string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var valueCol = ValidateIdentifier(guide.ValueColumn, "ValueColumn");
        var displayCol = ValidateIdentifier(guide.DisplayColumn, "DisplayColumn");
        var view = BracketView(guide.ViewName);

        var sql = $@"
            SELECT TOP(1) *
            FROM {view}
            WHERE [{valueCol}] = @Value;";

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Value", value);

        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
        {
            if (await reader.ReadAsync(ct))
            {
                var cells = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var colName = reader.GetName(i);
                    var colVal = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    cells[colName] = colVal;
                }

                var v = cells.TryGetValue(valueCol, out var vObj) && vObj != null ? vObj.ToString() ?? "" : "";
                var d = cells.TryGetValue(displayCol, out var dObj) && dObj != null ? dObj.ToString() : null;

                return new GuideResolveDto(Value: v, Display: d, Cells: cells);
            }
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════
    // Distinct degerler — filtre cipleri icin
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<string>> GetDistinctValuesAsync(
        GuideDefinition guide, string column, string? search, CancellationToken ct,
        IReadOnlyCollection<GuideConstraintDto>? constraints = null)
    {
        // 1) Kolon allowlist: GridColumnsJson icinde olmali
        var gridColumns = (JsonSerializer.Deserialize<string[]>(guide.GridColumnsJson) ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToArray();

        var safeColumn = ValidateIdentifier(column.Trim(), "Column");
        if (!gridColumns.Any(c => string.Equals(c, safeColumn, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(
                $"Kolon '{safeColumn}' guide '{guide.GuideCode}' icin tanimli degil.");

        var view = BracketView(guide.ViewName);

        // 1b) Defensive: view'in gercek kolonlarinda da olmali (stale GuideMas seed savunmasi)
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var actualCols = await GetActualViewColumnsAsync(conn, guide.ViewName, ct);
        if (!actualCols.Contains(safeColumn))
            throw new ArgumentException(
                $"Kolon '{safeColumn}' view '{guide.ViewName}' kolonlarinda yok. " +
                $"View kolonlari: [{string.Join(", ", actualCols)}].");

        // 2) DISTINCT sorgu — NULL/bos atilir, alfabetik siralanir, max 200 satir.
        //    Veri kalitesi normalize: NBSP (NCHAR(160)) → bosluk; LTRIM/RTRIM ile
        //    bas/son bosluklar kirpilir; Turkish_CI_AI collation ile case+accent
        //    farklari ayni distinct'e dusurulur. Boylece "TRABZON", "TRABZON ",
        //    "trabzon", "TRABZON " varyantlari popover'da TEK satir gozukur.
        //    search non-empty ise ayni normalize'li ifade uzerinde LIKE @Search.
        var normalizedExpr = "LTRIM(RTRIM(REPLACE(CAST([" + safeColumn + "] AS NVARCHAR(400)), NCHAR(160), N' '))) COLLATE Turkish_CI_AI";

        var hasSearch = !string.IsNullOrWhiteSpace(search);

        // 3) Constraint WHERE building — SearchAsync ile birebir mantik.
        //    guide.DefaultFilterJson her zaman AND ile prepend edilir; ardindan
        //    caller'in constraint'leri eklenir (rawSql/eq/in/...). Distinct popover
        //    listede gosterilen satirlardan turetilir → "view'a verilen filtrelere
        //    gore dolar" (rapor: kullanici geri bildirimi 2026-05-18).
        var (constraintClause, constraintParams) =
            BuildConstraintWhereFragment(guide, constraints);

        var whereParts = new List<string>
        {
            $"[{safeColumn}] IS NOT NULL",
            $"LTRIM(RTRIM(CAST([{safeColumn}] AS NVARCHAR(400)))) <> ''",
        };
        if (hasSearch) whereParts.Add(normalizedExpr + " LIKE @Search ESCAPE '^'");
        if (!string.IsNullOrEmpty(constraintClause)) whereParts.Add(constraintClause);
        var whereClause = "WHERE " + string.Join(" AND ", whereParts);

        var sql = $@"
            SELECT DISTINCT TOP(200) {normalizedExpr} AS V
            FROM {view}
            {whereClause}
            ORDER BY V;";

        // conn step 1b'de acildi
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (hasSearch)
        {
            // SearchAsync ile ayni escape: % _ [ kullanici girisinde literal olsun
            var escaped = search!.Trim()
                .Replace("^", "^^")
                .Replace("%", "^%")
                .Replace("_", "^_")
                .Replace("[", "^[");
            cmd.Parameters.AddWithValue("@Search", "%" + escaped + "%");
        }
        foreach (var (paramName, paramValue) in constraintParams)
        {
            cmd.Parameters.AddWithValue(paramName, paramValue);
        }

        var values = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                {
                    var v = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(v)) values.Add(v);
                }
            }
        }
        return values.AsReadOnly();
    }

    // ══════════════════════════════════════════════════════════
    // Constraint helper — SearchAsync + GetDistinctValuesAsync paylasir
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// SearchAsync icindeki constraint→WHERE fragment olusturma mantiginin
    /// extracted hali (GetDistinctValuesAsync ile paylasilir). guide.DefaultFilterJson
    /// her cagrida AND ile prepend edilir; ardindan caller'in constraint listesi gelir.
    /// Donus: (WHERE fragment, parametre listesi). Fragment bos olabilir (constraint
    /// hic yoksa). Parametre adlari @cp0, @cp1, ... uretilir.
    ///
    /// NOT: SearchAsync su anda kendi inline implementasyonunu kullaniyor; bu
    /// helper ileride SearchAsync refactor edildiginde de kullanilabilir
    /// (sadece test maliyeti var, fonksiyonel duplikasyon birebir esit kaldi).
    /// </summary>
    private static (string Clause, List<(string ParamName, object Value)> Params)
        BuildConstraintWhereFragment(GuideDefinition guide, IReadOnlyCollection<GuideConstraintDto>? constraints)
    {
        var effective = new List<GuideConstraintDto>();
        if (!string.IsNullOrWhiteSpace(guide.DefaultFilterJson))
        {
            effective.Add(new GuideConstraintDto(
                Field: null, Operator: null, Value: null,
                Logic: "and",
                RawSql: guide.DefaultFilterJson.Trim()));
        }
        if (constraints is { Count: > 0 }) effective.AddRange(constraints);

        var sqlParts = new List<(string Sql, string Logic)>();
        var paramList = new List<(string ParamName, object Value)>();
        var pIdx = 0;

        foreach (var c in effective)
        {
            var rawLogic = (c.Logic ?? "and").Trim().ToLowerInvariant();
            if (rawLogic != "or") rawLogic = "and";

            if (!string.IsNullOrWhiteSpace(c.RawSql))
            {
                sqlParts.Add(($"({c.RawSql!.Trim()})", rawLogic));
                continue;
            }
            if (string.IsNullOrWhiteSpace(c.Field) || string.IsNullOrWhiteSpace(c.Value)) continue;

            var safeField = ValidateIdentifier(c.Field.Trim(), "ConstraintField");
            string? fragment = null;
            var op = (c.Operator ?? "eq").Trim().ToLowerInvariant();
            switch (op)
            {
                case "eq":
                    fragment = $"[{safeField}] = @cp{pIdx}";
                    paramList.Add(($"@cp{pIdx}", c.Value.Trim())); pIdx++; break;
                case "neq":
                    fragment = $"[{safeField}] <> @cp{pIdx}";
                    paramList.Add(($"@cp{pIdx}", c.Value.Trim())); pIdx++; break;
                case "gt":
                    fragment = $"[{safeField}] > @cp{pIdx}";
                    paramList.Add(($"@cp{pIdx}", c.Value.Trim())); pIdx++; break;
                case "lt":
                    fragment = $"[{safeField}] < @cp{pIdx}";
                    paramList.Add(($"@cp{pIdx}", c.Value.Trim())); pIdx++; break;
                case "like":
                    var likeVal = c.Value.Trim()
                        .Replace("^", "^^").Replace("%", "^%").Replace("_", "^_").Replace("[", "^[");
                    fragment = $"[{safeField}] COLLATE Turkish_CI_AI LIKE @cp{pIdx} ESCAPE '^'";
                    paramList.Add(($"@cp{pIdx}", "%" + likeVal + "%")); pIdx++; break;
                case "in":
                    var inVals = c.Value.Split(',')
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .ToArray();
                    if (inVals.Length > 0)
                    {
                        var inParams = new List<string>();
                        foreach (var iv in inVals)
                        {
                            inParams.Add($"@cp{pIdx}");
                            paramList.Add(($"@cp{pIdx}", iv));
                            pIdx++;
                        }
                        fragment = $"[{safeField}] IN ({string.Join(",", inParams)})";
                    }
                    break;
            }
            if (fragment != null) sqlParts.Add((fragment, rawLogic));
        }

        if (sqlParts.Count == 0) return (string.Empty, paramList);

        var clause = sqlParts[0].Sql;
        for (int i = 1; i < sqlParts.Count; i++)
        {
            var joiner = sqlParts[i].Logic == "or" ? " OR " : " AND ";
            clause += joiner + sqlParts[i].Sql;
        }
        return ("(" + clause + ")", paramList);
    }

    // ══════════════════════════════════════════════════════════
    // Guvenlik helper'lari
    // ══════════════════════════════════════════════════════════

    private static string ValidateIdentifier(string raw, string role)
    {
        if (string.IsNullOrWhiteSpace(raw) || !IdentifierRegex.IsMatch(raw))
            throw new ArgumentException(
                $"Gecersiz {role}: '{raw}'. Sadece harf/rakam/altcizgi, harfle baslamali, max 64 karakter.");
        return raw;
    }

    // ══════════════════════════════════════════════════════════
    // Admin CRUD — ListViews, GetViewColumns, Upsert, Delete
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<GuideViewInfoDto>> ListGuideViewsAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) cbv_Guide_% view listesi
        var viewList = new List<(string SchemaName, string ViewName)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.[name] AS SchemaName, v.[name] AS ViewName
                FROM sys.views v
                INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
                WHERE v.[name] LIKE 'cbv[_]Guide[_]%'
                ORDER BY s.[name], v.[name];";
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
            {
                while (await reader.ReadAsync(ct))
                    viewList.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        // 2) Her view icin kolon listesi
        var result = new List<GuideViewInfoDto>(viewList.Count);
        foreach (var (schemaName, viewName) in viewList)
        {
            if (!IdentifierRegex.IsMatch(viewName) || !IdentifierRegex.IsMatch(schemaName))
                continue;

            var cols = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT [COLUMN_NAME]
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @View
                    ORDER BY [ORDINAL_POSITION];";
                cmd.Parameters.AddWithValue("@Schema", schemaName);
                cmd.Parameters.AddWithValue("@View", viewName);
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
                {
                    while (await reader.ReadAsync(ct))
                    {
                        var col = reader.GetString(0);
                        if (IdentifierRegex.IsMatch(col)) cols.Add(col);
                    }
                }
            }
            result.Add(new GuideViewInfoDto(viewName, schemaName, cols.AsReadOnly()));
        }
        return result;
    }

    public async Task<IReadOnlyCollection<string>> GetViewColumnsAsync(string viewName, CancellationToken ct)
    {
        ValidateIdentifier(viewName, "ViewName");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var cols = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT [COLUMN_NAME]
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @View
                ORDER BY [ORDINAL_POSITION];";
            cmd.Parameters.AddWithValue("@Schema", _schemaName);
            cmd.Parameters.AddWithValue("@View", viewName);
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var col = reader.GetString(0);
                    if (IdentifierRegex.IsMatch(col)) cols.Add(col);
                }
            }
        }
        return cols.AsReadOnly();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetViewColumnTypesAsync(string viewName, CancellationToken ct)
    {
        ValidateIdentifier(viewName, "ViewName");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [COLUMN_NAME], [DATA_TYPE]
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @View
            ORDER BY [ORDINAL_POSITION];";
        cmd.Parameters.AddWithValue("@Schema", _schemaName);
        cmd.Parameters.AddWithValue("@View", viewName);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct);
        while (await reader.ReadAsync(ct))
        {
            var col = reader.GetString(0);
            var typ = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (IdentifierRegex.IsMatch(col)) types[col] = typ;
        }
        return types;
    }

    // PR 3: UpsertAsync ve DeleteAsync admin metodlari kaldirildi — UI artik
    // /api/guides/views uzerinden fiziksel view'lari direkt kullaniyor.
    // GuideMas kayitlari startup auto-discovery (DiscoverAndRegisterGuidesAsync)
    // ile yonetiliyor; manuel CRUD gereksiz.

    // ══════════════════════════════════════════════════════════
    // Guvenlik helper'lari
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// "cbv_Guide_Items" → "[dbo].[cbv_Guide_Items]"
    /// "customSchema.cbv_Guide_Items" → "[customSchema].[cbv_Guide_Items]"
    /// Her parca ayri ayri validate edilir.
    /// </summary>
    private string BracketView(string viewName)
    {
        var parts = (viewName ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            var s = _schemaName.Replace("]", "]]");
            return $"[{s}].[{ValidateIdentifier(parts[0], "ViewName")}]";
        }
        if (parts.Length == 2)
            return $"[{ValidateIdentifier(parts[0], "Schema")}].[{ValidateIdentifier(parts[1], "ViewName")}]";
        throw new ArgumentException($"Gecersiz ViewName: '{viewName}'");
    }

    /// <summary>
    /// View'in INFORMATION_SCHEMA.COLUMNS'taki gercek kolon isimlerini doner
    /// (case-insensitive HashSet). Stale GuideMas seed / view rename durumunda
    /// SearchAsync ve GetDistinctValuesAsync bu listeyle kesisim alarak hatayi onler.
    /// ViewName "schema.view" formatinda olabilir, yoksa _schemaName kullanilir.
    /// </summary>
    private async Task<HashSet<string>> GetActualViewColumnsAsync(
        SqlConnection conn, string viewName, CancellationToken ct)
    {
        var parts = (viewName ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        string schema, view;
        if (parts.Length == 1)
        {
            schema = _schemaName;
            view = parts[0].Trim('[', ']', ' ');
        }
        else if (parts.Length == 2)
        {
            schema = parts[0].Trim('[', ']', ' ');
            view = parts[1].Trim('[', ']', ' ');
        }
        else
        {
            throw new ArgumentException($"Gecersiz ViewName: '{viewName}'");
        }

        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [COLUMN_NAME] FROM INFORMATION_SCHEMA.COLUMNS
            WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @View;";
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@View", view);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct);
        while (await reader.ReadAsync(ct))
            cols.Add(reader.GetString(0));
        return cols;
    }

    // ══════════════════════════════════════════════════════════
    // Faz F+ — Auto-Discovery (startup hook)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// sys.views uzerinden v_Guide% pattern'ine uyan tum view'lari bulup
    /// GuideMas'ta olmayanlari otomatik kaydeder.
    ///
    /// Heuristic (user karari):
    ///   1. kolon  → ValueColumn
    ///   2. kolon  → DisplayColumn (yoksa 1. kolonla ayni)
    ///   Tumu → GridColumnsJson
    ///   GuideCode → view adindan 'v_Guide' prefix'i atilmis hali (uppercase)
    ///
    /// Idempotent: ayni GuideCode zaten varsa atlanir. Kullanici sonradan SQL ile
    /// duzeltme yapabilir, sistem uzerine yazmaz.
    /// </summary>
    public async Task<int> DiscoverAndRegisterGuidesAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) cbv_Guide_% pattern'ine uyan tum view'lari bul (schema + name)
        var viewList = new List<(string SchemaName, string ViewName)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.[name] AS SchemaName, v.[name] AS ViewName
                FROM sys.views v
                INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
                WHERE v.[name] LIKE 'cbv[_]Guide[_]%'
                ORDER BY s.[name], v.[name];";

            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    viewList.Add((reader.GetString(0), reader.GetString(1)));
                }
            }
        }

        if (viewList.Count == 0) return 0;

        // 2) GuideMas'taki mevcut GuideCode'lar — caseinsensitive set
        var existingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT [GuideCode] FROM {_guideMasTable};";
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    existingSet.Add(reader.GetString(0));
                }
            }
        }

        // 3) Mevcut GuideMas.ViewName listesi — ikinci bir dedup filtresi
        var existingViewSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT [ViewName] FROM {_guideMasTable};";
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var vn = reader.GetString(0);
                    existingViewSet.Add((vn ?? string.Empty).Trim().ToLowerInvariant());
                }
            }
        }

        int added = 0;

        foreach (var (schemaName, viewName) in viewList)
        {
            // View adi regex check (defensive — sys.views'dan gelse bile)
            if (!IdentifierRegex.IsMatch(viewName))
            {
                Console.Error.WriteLine($"[Guide Discovery] Atlandi: gecersiz view adi '{viewName}'");
                continue;
            }

            // Heuristic: GuideCode = view_adi - 'cbv_Guide_' prefix + uppercase
            // cbv_Guide_Contacts → CONTACTACCOUNTS
            var bareName = viewName;
            if (bareName.StartsWith("cbv_Guide_", StringComparison.OrdinalIgnoreCase))
                bareName = bareName.Substring("cbv_Guide_".Length);
            else if (bareName.StartsWith("cbv_Guide", StringComparison.OrdinalIgnoreCase))
                bareName = bareName.Substring("cbv_Guide".Length);
            else if (bareName.StartsWith("cbv_", StringComparison.OrdinalIgnoreCase))
                bareName = bareName.Substring("cbv_".Length);
            if (string.IsNullOrWhiteSpace(bareName)) continue;

            var guideCode = bareName.ToUpperInvariant();

            // Zaten kayitli mi? (GuideCode veya ViewName bazinda)
            if (existingSet.Contains(guideCode)) continue;
            if (existingViewSet.Contains(viewName.ToLowerInvariant())) continue;

            // 4) View'in kolonlarini cek (ORDINAL_POSITION sirasinda)
            var cols = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT [COLUMN_NAME]
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @View
                    ORDER BY [ORDINAL_POSITION];";
                cmd.Parameters.AddWithValue("@Schema", schemaName);
                cmd.Parameters.AddWithValue("@View", viewName);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
                {
                    while (await reader.ReadAsync(ct))
                    {
                        cols.Add(reader.GetString(0));
                    }
                }
            }

            if (cols.Count == 0)
            {
                Console.Error.WriteLine($"[Guide Discovery] Atlandi: view '{viewName}' kolon okunamadi");
                continue;
            }

            // Tum kolonlar regex'ten geciyor mu? Defensive check.
            if (cols.Any(c => !IdentifierRegex.IsMatch(c)))
            {
                Console.Error.WriteLine($"[Guide Discovery] Atlandi: '{viewName}' gecersiz kolon adi iceriyor");
                continue;
            }

            // Standart rehber kurali: Code kolonu varsa value, Name kolonu varsa display.
            // Aksi halde 1./2. kolon (geriye donuk uyumluluk).
            var codeCol = cols.FirstOrDefault(c => string.Equals(c, "Code", StringComparison.OrdinalIgnoreCase));
            var nameCol = cols.FirstOrDefault(c => string.Equals(c, "Name", StringComparison.OrdinalIgnoreCase));
            var valueCol = codeCol ?? cols[0];
            var displayCol = nameCol ?? (cols.Count >= 2 ? cols[1] : cols[0]);
            var gridColumnsJson = JsonSerializer.Serialize(cols);

            // Human-readable label — CamelCase'i bosluklara bol
            var label = HumanizeCamelCase(bareName) + " Rehberi";

            // 5) INSERT — GuideMas'a yeni satir
            try
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $@"
                        INSERT INTO {_guideMasTable}
                            ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],
                             [GridColumnsJson],[DefaultSortColumn],[IsActive])
                        VALUES
                            (@GuideCode, @GuideLabel, @ViewName, @ValueColumn, @DisplayColumn,
                             @GridColumnsJson, @DefaultSort, 1);";
                    cmd.Parameters.AddWithValue("@GuideCode", guideCode);
                    cmd.Parameters.AddWithValue("@GuideLabel", label);
                    cmd.Parameters.AddWithValue("@ViewName", viewName);
                    cmd.Parameters.AddWithValue("@ValueColumn", valueCol);
                    cmd.Parameters.AddWithValue("@DisplayColumn", displayCol);
                    cmd.Parameters.AddWithValue("@GridColumnsJson", gridColumnsJson);
                    cmd.Parameters.AddWithValue("@DefaultSort", valueCol);

                    await cmd.ExecuteNonQueryAsync(cancellationToken: ct);
                }

                existingSet.Add(guideCode);
                existingViewSet.Add(viewName.ToLowerInvariant());
                added++;

                Console.WriteLine(
                    $"[Guide Discovery] Eklendi: {guideCode} → {viewName} " +
                    $"(Value={valueCol}, Display={displayCol}, {cols.Count} kolon)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Guide Discovery] INSERT hatasi '{viewName}': {ex.Message}");
            }
        }

        return added;
    }

    public async Task<int> NormalizeStandardColumnsAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) Tum aktif GuideMas kayitlarini cek
        var rows = new List<(int Id, string ViewName, string ValueColumn, string DisplayColumn)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT [Id],[ViewName],[ValueColumn],[DisplayColumn]
                FROM {_guideMasTable}
                WHERE [IsActive] = 1;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        if (rows.Count == 0) return 0;

        int updated = 0;
        foreach (var row in rows)
        {
            // ViewName "schema.view" olabilir — parcalara bol
            string schemaName = _schemaName;
            string viewName = row.ViewName;
            var dotIdx = viewName.IndexOf('.');
            if (dotIdx > 0)
            {
                schemaName = viewName.Substring(0, dotIdx);
                viewName = viewName.Substring(dotIdx + 1);
            }
            if (!IdentifierRegex.IsMatch(schemaName) || !IdentifierRegex.IsMatch(viewName)) continue;

            // 2) View kolonlarini cek
            var cols = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT [COLUMN_NAME]
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @View
                    ORDER BY [ORDINAL_POSITION];";
                cmd.Parameters.AddWithValue("@Schema", schemaName);
                cmd.Parameters.AddWithValue("@View", viewName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct);
                while (await reader.ReadAsync(ct)) cols.Add(reader.GetString(0));
            }
            if (cols.Count == 0) continue;

            // 3) Standart kural: Code → ValueColumn, Name → DisplayColumn
            var codeCol = cols.FirstOrDefault(c => string.Equals(c, "Code", StringComparison.OrdinalIgnoreCase));
            var nameCol = cols.FirstOrDefault(c => string.Equals(c, "Name", StringComparison.OrdinalIgnoreCase));
            var newValue = codeCol ?? row.ValueColumn;
            var newDisplay = nameCol ?? row.DisplayColumn;

            // 4) Degisiklik var mi? (case-sensitive — view kolon adlari aynen yazilsin)
            if (string.Equals(newValue, row.ValueColumn, StringComparison.Ordinal) &&
                string.Equals(newDisplay, row.DisplayColumn, StringComparison.Ordinal))
                continue;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    UPDATE {_guideMasTable}
                    SET [ValueColumn] = @Value, [DisplayColumn] = @Display, [UpdatedAt] = SYSUTCDATETIME()
                    WHERE [Id] = @Id;";
                cmd.Parameters.AddWithValue("@Value", newValue);
                cmd.Parameters.AddWithValue("@Display", newDisplay);
                cmd.Parameters.AddWithValue("@Id", row.Id);
                await cmd.ExecuteNonQueryAsync(cancellationToken: ct);
            }
            updated++;
            Console.WriteLine(
                $"[Guide Normalize] {row.ViewName}: Value '{row.ValueColumn}'→'{newValue}', " +
                $"Display '{row.DisplayColumn}'→'{newDisplay}'");
        }

        return updated;
    }

    public async Task<int> SetDefaultFilterAsync(string guideCode, string? filterJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(guideCode)) return 0;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // GetByCodeAsync ile ayni eslesme: hem GuideCode hem ViewName.
        // Birden fazla satir varsa hepsi ayni filtreye guncellenir (legacy data drift).
        cmd.CommandText = $@"
            UPDATE {_guideMasTable}
            SET [DefaultFilterJson] = @Filter,
                [UpdatedAt] = SYSUTCDATETIME()
            WHERE [GuideCode] = @Code OR [ViewName] = @Code;";
        cmd.Parameters.AddWithValue("@Code", guideCode);
        var trimmed = filterJson?.Trim();
        cmd.Parameters.AddWithValue(
            "@Filter",
            string.IsNullOrWhiteSpace(trimmed) ? (object)DBNull.Value : trimmed!);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected;
    }

    /// <summary>
    /// "Contacts" → "Contact Accounts"
    /// "CurrencyRates" → "Currency Rates"
    /// CamelCase'i bosluklara boler. Admin UI'da guide label olarak kullanilir.
    /// </summary>
    private static string HumanizeCamelCase(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

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
/// Dapper kullaniliyor â€” dinamik kolonlu sorgu satirlarini dynamic dictionary
/// olarak almayi kolaylastiriyor, GetAll / Resolve icin de strongly-typed
/// mapping'i tek satirda veriyor.
///
/// â•â•â• SQL INJECTION SAVUNMA KATMANI â•â•â•
///   - ViewName, ValueColumn, DisplayColumn, GridColumnsJson icindeki tum
///     kolonlar, sortColumn â€” hepsi IdentifierRegex allowlist'ten geciyor.
///   - sortDirection yalnizca 'ASC' veya 'DESC'.
///   - search parametresi @Search olarak Dapper DynamicParameters ile
///     parametreli, LIKE karakterleri (% _ [) escape ediliyor.
///   - ViewName cok parcali olabilir ('schema.view') â€” parcalar ayri ayri
///     validate edilip [bracket] ile birlestiriliyor.
/// </summary>
public sealed class SqlGuideRepository : IGuideRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schemaName;
    // Identifier allowlist: harfle basla, harf/rakam/altcizgi; max 64 karakter.
    private static readonly Regex IdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    public SqlGuideRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schemaName = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PR 4: GuideMas kaldÄ±rÄ±ldÄ±. View metadata INFORMATION_SCHEMA'dan dinamik olarak
    // alÄ±nÄ±r; ValueColumn/DisplayColumn Code/Name adlandÄ±rma kuralÄ±na gÃ¶re sezilir.
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    public async Task<GuideDefinition?> GetByCodeAsync(string guideCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(guideCode)) return null;

        // "schema.view" veya tek parÃ§a â€” her iki format desteklenir.
        guideCode = guideCode.Trim();
        string schema, view;
        var dot = guideCode.IndexOf('.');
        if (dot > 0)
        {
            schema = guideCode[..dot].Trim('[', ']', ' ');
            view   = guideCode[(dot + 1)..].Trim('[', ']', ' ');
        }
        else
        {
            schema = _schemaName;
            view   = guideCode;
        }

        if (!IdentifierRegex.IsMatch(schema) || !IdentifierRegex.IsMatch(view)) return null;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [COLUMN_NAME] FROM INFORMATION_SCHEMA.COLUMNS
            WHERE [TABLE_SCHEMA] = @Schema AND [TABLE_NAME] = @View
            ORDER BY [ORDINAL_POSITION];";
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@View", view);

        var cols = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var col = reader.GetString(0);
                if (IdentifierRegex.IsMatch(col)) cols.Add(col);
            }
        }

        if (cols.Count == 0) return null;

        // Standart kolon sezme: Code/Name > *Code/*Name/*Title > konum bazlÄ±
        var valueCol =
            cols.FirstOrDefault(c => c.Equals("Code", StringComparison.OrdinalIgnoreCase))
            ?? cols.FirstOrDefault(c => c.EndsWith("Code", StringComparison.OrdinalIgnoreCase)
                                        && !c.Equals("Id", StringComparison.OrdinalIgnoreCase))
            ?? cols.FirstOrDefault(c => !c.Equals("Id", StringComparison.OrdinalIgnoreCase))
            ?? cols[0];

        var displayCol =
            cols.FirstOrDefault(c => c.Equals("Name", StringComparison.OrdinalIgnoreCase))
            ?? cols.FirstOrDefault(c => c.EndsWith("Name", StringComparison.OrdinalIgnoreCase)
                                        && !c.Equals(valueCol, StringComparison.OrdinalIgnoreCase))
            ?? cols.FirstOrDefault(c => c.EndsWith("Title", StringComparison.OrdinalIgnoreCase))
            ?? cols.FirstOrDefault(c => !c.Equals("Id", StringComparison.OrdinalIgnoreCase)
                                        && !c.Equals(valueCol, StringComparison.OrdinalIgnoreCase))
            ?? valueCol;

        return new GuideDefinition
        {
            Id                = 0,
            GuideCode         = guideCode,
            GuideLabel        = guideCode,
            ViewName          = guideCode,
            ValueColumn       = valueCol,
            DisplayColumn     = displayCol,
            GridColumnsJson   = JsonSerializer.Serialize(cols),
            DefaultSortColumn = valueCol,
            DefaultFilterJson = null,
            IsActive          = true,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow,
        };
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Arama â€” dinamik SQL (guvenli)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
        // cek, kesisimi al â€” boylece olmayan kolon SELECT'e girmez (207 hatasi yok).
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

        // 4) SELECT kolon listesi â€” Value + Display + grid kolonlari (benzersiz)
        var selectColumns = new List<string>(columns.Length + 2);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in new[] { valueCol, displayCol }.Concat(columns))
        {
            if (seen.Add(c))
                selectColumns.Add($"[{c}]");
        }
        var selectClause = string.Join(", ", selectColumns);

        var view = BracketView(guide.ViewName);

        // 5) WHERE â€” tokenized arama: her kelime ayri ayri tum kolonlarda aranir (AND)
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

        // 5b) Dinamik kisitlar (constraints) â€” SQL Injection korumalÄ±
        // Her kisit AND veya OR ile birlestirilir (Logic property).
        // Gruplama: ardisik AND'ler parantez icinde, OR ile ayrilir.
        //
        // GuideMas.DefaultFilterJson â€” rehber bazli varsayilan WHERE fragment.
        // FldSet field-level filtresinden BAGIMSIZ olarak her arama cagrisinda
        // otomatik prepend edilir (AND ile birlesir). Boylece bir rehbere bir
        // kez verilen kisit (orn. cbv_Guide_Items: TYPID IN (2,3)) bu rehberin
        // kullanildigi tum form alanlarinda â€” BOM mamul, is emri mamul, satir
        // grid'i, vs. â€” otomatik gecerli olur. Token desteklenmiyor (global, form-bagimsiz).
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
                // Logic: yalnizca "and" veya "or" â€” diger degerler "and" olarak davranir (raw SQL icin de gecerli)
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

                // GÃ¼venlik 1: kolon adi allowlist'ten gecmeli
                var safeField = ValidateIdentifier(c.Field.Trim(), "ConstraintField");

                // Logic: yalnizca "and" veya "or" â€” diger degerler "and" olarak davranir
                var logic = (c.Logic ?? "and").Trim().ToLowerInvariant();
                if (logic != "or") logic = "and";

                // GÃ¼venlik 2: operatÃ¶r switch-case ile â€” asla kullanicidan gelen string SQL'e yazilmaz
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
                        // IN operatÃ¶rÃ¼: virgÃ¼lle ayrÄ±lmÄ±ÅŸ deÄŸerler â†’ ayrÄ± parametreler
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
                        // Bilinmeyen operatÃ¶r sessizce atlanÄ±r â€” gÃ¼venlik
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

        // 6) OFFSET / FETCH â€” hasMore hesabi icin pageSize + 1 satir cek
        var offset = (page - 1) * pageSize;
        var sql = $@"
            SELECT {selectClause}
            FROM {view}
            {whereClause}
            ORDER BY [{actualSort}] {direction}
            OFFSET @Offset ROWS FETCH NEXT @Fetch ROWS ONLY;";

        // 7) Execute â€” raw ADO.NET SqlDataReader (conn step 2b'de acildi)
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

        // Constraint parametreleri â€” her biri gÃ¼venli parametrize
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

        // 8) hasMore â€” fazla satir varsa son satiri at
        var hasMore = rows.Count > pageSize;
        if (hasMore && rows.Count > 0)
            rows.RemoveAt(rows.Count - 1);

        return new GuideSearchResultDto(rows, columns.AsReadOnly(), page, pageSize, hasMore);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Tek value â†’ display cozumleme
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Distinct degerler â€” filtre cipleri icin
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

        // 2) DISTINCT sorgu â€” NULL/bos atilir, alfabetik siralanir, max 200 satir.
        //    Veri kalitesi normalize: NBSP (NCHAR(160)) â†’ bosluk; LTRIM/RTRIM ile
        //    bas/son bosluklar kirpilir; Turkish_CI_AI collation ile case+accent
        //    farklari ayni distinct'e dusurulur. Boylece "TRABZON", "TRABZON ",
        //    "trabzon", "TRABZONÂ " varyantlari popover'da TEK satir gozukur.
        //    search non-empty ise ayni normalize'li ifade uzerinde LIKE @Search.
        var normalizedExpr = "LTRIM(RTRIM(REPLACE(CAST([" + safeColumn + "] AS NVARCHAR(400)), NCHAR(160), N' '))) COLLATE Turkish_CI_AI";

        var hasSearch = !string.IsNullOrWhiteSpace(search);

        // 3) Constraint WHERE building â€” SearchAsync ile birebir mantik.
        //    guide.DefaultFilterJson her zaman AND ile prepend edilir; ardindan
        //    caller'in constraint'leri eklenir (rawSql/eq/in/...). Distinct popover
        //    listede gosterilen satirlardan turetilir â†’ "view'a verilen filtrelere
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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Constraint helper â€” SearchAsync + GetDistinctValuesAsync paylasir
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// SearchAsync icindeki constraintâ†’WHERE fragment olusturma mantiginin
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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Guvenlik helper'lari
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private static string ValidateIdentifier(string raw, string role)
    {
        if (string.IsNullOrWhiteSpace(raw) || !IdentifierRegex.IsMatch(raw))
            throw new ArgumentException(
                $"Gecersiz {role}: '{raw}'. Sadece harf/rakam/altcizgi, harfle baslamali, max 64 karakter.");
        return raw;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Admin CRUD â€” ListViews, GetViewColumns, Upsert, Delete
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    public async Task<IReadOnlyCollection<GuideViewInfoDto>> ListGuideViewsAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) cbv_Guide_% view listesi — ViewMeta'dan IsStandard + Tags bilgisi alınır
        var viewList = new List<(string SchemaName, string ViewName, bool IsStandard, string? Tags)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.[name] AS SchemaName, v.[name] AS ViewName,
                       ISNULL(vm.[IsStandard], 0) AS IsStandard,
                       vm.[Tags]
                FROM sys.views v
                INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
                LEFT JOIN dbo.[ViewMeta] vm ON vm.[ViewName] = v.[name]
                WHERE v.[name] LIKE 'cbv[_]Guide[_]%'
                ORDER BY s.[name], v.[name];";
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
            {
                while (await reader.ReadAsync(ct))
                    viewList.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetBoolean(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        // 2) Her view icin kolon listesi
        var result = new List<GuideViewInfoDto>(viewList.Count);
        foreach (var (schemaName, viewName, isStandard, tags) in viewList)
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
            result.Add(new GuideViewInfoDto(viewName, schemaName, cols.AsReadOnly(), isStandard, tags));
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


    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Guvenlik helper'lari
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// "cbv_Guide_Items" â†’ "[dbo].[cbv_Guide_Items]"
    /// "customSchema.cbv_Guide_Items" â†’ "[customSchema].[cbv_Guide_Items]"
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

}

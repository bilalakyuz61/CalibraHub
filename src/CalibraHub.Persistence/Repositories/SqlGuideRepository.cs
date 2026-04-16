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
    // GuideMas katalog CRUD
    // ══════════════════════════════════════════════════════════

    public async Task<IReadOnlyCollection<GuideDefinition>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],
                   [GridColumnsJson],[DefaultSortColumn],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_guideMasTable}
            WHERE [IsActive] = 1
            ORDER BY [GuideLabel];";

        var result = new List<GuideDefinition>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken: ct))
        {
            while (await reader.ReadAsync(ct))
            {
                result.Add(new GuideDefinition
                {
                    Id = reader.GetInt32(0),
                    GuideCode = reader.GetString(1),
                    GuideLabel = reader.GetString(2),
                    ViewName = reader.GetString(3),
                    ValueColumn = reader.GetString(4),
                    DisplayColumn = reader.GetString(5),
                    GridColumnsJson = reader.GetString(6),
                    DefaultSortColumn = reader.IsDBNull(7) ? null : reader.GetString(7),
                    IsActive = reader.GetBoolean(8),
                    CreatedAt = reader.GetDateTime(9),
                    UpdatedAt = reader.GetDateTime(10)
                });
            }
        }
        return result;
    }

    public async Task<GuideDefinition?> GetByCodeAsync(string guideCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(guideCode)) return null;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP(1) [Id],[GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],
                          [GridColumnsJson],[DefaultSortColumn],[IsActive],[CreatedAt],[UpdatedAt]
            FROM {_guideMasTable}
            WHERE [GuideCode] = @Code AND [IsActive] = 1;";
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
                    IsActive = reader.GetBoolean(8),
                    CreatedAt = reader.GetDateTime(9),
                    UpdatedAt = reader.GetDateTime(10)
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
        var columns = (JsonSerializer.Deserialize<string[]>(guide.GridColumnsJson) ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => ValidateIdentifier(c.Trim(), "Column"))
            .ToArray();
        if (columns.Length == 0)
            throw new ArgumentException($"Guide '{guide.GuideCode}' icin GridColumnsJson bos.");

        var valueCol = ValidateIdentifier(guide.ValueColumn, "ValueColumn");
        var displayCol = ValidateIdentifier(guide.DisplayColumn, "DisplayColumn");

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
                var likes = columns.Select(c => $"[{c}] LIKE {paramName} ESCAPE '^'").ToArray();
                whereParts.Add("(" + string.Join(" OR ", likes) + ")");
            }
        }

        // 5b) Dinamik kisitlar (constraints) — SQL Injection korumalı
        // Her kisit AND veya OR ile birlestirilir (Logic property).
        // Gruplama: ardisik AND'ler parantez icinde, OR ile ayrilir.
        var constraintParams = new List<(string ParamName, object Value)>();
        var constraintSqlParts = new List<(string Sql, string Logic)>(); // (SQL fragment, logic)
        if (constraints is { Count: > 0 })
        {
            var pIdx = 0;
            foreach (var c in constraints)
            {
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
                        sqlFragment = $"[{safeField}] LIKE @cp{pIdx} ESCAPE '^'";
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

        // 7) Execute — raw ADO.NET SqlDataReader
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
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

    public async Task<int> UpsertAsync(UpsertGuideRequest request, CancellationToken ct)
    {
        // GuideCode: verilmemisse GuideLabel'dan uret (slug: bos → altcizgi, uppercase)
        var guideCode = string.IsNullOrWhiteSpace(request.GuideCode)
            ? GenerateGuideCode(request.GuideLabel)
            : request.GuideCode.Trim().ToUpperInvariant();

        ValidateIdentifier(request.ValueColumn.Trim(), "ValueColumn");
        ValidateIdentifier(request.DisplayColumn.Trim(), "DisplayColumn");
        ValidateIdentifier(request.ViewName.Trim().Split('.').Last(), "ViewName");

        var gridJson = System.Text.Json.JsonSerializer.Serialize(
            request.GridColumns?.ToArray() ?? Array.Empty<string>());

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        if (request.Id <= 0)
        {
            // INSERT
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO {_guideMasTable}
                    ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],
                     [GridColumnsJson],[DefaultSortColumn],[IsActive])
                VALUES
                    (@Code, @Label, @View, @ValueCol, @DisplayCol,
                     @Grid, @Sort, 1);
                SELECT SCOPE_IDENTITY();";
            cmd.Parameters.AddWithValue("@Code", guideCode);
            cmd.Parameters.AddWithValue("@Label", request.GuideLabel.Trim());
            cmd.Parameters.AddWithValue("@View", request.ViewName.Trim());
            cmd.Parameters.AddWithValue("@ValueCol", request.ValueColumn.Trim());
            cmd.Parameters.AddWithValue("@DisplayCol", request.DisplayColumn.Trim());
            cmd.Parameters.AddWithValue("@Grid", gridJson);
            cmd.Parameters.AddWithValue("@Sort", (object?)request.DefaultSortColumn ?? DBNull.Value);
            var scalar = await cmd.ExecuteScalarAsync(cancellationToken: ct);
            return Convert.ToInt32(scalar);
        }
        else
        {
            // UPDATE
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                UPDATE {_guideMasTable}
                SET [GuideLabel]      = @Label,
                    [ViewName]        = @View,
                    [ValueColumn]     = @ValueCol,
                    [DisplayColumn]   = @DisplayCol,
                    [GridColumnsJson] = @Grid,
                    [DefaultSortColumn] = @Sort,
                    [UpdatedAt]       = SYSUTCDATETIME()
                WHERE [Id] = @Id;";
            cmd.Parameters.AddWithValue("@Id", request.Id);
            cmd.Parameters.AddWithValue("@Label", request.GuideLabel.Trim());
            cmd.Parameters.AddWithValue("@View", request.ViewName.Trim());
            cmd.Parameters.AddWithValue("@ValueCol", request.ValueColumn.Trim());
            cmd.Parameters.AddWithValue("@DisplayCol", request.DisplayColumn.Trim());
            cmd.Parameters.AddWithValue("@Grid", gridJson);
            cmd.Parameters.AddWithValue("@Sort", (object?)request.DefaultSortColumn ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken: ct);
            return request.Id;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_guideMasTable} SET [IsActive]=0, [UpdatedAt]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken: ct);
    }

    /// <summary>
    /// "Cari Hesap Rehberi" → "CARIHESAPREHBERI" (slug)
    /// Sadece harf/rakam/altcizgi — diger karakterler '_' olur.
    /// </summary>
    private static string GenerateGuideCode(string label)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in (label ?? string.Empty).ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        // Basta rakam gelirse basina G_ ekle
        var result = sb.ToString().Trim('_');
        if (result.Length > 0 && char.IsDigit(result[0])) result = "G_" + result;
        return result.Length > 0 ? result : "GUIDE_" + DateTime.UtcNow.Ticks;
    }

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
            // cbv_Guide_ContactAccounts → CONTACTACCOUNTS
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

            var valueCol = cols[0];
            var displayCol = cols.Count >= 2 ? cols[1] : cols[0];
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

    /// <summary>
    /// "ContactAccounts" → "Contact Accounts"
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

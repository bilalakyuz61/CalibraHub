using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL View Yönetimi iş katmanı — şema introspeksiyonu (sys.*) + ScriptDom SQL doğrulama +
/// CREATE OR ALTER VIEW DDL uygulaması + ViewDefinition/ViewDefinitionRevision bookkeeping.
///
/// Persistence katmanına yerleştirildi çünkü hem SqlServerConnectionFactory hem de
/// Microsoft.SqlServer.TransactSql.ScriptDom paketi burada (Application referans vermez) —
/// ApprovalSqlQueryService/SqlPostProcedureExecutor/SqlDocumentNumberService ile aynı pattern.
///
/// Yazma akışı (ApplyAsync/RevertAsync) CREATE OR ALTER VIEW DDL'i ile ViewDefinition/
/// ViewDefinitionRevision satırlarını AYNI transaction içinde yazar — bu yüzden
/// IViewDefinitionRepository'nin (ayrı bağlantı açan) CRUD metodları yalnızca okuma için
/// kullanılır; yazma burada, doğrudan paylaşılan SqlConnection/SqlTransaction üzerinde yapılır.
/// </summary>
public sealed class ViewBuilderService : IViewBuilderService
{
    private readonly IViewDefinitionRepository _repository;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> AllowedOps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["="] = "=", ["<>"] = "<>", ["!="] = "<>", ["<"] = "<", [">"] = ">", ["<="] = "<=", [">="] = ">=",
    };

    private static readonly JsonSerializerOptions StorageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ViewBuilderService(IViewDefinitionRepository repository, SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _repository = repository;
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    // ════════════════════════════════════════════════════════════════
    // Şema yardımcıları (görsel kurucu dropdown'ları)
    // ════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<ViewSchemaTableDto>> GetSchemaObjectsAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        return await GetSchemaObjectsAsync(connection, ct);
    }

    private static async Task<IReadOnlyList<ViewSchemaTableDto>> GetSchemaObjectsAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.name, o.name, o.type
            FROM sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U','V') AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name;
            """;
        var list = new List<ViewSchemaTableDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var type = reader.GetString(2).Trim();
            list.Add(new ViewSchemaTableDto(reader.GetString(0), reader.GetString(1), type == "V" ? "view" : "table"));
        }
        return list;
    }

    public async Task<IReadOnlyList<ViewSchemaColumnDto>> GetSchemaColumnsAsync(string schema, string name, CancellationToken ct)
    {
        if (!IsValidIdentifier(schema) || !IsValidIdentifier(name)) return Array.Empty<ViewSchemaColumnDto>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.name, TYPE_NAME(c.user_type_id), c.is_nullable
            FROM sys.columns c
            INNER JOIN sys.objects o ON o.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = @Schema AND o.name = @Name
            ORDER BY c.column_id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", schema));
        cmd.Parameters.Add(new SqlParameter("@Name", name));
        var list = new List<ViewSchemaColumnDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new ViewSchemaColumnDto(reader.GetString(0), reader.IsDBNull(1) ? "?" : reader.GetString(1), reader.GetBoolean(2)));
        return list;
    }

    // ════════════════════════════════════════════════════════════════
    // View listeleme / tanım okuma
    // ════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<ViewListItemDto>> ListViewsAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.name, v.name
            FROM sys.views v
            INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
            WHERE v.is_ms_shipped = 0
            ORDER BY s.name, v.name;
            """;
        var dbViews = new List<(string Schema, string Name)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                dbViews.Add((reader.GetString(0), reader.GetString(1)));
        }

        var overrides = await _repository.ListActiveAsync(ct);
        var overrideByName = overrides.ToDictionary(o => o.ViewName, StringComparer.OrdinalIgnoreCase);

        var list = new List<ViewListItemDto>();
        foreach (var (schema, name) in dbViews)
        {
            overrideByName.TryGetValue(name, out var ov);
            list.Add(new ViewListItemDto(
                schema, name,
                HasOverride: ov is not null,
                OverrideKind: ov?.Kind.ToString(),
                OverrideIsActive: ov?.IsActive ?? false,
                ViewDefinitionId: ov?.Id,
                OverrideUpdated: ov is null ? null : (ov.Updated ?? ov.Created)));
        }

        // DB'de fiziksel karşılığı bulunamayan ama override kaydı olan satırlar (örn. dışarıdan
        // manuel DROP VIEW yapılmışsa) — kullanıcı yine görebilsin, Revert ile geri getirebilir.
        var dbViewNames = dbViews.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ov in overrides.Where(o => !dbViewNames.Contains(o.ViewName)))
        {
            list.Add(new ViewListItemDto(_schema, ov.ViewName, true, ov.Kind.ToString(), ov.IsActive, ov.Id, ov.Updated ?? ov.Created));
        }

        return list
            .OrderBy(x => x.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ViewDetailDto?> GetViewDetailAsync(string viewName, CancellationToken ct)
    {
        if (!IsValidIdentifier(viewName)) return null;

        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT OBJECT_DEFINITION(OBJECT_ID(@full, N'V'));";
        cmd.Parameters.Add(new SqlParameter("@full", $"[{_schema}].[{viewName}]"));
        var raw = await cmd.ExecuteScalarAsync(ct);
        var currentSql = raw is null or DBNull ? null : (string)raw;

        var existing = await _repository.GetByViewNameAsync(viewName, ct);
        if (currentSql is null && existing is null) return null;

        ViewOverrideDto? overrideDto = null;
        if (existing is not null)
        {
            var revCount = await _repository.CountRevisionsAsync(existing.Id, ct);
            overrideDto = new ViewOverrideDto(
                existing.Id, existing.Kind.ToString(), existing.DefinitionJson, existing.GeneratedSql,
                existing.IsRawMode, existing.IsActive, existing.Created, existing.CreatedBy,
                existing.Updated, existing.UpdatedBy, revCount);
        }

        return new ViewDetailDto(viewName, currentSql is not null, currentSql, existing is not null, overrideDto);
    }

    // ════════════════════════════════════════════════════════════════
    // SQL üretimi + doğrulama
    // ════════════════════════════════════════════════════════════════

    public async Task<ViewGenerateResult> GenerateSqlAsync(ViewDefinitionJson definition, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        return await GenerateSqlInternalAsync(connection, definition, ct);
    }

    private async Task<ViewGenerateResult> GenerateSqlInternalAsync(SqlConnection connection, ViewDefinitionJson? definition, CancellationToken ct)
    {
        var errors = new List<string>();
        if (definition is null) { errors.Add("Tanım (definition) boş olamaz."); return new ViewGenerateResult(false, null, errors); }
        if (definition.BaseObject is null) errors.Add("Temel tablo/view seçilmelidir.");
        if (definition.Columns is null || definition.Columns.Count == 0) errors.Add("En az bir sütun seçilmelidir.");
        if (errors.Count > 0) return new ViewGenerateResult(false, null, errors);

        var baseObj = definition.BaseObject!;
        var joins = definition.Joins ?? Array.Empty<ViewJoinDto>();

        // 1) Alias haritası + şekil doğrulaması
        var aliasMap = new Dictionary<string, (string Schema, string Name)>(StringComparer.OrdinalIgnoreCase);
        if (!IsValidIdentifier(baseObj.Schema) || !IsValidIdentifier(baseObj.Name))
            errors.Add("Temel tablo şema/adı geçersiz.");
        if (!IsValidIdentifier(baseObj.Alias))
            errors.Add($"Geçersiz alias: '{baseObj.Alias}'.");
        else
            aliasMap[baseObj.Alias] = (baseObj.Schema, baseObj.Name);

        foreach (var j in joins)
        {
            if (!TryMapJoinType(j.Type, out _))
                errors.Add($"Geçersiz join tipi: '{j.Type}' (inner/left/right olmalı).");
            if (!IsValidIdentifier(j.Schema) || !IsValidIdentifier(j.Name))
                errors.Add($"Geçersiz join tablo/şeması: '{j.Schema}.{j.Name}'.");
            if (!IsValidIdentifier(j.Alias))
                errors.Add($"Geçersiz join alias: '{j.Alias}'.");
            else if (aliasMap.ContainsKey(j.Alias))
                errors.Add($"Alias birden fazla kez kullanılıyor: '{j.Alias}'.");
            else
                aliasMap[j.Alias] = (j.Schema, j.Name);

            if (j.On is null || j.On.Count == 0)
                errors.Add($"'{j.Alias}' join'i için en az bir ON koşulu zorunludur (cross join yasak).");
        }
        if (errors.Count > 0) return new ViewGenerateResult(false, null, errors);

        // 2) Referans verilen tüm (schema,name) nesnelerinin varlığını doğrula
        var known = await GetKnownObjectsAsync(connection, ct);
        foreach (var kv in aliasMap)
        {
            if (!known.Contains(ObjectKey(kv.Value.Schema, kv.Value.Name)))
                errors.Add($"Şemada bulunamayan tablo/view: '{kv.Value.Schema}.{kv.Value.Name}' (alias '{kv.Key}').");
        }
        if (errors.Count > 0) return new ViewGenerateResult(false, null, errors);

        // 3) Her alias için kolon setini yükle (tek seferlik, alias başına bir sorgu)
        var columnSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in aliasMap)
            columnSets[kv.Key] = await GetColumnNamesAsync(connection, kv.Value.Schema, kv.Value.Name, ct);

        // 4) ON koşullarını doğrula + JOIN gövdelerini üret (alias görünürlüğü: base + o ana kadarki join'ler)
        var visibleAliases = new List<string> { baseObj.Alias };
        var joinSqlParts = new List<string>();
        foreach (var j in joins)
        {
            visibleAliases.Add(j.Alias);
            var onParts = new List<string>();
            foreach (var on in j.On!)
            {
                if (!IsValidIdentifier(on.LeftAlias) || !IsValidIdentifier(on.RightAlias) ||
                    !IsValidIdentifier(on.LeftCol) || !IsValidIdentifier(on.RightCol))
                { errors.Add($"'{j.Alias}' join'inde geçersiz ON alan adı."); continue; }
                if (!visibleAliases.Contains(on.LeftAlias, StringComparer.OrdinalIgnoreCase))
                { errors.Add($"'{j.Alias}' join'inde tanımsız/henüz görünür olmayan alias: '{on.LeftAlias}'."); continue; }
                if (!visibleAliases.Contains(on.RightAlias, StringComparer.OrdinalIgnoreCase))
                { errors.Add($"'{j.Alias}' join'inde tanımsız/henüz görünür olmayan alias: '{on.RightAlias}'."); continue; }
                if (!columnSets.TryGetValue(on.LeftAlias, out var lset) || !lset.Contains(on.LeftCol))
                { errors.Add($"'{on.LeftAlias}.{on.LeftCol}' kolonu bulunamadı."); continue; }
                if (!columnSets.TryGetValue(on.RightAlias, out var rset) || !rset.Contains(on.RightCol))
                { errors.Add($"'{on.RightAlias}.{on.RightCol}' kolonu bulunamadı."); continue; }
                if (!TryMapOperator(on.Op, out var opSql))
                { errors.Add($"Desteklenmeyen karşılaştırma operatörü: '{on.Op}'."); continue; }
                onParts.Add($"[{on.LeftAlias}].[{on.LeftCol}] {opSql} [{on.RightAlias}].[{on.RightCol}]");
            }
            if (onParts.Count == 0) { errors.Add($"'{j.Alias}' join'i için geçerli bir ON koşulu üretilemedi."); continue; }
            TryMapJoinType(j.Type, out var joinKeyword);
            joinSqlParts.Add($"{joinKeyword} [{j.Schema}].[{j.Name}] AS [{j.Alias}]{Environment.NewLine}    ON {string.Join(" AND ", onParts)}");
        }
        if (errors.Count > 0) return new ViewGenerateResult(false, null, errors);

        // 5) Sütunları doğrula + SELECT listesini üret
        var selectParts = new List<string>();
        var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in definition.Columns)
        {
            if (!IsValidIdentifier(c.Alias)) { errors.Add($"Geçersiz sütun adı: '{c.Alias}'."); continue; }
            if (!usedAliases.Add(c.Alias)) { errors.Add($"Sütun adı birden fazla kez kullanılıyor: '{c.Alias}'."); continue; }

            if (string.Equals(c.Kind, "calc", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(c.Expression)) { errors.Add($"'{c.Alias}' için ifade (expression) boş olamaz."); continue; }
                selectParts.Add($"({c.Expression}) AS [{c.Alias}]");
            }
            else if (string.Equals(c.Kind, "column", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(c.SourceAlias) || string.IsNullOrWhiteSpace(c.SourceCol))
                { errors.Add($"'{c.Alias}' için kaynak alias/kolon zorunludur."); continue; }
                if (!visibleAliases.Contains(c.SourceAlias, StringComparer.OrdinalIgnoreCase))
                { errors.Add($"'{c.Alias}' sütunu tanımsız alias'a referans veriyor: '{c.SourceAlias}'."); continue; }
                if (!columnSets.TryGetValue(c.SourceAlias, out var cset) || !cset.Contains(c.SourceCol))
                { errors.Add($"'{c.SourceAlias}.{c.SourceCol}' kolonu bulunamadı."); continue; }
                selectParts.Add($"[{c.SourceAlias}].[{c.SourceCol}] AS [{c.Alias}]");
            }
            else
            {
                errors.Add($"Geçersiz sütun tipi: '{c.Kind}' (column/calc olmalı).");
            }
        }
        if (errors.Count > 0) return new ViewGenerateResult(false, null, errors);

        var sb = new StringBuilder();
        sb.Append("SELECT").Append(Environment.NewLine);
        sb.Append("    ").Append(string.Join("," + Environment.NewLine + "    ", selectParts)).Append(Environment.NewLine);
        sb.Append("FROM [").Append(baseObj.Schema).Append("].[").Append(baseObj.Name).Append("] AS [").Append(baseObj.Alias).Append(']');
        foreach (var jp in joinSqlParts)
            sb.Append(Environment.NewLine).Append(jp);

        var sql = sb.ToString();

        // 6) Son savunma hattı: calc kolon expression'ları serbest metin olduğundan, üretilen
        // TÜM gövde raw-SQL guard'larından (ScriptDom parse + forbidden statement + tablo varlığı) geçer.
        var (validOk, validErrors) = await ValidateSqlBodyAsync(connection, sql, ct);
        if (!validOk) return new ViewGenerateResult(false, null, validErrors);

        return new ViewGenerateResult(true, sql, Array.Empty<string>());
    }

    public async Task<string?> ValidateRawSqlAsync(string? viewName, string sql, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        var normalized = NormalizeSqlBody(sql);

        var (ok, errors) = await ValidateSqlBodyAsync(connection, normalized, ct);
        if (!ok) return string.Join(" ", errors);

        var probeName = IsValidIdentifier(viewName) ? viewName! : $"__vb_probe_{Guid.NewGuid():N}";
        var (compileOk, compileError) = await TestCompileAsync(connection, probeName, normalized, ct);
        return compileOk ? null : compileError;
    }

    // ════════════════════════════════════════════════════════════════
    // Önizleme
    // ════════════════════════════════════════════════════════════════

    public async Task<ViewPreviewResult> PreviewAsync(PreviewViewRequest request, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var (sqlBody, _, resolveErrors) = await ResolveSqlBodyAsync(connection, request.IsRawMode, request.Definition, request.RawSql, ct);
        if (sqlBody is null)
            return new ViewPreviewResult(false, resolveErrors, null, Array.Empty<ViewColumnMetaDto>(),
                Array.Empty<IReadOnlyDictionary<string, object?>>(), null, null, null, false);

        var validationError = await ValidateRawSqlAsync(request.ViewName, sqlBody, ct);
        if (validationError is not null)
            return new ViewPreviewResult(false, new[] { validationError }, sqlBody, Array.Empty<ViewColumnMetaDto>(),
                Array.Empty<IReadOnlyDictionary<string, object?>>(), null, null, null, false);

        IReadOnlyList<ViewColumnMetaDto> columns;
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            columns = await DescribeColumnsAsync(connection, sqlBody, ct);
            var top = request.Top is > 0 and <= 100 ? request.Top : 20;
            rows = await SampleRowsAsync(connection, sqlBody, top, ct);
        }
        catch (SqlException ex)
        {
            return new ViewPreviewResult(false, new[] { $"SQL çalıştırma hatası ({ex.Number}): {ex.Message}" }, sqlBody,
                Array.Empty<ViewColumnMetaDto>(), Array.Empty<IReadOnlyDictionary<string, object?>>(), null, null, null, false);
        }

        var (baseCount, resultCount, factor, warn) = await ComputeCartesianSignalAsync(connection, sqlBody, request.Definition, ct);

        return new ViewPreviewResult(true, Array.Empty<string>(), sqlBody, columns, rows, resultCount, baseCount, factor, warn);
    }

    // ════════════════════════════════════════════════════════════════
    // Kaydet/uygula + geri al
    // ════════════════════════════════════════════════════════════════

    public async Task<ViewApplyResult> ApplyAsync(ApplyViewRequest request, string? username, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ViewName) || !IsValidIdentifier(request.ViewName))
            errors.Add("Geçersiz view adı.");
        if (!Enum.TryParse<ViewDefinitionKind>(request.Kind, ignoreCase: true, out var kind))
            errors.Add("Geçersiz Kind (SystemExtend veya Custom olmalı).");
        if (errors.Count > 0) return new ViewApplyResult(false, errors, null, null, false, null);

        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var (sqlBody, definitionJson, resolveErrors) = await ResolveSqlBodyAsync(connection, request.IsRawMode, request.Definition, request.RawSql, ct);
        if (sqlBody is null) return new ViewApplyResult(false, resolveErrors, null, null, false, null);

        var existing = await _repository.GetByViewNameAsync(request.ViewName, ct);
        return await ApplyCoreAsync(connection, request.ViewName, kind, request.IsRawMode, definitionJson, sqlBody, existing, username, ct);
    }

    public async Task<ViewApplyResult> RevertAsync(int viewDefinitionId, string? username, CancellationToken ct)
    {
        var existing = await _repository.GetByIdAsync(viewDefinitionId, ct);
        if (existing is null) return new ViewApplyResult(false, new[] { "View tanımı bulunamadı." }, null, null, false, null);

        var latestRevision = await _repository.GetLatestRevisionAsync(viewDefinitionId, ct);
        if (latestRevision is null)
            return new ViewApplyResult(false, new[] { "Geri alınacak önceki bir sürüm yok." }, existing.Id, null, false, null);

        await using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        var sqlBody = NormalizeSqlBody(latestRevision.GeneratedSql);
        return await ApplyCoreAsync(connection, existing.ViewName, existing.Kind, latestRevision.IsRawMode,
            latestRevision.DefinitionJson, sqlBody, existing, username, ct);
    }

    // ════════════════════════════════════════════════════════════════
    // Ortak uygulama çekirdeği (Apply + Revert aynı yolu kullanır)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// isRawMode true İSE request.RawSql'i, DEĞİLSE request.Definition'dan üretilen SQL'i döner.
    /// definitionJson: DB'ye yazılacak camelCase JSON metni (raw modda Definition verilmişse o da saklanır).
    /// </summary>
    private async Task<(string? Sql, string? DefinitionJson, IReadOnlyList<string> Errors)> ResolveSqlBodyAsync(
        SqlConnection connection, bool isRawMode, ViewDefinitionJson? definition, string? rawSql, CancellationToken ct)
    {
        if (isRawMode)
        {
            if (string.IsNullOrWhiteSpace(rawSql))
                return (null, null, new List<string> { "Ham SQL boş olamaz." });
            var definitionJson = definition is not null ? JsonSerializer.Serialize(definition, StorageJsonOptions) : null;
            return (NormalizeSqlBody(rawSql), definitionJson, Array.Empty<string>());
        }

        if (definition is null)
            return (null, null, new List<string> { "Tanım (definition) boş olamaz." });

        var gen = await GenerateSqlInternalAsync(connection, definition, ct);
        if (!gen.Success) return (null, null, gen.Errors);
        return (NormalizeSqlBody(gen.Sql!), JsonSerializer.Serialize(definition, StorageJsonOptions), Array.Empty<string>());
    }

    private async Task<ViewApplyResult> ApplyCoreAsync(
        SqlConnection connection, string viewName, ViewDefinitionKind kind, bool isRawMode,
        string? definitionJson, string sqlBody, ViewDefinition? existing, string? username, CancellationToken ct)
    {
        var (validOk, validErrors) = await ValidateSqlBodyAsync(connection, sqlBody, ct);
        if (!validOk) return new ViewApplyResult(false, validErrors, existing?.Id, null, false, null);

        if (kind == ViewDefinitionKind.SystemExtend)
        {
            var (nbOk, missing) = await CheckNonBreakingAsync(connection, viewName, sqlBody, ct);
            if (!nbOk)
            {
                var msg = "Bu değişiklik mevcut sütunları kaldırıyor/yeniden adlandırıyor: " + string.Join(", ", missing) +
                           ". Sistem view genişletmeleri yalnızca eklemeli (additive) olabilir.";
                return new ViewApplyResult(false, new[] { msg }, existing?.Id, null, false, null);
            }
        }

        // Kartezyen sinyali — yalnızca bilgilendirici, apply'ı bloklamaz.
        var (_, _, factor, warn) = await ComputeCartesianSignalAsync(connection, sqlBody, null, ct);

        await using var tx = (SqlTransaction)connection.BeginTransaction();
        int viewDefinitionId;
        try
        {
            var ddlCmd = connection.CreateCommand();
            ddlCmd.Transaction = tx;
            ddlCmd.CommandText = $"CREATE OR ALTER VIEW [{_schema}].[{viewName}] AS{Environment.NewLine}{sqlBody}";
            await ddlCmd.ExecuteNonQueryAsync(ct);

            if (existing is not null)
            {
                await InsertRevisionSnapshotAsync(connection, tx, existing, ct);
                await UpdateViewDefinitionRowAsync(connection, tx, existing.Id, kind, definitionJson, sqlBody, isRawMode, username, ct);
                viewDefinitionId = existing.Id;
            }
            else
            {
                viewDefinitionId = await InsertViewDefinitionRowAsync(connection, tx, viewName, kind, definitionJson, sqlBody, isRawMode, username, ct);
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return new ViewApplyResult(false, new[] { "Uygulama sırasında hata: " + ex.Message }, existing?.Id, null, warn, factor);
        }

        return new ViewApplyResult(true, Array.Empty<string>(), viewDefinitionId, sqlBody, warn, factor);
    }

    // ════════════════════════════════════════════════════════════════
    // ViewDefinition / ViewDefinitionRevision yazma (tek transaction içinde)
    // ════════════════════════════════════════════════════════════════

    private async Task InsertRevisionSnapshotAsync(SqlConnection connection, SqlTransaction tx, ViewDefinition existing, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            INSERT INTO [{_schema}].[ViewDefinitionRevision]
                ([ViewDefinitionId],[DefinitionJson],[GeneratedSql],[IsRawMode],[CreatedBy],[Created])
            VALUES
                (@Vid,@Def,@Sql,@Raw,@By,SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@Vid", existing.Id);
        cmd.Parameters.AddWithValue("@Def", (object?)existing.DefinitionJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sql", existing.GeneratedSql);
        cmd.Parameters.AddWithValue("@Raw", existing.IsRawMode);
        cmd.Parameters.AddWithValue("@By", (object?)existing.UpdatedBy ?? (object?)existing.CreatedBy ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<int> InsertViewDefinitionRowAsync(
        SqlConnection connection, SqlTransaction tx, string viewName, ViewDefinitionKind kind,
        string? definitionJson, string sqlBody, bool isRawMode, string? username, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            INSERT INTO [{_schema}].[ViewDefinition]
                ([ViewName],[Kind],[DefinitionJson],[GeneratedSql],[IsRawMode],[IsActive],[CreatedBy],[Created])
            VALUES
                (@Name,@Kind,@Def,@Sql,@Raw,1,@By,SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.AddWithValue("@Name", viewName);
        cmd.Parameters.AddWithValue("@Kind", kind.ToString());
        cmd.Parameters.AddWithValue("@Def", (object?)definitionJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sql", sqlBody);
        cmd.Parameters.AddWithValue("@Raw", isRawMode);
        cmd.Parameters.AddWithValue("@By", (object?)username ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null ? 0 : Convert.ToInt32(result);
    }

    private async Task UpdateViewDefinitionRowAsync(
        SqlConnection connection, SqlTransaction tx, int id, ViewDefinitionKind kind,
        string? definitionJson, string sqlBody, bool isRawMode, string? username, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            UPDATE [{_schema}].[ViewDefinition]
            SET    [Kind] = @Kind, [DefinitionJson] = @Def, [GeneratedSql] = @Sql, [IsRawMode] = @Raw,
                   [IsActive] = 1, [UpdatedBy] = @By, [Updated] = SYSUTCDATETIME()
            WHERE  [Id] = @Id;
            """;
        cmd.Parameters.AddWithValue("@Kind", kind.ToString());
        cmd.Parameters.AddWithValue("@Def", (object?)definitionJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sql", sqlBody);
        cmd.Parameters.AddWithValue("@Raw", isRawMode);
        cmd.Parameters.AddWithValue("@By", (object?)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ════════════════════════════════════════════════════════════════
    // Guard'lar: raw-SQL güvenliği (ScriptDom), derlenebilirlik testi, non-breaking, kartezyen
    // ════════════════════════════════════════════════════════════════

    /// <summary>Ucuz/hızlı statik kontrol: parse + tek SELECT ifadesi + yasaklı komut yok +
    /// referans verilen tüm tablo/view'lar şemada gerçekten var. Transaction/DDL YOK.</summary>
    private static async Task<(bool Ok, List<string> Errors)> ValidateSqlBodyAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(sql)) { errors.Add("SQL boş olamaz."); return (false, errors); }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        IList<ParseError> parseErrors;
        TSqlFragment fragment;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out parseErrors);

        if (parseErrors is { Count: > 0 })
        {
            errors.Add($"SQL ayrıştırma hatası ({parseErrors[0].Line}:{parseErrors[0].Column}): {parseErrors[0].Message}");
            return (false, errors);
        }
        if (fragment is not TSqlScript script || script.Batches.Count == 0)
        {
            errors.Add("Çalıştırılabilir bir SQL bulunamadı.");
            return (false, errors);
        }
        if (script.Batches.Count > 1 || script.Batches[0].Statements.Count != 1)
        {
            errors.Add("Yalnızca tek bir SELECT ifadesine izin verilir (çoklu ifade / ; ile ayrılmış komutlar reddedilir).");
            return (false, errors);
        }

        var statement = script.Batches[0].Statements[0];
        if (statement is not SelectStatement)
        {
            errors.Add("Yalnızca SELECT ifadesi kabul edilir.");
            return (false, errors);
        }

        var forbidden = new ForbiddenStatementVisitor();
        statement.Accept(forbidden);
        if (forbidden.Forbidden is not null)
        {
            errors.Add($"Yasaklı komut tespit edildi: {forbidden.Forbidden}.");
            return (false, errors);
        }

        var tableVisitor = new ViewTableReferenceVisitor();
        statement.Accept(tableVisitor);
        if (tableVisitor.Tables.Count > 0)
        {
            var known = await GetKnownObjectBareNamesAsync(connection, ct);
            foreach (var t in tableVisitor.Tables)
            {
                if (!known.Contains(t))
                    errors.Add($"Şemada bulunamayan tablo/view: '{t}'.");
            }
        }

        return (errors.Count == 0, errors);
    }

    /// <summary>Gerçek derlenebilirlik testi: transaction içinde CREATE OR ALTER VIEW dener, HER ZAMAN
    /// ROLLBACK eder (başarılı olsa bile) — şemaya kalıcı hiçbir iz bırakmaz.</summary>
    private static async Task<(bool Ok, string? Error)> TestCompileAsync(SqlConnection connection, string probeViewName, string sqlBody, CancellationToken ct)
    {
        var tx = (SqlTransaction)connection.BeginTransaction();
        try
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"CREATE OR ALTER VIEW [dbo].[{probeViewName}] AS{Environment.NewLine}{sqlBody}";
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, null);
        }
        catch (SqlException ex)
        {
            return (false, $"SQL derleme hatası ({ex.Number}): {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            tx.Rollback();
            await tx.DisposeAsync();
        }
    }

    /// <summary>System view genişletmede additive garantisi: mevcut view'ın (DB'deki hali) tüm
    /// kolonları, yeni gövdenin çıktısında isim bazlı olarak hâlâ mevcut olmalı.</summary>
    private static async Task<(bool Ok, List<string> Missing)> CheckNonBreakingAsync(
        SqlConnection connection, string viewName, string newSqlBody, CancellationToken ct)
    {
        var existsCmd = connection.CreateCommand();
        existsCmd.CommandText = "SELECT OBJECT_ID(@full, N'V');";
        existsCmd.Parameters.Add(new SqlParameter("@full", $"[dbo].[{viewName}]"));
        var objId = await existsCmd.ExecuteScalarAsync(ct);
        if (objId is null or DBNull) return (true, new List<string>()); // korunacak bir şey yok

        IReadOnlyList<ViewColumnMetaDto> oldColumns;
        IReadOnlyList<ViewColumnMetaDto> newColumns;
        try
        {
            oldColumns = await DescribeColumnsAsync(connection, $"SELECT * FROM [dbo].[{viewName}]", ct);
            newColumns = await DescribeColumnsAsync(connection, newSqlBody, ct);
        }
        catch (SqlException)
        {
            // describe başarısız olursa (örn. mevcut view zaten bozuk) engelleme — DDL adımı zaten yakalar.
            return (true, new List<string>());
        }

        var newNames = newColumns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = oldColumns.Select(c => c.Name).Where(n => !newNames.Contains(n)).ToList();
        return (missing.Count == 0, missing);
    }

    /// <summary>Bilgilendirici sinyal — her zaman try/catch ile sarmalı, hata olursa preview/apply
    /// akışını asla bozmaz (null/false döner).</summary>
    private static async Task<(long? BaseCount, long? ResultCount, decimal? Factor, bool Warn)> ComputeCartesianSignalAsync(
        SqlConnection connection, string sqlBody, ViewDefinitionJson? definition, CancellationToken ct)
    {
        long? resultCount = null;
        try
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM ({sqlBody}) AS __vb_cnt;";
            var v = await cmd.ExecuteScalarAsync(ct);
            resultCount = v is null or DBNull ? null : Convert.ToInt64(v);
        }
        catch { /* bilgilendirici — sessizce atla */ }

        if (definition?.BaseObject is null || definition.Joins is null || definition.Joins.Count == 0)
            return (resultCount, resultCount, resultCount.HasValue ? 1m : null, false);

        long? baseCount = null;
        try
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM [{definition.BaseObject.Schema}].[{definition.BaseObject.Name}];";
            var v = await cmd.ExecuteScalarAsync(ct);
            baseCount = v is null or DBNull ? null : Convert.ToInt64(v);
        }
        catch { /* bilgilendirici — sessizce atla */ }

        if (baseCount is null or 0 || resultCount is null) return (baseCount, resultCount, null, false);
        var factor = Math.Round((decimal)resultCount.Value / baseCount.Value, 2);
        return (baseCount, resultCount, factor, factor > 1m);
    }

    private static async Task<IReadOnlyList<ViewColumnMetaDto>> DescribeColumnsAsync(SqlConnection connection, string selectSql, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT [name], [system_type_name], [is_nullable]
            FROM sys.dm_exec_describe_first_result_set(@sql, NULL, 0)
            ORDER BY [column_ordinal];
            """;
        cmd.Parameters.Add(new SqlParameter("@sql", selectSql));
        var list = new List<ViewColumnMetaDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ViewColumnMetaDto(
                reader.IsDBNull(0) ? "?" : reader.GetString(0),
                reader.IsDBNull(1) ? "?" : reader.GetString(1),
                !reader.IsDBNull(2) && reader.GetBoolean(2)));
        }
        return list;
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SampleRowsAsync(
        SqlConnection connection, string selectSql, int top, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT TOP (@Top) * FROM ({selectSql}) AS __vb_sample;";
        cmd.Parameters.Add(new SqlParameter("@Top", top));
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i);
                row[reader.GetName(i)] = val is DBNull ? null : val;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static async Task<HashSet<string>> GetKnownObjectsAsync(SqlConnection connection, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.name, o.name
            FROM sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U','V') AND o.is_ms_shipped = 0;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            set.Add(ObjectKey(reader.GetString(0), reader.GetString(1)));
        return set;
    }

    /// <summary>ScriptDom'un çıkardığı tablo referansları şema-siz (bare) isimlerdir — ApprovalSqlQueryService
    /// ile aynı basitleştirme (per-company DB'de tek anlamlı şema 'dbo' olduğundan yeterli).</summary>
    private static async Task<HashSet<string>> GetKnownObjectBareNamesAsync(SqlConnection connection, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.objects WHERE type IN ('U','V') AND is_ms_shipped = 0;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) set.Add(reader.GetString(0));
        return set;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqlConnection connection, string schema, string name, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.name
            FROM sys.columns c
            INNER JOIN sys.objects o ON o.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = @Schema AND o.name = @Name;
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", schema));
        cmd.Parameters.Add(new SqlParameter("@Name", name));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) set.Add(reader.GetString(0));
        return set;
    }

    // ════════════════════════════════════════════════════════════════
    // Küçük yardımcılar
    // ════════════════════════════════════════════════════════════════

    private static string ObjectKey(string schema, string name) => $"{schema.Trim()}.{name.Trim()}";

    private static bool IsValidIdentifier(string? s) => !string.IsNullOrWhiteSpace(s) && IdentifierRegex.IsMatch(s);

    private static bool TryMapJoinType(string? type, out string sql)
    {
        sql = (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "inner" => "INNER JOIN",
            "left" => "LEFT JOIN",
            "right" => "RIGHT JOIN",
            _ => string.Empty,
        };
        return sql.Length > 0;
    }

    private static bool TryMapOperator(string? op, out string sql)
    {
        if (op is not null && AllowedOps.TryGetValue(op.Trim(), out var mapped)) { sql = mapped; return true; }
        sql = string.Empty;
        return false;
    }

    /// <summary>Tek bir sondaki ';' ve boşlukları temizler — sqlBody bir alt sorguya "(...)" olarak
    /// sarmalandığında (COUNT/TOP/describe) sondaki noktalı virgül syntax hatası üretir.</summary>
    private static string NormalizeSqlBody(string sql)
    {
        var trimmed = sql.Trim();
        if (trimmed.EndsWith(';')) trimmed = trimmed[..^1].TrimEnd();
        return trimmed;
    }

    // ════════════════════════════════════════════════════════════════
    // ScriptDom visitor'ları
    // ════════════════════════════════════════════════════════════════

    private sealed class ForbiddenStatementVisitor : TSqlFragmentVisitor
    {
        public string? Forbidden { get; private set; }
        public override void Visit(InsertStatement node) => Forbidden ??= "INSERT";
        public override void Visit(UpdateStatement node) => Forbidden ??= "UPDATE";
        public override void Visit(DeleteStatement node) => Forbidden ??= "DELETE";
        public override void Visit(MergeStatement node) => Forbidden ??= "MERGE";
        public override void Visit(TruncateTableStatement node) => Forbidden ??= "TRUNCATE";
        public override void Visit(ExecuteStatement node) => Forbidden ??= "EXEC";
        public override void Visit(CreateTableStatement node) => Forbidden ??= "CREATE TABLE";
        public override void Visit(DropTableStatement node) => Forbidden ??= "DROP TABLE";
        public override void Visit(AlterTableStatement node) => Forbidden ??= "ALTER TABLE";
        public override void Visit(CreateProcedureStatement node) => Forbidden ??= "CREATE PROC";
        public override void Visit(AlterProcedureStatement node) => Forbidden ??= "ALTER PROC";
        public override void Visit(DropProcedureStatement node) => Forbidden ??= "DROP PROC";
        public override void Visit(CreateFunctionStatement node) => Forbidden ??= "CREATE FUNCTION";
        public override void Visit(DropFunctionStatement node) => Forbidden ??= "DROP FUNCTION";
        public override void Visit(CreateViewStatement node) => Forbidden ??= "CREATE VIEW";
        public override void Visit(AlterViewStatement node) => Forbidden ??= "ALTER VIEW";
        public override void Visit(DropViewStatement node) => Forbidden ??= "DROP VIEW";
    }

    private sealed class ViewTableReferenceVisitor : TSqlFragmentVisitor
    {
        public List<string> Tables { get; } = new();
        public override void Visit(NamedTableReference node)
        {
            var ids = node.SchemaObject?.Identifiers;
            if (ids is null || ids.Count == 0) return;
            var name = ids[^1].Value;
            if (!string.IsNullOrWhiteSpace(name) && !Tables.Contains(name, StringComparer.OrdinalIgnoreCase))
                Tables.Add(name);
        }
    }
}

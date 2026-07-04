using System.Diagnostics;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// ApprovalSqlQuery iş katmanı — CRUD pass-through + güvenli SQL validate/execute.
///
/// Güvenlik katmanları (sırayla):
///   1. ScriptDom parse — tek statement + SELECT yalnız
///   2. Tablo whitelist — sabit tablo/view + cbv_* prefix'i
///   3. CommandTimeout = 5 sn (DOS koruması)
///   4. (TODO) read-only login — dedicated SQL user kurulduğunda
///
/// Persistence katmanına yerleştirildi çünkü SqlServerConnectionFactory burada
/// (Application referans vermez). DocumentNumberService/PostProcedureExecutor
/// ile aynı pattern.
/// </summary>
public sealed class ApprovalSqlQueryService : IApprovalSqlQueryService
{
    private readonly IApprovalSqlQueryRepository _repository;
    private readonly SqlServerConnectionFactory _connectionFactory;

    // Whitelist — Karar koşulları için aggregate/lookup'ta beklenen tablo/view'lar.
    // cbv_* (calibra view) prefix'i ayrıca runtime'da kabul edilir.
    private static readonly HashSet<string> _allowedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Document", "DocumentLine", "Contact", "Items", "Personnel", "Department",
        "UserProfile", "MaterialGroups", "MaterialGroupMappings", "CardGroup",
        "CardGroupMapping", "ApprovalFlow", "ApprovalInstance",
    };

    public ApprovalSqlQueryService(
        IApprovalSqlQueryRepository repository,
        SqlServerConnectionFactory connectionFactory)
    {
        _repository = repository;
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ApprovalSqlQueryDto>> GetAllAsync(CancellationToken ct)
    {
        var list = await _repository.GetAllAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<ApprovalSqlQueryDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<int> SaveAsync(SaveApprovalSqlQueryRequest request, int? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Sorgu adı zorunludur.");
        if (string.IsNullOrWhiteSpace(request.SqlText))
            throw new InvalidOperationException("SQL metni zorunludur.");

        // Kaydetmeden önce parse — bozuk/yasaklı SQL kütüphaneye girmesin.
        var (ok, err) = await ValidateSqlAsync(request.SqlText);
        if (!ok) throw new InvalidOperationException($"SQL geçersiz: {err}");

        var resultType = NormalizeResultType(request.ResultType);

        if (request.Id <= 0)
        {
            var entity = new ApprovalSqlQueryEntity
            {
                Id             = 0,
                Name           = request.Name.Trim(),
                Description    = request.Description,
                SqlText        = request.SqlText,
                ParametersJson = request.ParametersJson,
                ResultType     = resultType,
                IsActive       = request.IsActive,
                CreatedById    = userId,
            };
            return await _repository.AddAsync(entity, ct);
        }
        else
        {
            var entity = new ApprovalSqlQueryEntity
            {
                Id             = request.Id,
                Name           = request.Name.Trim(),
                Description    = request.Description,
                SqlText        = request.SqlText,
                ParametersJson = request.ParametersJson,
                ResultType     = resultType,
                IsActive       = request.IsActive,
                UpdatedById    = userId,
            };
            await _repository.UpdateAsync(entity, ct);
            return request.Id;
        }
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repository.DeleteAsync(id, ct);

    public Task<(bool Ok, string? Error)> ValidateSqlAsync(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Task.FromResult<(bool, string?)>((false, "SQL boş olamaz."));

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        IList<ParseError> parseErrors;
        TSqlFragment fragment;
        using (var reader = new StringReader(sql))
        {
            fragment = parser.Parse(reader, out parseErrors);
        }

        if (parseErrors is { Count: > 0 })
        {
            var first = parseErrors[0];
            return Task.FromResult<(bool, string?)>(
                (false, $"Parse hatası ({first.Line}:{first.Column}): {first.Message}"));
        }

        if (fragment is not TSqlScript script || script.Batches.Count == 0)
            return Task.FromResult<(bool, string?)>((false, "Çalıştırılabilir batch bulunamadı."));

        if (script.Batches.Count > 1 || script.Batches[0].Statements.Count != 1)
            return Task.FromResult<(bool, string?)>((false, "Tek bir SELECT statement gerekli."));

        var statement = script.Batches[0].Statements[0];
        if (statement is not SelectStatement)
            return Task.FromResult<(bool, string?)>((false, "Yalnızca SELECT statement kabul edilir."));

        // DML/DDL/INTO yazma kontrolü — SelectStatement içinde SELECT INTO veya CTE'de INSERT olabilir.
        var dmlVisitor = new ForbiddenStatementVisitor();
        statement.Accept(dmlVisitor);
        if (dmlVisitor.Forbidden is not null)
            return Task.FromResult<(bool, string?)>((false, $"Yasak ifade: {dmlVisitor.Forbidden}"));

        // Tablo whitelist
        var tableVisitor = new TableReferenceVisitor();
        statement.Accept(tableVisitor);
        foreach (var t in tableVisitor.Tables)
        {
            if (!IsAllowedTable(t))
                return Task.FromResult<(bool, string?)>(
                    (false, $"Whitelist dışı tablo/view: '{t}'"));
        }

        return Task.FromResult<(bool, string?)>((true, null));
    }

    public async Task<ExecuteApprovalSqlResult> ExecuteAsync(
        string? sqlText,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
            return new ExecuteApprovalSqlResult(false, null, "SQL boş.", 0);

        var (ok, err) = await ValidateSqlAsync(sqlText);
        if (!ok) return new ExecuteApprovalSqlResult(false, null, err, 0);

        var sw = Stopwatch.StartNew();
        try
        {
            // TODO: dedicated read-only user kurulduğunda (CalibraHubReader login)
            // burayı o credential ile açan ayrı bir factory metoduna geçir.
            await using var con = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sqlText;
            cmd.CommandTimeout = 5; // saniye — DOS koruması

            if (parameters is not null)
            {
                foreach (var kv in parameters)
                {
                    var pname = kv.Key.StartsWith('@') ? kv.Key : "@" + kv.Key;
                    cmd.Parameters.AddWithValue(pname, kv.Value ?? DBNull.Value);
                }
            }

            var value = await cmd.ExecuteScalarAsync(ct);
            sw.Stop();
            return new ExecuteApprovalSqlResult(true, value is DBNull ? null : value, null, sw.ElapsedMilliseconds);
        }
        catch (SqlException sqlEx)
        {
            sw.Stop();
            return new ExecuteApprovalSqlResult(false, null,
                $"SQL hatası ({sqlEx.Number}): {sqlEx.Message}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExecuteApprovalSqlResult(false, null, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static bool IsAllowedTable(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var bare = name.Trim();
        // schema kaldır
        var dotIdx = bare.LastIndexOf('.');
        if (dotIdx >= 0 && dotIdx < bare.Length - 1) bare = bare[(dotIdx + 1)..];
        bare = bare.Trim('[', ']', ' ');
        if (bare.StartsWith("cbv_", StringComparison.OrdinalIgnoreCase)) return true;
        return _allowedTables.Contains(bare);
    }

    private static string NormalizeResultType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "scalar";
        var t = raw.Trim().ToLowerInvariant();
        return t is "scalar" or "boolean" or "count" ? t : "scalar";
    }

    private static ApprovalSqlQueryDto ToDto(ApprovalSqlQueryEntity e) =>
        new(e.Id, e.Name, e.Description, e.SqlText, e.ParametersJson, e.ResultType,
            e.IsActive, e.CreatedById, e.Created, e.UpdatedById, e.Updated);

    // ── ScriptDom Visitors ───────────────────────────────────────────────────
    private sealed class ForbiddenStatementVisitor : TSqlFragmentVisitor
    {
        public string? Forbidden { get; private set; }
        public override void Visit(InsertStatement node)        => Forbidden ??= "INSERT";
        public override void Visit(UpdateStatement node)        => Forbidden ??= "UPDATE";
        public override void Visit(DeleteStatement node)        => Forbidden ??= "DELETE";
        public override void Visit(MergeStatement node)         => Forbidden ??= "MERGE";
        public override void Visit(TruncateTableStatement node) => Forbidden ??= "TRUNCATE";
        // NOT: SELECT ... INTO kontrolu kaldirildi — ScriptDom 170.x'te Into property'si
        // hem SelectStatement'ta hem QuerySpecification'ta yok. INSERT/UPDATE/DELETE/CREATE
        // TABLE statement'lari zaten yasakli — SELECT INTO ile yeni tablo yaratma yolu yok.
        public override void Visit(ExecuteStatement node)       => Forbidden ??= "EXEC";
        public override void Visit(CreateTableStatement node)   => Forbidden ??= "CREATE TABLE";
        public override void Visit(DropTableStatement node)     => Forbidden ??= "DROP TABLE";
        public override void Visit(AlterTableStatement node)    => Forbidden ??= "ALTER TABLE";
        public override void Visit(CreateProcedureStatement node) => Forbidden ??= "CREATE PROC";
        public override void Visit(AlterProcedureStatement node)  => Forbidden ??= "ALTER PROC";
        public override void Visit(DropProcedureStatement node)   => Forbidden ??= "DROP PROC";
        public override void Visit(CreateFunctionStatement node)  => Forbidden ??= "CREATE FUNCTION";
        public override void Visit(DropFunctionStatement node)    => Forbidden ??= "DROP FUNCTION";
        public override void Visit(CreateViewStatement node)      => Forbidden ??= "CREATE VIEW";
        public override void Visit(DropViewStatement node)        => Forbidden ??= "DROP VIEW";
    }

    private sealed class TableReferenceVisitor : TSqlFragmentVisitor
    {
        public List<string> Tables { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            // SchemaObjectName.Identifiers: [server, db, schema, name] - en sondaki ad.
            var ids = node.SchemaObject?.Identifiers;
            if (ids is null || ids.Count == 0) return;
            var name = ids[^1].Value;
            if (!string.IsNullOrWhiteSpace(name) && !Tables.Contains(name, StringComparer.OrdinalIgnoreCase))
                Tables.Add(name);
        }
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Services.CodeRule;

/// <summary>
/// Cari/Stok kod üretici. SqlServerConnectionFactory bağımlılığı sebebiyle Persistence
/// katmanında (ApprovalSqlQueryService ile aynı pattern). Application'da
/// <see cref="ICodeGeneratorService"/> interface'i tutulur.
///
/// Token sözdizimi (regex): {Type:Arg}
///   {Field:KolonAdi}     → request.FieldValues[KolonAdi]
///   {Widget:WidgetKey}   → request.WidgetValues[WidgetKey]
///   {Counter:N}          → kural sayacı, N hane zero-pad
///   {Year:yyyy|yy}       → şimdiki yıl
///   {Month:MM}           → şimdiki ay
///   {Day:dd}             → şimdiki gün
///
/// Çakışma: Contact.AccountCode / Items.Code DB'de varsa otomatik suffix (-A...-Z) eklenir.
/// </summary>
public sealed class CodeGeneratorService : ICodeGeneratorService
{
    private static readonly Regex TokenRegex = new(
        @"\{(?<type>Field|Widget|Counter|Year|Month|Day):(?<arg>[A-Za-z0-9_]+)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ICodeRuleRepository _repo;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly ILogger<CodeGeneratorService> _logger;

    public CodeGeneratorService(
        ICodeRuleRepository repo,
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        ILogger<CodeGeneratorService> logger)
    {
        _repo = repo;
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _logger = logger;
    }

    public async Task<GenerateCodeResult> GenerateAsync(GenerateCodeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EntityType))
            return new GenerateCodeResult(false, null, null, null, "EntityType zorunlu", 0);

        var rules = await _repo.GetActiveByEntityAsync(request.EntityType, ct);
        if (rules.Count == 0)
            return new GenerateCodeResult(false, null, null, null, "Aktif kural yok", 0);

        var matched = rules.FirstOrDefault(r => MatchesAllConditions(r, request));
        if (matched is null)
            return new GenerateCodeResult(false, null, null, null, "Eşleşen kural yok", 0);

        try
        {
            var baseCode = await ExpandTemplateAsync(matched, request, ct);
            if (string.IsNullOrWhiteSpace(baseCode))
                return new GenerateCodeResult(false, null, matched.Id, matched.Name, "Boş kod üretildi", 0);

            var (finalCode, attempts) = await EnsureUniqueAsync(request.EntityType, baseCode, ct);
            if (finalCode is null)
                return new GenerateCodeResult(false, baseCode, matched.Id, matched.Name,
                    "26 denemede benzersiz kod üretilemedi", attempts);

            return new GenerateCodeResult(true, finalCode, matched.Id, matched.Name, null, attempts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CodeGenerator hata (rule={Id})", matched.Id);
            return new GenerateCodeResult(false, null, matched.Id, matched.Name, ex.Message, 0);
        }
    }

    private static bool MatchesAllConditions(Domain.Entities.CodeRule rule, GenerateCodeRequest req)
    {
        if (rule.Conditions is null || rule.Conditions.Count == 0) return true;
        return rule.Conditions.All(c => MatchOne(c, req));
    }

    private static bool MatchOne(CodeRuleCondition cond, GenerateCodeRequest req)
    {
        var source = string.Equals(cond.FieldType, "Widget", StringComparison.OrdinalIgnoreCase)
            ? req.WidgetValues : req.FieldValues;
        source.TryGetValue(cond.FieldName, out var actual);

        var op = (cond.Operator ?? "=").Trim().ToLowerInvariant();
        switch (op)
        {
            case "isnull":    return string.IsNullOrEmpty(actual);
            case "isnotnull": return !string.IsNullOrEmpty(actual);
            case "=":         return string.Equals(actual ?? string.Empty, cond.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "!=":        return !string.Equals(actual ?? string.Empty, cond.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "startswith":
                return !string.IsNullOrEmpty(actual) && actual!.StartsWith(cond.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "in":
            case "notin":
                var values = ParseJsonArray(cond.Value);
                var inList = values.Any(v => string.Equals(v, actual ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                return op == "in" ? inList : !inList;
            default: return false;
        }
    }

    private static IReadOnlyList<string> ParseJsonArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return doc.RootElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private async Task<string> ExpandTemplateAsync(Domain.Entities.CodeRule rule, GenerateCodeRequest req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var resetKey = ComputeResetKey(rule.ResetPeriod, now);
        var matches = TokenRegex.Matches(rule.Template);

        long? counterValue = null;
        var hasCounter = matches.Any(m => m.Groups["type"].Value.Equals("Counter", StringComparison.OrdinalIgnoreCase));
        if (hasCounter)
        {
            counterValue = await _repo.IncrementCounterAsync(rule.Id, resetKey, startValue: 1, ct);
        }

        return TokenRegex.Replace(rule.Template, m =>
        {
            var type = m.Groups["type"].Value.ToLowerInvariant();
            var arg = m.Groups["arg"].Value;
            return type switch
            {
                "field"   => req.FieldValues.TryGetValue(arg, out var v) ? (v ?? string.Empty) : string.Empty,
                "widget"  => req.WidgetValues.TryGetValue(arg, out var v) ? (v ?? string.Empty) : string.Empty,
                "counter" => FormatCounter(counterValue ?? 0, arg),
                "year"    => arg.Equals("yy", StringComparison.OrdinalIgnoreCase) ? (now.Year % 100).ToString("D2") : now.Year.ToString(),
                "month"   => now.Month.ToString("D2"),
                "day"     => now.Day.ToString("D2"),
                _         => m.Value,
            };
        });
    }

    private static string FormatCounter(long value, string padArg)
    {
        if (!int.TryParse(padArg, out var width) || width < 1) width = 4;
        return value.ToString(new string('0', width));
    }

    private static string ComputeResetKey(DocumentNumberResetPeriod period, DateTime now) => period switch
    {
        DocumentNumberResetPeriod.Yearly  => now.Year.ToString(),
        DocumentNumberResetPeriod.Monthly => now.ToString("yyyyMM"),
        DocumentNumberResetPeriod.Daily   => now.ToString("yyyyMMdd"),
        _ => string.Empty,
    };

    private async Task<(string? code, int attempts)> EnsureUniqueAsync(string entityType, string baseCode, CancellationToken ct)
    {
        if (!await ExistsAsync(entityType, baseCode, ct)) return (baseCode, 0);
        for (var i = 0; i < 26; i++)
        {
            var candidate = $"{baseCode}-{(char)('A' + i)}";
            if (!await ExistsAsync(entityType, candidate, ct)) return (candidate, i + 1);
        }
        return (null, 26);
    }

    private async Task<bool> ExistsAsync(string entityType, string code, CancellationToken ct)
    {
        var (table, column) = ResolveTableAndColumn(entityType);
        if (table is null) return false;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 1 FROM [{_schema}].[{table}] WHERE [{column}] = @Code;";
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    private static (string? table, string column) ResolveTableAndColumn(string entityType) => entityType.ToLowerInvariant() switch
    {
        "contact" => ("Contact", "AccountCode"),
        "item"    => ("Items",   "Code"),
        _         => (null, string.Empty),
    };
}

using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Scheduling;

/// <summary>
/// SQL stored procedure calistiran executor.
/// ParametersJson format:
///   {"procedureName": "sp_Nightly_Analytics", "parameters": {"p1":"v1", "p2":42}, "timeoutSeconds": 300}
///
/// Parametre degerleri string ise, icindeki {COMPANY_ID}, {INTEGRATION_DB} gibi
/// placeholder'lar runtime'da IScheduledTaskTokenResolver tarafindan task.CompanyId
/// uzerinden gercek sirket degerleriyle replace edilir. Ornek:
///   {"procedureName": "sp_Sync", "parameters": {"tenantId": "{COMPANY_ID}", "dbName": "{INTEGRATION_DB}"}}
/// </summary>
public sealed class SqlProcedureTaskExecutor : IScheduledTaskExecutor
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IScheduledTaskTokenResolver _tokenResolver;
    private static readonly Regex SafeNameRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"\{([A-Z][A-Z0-9_]*)\}", RegexOptions.Compiled);

    public SqlProcedureTaskExecutor(SqlServerConnectionFactory connectionFactory, IScheduledTaskTokenResolver tokenResolver)
    {
        _connectionFactory = connectionFactory;
        _tokenResolver = tokenResolver;
    }

    public ScheduledTaskType SupportedType => ScheduledTaskType.SqlProcedure;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        string? executedCommand = null;
        try
        {
            var config = ParseConfig(task.ParametersJson);
            if (string.IsNullOrWhiteSpace(config.ProcedureName))
                return TaskExecutionResult.Error("Gecersiz ParametersJson: 'procedureName' gerekli.");

            if (!SafeNameRegex.IsMatch(config.ProcedureName))
                return TaskExecutionResult.Error($"Gecersiz procedure adi: '{config.ProcedureName}'. Sadece harf/rakam/alt cizgi + opsiyonel schema.");

            // Sirket bagli token'lari (COMPANY_ID, INTEGRATION_DB vb.) bir kere cozup
            // string parametre degerlerinde {TOKEN} placeholder'larini replace ederiz.
            // CompanyId yoksa (legacy task) bos sozluk — replace pas gecer.
            IReadOnlyDictionary<string, string?> tokens = task.CompanyId.HasValue
                ? await _tokenResolver.ResolveAsync(task.CompanyId.Value, cancellationToken)
                : new Dictionary<string, string?>();

            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText    = config.ProcedureName;
            cmd.CommandType    = CommandType.StoredProcedure;
            cmd.CommandTimeout = config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 300;

            // Calistirilan SQL'i debug/audit icin paralel olarak topluyoruz — token-resolved
            // gercek parametre degerleriyle. History modal'da kullaniciya gosterilir.
            var sqlBuilder = new StringBuilder();
            sqlBuilder.Append("EXEC ").Append(config.ProcedureName);

            if (config.Parameters is not null && config.Parameters.Count > 0)
            {
                var first = true;
                foreach (var kv in config.Parameters)
                {
                    var paramName = kv.Key.StartsWith('@') ? kv.Key : "@" + kv.Key;
                    object? value = kv.Value.ValueKind switch
                    {
                        JsonValueKind.Null      => DBNull.Value,
                        JsonValueKind.String    => ApplyTokens(kv.Value.GetString(), tokens),
                        JsonValueKind.Number    => kv.Value.TryGetInt64(out var l) ? (object)l : kv.Value.GetDouble(),
                        JsonValueKind.True      => true,
                        JsonValueKind.False     => false,
                        _                        => kv.Value.GetRawText(),
                    };
                    cmd.Parameters.Add(new SqlParameter(paramName, value ?? DBNull.Value));

                    sqlBuilder.Append(first ? ' ' : ',').Append(' ').Append(paramName).Append(" = ").Append(FormatSqlValue(value));
                    first = false;
                }
            }

            sqlBuilder.Append(';');
            executedCommand = sqlBuilder.ToString();

            var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return TaskExecutionResult.Success(
                $"{config.ProcedureName} calisti, affected rows: {rowsAffected}",
                executedCommand);
        }
        catch (OperationCanceledException) { return TaskExecutionResult.Error("Iptal edildi.", executedCommand); }
        catch (Exception ex) { return TaskExecutionResult.Error(ex.Message, executedCommand); }
    }

    /// <summary>SQL prosedur log'u icin parametre degerini guvenli string olarak formatlar.</summary>
    private static string FormatSqlValue(object? value)
    {
        if (value is null || value is DBNull) return "NULL";
        return value switch
        {
            string s    => "N'" + s.Replace("'", "''") + "'",
            bool b      => b ? "1" : "0",
            long l      => l.ToString(CultureInfo.InvariantCulture),
            int i       => i.ToString(CultureInfo.InvariantCulture),
            double d    => d.ToString(CultureInfo.InvariantCulture),
            DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "'",
            _           => "N'" + (value.ToString() ?? string.Empty).Replace("'", "''") + "'",
        };
    }

    private static string? ApplyTokens(string? input, IReadOnlyDictionary<string, string?> tokens)
    {
        if (string.IsNullOrEmpty(input) || tokens.Count == 0) return input;
        return TokenRegex.Replace(input, m =>
        {
            var key = m.Groups[1].Value;
            return tokens.TryGetValue(key, out var v) ? v ?? string.Empty : m.Value;
        });
    }

    private static SqlProcedureConfig ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var config = JsonSerializer.Deserialize<SqlProcedureConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return config ?? new();
        }
        catch { return new(); }
    }

    private sealed class SqlProcedureConfig
    {
        public string ProcedureName { get; set; } = string.Empty;
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public int TimeoutSeconds { get; set; } = 300;
    }
}

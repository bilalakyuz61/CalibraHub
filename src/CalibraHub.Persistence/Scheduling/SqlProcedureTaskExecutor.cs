using System.Data;
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
/// </summary>
public sealed class SqlProcedureTaskExecutor : IScheduledTaskExecutor
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private static readonly Regex SafeNameRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.Compiled);

    public SqlProcedureTaskExecutor(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public ScheduledTaskType SupportedType => ScheduledTaskType.SqlProcedure;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        try
        {
            var config = ParseConfig(task.ParametersJson);
            if (string.IsNullOrWhiteSpace(config.ProcedureName))
                return TaskExecutionResult.Error("Gecersiz ParametersJson: 'procedureName' gerekli.");

            if (!SafeNameRegex.IsMatch(config.ProcedureName))
                return TaskExecutionResult.Error($"Gecersiz procedure adi: '{config.ProcedureName}'. Sadece harf/rakam/alt cizgi + opsiyonel schema.");

            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText    = config.ProcedureName;
            cmd.CommandType    = CommandType.StoredProcedure;
            cmd.CommandTimeout = config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 300;

            if (config.Parameters is not null)
            {
                foreach (var kv in config.Parameters)
                {
                    var paramName = kv.Key.StartsWith('@') ? kv.Key : "@" + kv.Key;
                    object? value = kv.Value.ValueKind switch
                    {
                        JsonValueKind.Null      => DBNull.Value,
                        JsonValueKind.String    => kv.Value.GetString(),
                        JsonValueKind.Number    => kv.Value.TryGetInt64(out var l) ? (object)l : kv.Value.GetDouble(),
                        JsonValueKind.True      => true,
                        JsonValueKind.False     => false,
                        _                        => kv.Value.GetRawText(),
                    };
                    cmd.Parameters.Add(new SqlParameter(paramName, value ?? DBNull.Value));
                }
            }

            var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return TaskExecutionResult.Success($"{config.ProcedureName} calisti, affected rows: {rowsAffected}");
        }
        catch (OperationCanceledException) { return TaskExecutionResult.Error("Iptal edildi."); }
        catch (Exception ex) { return TaskExecutionResult.Error(ex.Message); }
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

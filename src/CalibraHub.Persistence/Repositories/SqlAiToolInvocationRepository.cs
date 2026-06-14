using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-05-24 — Calibo write tool denetim kaydi (AiToolInvocation).
/// </summary>
public sealed class SqlAiToolInvocationRepository : IAiToolInvocationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlAiToolInvocationRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _table = $"[{s}].[AiToolInvocation]";
    }

    public async Task LogExecutedAsync(AiToolInvocationLogEntry entry, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
              ([UserId],[ToolName],[ActionLabel],[ArgumentsJson],[Status],
               [ResultSummary],[AffectedEntity],[ErrorMessage])
            VALUES
              (@UserId,@ToolName,@ActionLabel,@ArgumentsJson,@Status,
               @ResultSummary,@AffectedEntity,@ErrorMessage);
            """;
        cmd.Parameters.Add(new SqlParameter("@UserId", entry.UserId));
        cmd.Parameters.Add(new SqlParameter("@ToolName", entry.ToolName));
        cmd.Parameters.Add(new SqlParameter("@ActionLabel", (object?)entry.ActionLabel ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ArgumentsJson", (object?)entry.ArgumentsJson ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Status", entry.Status));
        cmd.Parameters.Add(new SqlParameter("@ResultSummary", (object?)Truncate(entry.ResultSummary, 1000) ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@AffectedEntity", (object?)entry.AffectedEntity ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ErrorMessage", (object?)Truncate(entry.ErrorMessage, 1000) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}

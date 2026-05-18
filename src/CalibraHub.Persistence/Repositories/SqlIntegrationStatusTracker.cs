using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SqlServer impl — sadece 'document' BaseTable'i icin yazar. Diger tablolar icin
/// silently no-op (ileride Contact / Item gibi diger tablolar icin de eklenebilir).
///
/// Identifier validation: baseTable + baseRecordKey alphanumeric_underscore — SQL
/// injection guvenli (white-list yerine pattern check).
/// </summary>
public sealed class SqlIntegrationStatusTracker : IIntegrationStatusTracker
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    // Suanda destek verdigimiz BaseTable'lar — buraya ekleyerek genisletebilirsiniz.
    // Forms.BaseTable degeri ile case-insensitive eslesir.
    private static readonly HashSet<string> SupportedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "document",
        "Document",
    };

    public SqlIntegrationStatusTracker(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options?.Schema) ? "dbo" : options.Schema.Trim();
    }

    public Task MarkSentAsync(string baseTable, string baseRecordKey, string recordId,
        int integrationId, long runId, CancellationToken ct)
        => UpdateAsync(baseTable, baseRecordKey, recordId, integrationId, runId, "Sent", setSentAt: true, ct);

    public Task MarkFailedAsync(string baseTable, string baseRecordKey, string recordId,
        int integrationId, long runId, CancellationToken ct)
        => UpdateAsync(baseTable, baseRecordKey, recordId, integrationId, runId, "Failed", setSentAt: false, ct);

    public async Task<DateTime?> GetSentAtAsync(string baseTable, string baseRecordKey, string recordId,
        CancellationToken ct)
    {
        if (!IsSupported(baseTable, baseRecordKey, recordId)) return null;

        var s   = _schema.Replace("]", "]]");
        var tbl = baseTable.Replace("]", "]]");
        var key = baseRecordKey.Replace("]", "]]");

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TOP 1 [IntegrationSentAt] FROM [{s}].[{tbl}] WHERE [{key}] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", recordId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DateTime dt ? dt : (DateTime?)null;
    }

    private async Task UpdateAsync(string baseTable, string baseRecordKey, string recordId,
        int integrationId, long runId, string status, bool setSentAt, CancellationToken ct)
    {
        if (!IsSupported(baseTable, baseRecordKey, recordId)) return;

        var s   = _schema.Replace("]", "]]");
        var tbl = baseTable.Replace("]", "]]");
        var key = baseRecordKey.Replace("]", "]]");

        var setSentAtClause = setSentAt ? "[IntegrationSentAt] = SYSUTCDATETIME(), " : string.Empty;

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{s}].[{tbl}]
            SET {setSentAtClause}
                [IntegrationStatus]    = @Status,
                [LastIntegrationRunId] = @RunId,
                [LastIntegrationId]    = @IntegrationId
            WHERE [{key}] = @Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Status", status));
        cmd.Parameters.Add(new SqlParameter("@RunId", runId));
        cmd.Parameters.Add(new SqlParameter("@IntegrationId", integrationId));
        cmd.Parameters.Add(new SqlParameter("@Id", recordId));
        try { await cmd.ExecuteNonQueryAsync(ct); }
        catch { /* tablo kolon eksikligi vb. — sessizce yut, runner'i bozma */ }
    }

    private static bool IsSupported(string? baseTable, string? baseKey, string? recordId)
    {
        if (string.IsNullOrWhiteSpace(baseTable) || string.IsNullOrWhiteSpace(baseKey) || string.IsNullOrWhiteSpace(recordId))
            return false;
        if (!SupportedTables.Contains(baseTable)) return false;
        return IsSafeIdentifier(baseTable) && IsSafeIdentifier(baseKey);
    }

    private static bool IsSafeIdentifier(string s)
    {
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        return s.Length is > 0 and < 128;
    }
}

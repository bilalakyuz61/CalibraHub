using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlCollaborationLockRepository : ICollaborationLockRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlCollaborationLockRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[GlobalLock]";
    }

    public async Task<IReadOnlyCollection<GlobalLockRecord>> GetActiveAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [RecordType],[RecordId],[UserId],[UserName],[SessionId],
                   [RecordTitle],[PageUrl],[AcquiredAt],[LastHeartbeat]
            FROM   {_table}
            WHERE  [IsActive] = 1
            AND    [LastHeartbeat] >= DATEADD(SECOND, -120, SYSUTCDATETIME())
            ORDER  BY [AcquiredAt];
            """;

        var results = new List<GlobalLockRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new GlobalLockRecord(
                RecordType: reader.GetString(0),
                RecordId: reader.GetString(1),
                UserId: reader.GetString(2),
                UserName: reader.GetString(3),
                SessionId: reader.GetString(4),
                RecordTitle: reader.IsDBNull(5) ? null : reader.GetString(5),
                PageUrl: reader.IsDBNull(6) ? null : reader.GetString(6),
                AcquiredAt: reader.GetDateTime(7),
                LastHeartbeatAt: reader.GetDateTime(8)));
        }

        return results;
    }

    public async Task FullSyncAsync(IReadOnlyCollection<GlobalLockRecord> activeLocks, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            await using var delCmd = conn.CreateCommand();
            delCmd.Transaction = tx;
            delCmd.CommandText = $"UPDATE {_table} SET [IsActive]=0 WHERE [IsActive]=1;";
            await delCmd.ExecuteNonQueryAsync(ct);

            foreach (var lk in activeLocks)
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_table}
                        ([RecordType],[RecordId],[UserId],[UserName],[SessionId],
                         [RecordTitle],[PageUrl],[AcquiredAt],[LastHeartbeat],[IsActive],
                         [Created],[CreatedBy])
                    VALUES
                        (@RecordType,@RecordId,@UserId,@UserName,@SessionId,
                         @RecordTitle,@PageUrl,@AcquiredAt,@LastHeartbeat,1,
                         SYSUTCDATETIME(),@UserId);
                    """;
                insCmd.Parameters.AddWithValue("@RecordType", lk.RecordType);
                insCmd.Parameters.AddWithValue("@RecordId", lk.RecordId);
                insCmd.Parameters.AddWithValue("@UserId", lk.UserId);
                insCmd.Parameters.AddWithValue("@UserName", lk.UserName);
                insCmd.Parameters.AddWithValue("@SessionId", lk.SessionId);
                insCmd.Parameters.AddWithValue("@RecordTitle", (object?)lk.RecordTitle ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@PageUrl", (object?)lk.PageUrl ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@AcquiredAt", lk.AcquiredAt);
                insCmd.Parameters.AddWithValue("@LastHeartbeat", lk.LastHeartbeatAt);
                await insCmd.ExecuteNonQueryAsync(ct);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task ReleaseAsync(string recordType, string recordId, string sessionId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET    [IsActive]=0, [Updated]=SYSUTCDATETIME()
            WHERE  [IsActive]=1
            AND    [RecordType]=@RecordType AND [RecordId]=@RecordId AND [SessionId]=@SessionId;
            """;
        cmd.Parameters.AddWithValue("@RecordType", recordType);
        cmd.Parameters.AddWithValue("@RecordId", recordId);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReleaseAllForSessionAsync(string sessionId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET    [IsActive]=0, [Updated]=SYSUTCDATETIME()
            WHERE  [IsActive]=1 AND [SessionId]=@SessionId;
            """;
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AdminBreakAsync(string recordType, string recordId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET    [IsActive]=0, [Updated]=SYSUTCDATETIME()
            WHERE  [IsActive]=1
            AND    [RecordType]=@RecordType AND [RecordId]=@RecordId;
            """;
        cmd.Parameters.AddWithValue("@RecordType", recordType);
        cmd.Parameters.AddWithValue("@RecordId", recordId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CleanExpiredAsync(TimeSpan timeout, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_table}
            SET    [IsActive]=0, [Updated]=SYSUTCDATETIME()
            WHERE  [IsActive]=1
            AND    [LastHeartbeat] < DATEADD(SECOND, @NegativeSeconds, SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@NegativeSeconds", -(int)timeout.TotalSeconds);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

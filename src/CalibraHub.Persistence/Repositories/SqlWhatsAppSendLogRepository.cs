using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlWhatsAppSendLogRepository : IWhatsAppSendLogRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlWhatsAppSendLogRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[WhatsAppSendLog]";
    }

    public async Task<long> InsertAsync(WhatsAppSendLog entry, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
                ([SentAt],[ToPhone],[MessageHash],[MessageId],[Success],[ErrorMessage],[BlockReason])
            OUTPUT INSERTED.[Id]
            VALUES (@SentAt,@ToPhone,@Hash,@MsgId,@Success,@Err,@Block);
            """;
        cmd.Parameters.Add(new SqlParameter("@SentAt",  entry.SentAt));
        cmd.Parameters.Add(new SqlParameter("@ToPhone", (object?)entry.ToPhone      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Hash",    (object?)entry.MessageHash  ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@MsgId",   (object?)entry.MessageId    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Success", entry.Success));
        cmd.Parameters.Add(new SqlParameter("@Err",     (object?)entry.ErrorMessage ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Block",   (object?)entry.BlockReason  ?? DBNull.Value));
        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(res);
    }

    public async Task<int> CountSuccessAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM {_table} WHERE [Success]=1 AND [SentAt] >= @Since;";
        cmd.Parameters.Add(new SqlParameter("@Since", sinceUtc));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> CountSuccessForRecipientTodayAsync(string toPhone, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(1) FROM {_table}
             WHERE [Success]=1 AND [ToPhone]=@Phone
               AND [SentAt] >= CAST(GETUTCDATE() AS DATE);
            """;
        cmd.Parameters.Add(new SqlParameter("@Phone", toPhone));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> CountSuccessByHashTodayAsync(string messageHash, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(1) FROM {_table}
             WHERE [Success]=1 AND [MessageHash]=@Hash
               AND [SentAt] >= CAST(GETUTCDATE() AS DATE);
            """;
        cmd.Parameters.Add(new SqlParameter("@Hash", messageHash));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<List<WhatsAppSendLog>> GetRecentLogsAsync(int count, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@N) [Id],[SentAt],[ToPhone],[MessageHash],[MessageId],[Success],[ErrorMessage],[BlockReason]
              FROM {_table}
             ORDER BY [Id] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@N", count));
        var list = new List<WhatsAppSendLog>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new WhatsAppSendLog
            {
                Id           = r.GetInt64(0),
                SentAt       = r.GetDateTime(1),
                ToPhone      = r.IsDBNull(2) ? null : r.GetString(2),
                MessageHash  = r.IsDBNull(3) ? null : r.GetString(3),
                MessageId    = r.IsDBNull(4) ? null : r.GetString(4),
                Success      = r.GetBoolean(5),
                ErrorMessage = r.IsDBNull(6) ? null : r.GetString(6),
                BlockReason  = r.IsDBNull(7) ? null : r.GetString(7),
            });
        }
        return list;
    }
}

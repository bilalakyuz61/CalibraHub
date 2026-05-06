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
        _table = $"[{schema}].[whatsapp_send_log]";
    }

    public async Task<long> InsertAsync(WhatsAppSendLog entry, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
                ([sent_at],[to_phone],[message_hash],[message_id],[success],[error_message],[block_reason])
            OUTPUT INSERTED.[id]
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
        cmd.CommandText = $"SELECT COUNT(1) FROM {_table} WHERE [success]=1 AND [sent_at] >= @Since;";
        cmd.Parameters.Add(new SqlParameter("@Since", sinceUtc));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> CountSuccessForRecipientTodayAsync(string toPhone, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(1) FROM {_table}
             WHERE [success]=1 AND [to_phone]=@Phone
               AND [sent_at] >= CAST(GETUTCDATE() AS DATE);
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
             WHERE [success]=1 AND [message_hash]=@Hash
               AND [sent_at] >= CAST(GETUTCDATE() AS DATE);
            """;
        cmd.Parameters.Add(new SqlParameter("@Hash", messageHash));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<List<WhatsAppSendLog>> GetRecentLogsAsync(int count, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@N) [id],[sent_at],[to_phone],[message_hash],[message_id],[success],[error_message],[block_reason]
              FROM {_table}
             ORDER BY [id] DESC;
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

public sealed class SqlWhatsAppSafetyRulesRepository : IWhatsAppSafetyRulesRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlWhatsAppSafetyRulesRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[whatsapp_safety_rules]";
    }

    public async Task<WhatsAppSafetyRules?> GetAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[max_per_minute],[max_per_hour],[max_per_day],[max_per_recipient_per_day],
                   [min_delay_seconds],[max_delay_seconds],
                   [respect_quiet_hours],[quiet_hours_start_hour],[quiet_hours_end_hour],
                   [max_consecutive_failures],[failure_cooldown_minutes],
                   [warmup_days],[warmup_max_per_day],[max_identical_messages_per_day],
                   [Created],[Updated]
              FROM {_table} WHERE [id]=1;
            """;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        return new WhatsAppSafetyRules
        {
            Id                          = r.GetInt32(0),
            MaxPerMinute                = r.GetInt32(1),
            MaxPerHour                  = r.GetInt32(2),
            MaxPerDay                   = r.GetInt32(3),
            MaxPerRecipientPerDay       = r.GetInt32(4),
            MinDelaySeconds             = r.GetInt32(5),
            MaxDelaySeconds             = r.GetInt32(6),
            RespectQuietHours           = r.GetBoolean(7),
            QuietHoursStartHour         = r.GetInt32(8),
            QuietHoursEndHour           = r.GetInt32(9),
            MaxConsecutiveFailures      = r.GetInt32(10),
            FailureCooldownMinutes      = r.GetInt32(11),
            WarmupDays                  = r.GetInt32(12),
            WarmupMaxPerDay             = r.GetInt32(13),
            MaxIdenticalMessagesPerDay  = r.GetInt32(14),
            CreatedAt                   = r.GetDateTime(15),
            UpdatedAt                   = r.GetDateTime(16),
        };
    }

    public async Task SaveAsync(WhatsAppSafetyRules rules, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_table} WHERE [id]=1)
                UPDATE {_table} SET
                    [max_per_minute]=@MaxMin, [max_per_hour]=@MaxHour, [max_per_day]=@MaxDay,
                    [max_per_recipient_per_day]=@MaxRcpDay,
                    [min_delay_seconds]=@MinDelay, [max_delay_seconds]=@MaxDelay,
                    [respect_quiet_hours]=@RespectQH, [quiet_hours_start_hour]=@QHStart, [quiet_hours_end_hour]=@QHEnd,
                    [max_consecutive_failures]=@MaxFail, [failure_cooldown_minutes]=@CDMin,
                    [warmup_days]=@WarmDays, [warmup_max_per_day]=@WarmMax,
                    [max_identical_messages_per_day]=@MaxIdent,
                    [Updated]=GETUTCDATE()
                WHERE [id]=1;
            ELSE
                INSERT INTO {_table}
                    ([id],[max_per_minute],[max_per_hour],[max_per_day],[max_per_recipient_per_day],
                     [min_delay_seconds],[max_delay_seconds],
                     [respect_quiet_hours],[quiet_hours_start_hour],[quiet_hours_end_hour],
                     [max_consecutive_failures],[failure_cooldown_minutes],
                     [warmup_days],[warmup_max_per_day],[max_identical_messages_per_day],
                     [Created],[Updated])
                VALUES (1,@MaxMin,@MaxHour,@MaxDay,@MaxRcpDay,@MinDelay,@MaxDelay,
                        @RespectQH,@QHStart,@QHEnd,@MaxFail,@CDMin,
                        @WarmDays,@WarmMax,@MaxIdent,GETUTCDATE(),GETUTCDATE());
            """;
        cmd.Parameters.Add(new SqlParameter("@MaxMin",     rules.MaxPerMinute));
        cmd.Parameters.Add(new SqlParameter("@MaxHour",    rules.MaxPerHour));
        cmd.Parameters.Add(new SqlParameter("@MaxDay",     rules.MaxPerDay));
        cmd.Parameters.Add(new SqlParameter("@MaxRcpDay",  rules.MaxPerRecipientPerDay));
        cmd.Parameters.Add(new SqlParameter("@MinDelay",   rules.MinDelaySeconds));
        cmd.Parameters.Add(new SqlParameter("@MaxDelay",   rules.MaxDelaySeconds));
        cmd.Parameters.Add(new SqlParameter("@RespectQH",  rules.RespectQuietHours));
        cmd.Parameters.Add(new SqlParameter("@QHStart",    rules.QuietHoursStartHour));
        cmd.Parameters.Add(new SqlParameter("@QHEnd",      rules.QuietHoursEndHour));
        cmd.Parameters.Add(new SqlParameter("@MaxFail",    rules.MaxConsecutiveFailures));
        cmd.Parameters.Add(new SqlParameter("@CDMin",      rules.FailureCooldownMinutes));
        cmd.Parameters.Add(new SqlParameter("@WarmDays",   rules.WarmupDays));
        cmd.Parameters.Add(new SqlParameter("@WarmMax",    rules.WarmupMaxPerDay));
        cmd.Parameters.Add(new SqlParameter("@MaxIdent",   rules.MaxIdenticalMessagesPerDay));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

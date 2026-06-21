using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

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
            MaxConsecutiveFailures      = r.GetInt32(7),
            FailureCooldownMinutes      = r.GetInt32(8),
            WarmupDays                  = r.GetInt32(9),
            WarmupMaxPerDay             = r.GetInt32(10),
            MaxIdenticalMessagesPerDay  = r.GetInt32(11),
            CreatedAt                   = r.GetDateTime(12),
            UpdatedAt                   = r.GetDateTime(13),
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
                    [max_consecutive_failures]=@MaxFail, [failure_cooldown_minutes]=@CDMin,
                    [warmup_days]=@WarmDays, [warmup_max_per_day]=@WarmMax,
                    [max_identical_messages_per_day]=@MaxIdent,
                    [Updated]=GETUTCDATE()
                WHERE [id]=1;
            ELSE
                INSERT INTO {_table}
                    ([id],[max_per_minute],[max_per_hour],[max_per_day],[max_per_recipient_per_day],
                     [min_delay_seconds],[max_delay_seconds],
                     [max_consecutive_failures],[failure_cooldown_minutes],
                     [warmup_days],[warmup_max_per_day],[max_identical_messages_per_day],
                     [Created],[Updated])
                VALUES (1,@MaxMin,@MaxHour,@MaxDay,@MaxRcpDay,@MinDelay,@MaxDelay,
                        @MaxFail,@CDMin,@WarmDays,@WarmMax,@MaxIdent,GETUTCDATE(),GETUTCDATE());
            """;
        cmd.Parameters.Add(new SqlParameter("@MaxMin",     rules.MaxPerMinute));
        cmd.Parameters.Add(new SqlParameter("@MaxHour",    rules.MaxPerHour));
        cmd.Parameters.Add(new SqlParameter("@MaxDay",     rules.MaxPerDay));
        cmd.Parameters.Add(new SqlParameter("@MaxRcpDay",  rules.MaxPerRecipientPerDay));
        cmd.Parameters.Add(new SqlParameter("@MinDelay",   rules.MinDelaySeconds));
        cmd.Parameters.Add(new SqlParameter("@MaxDelay",   rules.MaxDelaySeconds));
        cmd.Parameters.Add(new SqlParameter("@MaxFail",    rules.MaxConsecutiveFailures));
        cmd.Parameters.Add(new SqlParameter("@CDMin",      rules.FailureCooldownMinutes));
        cmd.Parameters.Add(new SqlParameter("@WarmDays",   rules.WarmupDays));
        cmd.Parameters.Add(new SqlParameter("@WarmMax",    rules.WarmupMaxPerDay));
        cmd.Parameters.Add(new SqlParameter("@MaxIdent",   rules.MaxIdenticalMessagesPerDay));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

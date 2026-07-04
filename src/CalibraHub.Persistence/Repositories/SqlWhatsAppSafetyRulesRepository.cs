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
        _table = $"[{schema}].[WhatsAppSafetyRule]";
    }

    public async Task<WhatsAppSafetyRules?> GetAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[MaxPerMinute],[MaxPerHour],[MaxPerDay],[MaxPerRecipientPerDay],
                   [MinDelaySeconds],[MaxDelaySeconds],
                   [MaxConsecutiveFailures],[FailureCooldownMinutes],
                   [WarmupDays],[WarmupMaxPerDay],[MaxIdenticalMessagesPerDay],
                   [Created],[Updated]
              FROM {_table} WHERE [Id]=1;
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
            IF EXISTS (SELECT 1 FROM {_table} WHERE [Id]=1)
                UPDATE {_table} SET
                    [MaxPerMinute]=@MaxMin, [MaxPerHour]=@MaxHour, [MaxPerDay]=@MaxDay,
                    [MaxPerRecipientPerDay]=@MaxRcpDay,
                    [MinDelaySeconds]=@MinDelay, [MaxDelaySeconds]=@MaxDelay,
                    [MaxConsecutiveFailures]=@MaxFail, [FailureCooldownMinutes]=@CDMin,
                    [WarmupDays]=@WarmDays, [WarmupMaxPerDay]=@WarmMax,
                    [MaxIdenticalMessagesPerDay]=@MaxIdent,
                    [Updated]=GETUTCDATE()
                WHERE [Id]=1;
            ELSE
                INSERT INTO {_table}
                    ([Id],[MaxPerMinute],[MaxPerHour],[MaxPerDay],[MaxPerRecipientPerDay],
                     [MinDelaySeconds],[MaxDelaySeconds],
                     [MaxConsecutiveFailures],[FailureCooldownMinutes],
                     [WarmupDays],[WarmupMaxPerDay],[MaxIdenticalMessagesPerDay],
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

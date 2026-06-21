using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL impl — CodeRule CRUD + Condition listesi yönetimi + Counter atomic increment.
/// Per-company DB (SqlServerConnectionFactory tenant routing).
/// </summary>
public sealed class SqlCodeRuleRepository : ICodeRuleRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _ruleTable;
    private readonly string _condTable;
    private readonly string _counterTable;

    public SqlCodeRuleRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _ruleTable    = $"[{schema}].[CodeRule]";
        _condTable    = $"[{schema}].[CodeRuleCondition]";
        _counterTable = $"[{schema}].[CodeRuleCounter]";
    }

    public async Task<IReadOnlyList<CodeRule>> ListAsync(string entityType, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await LoadRulesAsync(conn, entityType, includeInactive: true, ct);
    }

    public async Task<IReadOnlyList<CodeRule>> GetActiveByEntityAsync(string entityType, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        return await LoadRulesAsync(conn, entityType, includeInactive: false, ct);
    }

    public async Task<CodeRule?> GetAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id],[EntityType],[Name],[Template],[Priority],[ResetPeriod],[IsActive],
                   [CreatedById],[Created],[UpdatedById],[Updated]
            FROM {_ruleTable} WHERE [Id]=@Id;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        CodeRule? rule = null;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct)) rule = MapRule(reader);
        }
        if (rule is null) return null;
        var condMap = await LoadConditionsAsync(conn, new[] { rule.Id }, ct);
        rule.Conditions = condMap.TryGetValue(rule.Id, out var conds) ? conds : Array.Empty<CodeRuleCondition>();
        return rule;
    }

    public async Task<int> SaveAsync(CodeRule rule, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int ruleId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                if (rule.Id > 0)
                {
                    cmd.CommandText = $"""
                        UPDATE {_ruleTable} SET
                          [EntityType]=@EntityType,[Name]=@Name,[Template]=@Template,
                          [Priority]=@Priority,[ResetPeriod]=@ResetPeriod,[IsActive]=@IsActive,
                          [UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
                        WHERE [Id]=@Id;
                        SELECT @Id;
                        """;
                    cmd.Parameters.Add(new SqlParameter("@Id", rule.Id));
                }
                else
                {
                    cmd.CommandText = $"""
                        INSERT INTO {_ruleTable}
                          ([EntityType],[Name],[Template],[Priority],[ResetPeriod],[IsActive],[CreatedById])
                        OUTPUT INSERTED.[Id]
                        VALUES (@EntityType,@Name,@Template,@Priority,@ResetPeriod,@IsActive,@CreatedById);
                        """;
                }
                cmd.Parameters.Add(new SqlParameter("@EntityType",  rule.EntityType));
                cmd.Parameters.Add(new SqlParameter("@Name",        rule.Name));
                cmd.Parameters.Add(new SqlParameter("@Template",    rule.Template));
                cmd.Parameters.Add(new SqlParameter("@Priority",    rule.Priority));
                cmd.Parameters.Add(new SqlParameter("@ResetPeriod", (int)rule.ResetPeriod));
                cmd.Parameters.Add(new SqlParameter("@IsActive",    rule.IsActive));
                cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)rule.CreatedById ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)rule.UpdatedById ?? DBNull.Value));
                ruleId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }

            // Conditions: tam değiştir (delete + reinsert) — küçük tablo, basit ve doğru
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = $"DELETE FROM {_condTable} WHERE [RuleId]=@Rid;";
                delCmd.Parameters.Add(new SqlParameter("@Rid", ruleId));
                await delCmd.ExecuteNonQueryAsync(ct);
            }
            foreach (var cond in rule.Conditions ?? Array.Empty<CodeRuleCondition>())
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $"""
                    INSERT INTO {_condTable} ([RuleId],[FieldType],[FieldName],[Operator],[Value])
                    VALUES (@Rid,@FieldType,@FieldName,@Operator,@Value);
                    """;
                insCmd.Parameters.Add(new SqlParameter("@Rid",       ruleId));
                insCmd.Parameters.Add(new SqlParameter("@FieldType", cond.FieldType));
                insCmd.Parameters.Add(new SqlParameter("@FieldName", cond.FieldName));
                insCmd.Parameters.Add(new SqlParameter("@Operator",  cond.Operator));
                insCmd.Parameters.Add(new SqlParameter("@Value",     (object?)cond.Value ?? DBNull.Value));
                await insCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return ruleId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_ruleTable} WHERE [Id]=@Id;"; // condition/counter CASCADE
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Atomic counter increment. Counter satırı yoksa (startValue - 1) ile yarat,
    /// sonra UPDATE OUTPUT ile +1 yap (single statement, race-safe).
    /// </summary>
    public async Task<long> IncrementCounterAsync(int ruleId, string resetKey, long startValue, CancellationToken ct)
    {
        resetKey ??= string.Empty;
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) Satır yoksa oluştur (idempotent, dup-key race ignore)
        await using (var seedCmd = conn.CreateCommand())
        {
            seedCmd.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM {_counterTable} WHERE [RuleId]=@Rid AND [ResetKey]=@Key)
                BEGIN
                    BEGIN TRY
                        INSERT INTO {_counterTable} ([RuleId],[ResetKey],[CurrentValue],[LastUpdated])
                        VALUES (@Rid,@Key,@Start,SYSUTCDATETIME());
                    END TRY
                    BEGIN CATCH
                        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;  -- duplicate key — başka thread eklemiş
                    END CATCH
                END;
                """;
            seedCmd.Parameters.Add(new SqlParameter("@Rid",   ruleId));
            seedCmd.Parameters.Add(new SqlParameter("@Key",   resetKey));
            seedCmd.Parameters.Add(new SqlParameter("@Start", startValue - 1));
            await seedCmd.ExecuteNonQueryAsync(ct);
        }

        // 2) UPDATE OUTPUT ile atomic +1
        await using var upd = conn.CreateCommand();
        upd.CommandText = $"""
            UPDATE {_counterTable}
            SET [CurrentValue] = [CurrentValue] + 1, [LastUpdated] = SYSUTCDATETIME()
            OUTPUT INSERTED.[CurrentValue]
            WHERE [RuleId]=@Rid AND [ResetKey]=@Key;
            """;
        upd.Parameters.Add(new SqlParameter("@Rid", ruleId));
        upd.Parameters.Add(new SqlParameter("@Key", resetKey));
        var result = await upd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<CodeRuleCounter>> GetCountersAsync(int ruleId, CancellationToken ct)
    {
        var list = new List<CodeRuleCounter>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[RuleId],[ResetKey],[CurrentValue],[LastUpdated]
            FROM {_counterTable} WHERE [RuleId]=@Rid ORDER BY [ResetKey] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Rid", ruleId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CodeRuleCounter
            {
                Id           = reader.GetInt32(0),
                RuleId       = reader.GetInt32(1),
                ResetKey     = reader.GetString(2),
                CurrentValue = reader.GetInt64(3),
                LastUpdated  = reader.GetDateTime(4),
            });
        }
        return list;
    }

    public async Task ResetCounterAsync(int ruleId, string resetKey, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_counterTable}
            SET [CurrentValue]=0, [LastUpdated]=SYSUTCDATETIME()
            WHERE [RuleId]=@Rid AND [ResetKey]=@Key;
            """;
        cmd.Parameters.Add(new SqlParameter("@Rid", ruleId));
        cmd.Parameters.Add(new SqlParameter("@Key", resetKey ?? string.Empty));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<CodeRule>> LoadRulesAsync(
        SqlConnection conn, string entityType, bool includeInactive, CancellationToken ct)
    {
        var rules = new List<CodeRule>();
        await using (var cmd = conn.CreateCommand())
        {
            var where = includeInactive
                ? "WHERE [EntityType]=@Et"
                : "WHERE [EntityType]=@Et AND [IsActive]=1";
            cmd.CommandText = $"""
                SELECT [Id],[EntityType],[Name],[Template],[Priority],[ResetPeriod],[IsActive],
                       [CreatedById],[Created],[UpdatedById],[Updated]
                FROM {_ruleTable} {where}
                ORDER BY [Priority] DESC, [Name];
                """;
            cmd.Parameters.Add(new SqlParameter("@Et", entityType));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rules.Add(MapRule(reader));
        }
        if (rules.Count == 0) return rules;

        var ids = rules.Select(r => r.Id).ToArray();
        var condMap = await LoadConditionsAsync(conn, ids, ct);
        foreach (var rule in rules)
        {
            rule.Conditions = condMap.TryGetValue(rule.Id, out var c) ? c : Array.Empty<CodeRuleCondition>();
        }
        return rules;
    }

    private async Task<Dictionary<int, List<CodeRuleCondition>>> LoadConditionsAsync(
        SqlConnection conn, int[] ruleIds, CancellationToken ct)
    {
        var map = new Dictionary<int, List<CodeRuleCondition>>();
        if (ruleIds.Length == 0) return map;
        await using var cmd = conn.CreateCommand();
        var inClause = string.Join(",", ruleIds.Select((_, i) => $"@R{i}"));
        cmd.CommandText = $"""
            SELECT [Id],[RuleId],[FieldType],[FieldName],[Operator],[Value]
            FROM {_condTable} WHERE [RuleId] IN ({inClause}) ORDER BY [Id];
            """;
        for (var i = 0; i < ruleIds.Length; i++)
            cmd.Parameters.Add(new SqlParameter($"@R{i}", ruleIds[i]));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var cond = new CodeRuleCondition
            {
                Id        = reader.GetInt32(0),
                RuleId    = reader.GetInt32(1),
                FieldType = reader.GetString(2),
                FieldName = reader.GetString(3),
                Operator  = reader.GetString(4),
                Value     = reader.IsDBNull(5) ? null : reader.GetString(5),
            };
            if (!map.TryGetValue(cond.RuleId, out var list))
            {
                list = new List<CodeRuleCondition>();
                map[cond.RuleId] = list;
            }
            list.Add(cond);
        }
        return map;
    }

    private static CodeRule MapRule(SqlDataReader r) => new()
    {
        Id           = r.GetInt32(0),
        EntityType   = r.GetString(1),
        Name         = r.GetString(2),
        Template     = r.GetString(3),
        Priority     = r.GetInt32(4),
        ResetPeriod  = (DocumentNumberResetPeriod)r.GetInt32(5),
        IsActive     = r.GetBoolean(6),
        CreatedById  = r.IsDBNull(7) ? null : r.GetInt32(7),
        Created      = r.GetDateTime(8),
        UpdatedById  = r.IsDBNull(9) ? null : r.GetInt32(9),
        Updated      = r.IsDBNull(10) ? null : r.GetDateTime(10),
    };
}

using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// 2026-06-12 — Satır görünürlük kuralları persistence. Kurallar PER-COMPANY DB'de durduğu için
/// <see cref="SqlServerConnectionFactory.OpenConnectionAsync"/> kullanılır (PermissionGrant'ın
/// aksine, o OpenSystemConnectionAsync kullanır). Yükleme metodları values + grants ile hydrate eder.
/// </summary>
public sealed class SqlDataVisibilityRuleRepository : IDataVisibilityRuleRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _ruleTable;
    private readonly string _valueTable;
    private readonly string _grantTable;

    public SqlDataVisibilityRuleRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _ruleTable  = $"[{s}].[DataVisibilityRule]";
        _valueTable = $"[{s}].[DataVisibilityRuleValue]";
        _grantTable = $"[{s}].[DataVisibilityGrant]";
    }

    private const string RuleCols =
        "[Id],[FormCode],[FieldKind],[FieldKey],[Operator],[WidgetId],[Name],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById]";

    public async Task<IReadOnlyList<DataVisibilityRule>> ListActiveByFormAsync(string formCode, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        var rules = new List<DataVisibilityRule>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT {RuleCols} FROM {_ruleTable} WHERE [FormCode]=@F AND [IsActive]=1 AND [CompanyId]=@CompanyId ORDER BY [Id];";
            cmd.Parameters.AddWithValue("@F", formCode);
            cmd.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) rules.Add(MapRule(r));
        }
        await HydrateAsync(conn, rules, ct);
        return rules;
    }

    public async Task<IReadOnlyList<DataVisibilityRule>> ListAllAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        var rules = new List<DataVisibilityRule>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = includeInactive
                ? $"SELECT {RuleCols} FROM {_ruleTable} WHERE [CompanyId]=@CompanyId ORDER BY [FormCode],[Id];"
                : $"SELECT {RuleCols} FROM {_ruleTable} WHERE [IsActive]=1 AND [CompanyId]=@CompanyId ORDER BY [FormCode],[Id];";
            cmd.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) rules.Add(MapRule(r));
        }
        await HydrateAsync(conn, rules, ct);
        return rules;
    }

    public async Task<DataVisibilityRule?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        DataVisibilityRule? rule = null;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT {RuleCols} FROM {_ruleTable} WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) rule = MapRule(r);
        }
        if (rule is null) return null;
        await HydrateAsync(conn, new[] { rule }, ct);
        return rule;
    }

    /// <summary>
    /// Verilen kuralların Values + Grants child'larını tek IN sorgusuyla yükler.
    /// tenant-ok: rules listesi buraya girmeden önce çağıran metodlarda (ListActiveByFormAsync/
    /// ListAllAsync/GetByIdAsync) [CompanyId] ile süzülmüş — RuleId IN (...) zaten o kapsamda.
    /// </summary>
    private async Task HydrateAsync(SqlConnection conn, IReadOnlyList<DataVisibilityRule> rules, CancellationToken ct)
    {
        if (rules.Count == 0) return;
        var byId = rules.ToDictionary(x => x.Id);
        var inClause = string.Join(",", rules.Select((_, i) => $"@r{i}"));

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT [Id],[RuleId],[ValueId],[ValueText] FROM {_valueTable} WHERE [RuleId] IN ({inClause});";
            for (int i = 0; i < rules.Count; i++) cmd.Parameters.AddWithValue($"@r{i}", rules[i].Id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var v = new DataVisibilityRuleValue
                {
                    Id        = r.GetInt32(0),
                    RuleId    = r.GetInt32(1),
                    ValueId   = r.IsDBNull(2) ? null : r.GetInt32(2),
                    ValueText = r.IsDBNull(3) ? null : r.GetString(3),
                };
                if (byId.TryGetValue(v.RuleId, out var rule)) rule.Values.Add(v);
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT [Id],[RuleId],[UserId],[DepartmentId] FROM {_grantTable} WHERE [RuleId] IN ({inClause});";
            for (int i = 0; i < rules.Count; i++) cmd.Parameters.AddWithValue($"@r{i}", rules[i].Id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var g = new DataVisibilityGrant
                {
                    Id           = r.GetInt32(0),
                    RuleId       = r.GetInt32(1),
                    UserId       = r.IsDBNull(2) ? null : r.GetInt32(2),
                    DepartmentId = r.IsDBNull(3) ? null : r.GetInt32(3),
                };
                if (byId.TryGetValue(g.RuleId, out var rule)) rule.Grants.Add(g);
            }
        }
    }

    public async Task<int> SaveAsync(DataVisibilityRule rule, CancellationToken ct)
    {
        rule.EnsureValid();
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int ruleId = rule.Id;
            if (ruleId > 0)
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $@"
                        UPDATE {_ruleTable} SET
                            [FormCode]=@FormCode,[FieldKind]=@FieldKind,[FieldKey]=@FieldKey,[Operator]=@Operator,
                            [WidgetId]=@WidgetId,[Name]=@Name,[IsActive]=@IsActive,
                            [Updated]=SYSUTCDATETIME(),[UpdatedById]=@UpdatedById
                        WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
                    cmd.Parameters.AddWithValue("@Id", ruleId);
                    BindRuleParams(cmd, rule);
                    cmd.Parameters.AddWithValue("@UpdatedById", (object?)rule.UpdatedById ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                // Child satırları REPLACE et (eski values + grants sil)
                await using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = $"""
                        DELETE FROM {_valueTable} WHERE [RuleId]=@R
                          AND EXISTS (SELECT 1 FROM {_ruleTable} p WHERE p.[Id]=@R AND p.[CompanyId]=@CompanyId);
                        DELETE FROM {_grantTable} WHERE [RuleId]=@R
                          AND EXISTS (SELECT 1 FROM {_ruleTable} p WHERE p.[Id]=@R AND p.[CompanyId]=@CompanyId);
                        """;
                    del.Parameters.AddWithValue("@R", ruleId);
                    del.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
                    await del.ExecuteNonQueryAsync(ct);
                }
            }
            else
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $@"
                    INSERT INTO {_ruleTable}
                        ([FormCode],[FieldKind],[FieldKey],[Operator],[WidgetId],[Name],[IsActive],[CreatedById],[CompanyId])
                    VALUES (@FormCode,@FieldKind,@FieldKey,@Operator,@WidgetId,@Name,@IsActive,@CreatedById,@CompanyId);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";
                BindRuleParams(cmd, rule);
                cmd.Parameters.AddWithValue("@CreatedById", (object?)rule.CreatedById ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
                ruleId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }

            foreach (var v in rule.Values)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_valueTable} ([RuleId],[ValueId],[ValueText],[CompanyId])
                    VALUES (@R,@VId,@VTxt,(SELECT p.[CompanyId] FROM {_ruleTable} p WHERE p.[Id]=@R));
                    """;
                ins.Parameters.AddWithValue("@R", ruleId);
                ins.Parameters.AddWithValue("@VId", (object?)v.ValueId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@VTxt", (object?)v.ValueText ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync(ct);
            }

            foreach (var g in rule.Grants)
            {
                g.EnsureValid();
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {_grantTable} ([RuleId],[UserId],[DepartmentId],[CompanyId])
                    VALUES (@R,@U,@D,(SELECT p.[CompanyId] FROM {_ruleTable} p WHERE p.[Id]=@R));
                    """;
                ins.Parameters.AddWithValue("@R", ruleId);
                ins.Parameters.AddWithValue("@U", (object?)g.UserId ?? DBNull.Value);
                ins.Parameters.AddWithValue("@D", (object?)g.DepartmentId ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return ruleId;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_ruleTable} WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetActiveAsync(int id, bool isActive, int? updatedById, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_ruleTable} SET [IsActive]=@A,[Updated]=SYSUTCDATETIME(),[UpdatedById]=@U WHERE [Id]=@Id AND [CompanyId]=@CompanyId;";
        cmd.Parameters.AddWithValue("@A", isActive);
        cmd.Parameters.AddWithValue("@U", (object?)updatedById ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", _factory.ResolveEffectiveCompanyId());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void BindRuleParams(SqlCommand cmd, DataVisibilityRule rule)
    {
        cmd.Parameters.AddWithValue("@FormCode", rule.FormCode);
        cmd.Parameters.AddWithValue("@FieldKind", (byte)rule.FieldKind);
        cmd.Parameters.AddWithValue("@FieldKey", rule.FieldKey);
        cmd.Parameters.AddWithValue("@Operator", rule.Operator);
        cmd.Parameters.AddWithValue("@WidgetId", (object?)rule.WidgetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Name", rule.Name);
        cmd.Parameters.AddWithValue("@IsActive", rule.IsActive);
    }

    private static DataVisibilityRule MapRule(SqlDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        FormCode    = r.GetString(1),
        FieldKind   = (DataVisibilityFieldKind)r.GetByte(2),
        FieldKey    = r.GetString(3),
        Operator    = r.GetString(4),
        WidgetId    = r.IsDBNull(5) ? null : r.GetInt32(5),
        Name        = r.GetString(6),
        IsActive    = r.GetBoolean(7),
        Created     = r.GetDateTime(8),
        Updated     = r.IsDBNull(9) ? null : r.GetDateTime(9),
        CreatedById = r.IsDBNull(10) ? null : r.GetInt32(10),
        UpdatedById = r.IsDBNull(11) ? null : r.GetInt32(11),
    };
}

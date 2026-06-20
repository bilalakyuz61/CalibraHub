using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlApprovalFlowRepository : IApprovalFlowRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _s;

    public SqlApprovalFlowRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _connectionFactory = factory;
        _s = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<IReadOnlyList<ApprovalFlowSummaryDto>> GetAllSummariesAsync(CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                f.[Id], f.[Name], f.[Description], f.[DocumentKind], f.[Priority], f.[IsActive],
                (SELECT COUNT(1) FROM [{_s}].[ApprovalFlowStep]  s WHERE s.[FlowId] = f.[Id] AND s.[IsActive] = 1) AS StepCount,
                (SELECT COUNT(1) FROM [{_s}].[ApprovalFlowRule]  r WHERE r.[FlowId] = f.[Id] AND r.[IsActive] = 1) AS RuleCount
            FROM [{_s}].[ApprovalFlow] f
            ORDER BY f.[Priority] DESC, f.[Name];
            """;
        var list = new List<ApprovalFlowSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ApprovalFlowSummaryDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetBoolean(5),
                reader.GetInt32(6),
                reader.GetInt32(7)));
        }
        return list;
    }

    public async Task<ApprovalFlowDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        return await ReadFlowAsync(con, id, ct);
    }

    public async Task<IReadOnlyList<ApprovalFlowDto>> GetByDocumentKindAsync(string documentKind, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        // 'Document' = "Tüm Belgeler" wildcard (yeni standart), 'All' = legacy wildcard.
        // İkisi de spesifik belge tipiyle eşleşir. Spesifik tip (örn. 'EInvoice') yalnız
        // kendi tipiyle eşleşir; başka spesifik tipe sızmaz.
        cmd.CommandText = $"""
            SELECT [Id] FROM [{_s}].[ApprovalFlow]
            WHERE [IsActive] = 1
              AND ([DocumentKind] = @Kind OR [DocumentKind] = N'Document' OR [DocumentKind] = N'All')
            ORDER BY [Priority] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Kind", documentKind));

        var ids = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetInt32(0));
        reader.Close();

        var result = new List<ApprovalFlowDto>();
        foreach (var flowId in ids)
        {
            var flow = await ReadFlowAsync(con, flowId, ct);
            if (flow is not null) result.Add(flow);
        }
        return result;
    }

    public async Task<int> SaveAsync(SaveApprovalFlowRequest req, int? byUserId, CancellationToken ct)
    {
        if (req is null) throw new ArgumentNullException(nameof(req));
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)con.BeginTransaction();
        try
        {
            int flowId;
            if (req.Id == 0)
            {
                await using var ins = con.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO [{_s}].[ApprovalFlow]
                        ([Name],[Description],[DocumentKind],[Priority],[IsActive],[ExtraColumnsView],[CreatedById],[Created],[UpdatedById],[Updated])
                    VALUES
                        (@Name,@Desc,@Kind,@Pri,@Active,@ExtraView,@ById,SYSUTCDATETIME(),@ById,SYSUTCDATETIME());
                    SELECT SCOPE_IDENTITY();
                    """;
                ins.Parameters.Add(new SqlParameter("@Name", (object?)req.Name ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Desc", (object?)req.Description ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Kind", (object?)req.DocumentKind ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@Pri", req.Priority));
                ins.Parameters.Add(new SqlParameter("@Active", req.IsActive));
                ins.Parameters.Add(new SqlParameter("@ExtraView", (object?)req.ExtraColumnsView ?? DBNull.Value));
                ins.Parameters.Add(new SqlParameter("@ById", (object?)byUserId ?? DBNull.Value));
                flowId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
            }
            else
            {
                flowId = req.Id;
                await using var upd = con.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE [{_s}].[ApprovalFlow]
                    SET [Name]=@Name,[Description]=@Desc,[DocumentKind]=@Kind,[Priority]=@Pri,
                        [IsActive]=@Active,[ExtraColumnsView]=@ExtraView,
                        [UpdatedById]=@ById,[Updated]=SYSUTCDATETIME()
                    WHERE [Id]=@Id;
                    """;
                upd.Parameters.Add(new SqlParameter("@Name", req.Name));
                upd.Parameters.Add(new SqlParameter("@Desc", (object?)req.Description ?? DBNull.Value));
                upd.Parameters.Add(new SqlParameter("@Kind", req.DocumentKind));
                upd.Parameters.Add(new SqlParameter("@Pri", req.Priority));
                upd.Parameters.Add(new SqlParameter("@Active", req.IsActive));
                upd.Parameters.Add(new SqlParameter("@ExtraView", (object?)req.ExtraColumnsView ?? DBNull.Value));
                upd.Parameters.Add(new SqlParameter("@ById", (object?)byUserId ?? DBNull.Value));
                upd.Parameters.Add(new SqlParameter("@Id", flowId));
                await upd.ExecuteNonQueryAsync(ct);

                // Mevcut alt kayıtları sil — aşağıda yeniden INSERT'lar
                await DeleteChildrenAsync(con, tx, flowId, ct);
            }

            // Rules
            foreach (var rule in req.Rules.Where(r => r.IsActive))
            {
                await using var rCmd = con.CreateCommand();
                rCmd.Transaction = tx;
                rCmd.CommandText = $"""
                    INSERT INTO [{_s}].[ApprovalFlowRule]
                        ([FlowId],[RuleType],[RuleValue],[IsActive],[CreatedById],[Created])
                    VALUES
                        (@Fid,@Type,@Val,1,@ById,SYSUTCDATETIME());
                    """;
                rCmd.Parameters.Add(new SqlParameter("@Fid", flowId));
                rCmd.Parameters.Add(new SqlParameter("@Type", rule.RuleType.ToString()));
                rCmd.Parameters.Add(new SqlParameter("@Val", (object?)rule.RuleValue ?? DBNull.Value));
                rCmd.Parameters.Add(new SqlParameter("@ById", (object?)byUserId ?? DBNull.Value));
                await rCmd.ExecuteNonQueryAsync(ct);
            }

            // Steps — client index → DB Id map (edge'ler bu map'ten çevrilir)
            var activeSteps = req.Steps.Where(s => s.IsActive).OrderBy(s => s.StepOrder).ToList();
            // Designer client-side step id (Save request'teki step.Id; yeni step için 0 olabilir)
            // → DB ID map. Aynı client id birden fazla 0 olursa, request'teki orijinal sırayı kullanırız.
            var clientIdToDbId = new Dictionary<int, int>();
            var newStepDbIds = new List<int>();
            foreach (var step in activeSteps)
            {
                await using var sCmd = con.CreateCommand();
                sCmd.Transaction = tx;
                sCmd.CommandText = $"""
                    INSERT INTO [{_s}].[ApprovalFlowStep]
                        ([FlowId],[StepOrder],[StepName],[ApproverType],[ApproverId],[ApproverLabel],[IsActive],[CreatedById],[Created],
                         [NodeType],[PosX],[PosY],[NodeData])
                    VALUES
                        (@Fid,@Ord,@SName,@AType,@Aid,@Alabel,1,@ById,SYSUTCDATETIME(),
                         @NodeType,@PosX,@PosY,@NodeData);
                    SELECT SCOPE_IDENTITY();
                    """;
                sCmd.Parameters.Add(new SqlParameter("@Fid", flowId));
                sCmd.Parameters.Add(new SqlParameter("@Ord", step.StepOrder));
                sCmd.Parameters.Add(new SqlParameter("@SName", step.StepName));
                sCmd.Parameters.Add(new SqlParameter("@AType", step.ApproverType.ToString()));
                sCmd.Parameters.Add(new SqlParameter("@Aid", (object?)step.ApproverId ?? DBNull.Value));
                sCmd.Parameters.Add(new SqlParameter("@Alabel", (object?)step.ApproverLabel ?? DBNull.Value));
                sCmd.Parameters.Add(new SqlParameter("@ById", (object?)byUserId ?? DBNull.Value));
                sCmd.Parameters.Add(new SqlParameter("@NodeType", (object?)(string.IsNullOrWhiteSpace(step.NodeType) ? "step" : step.NodeType)));
                sCmd.Parameters.Add(new SqlParameter("@PosX", step.PosX));
                sCmd.Parameters.Add(new SqlParameter("@PosY", step.PosY));
                sCmd.Parameters.Add(new SqlParameter("@NodeData", (object?)step.NodeData ?? DBNull.Value));
                var dbId = Convert.ToInt32(await sCmd.ExecuteScalarAsync(ct));
                newStepDbIds.Add(dbId);
                // Existing (non-zero) client id'leri birebir map'le; yeni (0) için skip — index ile çözülür
                if (step.Id > 0 && !clientIdToDbId.ContainsKey(step.Id))
                    clientIdToDbId[step.Id] = dbId;
            }

            // Variables — full replace
            if (req.Variables is { Count: > 0 })
            {
                int vsort = 0;
                foreach (var v in req.Variables)
                {
                    if (string.IsNullOrWhiteSpace(v.Name)) continue;
                    await using var vCmd = con.CreateCommand();
                    vCmd.Transaction = tx;
                    vCmd.CommandText = $"""
                        INSERT INTO [{_s}].[ApprovalFlowVariable]
                            ([FlowId],[Name],[TypeCode],[DefaultValue],[Description],[ValueSource],[SqlQuery],[SortOrder],[Created])
                        VALUES
                            (@Fid,@Name,@Type,@Default,@Desc,@ValueSource,@SqlQuery,@Sort,SYSUTCDATETIME());
                        """;
                    vCmd.Parameters.Add(new SqlParameter("@Fid",         flowId));
                    vCmd.Parameters.Add(new SqlParameter("@Name",        v.Name.Trim()));
                    vCmd.Parameters.Add(new SqlParameter("@Type",        string.IsNullOrWhiteSpace(v.TypeCode) ? "int" : v.TypeCode));
                    vCmd.Parameters.Add(new SqlParameter("@Default",     (object?)v.DefaultValue ?? DBNull.Value));
                    vCmd.Parameters.Add(new SqlParameter("@Desc",        (object?)v.Description ?? DBNull.Value));
                    vCmd.Parameters.Add(new SqlParameter("@ValueSource", string.IsNullOrWhiteSpace(v.ValueSource) ? "manual" : v.ValueSource));
                    vCmd.Parameters.Add(new SqlParameter("@SqlQuery",    (object?)v.SqlQuery ?? DBNull.Value));
                    vCmd.Parameters.Add(new SqlParameter("@Sort",        v.SortOrder > 0 ? v.SortOrder : ++vsort));
                    await vCmd.ExecuteNonQueryAsync(ct);
                }
            }

            // Edges — full replace (DeleteChildrenAsync zaten temizledi).
            // Client step id → DB id çözümleme:
            //  1) Pozitif client id varsa map'ten al,
            //  2) Yoksa designer index olarak yorumla (client tarafı 0..N-1 göndermiş olabilir)
            //     ve activeSteps sırasındaki dbId ile eşle.
            if (req.Edges is { Count: > 0 })
            {
                int sort = 0;
                foreach (var edge in req.Edges)
                {
                    if (!TryResolveStepId(edge.SourceStepClientId, clientIdToDbId, newStepDbIds, out var sourceDbId)) continue;
                    if (!TryResolveStepId(edge.TargetStepClientId, clientIdToDbId, newStepDbIds, out var targetDbId)) continue;

                    await using var eCmd = con.CreateCommand();
                    eCmd.Transaction = tx;
                    eCmd.CommandText = $"""
                        INSERT INTO [{_s}].[ApprovalFlowEdge]
                            ([FlowId],[SourceStepId],[TargetStepId],[Label],[EdgeKind],[Condition],[SortOrder],[SourceHandle],[TargetHandle],[Created])
                        VALUES
                            (@Fid,@Src,@Tgt,@Label,@Kind,@Cond,@Sort,@SrcH,@TgtH,SYSUTCDATETIME());
                        """;
                    eCmd.Parameters.Add(new SqlParameter("@Fid", flowId));
                    eCmd.Parameters.Add(new SqlParameter("@Src", sourceDbId));
                    eCmd.Parameters.Add(new SqlParameter("@Tgt", targetDbId));
                    eCmd.Parameters.Add(new SqlParameter("@Label", (object?)edge.Label ?? DBNull.Value));
                    eCmd.Parameters.Add(new SqlParameter("@Kind", string.IsNullOrWhiteSpace(edge.EdgeKind) ? "default" : edge.EdgeKind));
                    eCmd.Parameters.Add(new SqlParameter("@Cond", (object?)edge.Condition ?? DBNull.Value));
                    eCmd.Parameters.Add(new SqlParameter("@Sort", edge.SortOrder > 0 ? edge.SortOrder : ++sort));
                    eCmd.Parameters.Add(new SqlParameter("@SrcH", (object?)edge.SourceHandle ?? DBNull.Value));
                    eCmd.Parameters.Add(new SqlParameter("@TgtH", (object?)edge.TargetHandle ?? DBNull.Value));
                    await eCmd.ExecuteNonQueryAsync(ct);
                }
            }

            tx.Commit();
            return flowId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"DELETE FROM [{_s}].[ApprovalFlow] WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<ApprovalFlowDto?> ReadFlowAsync(SqlConnection con, int id, CancellationToken ct)
    {
        ApprovalFlowSummaryDto? header = null;
        string? extraColumnsView = null;
        await using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT [Id],[Name],[Description],[DocumentKind],[Priority],[IsActive],[ExtraColumnsView]
                FROM [{_s}].[ApprovalFlow] WHERE [Id] = @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", id));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                extraColumnsView = r.IsDBNull(6) ? null : r.GetString(6);
                header = new ApprovalFlowSummaryDto(
                    r.GetInt32(0), r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.GetString(3), r.GetInt32(4), r.GetBoolean(5), 0, 0);
            }
        }
        if (header is null) return null;

        var rules = await ReadRulesAsync(con, id, ct);
        var steps = await ReadStepsAsync(con, id, ct);
        var edges = await ReadEdgesAsync(con, id, ct);
        var variables = await ReadVariablesAsync(con, id, ct);

        return new ApprovalFlowDto(
            header.Id, header.Name, header.Description, header.DocumentKind,
            header.Priority, header.IsActive, rules, steps, edges, variables,
            ExtraColumnsView: extraColumnsView);
    }

    private async Task<IReadOnlyList<ApprovalFlowVariableDto>> ReadVariablesAsync(SqlConnection con, int flowId, CancellationToken ct)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[FlowId],[Name],[TypeCode],[DefaultValue],[Description],[SortOrder],
                   ISNULL([ValueSource], N'manual'), [SqlQuery]
            FROM [{_s}].[ApprovalFlowVariable] WHERE [FlowId] = @Fid
            ORDER BY [SortOrder], [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@Fid", flowId));
        var list = new List<ApprovalFlowVariableDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ApprovalFlowVariableDto(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                r.IsDBNull(3) ? "int" : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? 0 : r.GetInt32(6),
                r.IsDBNull(7) ? "manual" : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8)));
        }
        return list;
    }

    private async Task<IReadOnlyList<ApprovalFlowRuleDto>> ReadRulesAsync(SqlConnection con, int flowId, CancellationToken ct)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[FlowId],[RuleType],[RuleValue],[IsActive]
            FROM [{_s}].[ApprovalFlowRule] WHERE [FlowId] = @Fid ORDER BY [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@Fid", flowId));
        var list = new List<ApprovalFlowRuleDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            Enum.TryParse<ApprovalRuleType>(r.GetString(2), out var ruleType);
            list.Add(new ApprovalFlowRuleDto(
                r.GetInt32(0), r.GetInt32(1), ruleType,
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetBoolean(4)));
        }
        return list;
    }

    private async Task<IReadOnlyList<ApprovalFlowStepDto>> ReadStepsAsync(SqlConnection con, int flowId, CancellationToken ct)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[FlowId],[StepOrder],[StepName],[ApproverType],[ApproverId],[ApproverLabel],[IsActive],
                   [NodeType],[PosX],[PosY],[NodeData]
            FROM [{_s}].[ApprovalFlowStep] WHERE [FlowId] = @Fid ORDER BY [StepOrder];
            """;
        cmd.Parameters.Add(new SqlParameter("@Fid", flowId));
        var list = new List<ApprovalFlowStepDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            Enum.TryParse<ApproverType>(r.GetString(4), out var apType);
            list.Add(new ApprovalFlowStepDto(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3),
                apType,
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetBoolean(7),
                NodeType: r.IsDBNull(8) ? "step" : r.GetString(8),
                PosX: r.IsDBNull(9) ? 0 : r.GetInt32(9),
                PosY: r.IsDBNull(10) ? 0 : r.GetInt32(10),
                NodeData: r.IsDBNull(11) ? null : r.GetString(11)));
        }
        return list;
    }

    private async Task<IReadOnlyList<ApprovalFlowEdgeDto>> ReadEdgesAsync(SqlConnection con, int flowId, CancellationToken ct)
    {
        await using var cmd = con.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[FlowId],[SourceStepId],[TargetStepId],[Label],[EdgeKind],[Condition],[SortOrder],[SourceHandle],[TargetHandle]
            FROM [{_s}].[ApprovalFlowEdge] WHERE [FlowId] = @Fid
            ORDER BY [SortOrder], [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@Fid", flowId));
        var list = new List<ApprovalFlowEdgeDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ApprovalFlowEdgeDto(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? "default" : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? 0 : r.GetInt32(7),
                SourceHandle: r.IsDBNull(8) ? null : r.GetString(8),
                TargetHandle: r.IsDBNull(9) ? null : r.GetString(9)));
        }
        return list;
    }

    public async Task<IReadOnlyList<ApprovalFlowEdgeDto>> GetEdgesByFlowIdAsync(int flowId, CancellationToken ct)
    {
        await using var con = await _connectionFactory.OpenConnectionAsync(ct);
        return await ReadEdgesAsync(con, flowId, ct);
    }

    // Client-side step id → DB id çözümleme.
    // Pozitif id: map'ten al (mevcut step, save sırasında DB id atanmış olmalı).
    // Negatif veya 0..N-1 aralığındaki id: designer index olarak yorumla.
    private static bool TryResolveStepId(int clientId, Dictionary<int, int> map, List<int> orderedDbIds, out int dbId)
    {
        if (map.TryGetValue(clientId, out dbId)) return true;
        // Index fallback: client 0..N-1 (yeni step'ler için) veya negatif (designer geçici id)
        if (clientId >= 0 && clientId < orderedDbIds.Count)
        {
            dbId = orderedDbIds[clientId];
            return true;
        }
        dbId = 0;
        return false;
    }

    private async Task DeleteChildrenAsync(SqlConnection con, SqlTransaction tx, int flowId, CancellationToken ct)
    {
        // ApprovalFlowEdge önce silinmeli (FK → ApprovalFlowStep)
        foreach (var table in new[] { "ApprovalFlowEdge", "ApprovalFlowVariable", "ApprovalFlowRule", "ApprovalFlowStep" })
        {
            await using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM [{_s}].[{table}] WHERE [FlowId] = @Fid;";
            cmd.Parameters.Add(new SqlParameter("@Fid", flowId));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}

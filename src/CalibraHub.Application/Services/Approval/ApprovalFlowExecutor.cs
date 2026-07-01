using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CalibraHub.Application.Services.Approval;

/// <summary>
/// Faz 4 — Graph-aware Approval flow runtime executor.
///
/// MVP kapsamı:
///   - Step approve/reject sonrası: kaynak step'in çıkan edge'lerinden uygun
///     dalı seç (true/false/default), hedef node'u aktive et.
///   - Decision node: nodeData içindeki conditionRules'ı ApprovalDocumentContext'e
///     karşı evaluate et, true/false dalını izle, recursive olarak sonraki node'a in.
///   - Notification node: dispatcher.SendFromNodeAsync ile bildirim gönder, edge takip.
///   - Parallel split: tüm outgoing edge'leri sırayla aktive et (instance hâlâ Active).
///   - Parallel join (MVP): tüm incoming step'lerin Approved durumunu COUNT ile kontrol et.
///   - End node: Instance Completed olarak işaretle.
///
/// Sınırlamalar:
///   - Mevcut SqlApprovalInstanceRepository.CreateAsync linear logic'i kullanır
///     (tüm aktif step'leri pending olarak yaratır). Graph'a göre dinamik step
///     yaratma için repo seviyesinde "AddStepRecord(instanceId, step)" API'si
///     gerekir — bu aşamada eklenmemiştir. Bu sebeple parallel branches'in fiziksel
///     STEP RECORD katmanına yansıması TODO'dur; executor bilgi seviyesinde
///     decision/notification node'larını "pass-through" olarak çalıştırır ve
///     mevcut linear ilerlemeye bağlı kalır.
///
/// Tipik kullanım: ApprovalFlowService.ApproveStepAsync, repo.ApproveStepAsync'i
/// çağırdıktan sonra _executor.AfterStepActionAsync(...) çağırarak decision/notif
/// node'larını "atlamalı" şekilde işlemeyi tetikler.
/// </summary>
public interface IApprovalFlowExecutor
{
    /// <summary>
    /// Akış graph-tabanlı mı (edge'ler veya step dışı node'lar var mı).
    /// </summary>
    bool IsGraphBased(ApprovalFlowDto flow);

    /// <summary>
    /// Step approve/reject sonrası: sonraki yolda decision/notification node'ları
    /// varsa onları çalıştırır. Return: işlenen non-step node sayısı (info amaçlı).
    /// </summary>
    Task<int> AfterStepActionAsync(int instanceId, int currentStepOrder, bool isApproved, CancellationToken ct, string? capturedBaseUrl = null, string? choiceArmId = null);

    /// <summary>
    /// Instance start: Start node'dan ilk node'a kadar pass-through (decision/notif).
    /// CreateAsync linear step'leri kurduktan sonra çağrılır.
    /// capturedBaseUrl: HTTP context varken yakalanan dış erişim URL'i (Task.Run fire-and-forget için).
    /// </summary>
    Task<int> AfterStartAsync(int instanceId, CancellationToken ct, string? capturedBaseUrl = null);

    /// <summary>
    /// SLA timeout tetiklenmesi: currentStepOrder'dan çıkan "timeout" EdgeKind'lı
    /// edge'leri takip eder (Decision/Notification/End zinciri). Return &gt; 0 ise
    /// graph tabanlı timeout işlendi — SlaCheckerWorker legacy switch'i atlar.
    /// </summary>
    Task<int> AfterTimeoutAsync(int instanceId, int currentStepOrder, CancellationToken ct);

    /// <summary>
    /// Timer node bekleme süresi doldu: ilgili node'dan çıkan "out" edge'ini takip eder.
    /// SlaCheckerWorker'dan çağrılır. Return: işlenen node sayısı.
    /// </summary>
    Task<int> AfterTimerElapsedAsync(int instanceId, int timerNodeId, CancellationToken ct);
}

public sealed class ApprovalFlowExecutor : IApprovalFlowExecutor
{
    private const int MaxRecursionDepth = 50;

    private readonly IApprovalFlowRepository _flowRepo;
    private readonly IApprovalInstanceRepository _instRepo;
    private readonly IDecisionEvaluator _decisionEval;
    private readonly IApprovalEntityTypeRegistry _entityRegistry;
    private readonly IApprovalNotificationDispatcher _notifDispatcher;
    private readonly IIntegrationRunner _integrationRunner;
    private readonly IApprovalSqlQueryService _sqlQueryService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<ApprovalFlowExecutor> _logger;
    private readonly IApprovalNodeLogger? _nodeLogger;

    public ApprovalFlowExecutor(
        IApprovalFlowRepository flowRepo,
        IApprovalInstanceRepository instRepo,
        IDecisionEvaluator decisionEval,
        IApprovalEntityTypeRegistry entityRegistry,
        IApprovalNotificationDispatcher notifDispatcher,
        IIntegrationRunner integrationRunner,
        IApprovalSqlQueryService sqlQueryService,
        IHttpClientFactory httpClientFactory,
        ILogger<ApprovalFlowExecutor> logger,
        IApprovalNodeLogger? nodeLogger = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _flowRepo           = flowRepo;
        _instRepo           = instRepo;
        _decisionEval       = decisionEval;
        _entityRegistry     = entityRegistry;
        _notifDispatcher    = notifDispatcher;
        _integrationRunner  = integrationRunner;
        _sqlQueryService    = sqlQueryService;
        _httpClientFactory  = httpClientFactory;
        _scopeFactory       = scopeFactory;
        _logger             = logger;
        _nodeLogger         = nodeLogger;
    }

    private async Task TryLogAsync(int instanceId, int flowId, int? nodeId, string? nodeType, string? nodeName,
                                   string eventType, string? detail, int durationMs, CancellationToken ct)
    {
        if (_nodeLogger is null) return;
        try { await _nodeLogger.LogAsync(instanceId, flowId, nodeId, nodeType, nodeName, eventType, detail, durationMs, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Node log yazılamadı (instance={Iid}, event={Ev}).", instanceId, eventType); }
    }

    public bool IsGraphBased(ApprovalFlowDto flow)
    {
        if (flow.Edges is { Count: > 0 }) return true;
        return flow.Steps.Any(s => !string.IsNullOrEmpty(s.NodeType)
            && !IsStepKind(s.NodeType));
    }

    public async Task<int> AfterStepActionAsync(int instanceId, int currentStepOrder, bool isApproved, CancellationToken ct, string? capturedBaseUrl = null, string? choiceArmId = null)
    {
        var inst = await _instRepo.GetByIdAsync(instanceId, ct);
        if (inst is null) return 0;
        var flow = await _flowRepo.GetByIdAsync(inst.FlowId, ct);
        if (flow is null || !IsGraphBased(flow)) return 0;

        // currentStepOrder -> ApprovalFlowStep eşle (step veya vote nodeType)
        var currentNode = flow.Steps
            .OrderBy(s => s.StepOrder)
            .FirstOrDefault(s => s.StepOrder == currentStepOrder && (IsStepKind(s.NodeType) || IsVoteKind(s.NodeType)));
        if (currentNode is null) return 0;

        var ctx = await SafeBuildContextAsync(flow.DocumentKind, inst.DocumentId, ct);
        EnrichCtx(ctx, flow, inst);
        ctx.BaseUrl = capturedBaseUrl;

        var prevRecord = inst.StepRecords.FirstOrDefault(s => s.StepOrder == currentStepOrder);
        ctx.PreviousStepNote         = prevRecord?.Note;
        ctx.PreviousStepApproverName = prevRecord?.ApproverName;

        var processed = 0;

        foreach (var edge in SelectEdges(flow, currentNode.Id, isApproved, choiceArmId))
        {
            var target = flow.Steps.FirstOrDefault(s => s.Id == edge.TargetStepId);
            if (target is null) continue;
            processed += await TraverseAsync(flow, target, ctx, instanceId, depth: 0, ct);
        }

        return processed;
    }

    public async Task<int> AfterStartAsync(int instanceId, CancellationToken ct, string? capturedBaseUrl = null)
    {
        var inst = await _instRepo.GetByIdAsync(instanceId, ct);
        if (inst is null) return 0;
        var flow = await _flowRepo.GetByIdAsync(inst.FlowId, ct);
        if (flow is null || !IsGraphBased(flow)) return 0;

        var startNode = flow.Steps.FirstOrDefault(s =>
            string.Equals(s.NodeType, "start", StringComparison.OrdinalIgnoreCase));
        if (startNode is null) return 0;

        var ctx = await SafeBuildContextAsync(flow.DocumentKind, inst.DocumentId, ct);
        EnrichCtx(ctx, flow, inst);
        ctx.BaseUrl = capturedBaseUrl;
        // Fan-out koruma: birden fazla dal aynı step node'a ulaşabilir; her step yalnızca bir kez loglanır.
        var visitedStepIds = new HashSet<int>();
        var processed = 0;
        foreach (var edge in flow.Edges.Where(e => e.SourceStepId == startNode.Id).OrderBy(e => e.SortOrder))
        {
            var target = flow.Steps.FirstOrDefault(s => s.Id == edge.TargetStepId);
            if (target is null) continue;
            processed += await TraverseAsync(flow, target, ctx, instanceId, depth: 0, ct, visitedStepIds);
        }
        return processed;
    }

    public async Task<int> AfterTimeoutAsync(int instanceId, int currentStepOrder, CancellationToken ct)
    {
        var inst = await _instRepo.GetByIdAsync(instanceId, ct);
        if (inst is null) return 0;
        var flow = await _flowRepo.GetByIdAsync(inst.FlowId, ct);
        if (flow is null || !IsGraphBased(flow)) return 0;

        var currentNode = flow.Steps
            .OrderBy(s => s.StepOrder)
            .FirstOrDefault(s => s.StepOrder == currentStepOrder && IsStepKind(s.NodeType));
        if (currentNode is null) return 0;

        var timeoutEdges = flow.Edges
            .Where(e => e.SourceStepId == currentNode.Id &&
                   string.Equals((e.EdgeKind ?? "").Trim(), "timeout", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.SortOrder)
            .ToList();

        if (timeoutEdges.Count == 0) return 0;

        var ctx = await SafeBuildContextAsync(flow.DocumentKind, inst.DocumentId, ct);
        EnrichCtx(ctx, flow, inst);
        var processed = 0;
        foreach (var edge in timeoutEdges)
        {
            var target = flow.Steps.FirstOrDefault(s => s.Id == edge.TargetStepId);
            if (target is null) continue;
            processed += await TraverseAsync(flow, target, ctx, instanceId, depth: 0, ct);
        }
        _logger.LogInformation("Timeout graph traversal: instance={Iid}, step={Ord}, nodesProcessed={N}",
            instanceId, currentStepOrder, processed);
        return processed;
    }

    public async Task<int> AfterTimerElapsedAsync(int instanceId, int timerNodeId, CancellationToken ct)
    {
        var inst = await _instRepo.GetByIdAsync(instanceId, ct);
        if (inst is null) return 0;
        var flow = await _flowRepo.GetByIdAsync(inst.FlowId, ct);
        if (flow is null) return 0;

        var timerNode = flow.Steps.FirstOrDefault(s => s.Id == timerNodeId);
        if (timerNode is null) return 0;

        var ctx = await SafeBuildContextAsync(flow.DocumentKind, inst.DocumentId, ct);
        EnrichCtx(ctx, flow, inst);

        var outEdges = flow.Edges
            .Where(e => e.SourceStepId == timerNodeId)
            .OrderBy(e => e.SortOrder)
            .ToList();

        int processed = 0;
        foreach (var edge in outEdges)
        {
            var target = flow.Steps.FirstOrDefault(s => s.Id == edge.TargetStepId);
            if (target is null) continue;
            processed += await TraverseAsync(flow, target, ctx, instanceId, depth: 0, ct);
        }
        _logger.LogInformation("Timer node {NId} elapsed: instance={Iid}, nodesProcessed={N}", timerNodeId, instanceId, processed);
        return processed;
    }

    /// <summary>
    /// Recursive node traversal — Step node'a varınca durur (zaten DB'de step record'u var).
    /// Decision/Notification node'larını çalıştırır + edge'leri takip eder. End node'a
    /// varınca instance'i Completed olarak işaretler.
    /// </summary>
    private async Task<int> TraverseAsync(
        ApprovalFlowDto flow,
        ApprovalFlowStepDto node,
        ApprovalEntityContext ctx,
        int instanceId,
        int depth,
        CancellationToken ct,
        HashSet<int>? visitedStepIds = null)
    {
        if (depth >= MaxRecursionDepth)
        {
            _logger.LogWarning("Flow traversal max depth ({Depth}) aşıldı — durdu (flow={Fid}, node={Nid}).",
                MaxRecursionDepth, flow.Id, node.Id);
            return 0;
        }

        var kind = (node.NodeType ?? "step").Trim().ToLowerInvariant();
        switch (kind)
        {
            case "step":
            case "":
            {
                // Fan-out: aynı step birden fazla daldan ulaşılabilir — sadece ilk ziyarette işle.
                if (visitedStepIds == null || visitedStepIds.Add(node.Id))
                {
                    // Döngü geri dönüşü: SLA daha önce "graphTimeout" olarak işaretlenmiş olabilir.
                    // SLA'yı sıfırla ki SlaCheckerWorker bir sonraki gecikmede tekrar tetiklesin.
                    await _instRepo.ResetSlaForLoopAsync(instanceId, node.StepOrder, node.NodeData, ct);
                    _logger.LogInformation("Graph döngü: SLA sıfırlandı (instance={Iid}, step={Ord})",
                        instanceId, node.StepOrder);
                    await TryLogAsync(instanceId, flow.Id, node.Id, "step", node.StepName,
                        "StepActivated", "Onay bekleniyor (döngü)", 0, ct);
                }
                return 0;
            }

            case "decision":
            {
                var sw = Stopwatch.StartNew();
                var result = await _decisionEval.EvaluateAsync(node.NodeData, ctx, ct);
                sw.Stop();
                _logger.LogInformation("Decision node {Id} '{Name}' → {Result}", node.Id, node.StepName, result);
                var followEdges = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                var picked = followEdges.FirstOrDefault(e =>
                    string.Equals(e.EdgeKind, result ? "true" : "false", StringComparison.OrdinalIgnoreCase))
                    ?? followEdges.FirstOrDefault(e =>
                        string.Equals(e.EdgeKind, "default", StringComparison.OrdinalIgnoreCase))
                    ?? followEdges.FirstOrDefault();
                var edgeLabel = picked?.Label ?? picked?.EdgeKind ?? (result ? "true" : "false");
                await TryLogAsync(instanceId, flow.Id, node.Id, "decision", node.StepName,
                    "Decision", $"Sonuç: {(result ? "true" : "false")}, seçilen kenar: {edgeLabel}",
                    (int)sw.ElapsedMilliseconds, ct);
                if (picked is null) return 1;
                var next = flow.Steps.FirstOrDefault(s => s.Id == picked.TargetStepId);
                if (next is null) return 1;
                return 1 + await TraverseAsync(flow, next, ctx, instanceId, depth + 1, ct, visitedStepIds);
            }

            case "notification":
            {
                ctx.ApprovalInstanceId = instanceId;
                var sw = Stopwatch.StartNew();
                string notifDetail;
                try
                {
                    await _notifDispatcher.SendFromNodeAsync(node.NodeData, ctx, ct);
                    sw.Stop();
                    notifDetail = "Bildirim gönderildi";
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    notifDetail = $"Bildirim hatası: {ex.Message}";
                    _logger.LogWarning(ex, "Notification node {Id} gönderim hatası.", node.Id);
                }
                await TryLogAsync(instanceId, flow.Id, node.Id, "notification", node.StepName,
                    "Notification", notifDetail, (int)sw.ElapsedMilliseconds, ct);
                var followEdges = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                var picked = followEdges.FirstOrDefault();
                if (picked is null) return 1;
                var next = flow.Steps.FirstOrDefault(s => s.Id == picked.TargetStepId);
                if (next is null) return 1;
                return 1 + await TraverseAsync(flow, next, ctx, instanceId, depth + 1, ct, visitedStepIds);
            }

            case "integration":
            {
                // NodeData JSON: { integrationId, integrationName, recordIdSource:"entity"|"custom",
                //                  customRecordId, haltOnError }
                int? integrationId = null;
                string? recordIdSource = "entity";
                string? customRecordId = null;
                bool haltOnError = true;
                if (!string.IsNullOrWhiteSpace(node.NodeData))
                {
                    try
                    {
                        using var jd = JsonDocument.Parse(node.NodeData);
                        var r = jd.RootElement;
                        if (r.TryGetProperty("integrationId", out var iid) && iid.ValueKind == JsonValueKind.Number)
                            integrationId = iid.GetInt32();
                        if (r.TryGetProperty("recordIdSource", out var rs) && rs.ValueKind == JsonValueKind.String)
                            recordIdSource = rs.GetString();
                        if (r.TryGetProperty("customRecordId", out var cr) && cr.ValueKind == JsonValueKind.String)
                            customRecordId = cr.GetString();
                        if (r.TryGetProperty("haltOnError", out var ho) && (ho.ValueKind == JsonValueKind.True || ho.ValueKind == JsonValueKind.False))
                            haltOnError = ho.GetBoolean();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Integration node {Id} nodeData JSON parse hatası — düğüm atlandı.", node.Id);
                    }
                }

                if (integrationId is null or <= 0)
                {
                    _logger.LogWarning("Integration node {Id} '{Name}' — integrationId atanmamış, akış devam ediyor.", node.Id, node.StepName);
                    await TryLogAsync(instanceId, flow.Id, node.Id, "integration", node.StepName,
                        "Integration", "integrationId atanmamış — atlandı", 0, ct);
                }
                else
                {
                    var recordId = string.Equals(recordIdSource, "custom", StringComparison.OrdinalIgnoreCase)
                        ? customRecordId
                        : (string.IsNullOrWhiteSpace(ctx.EntityId) ? null : ctx.EntityId);

                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var result = await _integrationRunner.RunAsync(
                            integrationId.Value, recordId,
                            IntegrationTriggerType.Cascade,
                            triggeredBy: $"approval-flow:{flow.Id}:instance:{instanceId}",
                            ct);
                        sw.Stop();

                        if (!result.Success && haltOnError)
                        {
                            _logger.LogWarning(
                                "Integration node {Id} (integration={Iid}) başarısız — haltOnError, akış durdu. Hata: {Err}",
                                node.Id, integrationId, result.ErrorMessage);
                            await TryLogAsync(instanceId, flow.Id, node.Id, "integration", node.StepName,
                                "IntegrationHalt", $"Başarısız (haltOnError) — {result.ErrorMessage}", (int)sw.ElapsedMilliseconds, ct);
                            return 1; // sonraki node'a geçme
                        }

                        _logger.LogInformation(
                            "Integration node {Id} (integration={Iid}) tetiklendi — Success={Ok}, RunId={Rid}",
                            node.Id, integrationId, result.Success, result.RunId);
                        await TryLogAsync(instanceId, flow.Id, node.Id, "integration", node.StepName,
                            "Integration", $"Başarılı — RunId: {result.RunId}", (int)sw.ElapsedMilliseconds, ct);
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        _logger.LogError(ex, "Integration node {Id} run exception — haltOnError={Halt}", node.Id, haltOnError);
                        await TryLogAsync(instanceId, flow.Id, node.Id, "integration", node.StepName,
                            "IntegrationFail", $"Hata: {ex.Message}", (int)sw.ElapsedMilliseconds, ct);
                        if (haltOnError) return 1;
                    }
                }

                var followEdgesI = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                var pickedI = followEdgesI.FirstOrDefault();
                if (pickedI is null) return 1;
                var nextI = flow.Steps.FirstOrDefault(s => s.Id == pickedI.TargetStepId);
                if (nextI is null) return 1;
                return 1 + await TraverseAsync(flow, nextI, ctx, instanceId, depth + 1, ct, visitedStepIds);
            }

            case "setvariable":
            {
                string? variableName = null;
                string? expression   = null;
                string? valueType    = "expression";
                string? sqlText      = null;
                if (!string.IsNullOrWhiteSpace(node.NodeData))
                {
                    try
                    {
                        using var jd = JsonDocument.Parse(node.NodeData);
                        var r = jd.RootElement;
                        if (r.TryGetProperty("variableName", out var vn) && vn.ValueKind == JsonValueKind.String)
                            variableName = vn.GetString();
                        if (r.TryGetProperty("expression", out var ex) && ex.ValueKind == JsonValueKind.String)
                            expression = ex.GetString();
                        if (r.TryGetProperty("valueType", out var vt) && vt.ValueKind == JsonValueKind.String)
                            valueType = vt.GetString() ?? "expression";
                        if (r.TryGetProperty("sqlText", out var st) && st.ValueKind == JsonValueKind.String)
                            sqlText = st.GetString();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SetVariable node {Id} nodeData JSON parse hatası — atlandı.", node.Id);
                    }
                }

                if (!string.IsNullOrWhiteSpace(variableName))
                {
                    // Değişken tanımından valueSource + sqlQuery'yi al (öncelikli kaynak)
                    var varDef = flow.Variables.FirstOrDefault(v =>
                        string.Equals(v.Name, variableName, StringComparison.OrdinalIgnoreCase));
                    var resolvedSource = varDef?.ValueSource ?? valueType ?? "manual";
                    var resolvedSql    = varDef?.SqlQuery ?? sqlText;

                    object? evaluated = null;
                    if (string.Equals(resolvedSource, "sql", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(resolvedSql))
                    {
                        var sqlParams = MergeSqlParams(ctx);
                        var sqlResult = await _sqlQueryService.ExecuteAsync(resolvedSql, sqlParams, ct);
                        if (sqlResult.Ok)
                        {
                            evaluated = sqlResult.Value;
                            ctx.FlowVariables[variableName] = evaluated;
                            _logger.LogInformation("SetVariable(SQL) '{Var}' = '{Val}' (node={Id})", variableName, evaluated, node.Id);
                            await TryLogAsync(instanceId, flow.Id, node.Id, "setvariable", node.StepName,
                                "SetVariable", $"{variableName} = {evaluated} (SQL)", 0, ct);
                        }
                        else
                        {
                            _logger.LogWarning("SetVariable(SQL) node {Id} SQL hatası: {Err}", node.Id, sqlResult.Error);
                            await TryLogAsync(instanceId, flow.Id, node.Id, "setvariable", node.StepName,
                                "SetVariable", $"SQL hata: {sqlResult.Error}", 0, ct);
                        }
                    }
                    else if (expression is not null)
                    {
                        evaluated = EvaluateExpression(expression.Trim(), ctx);
                        ctx.FlowVariables[variableName] = evaluated;
                        _logger.LogInformation("SetVariable '{Var}' = '{Val}' (node={Id})", variableName, evaluated, node.Id);
                        await TryLogAsync(instanceId, flow.Id, node.Id, "setvariable", node.StepName,
                            "SetVariable", $"{variableName} = {evaluated}", 0, ct);
                    }
                    else
                    {
                        _logger.LogWarning("SetVariable node {Id} — expression/sqlText boş, atlandı.", node.Id);
                        await TryLogAsync(instanceId, flow.Id, node.Id, "setvariable", node.StepName,
                            "SetVariable", "İfade/SQL boş — atlandı", 0, ct);
                    }
                }
                else
                {
                    _logger.LogWarning("SetVariable node {Id} — variableName boş, atlandı.", node.Id);
                    await TryLogAsync(instanceId, flow.Id, node.Id, "setvariable", node.StepName,
                        "SetVariable", "Değişken adı boş — atlandı", 0, ct);
                }

                var followEdgesSV = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                var pickedSV = followEdgesSV.FirstOrDefault();
                if (pickedSV is null) return 1;
                var nextSV = flow.Steps.FirstOrDefault(s => s.Id == pickedSV.TargetStepId);
                if (nextSV is null) return 1;
                return 1 + await TraverseAsync(flow, nextSV, ctx, instanceId, depth + 1, ct, visitedStepIds);
            }

            case "parallel":
            {
                // Split: tüm outgoing edge'leri sırayla traverse et. Join logic'i
                // join node'a varan tek-uygulamalı recursion ile MVP'de sağlanır.
                var outEdges = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                await TryLogAsync(instanceId, flow.Id, node.Id, "parallel", node.StepName,
                    "Parallel", $"{outEdges.Count} paralel dal başlatıldı", 0, ct);
                int sub = 1;
                foreach (var oe in outEdges)
                {
                    var t = flow.Steps.FirstOrDefault(s => s.Id == oe.TargetStepId);
                    if (t is null) continue;
                    sub += await TraverseAsync(flow, t, ctx, instanceId, depth + 1, ct, visitedStepIds);
                }
                return sub;
            }

            case "end":
            {
                _logger.LogInformation("Flow end node ulaşıldı (instance={Iid}).", instanceId);
                await TryLogAsync(instanceId, flow.Id, node.Id, "end", node.StepName,
                    "FlowEnd", "Akış tamamlandı", 0, ct);
                // Timeout path'te buraya gelindi → instance hâlâ Pending; tamamla.
                // Normal approve/reject path'te ApproveStepAsync zaten Approved yaptı
                // → idempotent WHERE Pending koruması sayesinde no-op olur.
                await _instRepo.ForceCompleteAsync(instanceId, ct);
                return 1;
            }

            case "timer":
            {
                // NodeData: { waitValue, waitUnit: 'minutes'|'hours'|'days'|'businessDays' }
                int waitValue = 1;
                string waitUnit = "hours";
                if (!string.IsNullOrWhiteSpace(node.NodeData))
                {
                    try
                    {
                        using var jd = JsonDocument.Parse(node.NodeData);
                        var r = jd.RootElement;
                        if (r.TryGetProperty("waitValue", out var wv) && wv.ValueKind == JsonValueKind.Number)
                            waitValue = Math.Max(1, wv.GetInt32());
                        if (r.TryGetProperty("waitUnit", out var wu) && wu.ValueKind == JsonValueKind.String)
                            waitUnit = wu.GetString() ?? "hours";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Timer node {Id} nodeData parse hatası.", node.Id);
                    }
                }

                var fireAt = CalculateTimerFireAt(waitValue, waitUnit);
                await _instRepo.CreateTimerNodeRecordAsync(instanceId, node.Id, node.StepOrder, node.StepName ?? "Bekleme", fireAt, ct);
                await TryLogAsync(instanceId, flow.Id, node.Id, "timer", node.StepName,
                    "TimerScheduled", $"Zamanlayıcı kuruldu: {waitValue} {waitUnit}, tetikleme: {fireAt:dd.MM.yyyy HH:mm} UTC", 0, ct);
                _logger.LogInformation("Timer node {Id} scheduled for {FireAt} (instance={Iid})", node.Id, fireAt, instanceId);
                // Traversal durur — SlaCheckerWorker süre dolunca AfterTimerElapsedAsync çağırır.
                return 1;
            }

            case "vote":
            {
                // Oylama node: oylayıcı listesinden step record'ları yarat, consensus'u bekle.
                string voteConsensusType = "majority";
                var voters = new List<(string Id, string Name)>();
                if (!string.IsNullOrWhiteSpace(node.NodeData))
                {
                    try
                    {
                        using var jd = JsonDocument.Parse(node.NodeData);
                        var r = jd.RootElement;
                        if (r.TryGetProperty("votingType", out var vt) && vt.ValueKind == JsonValueKind.String)
                            voteConsensusType = vt.GetString() ?? "majority";
                        if (r.TryGetProperty("approverIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                        {
                            var labelsOk = r.TryGetProperty("approverLabels", out var lblEl) && lblEl.ValueKind == JsonValueKind.Array;
                            var idArr  = ids.EnumerateArray().ToArray();
                            var lblArr = labelsOk ? lblEl.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                            for (int i = 0; i < idArr.Length; i++)
                            {
                                var vid   = idArr[i].GetString() ?? "";
                                var vname = (i < lblArr.Length ? lblArr[i].GetString() : null) ?? vid;
                                if (!string.IsNullOrWhiteSpace(vid))
                                    voters.Add((vid, vname));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Vote node {Id} nodeData parse hatası.", node.Id);
                    }
                }

                if (voters.Count == 0)
                {
                    _logger.LogWarning("Vote node {Id} '{Name}' — oylayıcı tanımlanmamış, akış ilk edge'e devam etti.", node.Id, node.StepName);
                    await TryLogAsync(instanceId, flow.Id, node.Id, "vote", node.StepName,
                        "VoteNode", "Oylayıcı tanımlanmamış — atlandı", 0, ct);
                    var fbEdge = flow.Edges.Where(e => e.SourceStepId == node.Id).OrderBy(e => e.SortOrder).FirstOrDefault();
                    if (fbEdge is null) return 1;
                    var fbNext = flow.Steps.FirstOrDefault(s => s.Id == fbEdge.TargetStepId);
                    if (fbNext is null) return 1;
                    return 1 + await TraverseAsync(flow, fbNext, ctx, instanceId, depth + 1, ct, visitedStepIds);
                }

                await _instRepo.CreateVoteStepRecordsAsync(
                    instanceId, node.StepOrder, node.StepName ?? "Oylama",
                    voters.AsReadOnly(), node.NodeData, ct);
                await TryLogAsync(instanceId, flow.Id, node.Id, "vote", node.StepName,
                    "VoteStarted", $"Oylama başlatıldı: {voters.Count} oy kullanıcı, tür: {voteConsensusType}", 0, ct);
                _logger.LogInformation("Vote node {Id} — {N} oylayıcı eklendi (consensus={Type}, instance={Iid})",
                    node.Id, voters.Count, voteConsensusType, instanceId);
                return 1; // Traversal durur — oylar bekleniyor
            }

            case "subprocess":
            {
                // Alt akış: IApprovalFlowService.StartAsync ile bağımsız instance başlatır (fire-and-forget).
                // Üst akış hemen devam eder — alt akışın tamamlanması beklenmez.
                int subFlowId = 0;
                if (!string.IsNullOrWhiteSpace(node.NodeData))
                {
                    try
                    {
                        using var jd = JsonDocument.Parse(node.NodeData);
                        var r = jd.RootElement;
                        if (r.TryGetProperty("subFlowId", out var sfid))
                        {
                            if (sfid.ValueKind == JsonValueKind.Number)
                                subFlowId = sfid.GetInt32();
                            else if (sfid.ValueKind == JsonValueKind.String)
                                int.TryParse(sfid.GetString(), out subFlowId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SubProcess node {Id} nodeData parse hatası.", node.Id);
                    }
                }

                if (subFlowId > 0 && _scopeFactory is not null)
                {
                    var capturedSubFlowId  = subFlowId;
                    var capturedDocId      = int.TryParse(ctx.EntityId, out var eid) ? (int?)eid : null;
                    var capturedEntityKind = flow.DocumentKind ?? "Document";
                    var capturedBy         = ctx.RequesterName ?? "SubProcess";
                    var capturedBaseUrl    = ctx.BaseUrl;
                    var capturedParentId   = instanceId;
                    var factory = _scopeFactory;
                    var log     = _logger;
                    _ = Task.Run(async () =>
                    {
                        await using var scope = factory.CreateAsyncScope();
                        var svc = scope.ServiceProvider.GetRequiredService<IApprovalFlowService>();
                        try
                        {
                            await svc.StartAsync(new StartApprovalRequest(
                                DocumentId:      capturedDocId,
                                FlowId:          capturedSubFlowId,
                                StartedBy:       capturedBy,
                                StartedByUserId: null,
                                EntityKind:      capturedEntityKind), CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            log?.LogWarning(ex, "SubProcess start hatası (parentInstance={Pid}, subFlow={Sfid}).",
                                capturedParentId, capturedSubFlowId);
                        }
                    });
                    await TryLogAsync(instanceId, flow.Id, node.Id, "subprocess", node.StepName,
                        "SubProcessStarted", $"Alt akış başlatıldı: FlowId={subFlowId} (fire-and-forget)", 0, ct);
                    _logger.LogInformation("SubProcess node {Id} — alt akış FlowId={Sfid} başlatıldı (instance={Iid})",
                        node.Id, subFlowId, instanceId);
                }
                else
                {
                    var reason = subFlowId <= 0 ? "subFlowId tanımlanmamış" : "scopeFactory null (DI eksik)";
                    await TryLogAsync(instanceId, flow.Id, node.Id, "subprocess", node.StepName,
                        "SubProcess", $"{reason} — atlandı", 0, ct);
                    _logger.LogWarning("SubProcess node {Id} — {Reason} (instance={Iid})", node.Id, reason, instanceId);
                }

                // Üst akış hemen devam et
                var spEdges  = flow.Edges.Where(e => e.SourceStepId == node.Id).OrderBy(e => e.SortOrder).ToList();
                var pickedSP = spEdges.FirstOrDefault();
                if (pickedSP is null) return 1;
                var nextSP = flow.Steps.FirstOrDefault(s => s.Id == pickedSP.TargetStepId);
                if (nextSP is null) return 1;
                return 1 + await TraverseAsync(flow, nextSP, ctx, instanceId, depth + 1, ct, visitedStepIds);
            }

            case "webhook":
            {
                // NodeData: { url, method, headersJson, bodyTemplate, successStatusCodes, timeoutSeconds }
                string? webhookUrl   = null;
                string  httpMethod   = "POST";
                string? headersJson  = null;
                string? bodyTemplate = null;
                string  successCodes = "200,201,204";
                int     timeoutSec   = 30;
                if (!string.IsNullOrWhiteSpace(node.NodeData))
                {
                    try
                    {
                        using var jd = JsonDocument.Parse(node.NodeData);
                        var r = jd.RootElement;
                        if (r.TryGetProperty("url",                out var u)   && u.ValueKind  == JsonValueKind.String) webhookUrl   = u.GetString();
                        if (r.TryGetProperty("method",             out var m)   && m.ValueKind  == JsonValueKind.String) httpMethod   = (m.GetString() ?? "POST").ToUpperInvariant();
                        if (r.TryGetProperty("headersJson",        out var h)   && h.ValueKind  == JsonValueKind.String) headersJson  = h.GetString();
                        if (r.TryGetProperty("bodyTemplate",       out var b)   && b.ValueKind  == JsonValueKind.String) bodyTemplate = b.GetString();
                        if (r.TryGetProperty("successStatusCodes", out var sc)  && sc.ValueKind == JsonValueKind.String) successCodes = sc.GetString() ?? "200,201,204";
                        if (r.TryGetProperty("timeoutSeconds",     out var ts)  && ts.ValueKind == JsonValueKind.Number) timeoutSec   = ts.GetInt32();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Webhook node {Id} nodeData parse hatası.", node.Id);
                    }
                }

                bool webhookOk = false;
                if (string.IsNullOrWhiteSpace(webhookUrl))
                {
                    _logger.LogWarning("Webhook node {Id} — URL tanımlanmamış, hata kolu seçilecek.", node.Id);
                    await TryLogAsync(instanceId, flow.Id, node.Id, "webhook", node.StepName, "WebhookSkip", "URL tanımlanmamış", 0, ct);
                }
                else
                {
                    var successCodeSet = (successCodes).Split(',')
                        .Select(s => int.TryParse(s.Trim(), out var c) ? c : 0)
                        .Where(c => c > 0).ToHashSet();

                    var body = ApplyWebhookTokens(bodyTemplate ?? "", ctx);
                    var sw   = Stopwatch.StartNew();
                    try
                    {
                        var client  = _httpClientFactory.CreateClient("approval-webhook");
                        using var req = new HttpRequestMessage(new HttpMethod(httpMethod), webhookUrl);
                        if (!string.IsNullOrWhiteSpace(headersJson))
                        {
                            try
                            {
                                using var hDoc = JsonDocument.Parse(headersJson);
                                foreach (var prop in hDoc.RootElement.EnumerateObject())
                                    req.Headers.TryAddWithoutValidation(prop.Name, prop.Value.GetString() ?? "");
                            }
                            catch { /* bozuk JSON — geç */ }
                        }
                        if (!string.IsNullOrEmpty(body) && !string.Equals(httpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                        using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSec)));
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                        var resp = await client.SendAsync(req, linked.Token);
                        sw.Stop();
                        webhookOk = successCodeSet.Contains((int)resp.StatusCode);
                        _logger.LogInformation("Webhook node {Id} → HTTP {Status} success={Ok}", node.Id, (int)resp.StatusCode, webhookOk);
                        await TryLogAsync(instanceId, flow.Id, node.Id, "webhook", node.StepName,
                            webhookOk ? "WebhookOk" : "WebhookFail",
                            $"HTTP {(int)resp.StatusCode} — {(webhookOk ? "başarılı" : "başarısız")}", (int)sw.ElapsedMilliseconds, ct);
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        _logger.LogError(ex, "Webhook node {Id} HTTP çağrı hatası.", node.Id);
                        await TryLogAsync(instanceId, flow.Id, node.Id, "webhook", node.StepName,
                            "WebhookError", $"İstek hatası: {ex.Message}", (int)sw.ElapsedMilliseconds, ct);
                    }
                }

                var wEdges = flow.Edges.Where(e => e.SourceStepId == node.Id).OrderBy(e => e.SortOrder).ToList();
                var pickedW = wEdges.FirstOrDefault(e =>
                    string.Equals(e.SourceHandle, webhookOk ? "success" : "error", StringComparison.OrdinalIgnoreCase))
                    ?? wEdges.FirstOrDefault(e =>
                        string.Equals(e.EdgeKind, webhookOk ? "true" : "false", StringComparison.OrdinalIgnoreCase))
                    ?? wEdges.FirstOrDefault();
                if (pickedW is null) return 1;
                var nextW = flow.Steps.FirstOrDefault(s => s.Id == pickedW.TargetStepId);
                if (nextW is null) return 1;
                return 1 + await TraverseAsync(flow, nextW, ctx, instanceId, depth + 1, ct, visitedStepIds);
            }

            default:
                _logger.LogWarning("Bilinmeyen nodeType='{Type}' (id={Id}) — atlandı.", kind, node.Id);
                return 0;
        }
    }

    private static DateTime CalculateTimerFireAt(int value, string unit)
    {
        var now = DateTime.UtcNow;
        return unit switch
        {
            "minutes"     => now.AddMinutes(value),
            "days"        => now.AddDays(value),
            "businessDays" => AddBusinessDays(now, value),
            _             => now.AddHours(value),  // "hours" default
        };
    }

    private static DateTime AddBusinessDays(DateTime start, int days)
    {
        var result = start;
        var added  = 0;
        while (added < days)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek != DayOfWeek.Saturday && result.DayOfWeek != DayOfWeek.Sunday)
                added++;
        }
        return result;
    }

    private static string ApplyWebhookTokens(string template, ApprovalEntityContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return template
            .Replace("{entityId}",         ctx.EntityId)
            .Replace("{entityTypeCode}",   ctx.EntityTypeCode)
            .Replace("{requesterName}",    ctx.RequesterName ?? "")
            .Replace("{flowName}",         ctx.FlowName ?? "")
            .Replace("{approvalInstanceId}", ctx.ApprovalInstanceId?.ToString() ?? "");
    }

    /// <summary>
    /// Step node sonrası izlenecek edge'leri seç.
    /// choiceArmId varsa: yalnızca SourceHandle == choiceArmId olan edge izlenir.
    /// choiceArmId yoksa: true/default → approve, false → reject standart mantığı.
    /// </summary>
    private static IEnumerable<ApprovalFlowEdgeDto> SelectEdges(ApprovalFlowDto flow, int sourceStepId, bool isApproved, string? choiceArmId = null)
    {
        var all = flow.Edges
            .Where(e => e.SourceStepId == sourceStepId)
            .OrderBy(e => e.SortOrder)
            .ToList();
        if (all.Count == 0) yield break;

        // Çoklu seçim kolu: belirli bir SourceHandle ile onaylama
        if (!string.IsNullOrWhiteSpace(choiceArmId))
        {
            foreach (var e in all.Where(e => e.SourceHandle == choiceArmId))
                yield return e;
            yield break;
        }

        // Standart edge kind filtreleme:
        //   true / default / approve / info  → onay akışında tetiklenir
        //   false / reject                   → red akışında tetiklenir
        //   timeout                          → SLA timeout iş kuralı tarafından ayrı tetiklenir
        //   error                            → şimdilik runtime'da pas (gelecek: integration hata akışı)
        foreach (var e in all)
        {
            var kind = (e.EdgeKind ?? "default").Trim().ToLowerInvariant();
            if (isApproved && (kind is "true" or "default" or "approve" or "info"))
                yield return e;
            else if (!isApproved && (kind is "false" or "reject"))
                yield return e;
        }
    }

    private static bool IsStepKind(string? nodeType)
        => string.IsNullOrWhiteSpace(nodeType)
        || string.Equals(nodeType, "step", StringComparison.OrdinalIgnoreCase);

    private static bool IsVoteKind(string? nodeType)
        => string.Equals(nodeType, "vote", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SetVariable expression değerlendirme.
    /// Desteklenen formlar: 42 | true | false | metin | varName | {var:varName}
    ///                      varName + 1 | {var:x} - 5
    /// </summary>
    private static object? EvaluateExpression(string expr, ApprovalEntityContext ctx)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;

        if (string.Equals(expr, "true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(expr, "false", StringComparison.OrdinalIgnoreCase)) return false;

        if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var numLit))
            return numLit;

        // Aritmetik: sol taraf +/- sayısal literal
        var plusIdx  = expr.LastIndexOf('+');
        var minusIdx = expr.LastIndexOf('-');
        var opIdx    = -1;
        var opChar   = ' ';
        if (plusIdx > 0 && (minusIdx < 0 || plusIdx > minusIdx)) { opIdx = plusIdx; opChar = '+'; }
        else if (minusIdx > 0)                                    { opIdx = minusIdx; opChar = '-'; }

        if (opIdx > 0)
        {
            var leftPart  = expr[..opIdx].Trim();
            var rightPart = expr[(opIdx + 1)..].Trim();
            if (decimal.TryParse(rightPart, NumberStyles.Any, CultureInfo.InvariantCulture, out var rhs))
            {
                var leftVal = LookupFlowValue(leftPart, ctx);
                if (leftVal is not null &&
                    decimal.TryParse(Convert.ToString(leftVal, CultureInfo.InvariantCulture),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out var lhsNum))
                {
                    return opChar == '+' ? lhsNum + rhs : lhsNum - rhs;
                }
            }
        }

        // Saf değişken referans veya string literal
        return LookupFlowValue(expr, ctx) ?? (object?)expr;
    }

    /// <summary>FlowVariables → HeaderValues sırasıyla arar; {var:name} sarmalayıcısını soyar.</summary>
    private static object? LookupFlowValue(string name, ApprovalEntityContext ctx)
    {
        var key = name;
        if (name.StartsWith("{var:", StringComparison.OrdinalIgnoreCase) && name.EndsWith("}"))
            key = name[5..^1].Trim();

        if (ctx.FlowVariables.TryGetValue(key, out var flowVal)) return flowVal;
        if (ctx.HeaderValues.TryGetValue(key, out var headerVal)) return headerVal;
        return null;
    }

    /// <summary>
    /// Flow'daki değişken tanımlarının DefaultValue'larını ctx.FlowVariables'a yükler.
    /// SetVariable node'u çalışmadan önce bile token'lar varsayılan değeri gösterir.
    /// </summary>
    private static void EnrichCtx(ApprovalEntityContext ctx, ApprovalFlowDto flow, ApprovalInstanceDto inst)
    {
        ctx.FlowName      = flow.Name;
        ctx.RequesterName = inst.StartedBy ?? "";
        InitFlowVariables(ctx, flow);
    }

    private static void InitFlowVariables(ApprovalEntityContext ctx, ApprovalFlowDto flow)
    {
        if (flow.Variables is not { Count: > 0 }) return;
        foreach (var v in flow.Variables)
        {
            if (string.IsNullOrWhiteSpace(v.Name)) continue;
            if (ctx.FlowVariables.ContainsKey(v.Name)) continue;
            ctx.FlowVariables[v.Name] = ParseDefaultValue(v.DefaultValue, v.TypeCode);
        }
    }

    /// <summary>
    /// ctx.SqlParameters (standart: documentId, userId, ...) + ctx.FlowVariables'ı birleştirir.
    /// FlowVariables, standart parametreleri ezmez; sadece ek parametre olarak eklenir.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> MergeSqlParams(ApprovalEntityContext ctx)
    {
        var merged = new Dictionary<string, object?>(ctx.SqlParameters, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in ctx.FlowVariables)
        {
            if (!merged.ContainsKey(k))
                merged[k] = v;
        }
        return merged;
    }

    private static object? ParseDefaultValue(string? raw, string? typeCode)
    {
        if (raw is null) return null;
        return (typeCode ?? "string").ToLowerInvariant() switch
        {
            "int"     => int.TryParse(raw, out var i) ? i : (object?)null,
            "decimal" => decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (object?)null,
            "bool"    => bool.TryParse(raw, out var b) ? b : (object?)null,
            "date"    => DateTime.TryParse(raw, out var dt) ? dt : (object?)null,
            _         => raw,
        };
    }

    /// <summary>
    /// Flow.DocumentKind = entity type code (Document/WorkOrder/Item/...). Eski enum
    /// değerleri DB migration ile 'Document'a çevrildiği için lookup başarılı olur.
    /// Bilinmeyen tip → boş context (decision evaluator false döner).
    /// </summary>
    private async Task<ApprovalEntityContext> SafeBuildContextAsync(string entityTypeCode, int? documentId, CancellationToken ct)
    {
        var typeCode = string.IsNullOrWhiteSpace(entityTypeCode) ? "Document" : entityTypeCode;
        var entityIdStr = documentId?.ToString() ?? string.Empty;
        var entityType = _entityRegistry.Get(typeCode);
        if (entityType is null)
        {
            _logger.LogWarning("ApprovalEntityType '{Code}' kayıtlı değil — boş context.", typeCode);
            return new ApprovalEntityContext { EntityTypeCode = typeCode, EntityId = entityIdStr };
        }
        try
        {
            return await entityType.BuildContextAsync(entityIdStr, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EntityContext build hatası ({Type}, {Id}) — boş context kullanıldı.",
                typeCode, documentId);
            return new ApprovalEntityContext { EntityTypeCode = typeCode, EntityId = entityIdStr };
        }
    }
}

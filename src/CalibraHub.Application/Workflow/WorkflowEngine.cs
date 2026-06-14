using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using NCalc;

namespace CalibraHub.Application.Workflow;

/// <summary>
/// Workflow runtime motoru. Definition graph + NCalc condition değerlendirme ile
/// bir instance'ı adım adım ilerletir.
/// </summary>
public sealed class WorkflowEngine(
    IWorkflowDefinitionRepository definitionRepo,
    IWorkflowInstanceRepository   instanceRepo,
    IActorResolver                actorResolver,
    OrgChartNCalcFunctions        orgChartFunctions)
{
    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Yeni bir instance başlatır. Start node'u bulur, ilk Task node'larına token iletir.
    /// </summary>
    public async Task<WorkflowInstance> StartAsync(
        int    sourceId,
        int    definitionId,
        string contextJson,
        int?   startedBy,
        CancellationToken ct = default,
        string sourceType = "Document")
    {
        var def = await definitionRepo.GetRawAsync(definitionId, ct)
            ?? throw new InvalidOperationException($"WorkflowDefinition {definitionId} bulunamadı.");

        if (!def.IsPublished)
            throw new InvalidOperationException("Yayınlanmamış workflow başlatılamaz.");

        var instance = new WorkflowInstance
        {
            DefinitionId = definitionId,
            SourceType   = sourceType,
            SourceId     = sourceId,
            CreatedById  = startedBy,
        };
        instance.Start(contextJson, startedBy?.ToString());

        var instanceId = await instanceRepo.CreateAsync(instance, ct);
        instance.Id = instanceId;

        // Start node'unu bul → otomatik ilerlet
        var startNode = def.Nodes.FirstOrDefault(n => n.NodeType == WorkflowNodeType.Start)
            ?? throw new InvalidOperationException("Workflow'da Start node yok.");

        await AdvanceFromNodeAsync(instance, def, startNode.Id, contextJson, startedBy, ct);
        return instance;
    }

    /// <summary>
    /// Bir Task node'unu onayla ve bir sonraki adıma geç.
    /// </summary>
    public async Task ApproveStepAsync(
        int instanceNodeId, string? note, string? actionBy, CancellationToken ct = default)
    {
        var node = await instanceRepo.GetNodeByIdAsync(instanceNodeId, ct)
            ?? throw new InvalidOperationException($"InstanceNode {instanceNodeId} bulunamadı.");

        if (node.Status != WorkflowInstanceNodeStatus.Active)
            throw new InvalidOperationException("Yalnızca aktif adımlar onaylanabilir.");

        node.Status      = WorkflowInstanceNodeStatus.Completed;
        node.Action      = "Approve";
        node.ActionBy    = actionBy;
        node.Note        = note;
        node.CompletedAt = DateTime.UtcNow;
        await instanceRepo.UpdateInstanceNodeAsync(node, ct);

        var instance = await instanceRepo.GetByIdAsync(node.InstanceId, ct)!;
        var def      = await definitionRepo.GetRawAsync(instance!.DefinitionId, ct)!;
        int? actionById = int.TryParse(actionBy, out var aid) ? aid : null;
        await AdvanceFromNodeAsync(instance!, def!, node.NodeId, instance!.ContextJson, actionById, ct);
    }

    /// <summary>
    /// Bir Task node'unu reddet. OnRejectPolicy'ye göre instance iptal veya geri döner.
    /// </summary>
    public async Task RejectStepAsync(
        int instanceNodeId, string? reason, string? actionBy, CancellationToken ct = default)
    {
        var node = await instanceRepo.GetNodeByIdAsync(instanceNodeId, ct)
            ?? throw new InvalidOperationException($"InstanceNode {instanceNodeId} bulunamadı.");

        if (node.Status != WorkflowInstanceNodeStatus.Active)
            throw new InvalidOperationException("Yalnızca aktif adımlar reddedilebilir.");

        node.Status      = WorkflowInstanceNodeStatus.Rejected;
        node.Action      = "Reject";
        node.ActionBy    = actionBy;
        node.Note        = reason;
        node.CompletedAt = DateTime.UtcNow;
        await instanceRepo.UpdateInstanceNodeAsync(node, ct);

        var instance = await instanceRepo.GetByIdAsync(node.InstanceId, ct)!;
        var def      = await definitionRepo.GetRawAsync(instance!.DefinitionId, ct)!;

        // OnRejectPolicy: def node config'den oku
        var defNode = def!.Nodes.FirstOrDefault(n => n.Id == node.NodeId);
        var policy  = defNode?.OnRejectPolicy ?? WorkflowOnRejectPolicy.Cancel;

        int? actionById = int.TryParse(actionBy, out var aid) ? aid : null;
        if (policy == WorkflowOnRejectPolicy.Cancel)
        {
            instance!.Cancel(actionBy, reason);
            await instanceRepo.UpdateStatusAsync(instance!, ct);
        }
        else
        {
            // Return: Start'a geri dön (basit implementasyon)
            var startNode = def!.Nodes.FirstOrDefault(n => n.NodeType == WorkflowNodeType.Start);
            if (startNode is not null)
                await AdvanceFromNodeAsync(instance!, def!, startNode.Id, instance!.ContextJson, actionById, ct);
        }
    }

    /// <summary>
    /// Instance'ı iptal eder — tüm aktif tokenlar Skipped olur.
    /// </summary>
    public async Task CancelAsync(int instanceId, string? reason, string? actionBy, CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException($"Instance {instanceId} bulunamadı.");

        if (instance.Status != WorkflowInstanceStatus.Active &&
            instance.Status != WorkflowInstanceStatus.Pending)
            throw new InvalidOperationException("Yalnızca aktif/pending instance'lar iptal edilebilir.");

        var activeNodes = await instanceRepo.GetActiveNodesByInstanceAsync(instanceId, ct);
        foreach (var n in activeNodes)
        {
            n.Status      = WorkflowInstanceNodeStatus.Skipped;
            n.Action      = "Skipped";
            n.ActionBy    = actionBy;
            n.Note        = reason;
            n.CompletedAt = DateTime.UtcNow;
            await instanceRepo.UpdateInstanceNodeAsync(n, ct);
        }

        instance.Status      = WorkflowInstanceStatus.Cancelled;
        instance.CompletedAt = DateTime.UtcNow;
        await instanceRepo.UpdateStatusAsync(instance, ct);
    }

    // ── Internal advance logic ────────────────────────────────────────────

    private async Task AdvanceFromNodeAsync(
        WorkflowInstance instance,
        WorkflowDefinition def,
        int fromNodeId,
        string? contextJson,
        int? actor,
        CancellationToken ct)
    {
        var context     = DocumentContextBuilder.ParseSnapshot(contextJson);
        var transitions = def.Transitions
            .Where(t => t.FromNodeId == fromNodeId)
            .OrderBy(t => t.Priority)
            .ToList();

        var fromNode = def.Nodes.FirstOrDefault(n => n.Id == fromNodeId);
        if (fromNode is null) return;

        switch (fromNode.NodeType)
        {
            case WorkflowNodeType.Start:
            case WorkflowNodeType.Decision:
                // İlk true condition'lı transition (priority sıralı); yoksa default
                var picked = transitions.FirstOrDefault(t => EvaluateCondition(t.Condition, context))
                          ?? transitions.FirstOrDefault(t => t.IsDefault);
                if (picked is not null)
                    await EnterNodeAsync(instance, def, picked.ToNodeId, contextJson, actor, ct, context);
                break;

            case WorkflowNodeType.ParallelSplit:
                // Condition true olan TÜM transition'lar; condition yoksa hepsi
                var selected = transitions.Where(t =>
                    string.IsNullOrWhiteSpace(t.Condition) || EvaluateCondition(t.Condition, context)).ToList();
                foreach (var tr in selected)
                    await EnterNodeAsync(instance, def, tr.ToNodeId, contextJson, actor, ct, context);
                break;

            case WorkflowNodeType.Task:
                // Task tamamlandı → sadece geçerli transition'a git
                var next = transitions.FirstOrDefault(t =>
                    string.IsNullOrWhiteSpace(t.Condition) || EvaluateCondition(t.Condition, context))
                    ?? transitions.FirstOrDefault(t => t.IsDefault);
                if (next is not null)
                    await EnterNodeAsync(instance, def, next.ToNodeId, contextJson, actor, ct, context);
                break;

            case WorkflowNodeType.ParallelJoin:
                // Token sayısı == expected → tek çıkış
                var arrived = await instanceRepo.CountCompletedTokensAtNodeAsync(instance.Id, fromNodeId, ct);
                var expected = fromNode.JoinExpectedTokens ?? transitions.Count;
                if (arrived >= expected)
                {
                    var joinOut = transitions.FirstOrDefault();
                    if (joinOut is not null)
                        await EnterNodeAsync(instance, def, joinOut.ToNodeId, contextJson, actor, ct, context);
                }
                break;
        }
    }

    private async Task EnterNodeAsync(
        WorkflowInstance instance,
        WorkflowDefinition def,
        int nodeId,
        string? contextJson,
        int? actor,
        CancellationToken ct,
        Dictionary<string, object?>? resolvedContext = null)
    {
        var node = def.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;

        var context = resolvedContext ?? DocumentContextBuilder.ParseSnapshot(contextJson);

        switch (node.NodeType)
        {
            case WorkflowNodeType.End:
                instance.Complete();
                await instanceRepo.UpdateStatusAsync(instance, ct);
                break;

            case WorkflowNodeType.Decision:
            case WorkflowNodeType.ParallelSplit:
                // Otomatik geçiş — InstanceNode oluşturma, doğrudan Advance
                await AdvanceFromNodeAsync(instance, def, nodeId, contextJson, actor, ct);
                break;

            case WorkflowNodeType.ParallelJoin:
            {
                // Token oluştur ve join sayısını kontrol et
                var joinNode = new WorkflowInstanceNode
                {
                    InstanceId = instance.Id,
                    NodeId     = nodeId,
                    Status     = WorkflowInstanceNodeStatus.Completed,
                    EnteredAt  = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    Action     = "Skipped",
                    ActionBy   = "SYSTEM",
                    CreatedById = actor,
                };
                await instanceRepo.CreateInstanceNodeAsync(joinNode, ct);
                await AdvanceFromNodeAsync(instance, def, nodeId, contextJson, actor, ct);
                break;
            }

            case WorkflowNodeType.Task:
            {
                var resolvedActor = await actorResolver.ResolveAsync(node, context, ct);
                var taskNode = new WorkflowInstanceNode
                {
                    InstanceId     = instance.Id,
                    NodeId         = nodeId,
                    Status         = WorkflowInstanceNodeStatus.Active,
                    AssignedUserId = resolvedActor ?? node.ActorRefId,
                    EnteredAt      = DateTime.UtcNow,
                    CreatedById    = actor,
                };
                await instanceRepo.CreateInstanceNodeAsync(taskNode, ct);
                break;
            }
        }
    }

    private bool EvaluateCondition(string? condition, Dictionary<string, object?> context)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        try
        {
            var expr = new Expression(condition);
            orgChartFunctions.Register(expr);
            foreach (var kv in context)
                expr.Parameters[kv.Key] = kv.Value;
            var result = expr.Evaluate();
            return result is bool b && b;
        }
        catch { return false; }
    }
}

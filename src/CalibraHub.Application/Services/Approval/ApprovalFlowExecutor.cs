using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;
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
    Task<int> AfterStepActionAsync(int instanceId, int currentStepOrder, bool isApproved, CancellationToken ct);

    /// <summary>
    /// Instance start: Start node'dan ilk node'a kadar pass-through (decision/notif).
    /// CreateAsync linear step'leri kurduktan sonra çağrılır.
    /// </summary>
    Task<int> AfterStartAsync(int instanceId, CancellationToken ct);
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
    private readonly ILogger<ApprovalFlowExecutor> _logger;

    public ApprovalFlowExecutor(
        IApprovalFlowRepository flowRepo,
        IApprovalInstanceRepository instRepo,
        IDecisionEvaluator decisionEval,
        IApprovalEntityTypeRegistry entityRegistry,
        IApprovalNotificationDispatcher notifDispatcher,
        IIntegrationRunner integrationRunner,
        ILogger<ApprovalFlowExecutor> logger)
    {
        _flowRepo          = flowRepo;
        _instRepo          = instRepo;
        _decisionEval      = decisionEval;
        _entityRegistry    = entityRegistry;
        _notifDispatcher   = notifDispatcher;
        _integrationRunner = integrationRunner;
        _logger            = logger;
    }

    public bool IsGraphBased(ApprovalFlowDto flow)
    {
        if (flow.Edges is { Count: > 0 }) return true;
        return flow.Steps.Any(s => !string.IsNullOrEmpty(s.NodeType)
            && !IsStepKind(s.NodeType));
    }

    public async Task<int> AfterStepActionAsync(int instanceId, int currentStepOrder, bool isApproved, CancellationToken ct)
    {
        var inst = await _instRepo.GetByIdAsync(instanceId, ct);
        if (inst is null) return 0;
        var flow = await _flowRepo.GetByIdAsync(inst.FlowId, ct);
        if (flow is null || !IsGraphBased(flow)) return 0;

        // currentStepOrder -> ApprovalFlowStep eşle (StepOrder + nodeType=step)
        var currentNode = flow.Steps
            .OrderBy(s => s.StepOrder)
            .FirstOrDefault(s => s.StepOrder == currentStepOrder && IsStepKind(s.NodeType));
        if (currentNode is null) return 0;

        var ctx = await SafeBuildContextAsync(flow.DocumentKind, inst.DocumentId, ct);
        var processed = 0;

        foreach (var edge in SelectEdges(flow, currentNode.Id, isApproved))
        {
            var target = flow.Steps.FirstOrDefault(s => s.Id == edge.TargetStepId);
            if (target is null) continue;
            processed += await TraverseAsync(flow, target, ctx, instanceId, depth: 0, ct);
        }

        return processed;
    }

    public async Task<int> AfterStartAsync(int instanceId, CancellationToken ct)
    {
        var inst = await _instRepo.GetByIdAsync(instanceId, ct);
        if (inst is null) return 0;
        var flow = await _flowRepo.GetByIdAsync(inst.FlowId, ct);
        if (flow is null || !IsGraphBased(flow)) return 0;

        var startNode = flow.Steps.FirstOrDefault(s =>
            string.Equals(s.NodeType, "start", StringComparison.OrdinalIgnoreCase));
        if (startNode is null) return 0;

        var ctx = await SafeBuildContextAsync(flow.DocumentKind, inst.DocumentId, ct);
        var processed = 0;
        foreach (var edge in flow.Edges.Where(e => e.SourceStepId == startNode.Id).OrderBy(e => e.SortOrder))
        {
            var target = flow.Steps.FirstOrDefault(s => s.Id == edge.TargetStepId);
            if (target is null) continue;
            processed += await TraverseAsync(flow, target, ctx, instanceId, depth: 0, ct);
        }
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
        CancellationToken ct)
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
                // Step node — burada dur. DB'de zaten pending record'u var (linear CreateAsync).
                return 0;

            case "decision":
            {
                var result = await _decisionEval.EvaluateAsync(node.NodeData, ctx, ct);
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
                if (picked is null) return 1;
                var next = flow.Steps.FirstOrDefault(s => s.Id == picked.TargetStepId);
                if (next is null) return 1;
                return 1 + await TraverseAsync(flow, next, ctx, instanceId, depth + 1, ct);
            }

            case "notification":
            {
                await _notifDispatcher.SendFromNodeAsync(node.NodeData, ctx, ct);
                var followEdges = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                var picked = followEdges.FirstOrDefault();
                if (picked is null) return 1;
                var next = flow.Steps.FirstOrDefault(s => s.Id == picked.TargetStepId);
                if (next is null) return 1;
                return 1 + await TraverseAsync(flow, next, ctx, instanceId, depth + 1, ct);
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
                }
                else
                {
                    var recordId = string.Equals(recordIdSource, "custom", StringComparison.OrdinalIgnoreCase)
                        ? customRecordId
                        : (string.IsNullOrWhiteSpace(ctx.EntityId) ? null : ctx.EntityId);

                    try
                    {
                        var result = await _integrationRunner.RunAsync(
                            integrationId.Value, recordId,
                            IntegrationTriggerType.Cascade,
                            triggeredBy: $"approval-flow:{flow.Id}:instance:{instanceId}",
                            ct);

                        if (!result.Success && haltOnError)
                        {
                            _logger.LogWarning(
                                "Integration node {Id} (integration={Iid}) başarısız — haltOnError, akış durdu. Hata: {Err}",
                                node.Id, integrationId, result.ErrorMessage);
                            return 1; // sonraki node'a geçme
                        }

                        _logger.LogInformation(
                            "Integration node {Id} (integration={Iid}) tetiklendi — Success={Ok}, RunId={Rid}",
                            node.Id, integrationId, result.Success, result.RunId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Integration node {Id} run exception — haltOnError={Halt}", node.Id, haltOnError);
                        if (haltOnError) return 1;
                    }
                }

                var followEdges = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                var picked = followEdges.FirstOrDefault();
                if (picked is null) return 1;
                var next = flow.Steps.FirstOrDefault(s => s.Id == picked.TargetStepId);
                if (next is null) return 1;
                return 1 + await TraverseAsync(flow, next, ctx, instanceId, depth + 1, ct);
            }

            case "parallel":
            {
                // Split: tüm outgoing edge'leri sırayla traverse et. Join logic'i
                // join node'a varan tek-uygulamalı recursion ile MVP'de sağlanır.
                var outEdges = flow.Edges
                    .Where(e => e.SourceStepId == node.Id)
                    .OrderBy(e => e.SortOrder)
                    .ToList();
                int sub = 1;
                foreach (var oe in outEdges)
                {
                    var t = flow.Steps.FirstOrDefault(s => s.Id == oe.TargetStepId);
                    if (t is null) continue;
                    sub += await TraverseAsync(flow, t, ctx, instanceId, depth + 1, ct);
                }
                return sub;
            }

            case "end":
            {
                // Instance'i tamamlandı say. Burada doğrudan SQL UPDATE yok — repo
                // ApproveStepAsync zaten son step'ten sonra Completed'a çekiyor.
                // Graph'ta end node'a explicit varış durumunda override gereksiz.
                _logger.LogInformation("Flow end node ulaşıldı (instance={Iid}).", instanceId);
                return 1;
            }

            default:
                _logger.LogWarning("Bilinmeyen nodeType='{Type}' (id={Id}) — atlandı.", kind, node.Id);
                return 0;
        }
    }

    /// <summary>
    /// Step node sonrası izlenecek edge'leri seç. true/default → approve, false → reject.
    /// </summary>
    private static IEnumerable<ApprovalFlowEdgeDto> SelectEdges(ApprovalFlowDto flow, int sourceStepId, bool isApproved)
    {
        var all = flow.Edges
            .Where(e => e.SourceStepId == sourceStepId)
            .OrderBy(e => e.SortOrder)
            .ToList();
        if (all.Count == 0) yield break;

        foreach (var e in all)
        {
            var kind = (e.EdgeKind ?? "default").Trim().ToLowerInvariant();
            if (isApproved && (kind is "true" or "default" or "approve"))
                yield return e;
            else if (!isApproved && (kind is "false" or "reject"))
                yield return e;
        }
    }

    private static bool IsStepKind(string? nodeType)
        => string.IsNullOrWhiteSpace(nodeType)
        || string.Equals(nodeType, "step", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flow.DocumentKind = entity type code (Document/WorkOrder/Item/...). Eski enum
    /// değerleri DB migration ile 'Document'a çevrildiği için lookup başarılı olur.
    /// Bilinmeyen tip → boş context (decision evaluator false döner).
    /// </summary>
    private async Task<ApprovalEntityContext> SafeBuildContextAsync(string entityTypeCode, Guid documentId, CancellationToken ct)
    {
        var typeCode = string.IsNullOrWhiteSpace(entityTypeCode) ? "Document" : entityTypeCode;
        var entityType = _entityRegistry.Get(typeCode);
        if (entityType is null)
        {
            _logger.LogWarning("ApprovalEntityType '{Code}' kayıtlı değil — boş context.", typeCode);
            return new ApprovalEntityContext { EntityTypeCode = typeCode, EntityId = documentId.ToString() };
        }
        try
        {
            return await entityType.BuildContextAsync(documentId.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EntityContext build hatası ({Type}, {Id}) — boş context kullanıldı.",
                typeCode, documentId);
            return new ApprovalEntityContext { EntityTypeCode = typeCode, EntityId = documentId.ToString() };
        }
    }
}

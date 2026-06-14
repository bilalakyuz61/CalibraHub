using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Approval;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CalibraHub.Application.Services;

public sealed class ApprovalFlowService : IApprovalFlowService
{
    private readonly IApprovalFlowRepository _flowRepo;
    private readonly IApprovalInstanceRepository _instanceRepo;
    private readonly IApprovalFlowExecutor? _executor;
    private readonly ILogger<ApprovalFlowService>? _logger;

    public ApprovalFlowService(
        IApprovalFlowRepository flowRepo,
        IApprovalInstanceRepository instanceRepo,
        IApprovalFlowExecutor? executor = null,
        ILogger<ApprovalFlowService>? logger = null)
    {
        _flowRepo = flowRepo;
        _instanceRepo = instanceRepo;
        _executor = executor;
        _logger = logger;
    }

    public Task<IReadOnlyList<ApprovalFlowSummaryDto>> GetAllAsync(CancellationToken ct)
        => _flowRepo.GetAllSummariesAsync(ct);

    public Task<ApprovalFlowDto?> GetByIdAsync(int id, CancellationToken ct)
        => _flowRepo.GetByIdAsync(id, ct);

    public Task<int> SaveAsync(SaveApprovalFlowRequest request, int? byUserId, CancellationToken ct)
        => _flowRepo.SaveAsync(request, byUserId, ct);

    public Task DeleteAsync(int id, CancellationToken ct)
        => _flowRepo.DeleteAsync(id, ct);

    public async Task<int> DuplicateAsync(int sourceId, int? byUserId, CancellationToken ct)
    {
        var src = await _flowRepo.GetByIdAsync(sourceId, ct)
            ?? throw new InvalidOperationException($"Kopyalanacak akış bulunamadı: {sourceId}");

        // Step id sıfırlanır → save sırasında yeni id atanır. Edge'lerin source/target
        // step id'leri eski DB id'leri olduğu için repo'ya client-id index üzerinden
        // mapping yaparak verebilmek için aşağıda step listesi üzerinden index'le SourceStepClientId
        // hesaplanır (Repository SaveAsync zaten edges için "clientIndex → stepId" map'i kullanıyor).
        var steps = src.Steps.Select((s, i) => new SaveApprovalFlowStepRequest(
            Id:            0,
            StepOrder:     s.StepOrder,
            StepName:      s.StepName,
            ApproverType:  s.ApproverType,
            ApproverId:    s.ApproverId,
            ApproverLabel: s.ApproverLabel,
            IsActive:      s.IsActive,
            NodeType:      s.NodeType,
            PosX:          s.PosX,
            PosY:          s.PosY,
            NodeData:      s.NodeData)).ToList();

        // Edge'lerde source/target step id'leri → orijinal listede index bul → kopya step listesindeki
        // aynı index'i ata. SaveApprovalFlowEdgeRequest "SourceStepClientId" alanı index'i bekler.
        var origStepIds = src.Steps.Select(s => s.Id).ToList();
        var edges = src.Edges.Select(e => new SaveApprovalFlowEdgeRequest(
            Id:                 0,
            SourceStepClientId: origStepIds.IndexOf(e.SourceStepId),
            TargetStepClientId: origStepIds.IndexOf(e.TargetStepId),
            Label:              e.Label,
            EdgeKind:           e.EdgeKind,
            Condition:          e.Condition,
            SortOrder:          e.SortOrder)).ToList();

        var rules = src.Rules.Select(r => new SaveApprovalFlowRuleRequest(
            Id:        0,
            RuleType:  r.RuleType,
            RuleValue: r.RuleValue,
            IsActive:  r.IsActive)).ToList();

        var variables = src.Variables.Select(v => new SaveApprovalFlowVariableRequest(
            Id:           0,
            Name:         v.Name,
            TypeCode:     v.TypeCode,
            DefaultValue: v.DefaultValue,
            Description:  v.Description,
            SortOrder:    v.SortOrder)).ToList();

        var copy = new SaveApprovalFlowRequest(
            Id:           0,
            Name:         src.Name + " (Kopya)",
            Description:  src.Description,
            DocumentKind: src.DocumentKind,
            Priority:     src.Priority,
            IsActive:     false, // Kopya pasif başlasın — kullanıcı aktive etmeden devreye girmesin
            Rules:        rules,
            Steps:        steps,
            Edges:        edges,
            Variables:    variables);

        return await _flowRepo.SaveAsync(copy, byUserId, ct);
    }

    public async Task<ApprovalFlowDto?> MatchFlowAsync(
        string documentKind,
        decimal? totalAmount,
        string? senderTaxNo,
        int? departmentId,
        CancellationToken ct)
    {
        var flows = await _flowRepo.GetByDocumentKindAsync(documentKind, ct);

        // Öncelik sırasına göre (büyükten küçüğe) değerlendir; ilk eşleşen kazanır
        foreach (var flow in flows.OrderByDescending(f => f.Priority))
        {
            if (FlowMatchesRules(flow, totalAmount, senderTaxNo, departmentId))
                return flow;
        }
        return null;
    }

    public async Task<ApprovalInstanceDto> StartAsync(StartApprovalRequest request, CancellationToken ct)
    {
        var flow = await _flowRepo.GetByIdAsync(request.FlowId, ct)
            ?? throw new InvalidOperationException($"Akış bulunamadı: {request.FlowId}");

        // Yalnizca onaylayici karari gerektiren "step" tipi dugumler instance step record'una
        // donusur. Start/End/Decision dugumleri runtime'da edge takibi icin kullanilir, ama
        // step record'una eklenmez.
        var activeSteps = flow.Steps
            .Where(s => s.IsActive && IsApproverStep(s.NodeType))
            .OrderBy(s => s.StepOrder)
            .ToArray();

        if (activeSteps.Length == 0)
            throw new InvalidOperationException("Akışta aktif adım tanımlanmamış.");

        var instanceId = await _instanceRepo.CreateAsync(request, activeSteps, ct);

        // Faz 4 — Graph-aware: start node sonrası decision/notification node'ları
        // pass-through olarak çalıştır. Linear akışlarda no-op.
        if (_executor is not null)
        {
            try { await _executor.AfterStartAsync(instanceId, ct); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStartAsync hatası (instance={Iid}).", instanceId); }
        }

        return await _instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException("Oluşturulan onay örneği okunamadı.");
    }

    private static bool IsApproverStep(string? nodeType)
    {
        // null/empty → legacy "step" sayılır (geri-uyumluluk)
        if (string.IsNullOrWhiteSpace(nodeType)) return true;
        return string.Equals(nodeType, "step", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ApprovalInstanceDto> ApproveStepAsync(ApproveStepRequest request, CancellationToken ct)
    {
        var instance = await _instanceRepo.GetByIdAsync(request.InstanceId, ct)
            ?? throw new InvalidOperationException($"Onay örneği bulunamadı: {request.InstanceId}");

        if (instance.Status != "Pending")
            throw new InvalidOperationException($"Onay örneği işlem yapılabilir durumda değil: {instance.Status}");

        // Graph-aware branching: o anki step'in flow'daki karsiligini bul,
        // out-edges varsa "default"/"true" dali ile sonraki step record'unu sec.
        // Edge yoksa veya end node'a ulasildigi tespit edilirse instance repo
        // mevcut linear logic ile sonu belirler (StepOrder > current ile next pending).
        // Bu MVP: graph-aware decision degerlendirilmesi yapmaz, branch'ler
        // "default"/"true" oncelik sirasiyla cozulur.
        var stepBefore = instance.CurrentStep;
        await _instanceRepo.ApproveStepAsync(
            request.InstanceId,
            stepBefore,
            request.ApproverId,
            request.ApproverName,
            request.Note,
            ct);

        // Faz 4 — Graph executor: sonraki yolda decision/notification node'larını işle.
        if (_executor is not null)
        {
            try { await _executor.AfterStepActionAsync(request.InstanceId, stepBefore, isApproved: true, ct); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStepActionAsync hatası (instance={Iid}, step={St}).", request.InstanceId, stepBefore); }
        }

        return await _instanceRepo.GetByIdAsync(request.InstanceId, ct)
            ?? throw new InvalidOperationException("Onay örneği okunamadı.");
    }

    public async Task<ApprovalInstanceDto> RejectAsync(RejectStepRequest request, CancellationToken ct)
    {
        var instance = await _instanceRepo.GetByIdAsync(request.InstanceId, ct)
            ?? throw new InvalidOperationException($"Onay örneği bulunamadı: {request.InstanceId}");

        if (instance.Status != "Pending")
            throw new InvalidOperationException($"Onay örneği işlem yapılabilir durumda değil: {instance.Status}");

        var stepBefore = instance.CurrentStep;
        await _instanceRepo.RejectAsync(
            request.InstanceId,
            stepBefore,
            request.ApproverId,
            request.ApproverName,
            request.Note,
            ct);

        // Faz 4 — Graph executor: reject yolundaki notification node'larını işle.
        // (Reject sonrası instance zaten Rejected; sadece notification side-effect.)
        if (_executor is not null)
        {
            try { await _executor.AfterStepActionAsync(request.InstanceId, stepBefore, isApproved: false, ct); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStepActionAsync (reject) hatası (instance={Iid}, step={St}).", request.InstanceId, stepBefore); }
        }

        return await _instanceRepo.GetByIdAsync(request.InstanceId, ct)
            ?? throw new InvalidOperationException("Onay örneği okunamadı.");
    }

    public async Task<ApprovalInstanceDto> CancelAsync(int instanceId, string byUser, CancellationToken ct)
    {
        await _instanceRepo.CancelAsync(instanceId, byUser, ct);
        return await _instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException("Onay örneği okunamadı.");
    }

    public Task<ApprovalInstanceDto?> GetInstanceByDocumentIdAsync(Guid documentId, CancellationToken ct)
        => _instanceRepo.GetByDocumentIdAsync(documentId, ct);

    // ── Kural motoru ─────────────────────────────────────────────────────────
    private static bool FlowMatchesRules(ApprovalFlowDto flow, decimal? totalAmount, string? senderTaxNo, int? departmentId)
    {
        var activeRules = flow.Rules.Where(r => r.IsActive).ToArray();

        if (activeRules.Length == 0)
            return true; // kural yok → her belgeye uyar

        // Her kural bir koşuldur — hepsi sağlanmalı (AND mantığı)
        foreach (var rule in activeRules)
        {
            if (!RuleMatches(rule, totalAmount, senderTaxNo, departmentId))
                return false;
        }
        return true;
    }

    private static bool RuleMatches(ApprovalFlowRuleDto rule, decimal? totalAmount, string? senderTaxNo, int? departmentId)
    {
        switch (rule.RuleType)
        {
            case ApprovalRuleType.Always:
                return true;

            case ApprovalRuleType.MinAmount:
                if (!decimal.TryParse(rule.RuleValue, out var min)) return false;
                return totalAmount.HasValue && totalAmount.Value >= min;

            case ApprovalRuleType.MaxAmount:
                if (!decimal.TryParse(rule.RuleValue, out var max)) return false;
                return totalAmount.HasValue && totalAmount.Value <= max;

            case ApprovalRuleType.AmountRange:
                try
                {
                    var range = JsonSerializer.Deserialize<AmountRange>(rule.RuleValue ?? "{}");
                    if (range is null) return false;
                    return totalAmount.HasValue
                        && totalAmount.Value >= range.Min
                        && totalAmount.Value <= range.Max;
                }
                catch { return false; }

            case ApprovalRuleType.SenderTaxNo:
                return !string.IsNullOrWhiteSpace(senderTaxNo)
                    && string.Equals(
                        senderTaxNo.Trim(),
                        rule.RuleValue?.Trim(),
                        StringComparison.OrdinalIgnoreCase);

            case ApprovalRuleType.Department:
                // RuleValue = "3,5,7" gibi virgulle ayrilmis Department.Id seti.
                // Belge bu setteki herhangi bir departmana aitse esleme saglanir.
                if (!departmentId.HasValue || string.IsNullOrWhiteSpace(rule.RuleValue)) return false;
                foreach (var part in rule.RuleValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(part, out var id) && id == departmentId.Value) return true;
                }
                return false;

            default:
                return false;
        }
    }

    private sealed class AmountRange
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
    }
}

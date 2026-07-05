using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Approval;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CalibraHub.Application.Services;

public sealed class ApprovalFlowService : IApprovalFlowService
{
    private readonly IApprovalFlowRepository _flowRepo;
    private readonly IApprovalInstanceRepository _instanceRepo;
    private readonly IUserProfileRepository _userRepo;
    private readonly IApprovalFlowExecutor? _executor;
    private readonly ILogger<ApprovalFlowService>? _logger;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IApprovalNodeLogger? _nodeLogger;
    private readonly ICurrentCompanyProvider? _companyProvider;

    public ApprovalFlowService(
        IApprovalFlowRepository flowRepo,
        IApprovalInstanceRepository instanceRepo,
        IUserProfileRepository userRepo,
        IApprovalFlowExecutor? executor = null,
        ILogger<ApprovalFlowService>? logger = null,
        IServiceScopeFactory? scopeFactory = null,
        IApprovalNodeLogger? nodeLogger = null,
        ICurrentCompanyProvider? companyProvider = null)
    {
        _flowRepo        = flowRepo;
        _instanceRepo    = instanceRepo;
        _userRepo        = userRepo;
        _executor        = executor;
        _logger          = logger;
        _scopeFactory    = scopeFactory;
        _nodeLogger      = nodeLogger;
        _companyProvider = companyProvider;
    }

    private async Task TryLogAsync(int instanceId, int flowId, string? nodeType, string? nodeName,
                                   string eventType, string? detail, CancellationToken ct)
    {
        if (_nodeLogger is null) return;
        try { await _nodeLogger.LogAsync(instanceId, flowId, null, nodeType, nodeName, eventType, detail, null, ct); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Service log yazılamadı (instance={Iid}, event={Ev}).", instanceId, eventType); }
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
            SortOrder:          e.SortOrder,
            SourceHandle:       e.SourceHandle,
            TargetHandle:       e.TargetHandle)).ToList();

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

        foreach (var flow in flows)
        {
            if (FlowMatchesRules(flow, totalAmount, senderTaxNo, departmentId))
                return flow;
        }
        return null;
    }

    public async Task<ApprovalInstanceDto> StartAsync(StartApprovalRequest request, CancellationToken ct)
    {
        // Duplicate guard: aynı belge için bekleyen bir onay süreci varken ikinci bir
        // süreç başlatılamaz. (Auto-start + manuel Onaya Gönder + İşleme Al modalı
        // üç bağımsız yol — server-side tek koruma noktası burasıdır.)
        // DocumentId null olabilir (belge-bağımsız entity akışları) — o durumda guard atlanır.
        // EntityKind karşılaştırması: farklı entity tipinin (Item/Contact) aynı ID'li instance'ı
        // belge onayını yanlışlıkla engellemesin.
        if (request.DocumentId.HasValue)
        {
            var existing = await _instanceRepo.GetByDocumentIdAsync(request.DocumentId.Value, ct);
            if (existing is not null
                && string.Equals(existing.Status, "Pending", StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.EntityKind, request.EntityKind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Bu belge için zaten bekleyen bir onay süreci var.");
        }

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

        // ManagerOfRequester adımları için talep sahibinin amirini çöz ve ApproverId'yi set et.
        // Böylece step record'a doğrudan amir ID'si yazılır — "Sadece benim" scope'u çalışır.
        if (request.StartedByUserId.HasValue
            && activeSteps.Any(s => s.ApproverType == ApproverType.ManagerOfRequester))
        {
            var requester = await _userRepo.GetByIdAsync(request.StartedByUserId.Value, ct);
            if (requester?.SupervisorUserId.HasValue == true)
            {
                var supervisor = await _userRepo.GetByIdAsync(requester.SupervisorUserId.Value, ct);
                if (supervisor is not null)
                {
                    activeSteps = activeSteps
                        .Select(s => s.ApproverType == ApproverType.ManagerOfRequester
                            ? s with { ApproverId = supervisor.Id.ToString(), ApproverLabel = supervisor.FullName }
                            : s)
                        .ToArray();
                }
            }
        }

        var instanceId = await _instanceRepo.CreateAsync(request, activeSteps, ct);

        // Hangi revizyonla başlatıldığını kaydet — revizyon ID ile instance'ı bağla.
        var latestRevisionId = await _flowRepo.GetLatestRevisionIdAsync(request.FlowId, ct);
        if (latestRevisionId.HasValue)
            await _instanceRepo.UpdateRevisionIdAsync(instanceId, latestRevisionId.Value, ct);

        // Execution log: akış başlatıldı
        await TryLogAsync(instanceId, request.FlowId, "start", flow.Name,
            "FlowStarted", $"Başlatan: {request.StartedBy}", ct);

        // Faz 4 — Graph-aware: start node sonrası decision/notification node'ları
        // pass-through olarak çalıştır. Linear akışlarda no-op.
        // Fire-and-forget: bildirim gönderimi (mail/WhatsApp insan gecikmesi) HTTP yanıtını
        // bloklamasın. Scoped servisler request scope sonrasında dispose olacağından
        // IServiceScopeFactory ile bağımsız yeni bir scope açılır.
        if (_executor is not null)
        {
            var capturedId      = instanceId;
            var capturedBaseUrl = _companyProvider?.GetBaseUrl(); // HTTP context canlıyken yakala
            if (_scopeFactory is not null)
            {
                var factory = _scopeFactory;
                var log     = _logger;
                _ = Task.Run(async () =>
                {
                    await using var scope = factory.CreateAsyncScope();
                    var exec = scope.ServiceProvider.GetRequiredService<IApprovalFlowExecutor>();
                    try { await exec.AfterStartAsync(capturedId, CancellationToken.None, capturedBaseUrl); }
                    catch (Exception ex) { log?.LogWarning(ex, "Executor AfterStartAsync hatası (instance={Iid}).", capturedId); }
                });
            }
            else
            {
                try { await _executor.AfterStartAsync(instanceId, ct, capturedBaseUrl); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStartAsync hatası (instance={Iid}).", instanceId); }
            }
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

        var stepBefore = instance.CurrentStep;
        var stepRecord = instance.StepRecords.FirstOrDefault(s => s.StepOrder == stepBefore);

        // Oylama adımı tespiti: flow tanımında bu StepOrder'da "vote" nodeType var mı?
        var flow         = _executor is not null ? await _flowRepo.GetByIdAsync(instance.FlowId, ct) : null;
        var currentNode  = flow?.Steps.FirstOrDefault(s => s.StepOrder == stepBefore);
        var isVoteStep   = string.Equals(currentNode?.NodeType, "vote", StringComparison.OrdinalIgnoreCase);

        if (isVoteStep)
        {
            // Oylama: sadece bu oylayıcının kaydını güncelle, consensus kontrolü yap
            var voteConsensusType = ParseVoteConsensusType(currentNode?.NodeData);
            var vr = await _instanceRepo.VoteOnStepAsync(
                request.InstanceId, stepBefore,
                request.ApproverId, request.ApproverName, request.Note,
                isApprove: true, voteConsensusType, ct);

            if (!vr.Voted)
                throw new InvalidOperationException("Oy kullanılamadı — bu kullanıcıya ait bekleyen oy kaydı bulunamadı.");

            if (vr.ConsensusReached)
            {
                await TryLogAsync(request.InstanceId, instance.FlowId, "vote", stepRecord?.StepName,
                    "VoteConsensus", $"Consensus: {(vr.ConsensusApproved ? "Kabul" : "Red")} ({vr.ApprovedCount}/{vr.TotalVoters})", ct);
                if (_executor is not null)
                {
                    var capturedBaseUrl = _companyProvider?.GetBaseUrl();
                    try { await _executor.AfterStepActionAsync(request.InstanceId, stepBefore, isApproved: vr.ConsensusApproved, ct, capturedBaseUrl, request.ChoiceArmId); }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStepActionAsync (vote) hatası (instance={Iid}).", request.InstanceId); }
                }
            }
            else
            {
                await TryLogAsync(request.InstanceId, instance.FlowId, "vote", stepRecord?.StepName,
                    "VoteCast", $"{request.ApproverName} oy kullandı: Kabul ({vr.ApprovedCount}/{vr.TotalVoters}, {vr.PendingCount} bekliyor)", ct);
            }
        }
        else
        {
            // Normal onay adımı
            await _instanceRepo.ApproveStepAsync(
                request.InstanceId, stepBefore,
                request.ApproverId, request.ApproverName, request.Note, ct);

            await TryLogAsync(request.InstanceId, instance.FlowId, "step", stepRecord?.StepName,
                "StepApproved", $"{request.ApproverName} onayladı{(string.IsNullOrWhiteSpace(request.Note) ? "" : $" — {request.Note}")}", ct);

            if (_executor is not null)
            {
                var capturedBaseUrl = _companyProvider?.GetBaseUrl();
                try { await _executor.AfterStepActionAsync(request.InstanceId, stepBefore, isApproved: true, ct, capturedBaseUrl, request.ChoiceArmId); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStepActionAsync hatası (instance={Iid}, step={St}).", request.InstanceId, stepBefore); }
            }
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
        var stepRecord = instance.StepRecords.FirstOrDefault(s => s.StepOrder == stepBefore);

        var flow        = _executor is not null ? await _flowRepo.GetByIdAsync(instance.FlowId, ct) : null;
        var currentNode = flow?.Steps.FirstOrDefault(s => s.StepOrder == stepBefore);
        var isVoteStep  = string.Equals(currentNode?.NodeType, "vote", StringComparison.OrdinalIgnoreCase);

        if (isVoteStep)
        {
            var voteConsensusType = ParseVoteConsensusType(currentNode?.NodeData);
            var vr = await _instanceRepo.VoteOnStepAsync(
                request.InstanceId, stepBefore,
                request.ApproverId, request.ApproverName, request.Note,
                isApprove: false, voteConsensusType, ct);

            if (!vr.Voted)
                throw new InvalidOperationException("Oy kullanılamadı — bu kullanıcıya ait bekleyen oy kaydı bulunamadı.");

            if (vr.ConsensusReached)
            {
                await TryLogAsync(request.InstanceId, instance.FlowId, "vote", stepRecord?.StepName,
                    "VoteConsensus", $"Consensus: {(vr.ConsensusApproved ? "Kabul" : "Red")} ({vr.RejectedCount}/{vr.TotalVoters})", ct);
                if (_executor is not null)
                {
                    var capturedBaseUrl = _companyProvider?.GetBaseUrl();
                    try { await _executor.AfterStepActionAsync(request.InstanceId, stepBefore, isApproved: vr.ConsensusApproved, ct, capturedBaseUrl); }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStepActionAsync (vote-reject) hatası (instance={Iid}).", request.InstanceId); }
                }
            }
            else
            {
                await TryLogAsync(request.InstanceId, instance.FlowId, "vote", stepRecord?.StepName,
                    "VoteCast", $"{request.ApproverName} oy kullandı: Red ({vr.RejectedCount}/{vr.TotalVoters}, {vr.PendingCount} bekliyor)", ct);
            }
        }
        else
        {
            await _instanceRepo.RejectAsync(
                request.InstanceId, stepBefore,
                request.ApproverId, request.ApproverName, request.Note, ct);

            await TryLogAsync(request.InstanceId, instance.FlowId, "step", stepRecord?.StepName,
                "StepRejected", $"{request.ApproverName} reddetti — {request.Note}", ct);

            if (_executor is not null)
            {
                var capturedBaseUrl = _companyProvider?.GetBaseUrl();
                try { await _executor.AfterStepActionAsync(request.InstanceId, stepBefore, isApproved: false, ct, capturedBaseUrl); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Executor AfterStepActionAsync (reject) hatası (instance={Iid}, step={St}).", request.InstanceId, stepBefore); }
            }
        }

        return await _instanceRepo.GetByIdAsync(request.InstanceId, ct)
            ?? throw new InvalidOperationException("Onay örneği okunamadı.");
    }

    public async Task<ApprovalInstanceDto> CancelAsync(int instanceId, string byUser, CancellationToken ct)
    {
        var instance = await _instanceRepo.GetByIdAsync(instanceId, ct);
        await _instanceRepo.CancelAsync(instanceId, byUser, ct);
        // Execution log: akış iptal edildi
        if (instance is not null)
            await TryLogAsync(instanceId, instance.FlowId, "flow", null,
                "FlowCancelled", $"İptal eden: {byUser}", ct);
        return await _instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException("Onay örneği okunamadı.");
    }

    public Task<IReadOnlyList<ApprovalNodeLogDto>> GetInstanceLogsAsync(int instanceId, CancellationToken ct)
        => _nodeLogger is not null
           ? _nodeLogger.GetLogsAsync(instanceId, ct)
           : Task.FromResult<IReadOnlyList<ApprovalNodeLogDto>>(Array.Empty<ApprovalNodeLogDto>());

    public Task<ApprovalInstanceDto?> GetInstanceByDocumentIdAsync(int documentId, CancellationToken ct)
        => _instanceRepo.GetByDocumentIdAsync(documentId, ct);

    public Task<IReadOnlyList<ApprovalFlowRevisionSummaryDto>> GetRevisionsAsync(int flowId, CancellationToken ct)
        => _flowRepo.GetRevisionsAsync(flowId, ct);

    public Task<ApprovalFlowRevisionDetailDto?> GetRevisionDetailAsync(int revisionId, CancellationToken ct)
        => _flowRepo.GetRevisionDetailAsync(revisionId, ct);

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

    private static string ParseVoteConsensusType(string? nodeData)
    {
        if (string.IsNullOrWhiteSpace(nodeData)) return "majority";
        try
        {
            using var jd = JsonDocument.Parse(nodeData);
            if (jd.RootElement.TryGetProperty("votingType", out var vt) && vt.ValueKind == JsonValueKind.String)
                return vt.GetString() ?? "majority";
        }
        catch { /* ignore */ }
        return "majority";
    }
}

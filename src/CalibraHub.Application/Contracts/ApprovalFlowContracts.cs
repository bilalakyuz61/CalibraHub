using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

// ── Akış şablonu ──────────────────────────────────────────────────────────────
public sealed record ApprovalFlowDto(
    int Id,
    string Name,
    string? Description,
    string DocumentKind,
    bool IsActive,
    IReadOnlyList<ApprovalFlowRuleDto> Rules,
    IReadOnlyList<ApprovalFlowStepDto> Steps,
    IReadOnlyList<ApprovalFlowEdgeDto> Edges,
    IReadOnlyList<ApprovalFlowVariableDto> Variables);

public sealed record ApprovalFlowSummaryDto(
    int Id,
    string Name,
    string? Description,
    string DocumentKind,
    bool IsActive,
    int StepCount,
    int RuleCount);

public sealed record ApprovalFlowRuleDto(
    int Id,
    int FlowId,
    ApprovalRuleType RuleType,
    string? RuleValue,
    bool IsActive);

public sealed record ApprovalFlowStepDto(
    int Id,
    int FlowId,
    int StepOrder,
    string StepName,
    ApproverType ApproverType,
    string? ApproverId,
    string? ApproverLabel,
    bool IsActive,
    string NodeType = "step",
    int PosX = 0,
    int PosY = 0,
    string? NodeData = null);

public sealed record ApprovalFlowEdgeDto(
    int Id,
    int FlowId,
    int SourceStepId,
    int TargetStepId,
    string? Label,
    string EdgeKind,
    string? Condition,
    int SortOrder,
    string? SourceHandle = null,
    string? TargetHandle = null);

public sealed record ApprovalFlowVariableDto(
    int Id,
    int FlowId,
    string Name,
    string TypeCode,
    string? DefaultValue,
    string? Description,
    int SortOrder,
    string ValueSource = "manual",
    string? SqlQuery = null);

// ── Save isteği ───────────────────────────────────────────────────────────────
public sealed record SaveApprovalFlowRequest(
    int Id,
    string Name,
    string? Description,
    string DocumentKind,
    bool IsActive,
    IReadOnlyList<SaveApprovalFlowRuleRequest> Rules,
    IReadOnlyList<SaveApprovalFlowStepRequest> Steps,
    IReadOnlyList<SaveApprovalFlowEdgeRequest>? Edges = null,
    IReadOnlyList<SaveApprovalFlowVariableRequest>? Variables = null);

public sealed record SaveApprovalFlowRuleRequest(
    int Id,
    ApprovalRuleType RuleType,
    string? RuleValue,
    bool IsActive);

public sealed record SaveApprovalFlowStepRequest(
    int Id,
    int StepOrder,
    string StepName,
    ApproverType ApproverType,
    string? ApproverId,
    string? ApproverLabel,
    bool IsActive,
    string NodeType = "step",
    int PosX = 0,
    int PosY = 0,
    string? NodeData = null);

public sealed record SaveApprovalFlowEdgeRequest(
    int Id,
    int SourceStepClientId,  // designer client-side step id (yeni step'lerin Id=0)
    int TargetStepClientId,  // designer client-side step id
    string? Label,
    string EdgeKind,
    string? Condition,
    int SortOrder,
    string? SourceHandle = null,
    string? TargetHandle = null);

public sealed record SaveApprovalFlowVariableRequest(
    int Id,
    string Name,
    string TypeCode,         // int | bool | string | decimal | date
    string? DefaultValue,
    string? Description,
    int SortOrder,
    string ValueSource = "manual",
    string? SqlQuery = null);

// ── Onay örneği (Instance) ────────────────────────────────────────────────────
public sealed record ApprovalInstanceDto(
    int Id,
    int? DocumentId,
    string EntityKind,
    int FlowId,
    string FlowName,
    string Status,
    int CurrentStep,
    int TotalSteps,
    string? StartedBy,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? RejectNote,
    IReadOnlyList<ApprovalStepRecordDto> StepRecords);

public sealed record ApprovalStepRecordDto(
    int Id,
    int InstanceId,
    int StepOrder,
    string StepName,
    string Status,
    string? ApproverId,
    string? ApproverName,
    string? Note,
    DateTime? ActionDate,
    DateTime? DueDate = null,
    DateTime? SlaWarnedAt = null,
    DateTime? SlaActionAt = null,
    string? SlaActionType = null,
    DateTime? EnteredAt = null);

// ── SLA tarama icin worker DTO'su ──────────────────────────────────────────
// Adım NodeData JSON'undaki SLA ayarları repository tarafında parse edilip
// dispatch + aksiyon helper'larına flat olarak verilir.
public sealed record OverdueStepRecord(
    int InstanceId,
    int StepRecordId,
    int StepOrder,
    string StepName,
    DateTime? DueDate,
    string? ApproverId,
    string? ApproverName,
    int? DocumentId,
    string? DocumentNumber,
    string FlowName,
    bool SlaEnabled,
    int SlaHours,
    string SlaTimeUnit,
    string SlaAction,
    string? SlaEscalateToType,
    string? SlaEscalateToId,
    string? SlaEscalateToLabel,
    int SlaReminderHoursBefore,
    string? SlaMessageTemplate,
    string? SlaRejectReason);

// Timer node bekleme kaydı — SlaCheckerWorker tarafından pollanan pending timer.
public sealed record PendingTimerRecord(
    int RecordId,
    int InstanceId,
    int TimerNodeId,
    DateTime FireAt);

// ── Onay/Red isteği ───────────────────────────────────────────────────────────
public sealed record StartApprovalRequest(
    int? DocumentId,
    int FlowId,
    string StartedBy,
    int? StartedByUserId = null,
    string EntityKind = "Document");

public sealed record ApproveStepRequest(
    int InstanceId,
    string ApproverId,
    string ApproverName,
    string? Note,
    string? ChoiceArmId = null);

public sealed record RejectStepRequest(
    int InstanceId,
    string ApproverId,
    string ApproverName,
    string Note);

// ── Akış revizyon geçmişi ─────────────────────────────────────────────────────
public sealed record ApprovalFlowRevisionSummaryDto(
    int Id,
    int FlowId,
    string FlowName,
    int RevisionNo,
    string? CreatedBy,
    DateTime CreatedAt,
    int PendingCount);

public sealed record ApprovalFlowRevisionDetailDto(
    int Id,
    int RevisionNo,
    string? CreatedBy,
    DateTime CreatedAt,
    string Snapshot,
    IReadOnlyList<RevisionPendingInstanceDto> PendingInstances);

public sealed record RevisionPendingInstanceDto(
    int InstanceId,
    int? DocumentId,
    string? DocumentNumber,
    string? CurrentStepName,
    string? ApproverName,
    DateTime StartedAt);

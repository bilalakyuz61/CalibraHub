using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Onay akışı adımı — sıra, onaylayıcı tipi (herhangi kullanıcı / belirli kişi / departman) ve React Flow tasarımcı konumu.")]
public sealed class ApprovalFlowStepEntity
{
    public int Id { get; init; }
    public int FlowId { get; init; }
    public int StepOrder { get; init; }
    public required string StepName { get; init; }
    public ApproverType ApproverType { get; init; } = ApproverType.AnyUser;
    public string? ApproverId { get; init; }
    public string? ApproverLabel { get; init; }
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    // Visual designer (React Flow) destekli alanlar
    public string NodeType { get; init; } = "step"; // 'start' | 'step' | 'decision' | 'end'
    public int PosX { get; init; }
    public int PosY { get; init; }
    public string? NodeData { get; init; } // JSON (designer payload)
}

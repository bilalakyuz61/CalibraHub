using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Onay akışı tanımı — belge türüne göre adımları, onaylayıcı kurallarını ve karar mantığını içerir.")]
public sealed class ApprovalFlowEntity
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string DocumentKind { get; init; }
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
}

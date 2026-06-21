using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Onay akışı revizyon geçmişi — akış her güncellendiğinde önceki yapılandırmanın JSON snapshot'ı saklanır; çalışan instance'lar hangi revizyonla başlatıldıklarını buradan izler.")]
public sealed class ApprovalFlowRevision
{
    public int Id { get; init; }
    public int FlowId { get; init; }
    public int RevisionNo { get; init; }
    /// <summary>JSON snapshot of flow configuration at save time.</summary>
    public required string Snapshot { get; init; }
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
}

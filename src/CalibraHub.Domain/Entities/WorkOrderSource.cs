using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Uretim is emri ile kaynak sales order kalem(ler)i many-to-many baglantisi. Bolme: ayni SourceLineId, farkli WorkOrderId. Toplama: ayni WorkOrderId, farkli SourceLineId.")]
public sealed class WorkOrderSource
{
    public int Id { get; init; }
    public int WorkOrderId { get; init; }
    public int SourceDocumentId { get; init; }
    public int SourceLineId { get; init; }
    public decimal AllocatedQuantity { get; init; }
    public DateTime Created { get; init; }
}

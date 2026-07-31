using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>DÖF aksiyon satırı — bir Capa'ya bağlı planlanan/uygulanan tekil aksiyon.</summary>
[Description("DÖF aksiyon satırı — acil sınırlama / düzeltici / önleyici tekil aksiyon.")]
public sealed class CapaAction
{
    public int Id { get; init; }

    [Description("Bağlı DÖF kaydı. FK -> Capa.Id.")]
    public int CapaId { get; init; }

    public CapaActionType ActionType { get; set; } = CapaActionType.Duzeltici;

    public string Description { get; set; } = string.Empty;

    [Description("Sorumlu personel. FK -> Personnel.Id.")]
    public int? ResponsiblePersonnelId { get; set; }

    public DateTime? DueDate { get; set; }

    public CapaActionStatus Status { get; set; } = CapaActionStatus.Planlandi;

    public DateTime? CompletedAt { get; set; }

    public int OrderNo { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

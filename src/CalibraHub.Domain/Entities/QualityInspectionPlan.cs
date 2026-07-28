using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Muayene planı başlığı — bir malzeme (veya kalem grubu) için belirli muayene tipinde
/// hangi karakteristiklerin hangi toleransla kontrol edileceğini tanımlar (standalone tanım).
/// </summary>
[Description("Muayene planı (malzeme × muayene tipi × karakteristikler + tolerans).")]
public sealed class QualityInspectionPlan
{
    public int Id { get; init; }

    [Description("Plan bu malzeme için. FK -> Items.Id (nullable; grup bazlı plan için MaterialGroupId kullanılır).")]
    public int? ItemId { get; set; }

    [Description("Kalem grubu bazlı plan. FK -> MaterialGroups.Id (nullable).")]
    public int? MaterialGroupId { get; set; }

    public InspectionType InspectionType { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

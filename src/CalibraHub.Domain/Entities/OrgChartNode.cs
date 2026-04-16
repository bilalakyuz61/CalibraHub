using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class OrgChartNode : Entity
{
    public Guid ChartId { get; init; }
    public Guid UserId { get; init; }
    public Guid? ParentUserId { get; init; }
    public string? PositionTitle { get; init; }
    public int SortOrder { get; init; }
}

using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class OrgChart : EntityInt
{
    public int CompanyId { get; init; }
    public required string Name { get; set; }
    public bool IsDefault { get; private set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public int? CreatedById { get; init; }
    public int? UpdatedById { get; set; }

    public void MarkAsDefault() => IsDefault = true;
    public void ClearDefault() => IsDefault = false;

    public static IReadOnlyList<string> ValidateNodes(IReadOnlyCollection<OrgChartNode> nodes)
    {
        var warnings = new List<string>();
        var nodeIds = nodes.Select(n => n.Id).ToHashSet();

        if (!nodes.Any(n => n.ParentNodeId == null))
            warnings.Add("Şemada en az bir kök node olmalı.");

        var orphans = nodes.Count(n => n.ParentNodeId != null && !nodeIds.Contains(n.ParentNodeId.Value));
        if (orphans > 0)
            warnings.Add($"{orphans} orphan node tespit edildi (bağlı olmayan kayıtlar).");

        return warnings;
    }
}

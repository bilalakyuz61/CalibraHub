using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// OrgChart domain logic — cycle detection, reparent validation, schema validation.
/// Saf algoritma servisi: infrastructure bağımlılığı yok, unit test edilebilir.
/// </summary>
public sealed class OrgChartDomainService
{
    /// <summary>
    /// Verilen node'u newParentNodeId altına taşımak döngü yaratır mı?
    /// Self-move (nodeId == newParentNodeId) ve ancestor-taşıma döngü sayılır.
    /// </summary>
    public bool WouldCreateCycle(
        IReadOnlyCollection<OrgChartNode> allNodes,
        int nodeId,
        int? newParentNodeId)
    {
        if (newParentNodeId is null) return false;
        if (newParentNodeId == nodeId) return true;

        var parentMap = allNodes.ToDictionary(n => n.Id, n => n.ParentNodeId);

        // newParentNodeId'nin ata zincirini gez — nodeId'ye ulaşılırsa döngü var
        var current = newParentNodeId;
        var visited = new HashSet<int>();
        while (current is not null)
        {
            if (current == nodeId) return true;
            if (!visited.Add(current.Value)) break; // cycle already in tree (shouldn't happen)
            if (!parentMap.TryGetValue(current.Value, out var grandParent)) break;
            current = grandParent;
        }
        return false;
    }

    /// <summary>
    /// Şema bütünlük kontrolü — uyarı listesi döner (boşsa şema geçerli).
    /// </summary>
    public IReadOnlyList<string> Validate(IReadOnlyCollection<OrgChartNode> nodes)
    {
        var warnings = new List<string>();
        if (!nodes.Any()) return warnings;

        var nodeIds = nodes.Select(n => n.Id).ToHashSet();

        if (!nodes.Any(n => n.ParentNodeId is null))
            warnings.Add("Şemada en az bir kök node olmalı.");

        var orphans = nodes.Count(n => n.ParentNodeId is not null && !nodeIds.Contains(n.ParentNodeId.Value));
        if (orphans > 0)
            warnings.Add($"{orphans} orphan node tespit edildi (bağlı olmayan kayıtlar).");

        return warnings;
    }

    /// <summary>
    /// Belirtilen nodeId'nin alt ağacındaki tüm node ID'lerini döner (nodeId dahil).
    /// </summary>
    public IReadOnlySet<int> GetSubtree(IReadOnlyCollection<OrgChartNode> allNodes, int nodeId)
    {
        var childMap = allNodes
            .Where(n => n.ParentNodeId is not null)
            .GroupBy(n => n.ParentNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

        var result = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(nodeId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            result.Add(id);
            if (childMap.TryGetValue(id, out var children))
                foreach (var c in children) queue.Enqueue(c);
        }
        return result;
    }
}

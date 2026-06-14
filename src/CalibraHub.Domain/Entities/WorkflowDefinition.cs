namespace CalibraHub.Domain.Entities;

public sealed class WorkflowDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int? DocumentTypeId { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public bool IsPublished { get; set; }
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }

    private readonly List<WorkflowNode> _nodes = [];
    private readonly List<WorkflowTransition> _transitions = [];
    public IReadOnlyList<WorkflowNode> Nodes => _nodes;
    public IReadOnlyList<WorkflowTransition> Transitions => _transitions;

    public void AddNode(WorkflowNode node) => _nodes.Add(node);

    public void RemoveNode(int nodeId)
    {
        _nodes.RemoveAll(n => n.Id == nodeId);
        _transitions.RemoveAll(t => t.FromNodeId == nodeId || t.ToNodeId == nodeId);
    }

    public void Connect(WorkflowTransition transition) => _transitions.Add(transition);

    public void Disconnect(int transitionId) => _transitions.RemoveAll(t => t.Id == transitionId);

    public void Publish()
    {
        var errors = Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException($"Workflow yayınlanamaz: {string.Join("; ", errors)}");
        IsPublished = true;
        Updated = DateTime.UtcNow;
    }

    public WorkflowDefinition CreateNewVersion(int? createdBy = null) => new()
    {
        Name = Name,
        Description = Description,
        DocumentTypeId = DocumentTypeId,
        Version = Version + 1,
        IsActive = true,
        CreatedById = createdBy,
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (_nodes.Count == 0) { errors.Add("Workflow node içermiyor."); return errors; }

        var startCount = _nodes.Count(n => n.NodeType == Enums.WorkflowNodeType.Start);
        if (startCount == 0) errors.Add("En az bir Başlangıç node'u gerekli.");
        if (startCount > 1) errors.Add("Birden fazla Başlangıç node'u olamaz.");

        if (!_nodes.Any(n => n.NodeType == Enums.WorkflowNodeType.End))
            errors.Add("En az bir Bitiş node'u gerekli.");

        var nodeIds = _nodes.Select(n => n.Id).ToHashSet();
        var startNode = _nodes.FirstOrDefault(n => n.NodeType == Enums.WorkflowNodeType.Start);
        if (startNode is not null)
        {
            var reachable = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startNode.Id);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!reachable.Add(id)) continue;
                foreach (var t in _transitions.Where(t => t.FromNodeId == id && nodeIds.Contains(t.ToNodeId)))
                    queue.Enqueue(t.ToNodeId);
            }
            var unreachable = nodeIds.Except(reachable).Count();
            if (unreachable > 0) errors.Add($"{unreachable} ulaşılamayan node var.");
        }

        foreach (var decision in _nodes.Where(n => n.NodeType == Enums.WorkflowNodeType.Decision))
        {
            var outgoing = _transitions.Where(t => t.FromNodeId == decision.Id).ToList();
            if (!outgoing.Any(t => t.IsDefault))
                errors.Add($"Karar node'u '{decision.Name}' için varsayılan çıkış gerekli.");
        }

        return errors;
    }
}

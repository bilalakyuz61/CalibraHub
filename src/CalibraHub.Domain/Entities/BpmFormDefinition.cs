namespace CalibraHub.Domain.Entities;

public sealed class BpmFormDefinition
{
    public int     Id                   { get; set; }
    public string  Name                 { get; set; } = "";
    public string  Code                 { get; set; } = "";
    public string? Description          { get; set; }
    public int?    WorkflowDefinitionId { get; set; }
    public bool    IsActive             { get; set; } = true;
    public int?    CreatedById           { get; set; }
    public DateTime  Created            { get; set; } = DateTime.UtcNow;
    public int?    UpdatedById           { get; set; }
    public DateTime? Updated            { get; set; }

    private readonly List<BpmFormField> _fields = [];
    public IReadOnlyList<BpmFormField> Fields => _fields;

    public void AddField(BpmFormField field) => _fields.Add(field);

    public void RemoveField(int fieldId) =>
        _fields.RemoveAll(f => f.Id == fieldId);

    public void ReorderFields(IEnumerable<int> orderedIds)
    {
        var order = orderedIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        foreach (var f in _fields)
            if (order.TryGetValue(f.Id, out var idx))
                f.SortOrder = idx;
    }
}

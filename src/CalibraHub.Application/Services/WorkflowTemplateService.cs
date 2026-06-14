using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

/// <summary>
/// Hazır workflow şablonları — new definition oluştururken kullanıcıya sunulur.
/// </summary>
public sealed class WorkflowTemplateService(IWorkflowDefinitionRepository repo)
{
    public IReadOnlyList<(int Id, string Name, string Description)> GetTemplates() =>
    [
        (1, "Tutar Bazlı Onay",         "Belge tutarına göre standart/yönetici onayı yönlendirmesi"),
        (2, "Çoklu Grup Paralel Onay",   "İki paralel onay kolu ve AND-Join"),
        (3, "İhracat Eskalasyonu",       "Dövizli belgeler için ek dış ticaret onayı"),
    ];

    /// <summary>
    /// Şablon ID'sine göre hazır Definition + Node + Transition'ları oluşturur ve kaydeder.
    /// </summary>
    public async Task<int> ApplyTemplateAsync(int templateId, string name, int? actor, CancellationToken ct)
    {
        var def = templateId switch
        {
            1 => BuildAmountBasedTemplate(name),
            2 => BuildParallelGroupTemplate(name),
            3 => BuildExportEscalationTemplate(name),
            _ => throw new ArgumentException($"Bilinmeyen şablon: {templateId}"),
        };

        var defId = await repo.SaveDefinitionAsync(def, actor, ct);

        // placeholder node.Id → real DB Id
        var nodeIdMap = new Dictionary<int, int>();
        foreach (var node in def.Nodes)
        {
            var n = new WorkflowNode
            {
                DefinitionId       = defId,
                NodeType           = node.NodeType,
                Name               = node.Name,
                PositionX          = node.PositionX,
                PositionY          = node.PositionY,
                ActorType          = node.ActorType,
                ActorRefId         = node.ActorRefId,
                ActorExpression    = node.ActorExpression,
                TimeoutHours       = node.TimeoutHours,
                OnRejectPolicy     = node.OnRejectPolicy,
                JoinExpectedTokens = node.JoinExpectedTokens,
            };
            var realId = await repo.SaveNodeAsync(n, actor, ct);
            nodeIdMap[node.Id] = realId;
        }

        foreach (var t in def.Transitions)
        {
            if (!nodeIdMap.TryGetValue(t.FromNodeId, out var realFrom)) continue;
            if (!nodeIdMap.TryGetValue(t.ToNodeId,   out var realTo))   continue;
            var tr = new WorkflowTransition
            {
                DefinitionId = defId,
                FromNodeId   = realFrom,
                ToNodeId     = realTo,
                Label        = t.Label,
                Condition    = t.Condition,
                Priority     = t.Priority,
                IsDefault    = t.IsDefault,
            };
            await repo.SaveTransitionAsync(tr, actor, ct);
        }

        return defId;
    }

    // ── Template builders ─────────────────────────────────────────────────

    private static WorkflowDefinition BuildAmountBasedTemplate(string name)
    {
        var def = new WorkflowDefinition { Name = name, Description = "Tutar bazlı onay şablonu" };
        def.AddNode(new WorkflowNode { Id = 1, DefinitionId = 0, NodeType = WorkflowNodeType.Start,    Name = "Başlangıç",      PositionX = 100, PositionY = 100 });
        def.AddNode(new WorkflowNode { Id = 2, DefinitionId = 0, NodeType = WorkflowNodeType.Decision, Name = "Tutar Kontrolü",  PositionX = 300, PositionY = 100 });
        def.AddNode(new WorkflowNode { Id = 3, DefinitionId = 0, NodeType = WorkflowNodeType.Task,     Name = "Yönetici Onayı", PositionX = 500, PositionY = 50, ActorType = WorkflowActorType.Role, ActorRefId = "Manager" });
        def.AddNode(new WorkflowNode { Id = 4, DefinitionId = 0, NodeType = WorkflowNodeType.Task,     Name = "Standart Onay",  PositionX = 500, PositionY = 200, ActorType = WorkflowActorType.Role, ActorRefId = "Approver" });
        def.AddNode(new WorkflowNode { Id = 5, DefinitionId = 0, NodeType = WorkflowNodeType.End,      Name = "Bitiş",          PositionX = 700, PositionY = 100 });
        def.Connect(new WorkflowTransition { Id = 1, DefinitionId = 0, FromNodeId = 1, ToNodeId = 2, IsDefault = true });
        def.Connect(new WorkflowTransition { Id = 2, DefinitionId = 0, FromNodeId = 2, ToNodeId = 3, Condition = "Amount > 10000", Priority = 0 });
        def.Connect(new WorkflowTransition { Id = 3, DefinitionId = 0, FromNodeId = 2, ToNodeId = 4, IsDefault = true, Priority = 1 });
        def.Connect(new WorkflowTransition { Id = 4, DefinitionId = 0, FromNodeId = 3, ToNodeId = 5, IsDefault = true });
        def.Connect(new WorkflowTransition { Id = 5, DefinitionId = 0, FromNodeId = 4, ToNodeId = 5, IsDefault = true });
        return def;
    }

    private static WorkflowDefinition BuildParallelGroupTemplate(string name)
    {
        var def = new WorkflowDefinition { Name = name, Description = "İki paralel onay kolu" };
        def.AddNode(new WorkflowNode { Id = 1, DefinitionId = 0, NodeType = WorkflowNodeType.Start,         Name = "Başlangıç",    PositionX = 100, PositionY = 150 });
        def.AddNode(new WorkflowNode { Id = 2, DefinitionId = 0, NodeType = WorkflowNodeType.ParallelSplit,  Name = "Paralel Ayrım",PositionX = 250, PositionY = 150 });
        def.AddNode(new WorkflowNode { Id = 3, DefinitionId = 0, NodeType = WorkflowNodeType.Task,           Name = "Grup A Onayı", PositionX = 420, PositionY = 80, ActorType = WorkflowActorType.Role, ActorRefId = "GroupA" });
        def.AddNode(new WorkflowNode { Id = 4, DefinitionId = 0, NodeType = WorkflowNodeType.Task,           Name = "Grup B Onayı", PositionX = 420, PositionY = 220, ActorType = WorkflowActorType.Role, ActorRefId = "GroupB" });
        def.AddNode(new WorkflowNode { Id = 5, DefinitionId = 0, NodeType = WorkflowNodeType.ParallelJoin,   Name = "Paralel Birleşim", PositionX = 600, PositionY = 150, JoinExpectedTokens = 2 });
        def.AddNode(new WorkflowNode { Id = 6, DefinitionId = 0, NodeType = WorkflowNodeType.End,            Name = "Bitiş",        PositionX = 750, PositionY = 150 });
        def.Connect(new WorkflowTransition { Id = 1, DefinitionId = 0, FromNodeId = 1, ToNodeId = 2, IsDefault = true });
        def.Connect(new WorkflowTransition { Id = 2, DefinitionId = 0, FromNodeId = 2, ToNodeId = 3, IsDefault = false });
        def.Connect(new WorkflowTransition { Id = 3, DefinitionId = 0, FromNodeId = 2, ToNodeId = 4, IsDefault = false });
        def.Connect(new WorkflowTransition { Id = 4, DefinitionId = 0, FromNodeId = 3, ToNodeId = 5, IsDefault = true });
        def.Connect(new WorkflowTransition { Id = 5, DefinitionId = 0, FromNodeId = 4, ToNodeId = 5, IsDefault = true });
        def.Connect(new WorkflowTransition { Id = 6, DefinitionId = 0, FromNodeId = 5, ToNodeId = 6, IsDefault = true });
        return def;
    }

    private static WorkflowDefinition BuildExportEscalationTemplate(string name)
    {
        var def = new WorkflowDefinition { Name = name, Description = "İhracat eskalasyonu şablonu" };
        def.AddNode(new WorkflowNode { Id = 1, DefinitionId = 0, NodeType = WorkflowNodeType.Start,    Name = "Başlangıç",      PositionX = 100, PositionY = 150 });
        def.AddNode(new WorkflowNode { Id = 2, DefinitionId = 0, NodeType = WorkflowNodeType.Decision, Name = "İhracat mı?",    PositionX = 280, PositionY = 150 });
        def.AddNode(new WorkflowNode { Id = 3, DefinitionId = 0, NodeType = WorkflowNodeType.Task,     Name = "Dış Ticaret",    PositionX = 460, PositionY = 80, ActorType = WorkflowActorType.Role, ActorRefId = "ForeignTrade" });
        def.AddNode(new WorkflowNode { Id = 4, DefinitionId = 0, NodeType = WorkflowNodeType.Task,     Name = "Normal Onay",    PositionX = 460, PositionY = 230, ActorType = WorkflowActorType.Role, ActorRefId = "Approver" });
        def.AddNode(new WorkflowNode { Id = 5, DefinitionId = 0, NodeType = WorkflowNodeType.End,      Name = "Bitiş",          PositionX = 640, PositionY = 150 });
        def.Connect(new WorkflowTransition { Id = 1, DefinitionId = 0, FromNodeId = 1, ToNodeId = 2, IsDefault = true });
        def.Connect(new WorkflowTransition { Id = 2, DefinitionId = 0, FromNodeId = 2, ToNodeId = 3, Condition = "IsExport = true", Priority = 0 });
        def.Connect(new WorkflowTransition { Id = 3, DefinitionId = 0, FromNodeId = 2, ToNodeId = 4, IsDefault = true, Priority = 1 });
        def.Connect(new WorkflowTransition { Id = 4, DefinitionId = 0, FromNodeId = 3, ToNodeId = 5, IsDefault = true });
        def.Connect(new WorkflowTransition { Id = 5, DefinitionId = 0, FromNodeId = 4, ToNodeId = 5, IsDefault = true });
        return def;
    }
}

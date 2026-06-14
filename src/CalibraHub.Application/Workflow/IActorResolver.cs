using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Workflow;

/// <summary>
/// Bir WorkflowNode'un ActorType + ActorRefId/ActorExpression tanımından
/// çalışma zamanında atanacak kullanıcı ID'sini çözümler.
/// </summary>
public interface IActorResolver
{
    /// <summary>
    /// Resolved userId döner. Birden fazla aday varsa (Role / Department)
    /// ilk işlem yapan kilitler — şimdilik boş string döner, handler kendi lock'u alır.
    /// </summary>
    Task<string?> ResolveAsync(
        WorkflowNode            node,
        Dictionary<string, object?> context,
        CancellationToken       ct = default);
}

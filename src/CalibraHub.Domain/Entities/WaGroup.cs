namespace CalibraHub.Domain.Entities;

/// <summary>WhatsApp grup master kaydı.</summary>
public sealed class WaGroup
{
    public int Id { get; init; }
    public required string GroupJid { get; init; }
    public required string Subject { get; init; }
    public string? Description { get; init; }
    public int MemberCount { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
}

/// <summary>Grup üyesi kaydı.</summary>
public sealed class WaGroupMember
{
    public int Id { get; init; }
    public int GroupId { get; init; }
    public int? ContactId { get; init; }
    public required string Jid { get; init; }
    public string? Name { get; init; }
    public string Role { get; init; } = "member";
    public DateTime JoinedAt { get; init; }
    public DateTime? LeftAt { get; init; }
}

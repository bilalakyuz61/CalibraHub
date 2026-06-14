using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>WhatsApp grup ve üye CRUD.</summary>
public interface IWaGroupRepository
{
    /// <summary>GroupJid varsa döner, yoksa yeni satır ekler (MERGE).</summary>
    Task<WaGroup> GetOrCreateAsync(string groupJid, string subject, CancellationToken ct);

    /// <summary>Grup bilgisini günceller (subject, description, memberCount).</summary>
    Task UpdateAsync(string groupJid, string subject, string? description, int memberCount, CancellationToken ct);

    /// <summary>Tüm aktif grupların listesi.</summary>
    Task<IReadOnlyList<WaGroup>> GetAllAsync(CancellationToken ct);

    /// <summary>GroupJid ile tek grup.</summary>
    Task<WaGroup?> GetByJidAsync(string groupJid, CancellationToken ct);

    /// <summary>Bir grubun üyelerini toplu upsert eder (MERGE by Jid).</summary>
    Task UpsertMembersAsync(int groupId, IReadOnlyList<WaGroupMemberInput> members, CancellationToken ct);

    /// <summary>Belirtilen JID'leri gruba ekle (yoksa insert).</summary>
    Task AddMembersAsync(int groupId, IReadOnlyList<string> jids, CancellationToken ct);

    /// <summary>Belirtilen JID'leri gruptan çıkar (LeftAt set).</summary>
    Task RemoveMembersAsync(int groupId, IReadOnlyList<string> jids, CancellationToken ct);

    /// <summary>Bir grubun aktif üyeleri.</summary>
    Task<IReadOnlyList<WaGroupMember>> GetMembersAsync(int groupId, CancellationToken ct);
}

public sealed record WaGroupMemberInput(string Jid, string? Name, string Role = "member");

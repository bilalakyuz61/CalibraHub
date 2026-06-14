using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IWaContactRepository
{
    /// <summary>JID'e göre WaContact bul (WaContactJid tablosundan arama).</summary>
    Task<WaContact?> FindByJidAsync(string jid, CancellationToken ct);

    /// <summary>Telefon numarasına göre WaContact bul (PrimaryPhone).</summary>
    Task<WaContact?> FindByPhoneAsync(string phone, CancellationToken ct);

    /// <summary>Yeni WaContact oluştur, Id ile geri döner.</summary>
    Task<int> CreateAsync(WaContact contact, CancellationToken ct);

    /// <summary>WaContact güncelle (DisplayName, ProfilePicUrl, Updated vb.).</summary>
    Task UpdateAsync(WaContact contact, CancellationToken ct);

    /// <summary>Mevcut WaContact'a yeni bir JID ekle.</summary>
    Task AddJidAsync(WaContactJid jid, CancellationToken ct);

    /// <summary>wa_inbox'taki distinct contact_phone listesinden WaContact satırları üret (backfill).</summary>
    Task BackfillFromInboxAsync(CancellationToken ct);

    /// <summary>wa_inbox satırlarına contact_id'yi set et (WaContact.Id).</summary>
    Task LinkInboxContactIdsAsync(CancellationToken ct);
}

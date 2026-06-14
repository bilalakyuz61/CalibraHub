using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// WhatsApp inbox/outbox CRUD — Bridge polling tarafindan yazilir, UI tarafindan okunur.
/// </summary>
public interface IWaInboxRepository
{
    /// <summary>Bridge'ten gelen mesaji kaydet. UNIQUE constraint sayesinde dedup otomatik (catch swallow).</summary>
    Task<long?> InsertIfNotExistsAsync(WaInboxMessage message, CancellationToken cancellationToken);

    /// <summary>Sohbet listesi — her telefon icin son mesaj + okunmamis sayisi.</summary>
    Task<IReadOnlyList<WaConversationSummary>> GetConversationsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Bir sohbetin mesajlari (eski->yeni siralanir, en yenisi sonda).</summary>
    Task<IReadOnlyList<WaInboxMessage>> GetMessagesByPhoneAsync(string contactPhone, int limit, CancellationToken cancellationToken);

    /// <summary>Bir sohbetin tum gelen mesajlarini okundu isaretle.</summary>
    Task<int> MarkConversationReadAsync(string contactPhone, DateTime readAt, CancellationToken cancellationToken);

    /// <summary>En son mesajin received_at degerini getir — polling icin since cursor.</summary>
    Task<DateTime?> GetLastReceivedAtAsync(CancellationToken cancellationToken);

    /// <summary>Bir sohbetin TUM mesajlarini DB'den siler (UI'dan sohbet kartı silme butonu).</summary>
    Task<int> DeleteConversationAsync(string contactPhone, CancellationToken cancellationToken);

    /// <summary>media_path = NULL olan medya mesajlarini bul (backfill icin). Bridge restart sonrasi ./media/ kalmasa bile DB'den hangi msg id'lerin media oldugu bilinir.</summary>
    Task<IReadOnlyList<(long Id, string BridgeMsgId)>> GetMediaMessagesMissingFileAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Belirli msg id icin media_path/mime/filename/size guncelle (backfill).</summary>
    Task<int> UpdateMediaPathAsync(long id, string mediaPath, string? mediaMime, string? mediaFileName, int? mediaSize, CancellationToken cancellationToken);

    // ── Faz 3 ────────────────────────────────────────────────────────────

    /// <summary>BridgeMsgId üzerinden tek mesaj getir (reaksiyon/silme için).</summary>
    Task<WaInboxMessage?> GetByBridgeMsgIdAsync(string bridgeMsgId, CancellationToken cancellationToken);

    /// <summary>Mesajı silindi olarak işaretle (is_deleted=1, body=null).</summary>
    Task<int> MarkDeletedAsync(string bridgeMsgId, CancellationToken cancellationToken);

    /// <summary>Mesaja reaksiyon ekle/kaldır (null = reaksiyon kaldır).</summary>
    Task<int> UpdateReactionAsync(string bridgeMsgId, string? emoji, CancellationToken cancellationToken);

    /// <summary>Gönderilen mesaj için iletim durumunu güncelle (sent/delivered/read).</summary>
    Task<int> UpdateDeliveryStatusAsync(string bridgeMsgId, string status, CancellationToken cancellationToken);

    /// <summary>Sohbet içinde metin arama. Sadece body LIKE sorgusu.</summary>
    Task<IReadOnlyList<WaInboxMessage>> SearchMessagesAsync(string contactPhone, string query, int limit, CancellationToken cancellationToken);

    /// <summary>Sohbetin son gelen mesajını okunmamış olarak işaretle (read_at = NULL).</summary>
    Task<int> MarkUnreadAsync(string contactPhone, CancellationToken ct);
}

/// <summary>Sohbet listesi satiri — telefon/grup basina ozet.</summary>
public sealed record WaConversationSummary(
    string ContactPhone,
    int? ContactId,
    string? ContactName,
    string? AccountTitle,    // Contact tablosundan join
    string? AccountCode,
    string? WaName,          // Contact.WaName — varsa pushname'i ezer
    string? LastBody,
    string? LastMediaType,
    bool LastFromMe,
    DateTime LastAt,
    int UnreadCount,
    bool IsLid = false,
    // ── Faz 4: grup alanları ─────────────────────────────────────────────
    bool IsGroup = false,
    string? GroupJid = null,
    string? GroupSubject = null,
    int GroupMemberCount = 0);

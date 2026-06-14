namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Polling servisinin yeni mesaj/durum değişikliklerini bağlı UI istemcilerine
/// (SignalR hub) push etmesi için soyutlama. Web katmanında SignalR ile implemente edilir.
/// </summary>
public interface IWhatsAppRealTimeNotifier
{
    Task MessageReceivedAsync(
        string phone, string messageId, int direction,
        string? body, string? mediaType, bool hasMedia,
        string? mediaUrl, string? mediaMime, string? mediaFileName, int? mediaSize,
        DateTime at, CancellationToken ct = default);

    Task ConversationUpdatedAsync(string phone, CancellationToken ct = default);
    Task PresenceUpdatedAsync(string phone, string status, DateTime? lastSeen, CancellationToken ct = default);
    Task TypingUpdatedAsync(string phone, bool isTyping, CancellationToken ct = default);
    Task MessageStatusUpdatedAsync(string messageId, string phone, string status, CancellationToken ct = default);

    /// <summary>Karşı taraftan gelen reaksiyon güncellemesini UI'a push eder. emoji null = reaksiyon kaldırıldı.</summary>
    Task ReactionUpdatedAsync(string targetMsgId, string phone, string? emoji, CancellationToken ct = default);
}

using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CalibraHub.Web.Infrastructure.WhatsApp;

/// <summary>
/// IWhatsAppRealTimeNotifier → SignalR hub broadcast implementasyonu.
/// Singleton olarak kaydedilir; IHubContext&lt;WhatsAppHub&gt; de singleton'dır.
/// </summary>
public sealed class SignalRWhatsAppNotifier : IWhatsAppRealTimeNotifier
{
    private readonly IHubContext<WhatsAppHub> _hub;

    public SignalRWhatsAppNotifier(IHubContext<WhatsAppHub> hub)
    {
        _hub = hub;
    }

    public Task MessageReceivedAsync(
        string phone, string messageId, int direction,
        string? body, string? mediaType, bool hasMedia,
        string? mediaUrl, string? mediaMime, string? mediaFileName, int? mediaSize,
        DateTime at, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("MessageReceived", new
        {
            phone,
            id           = messageId,
            direction,
            body,
            mediaType,
            hasMedia,
            mediaUrl,
            mediaMime,
            mediaFileName,
            mediaSize,
            at           = DateTime.SpecifyKind(at, DateTimeKind.Utc),
        }, ct);

    public Task ConversationUpdatedAsync(string phone, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("ConversationUpdated", phone, ct);

    public Task PresenceUpdatedAsync(string phone, string status, DateTime? lastSeen, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("PresenceUpdated", new
        {
            phone,
            status,
            lastSeen = lastSeen.HasValue
                ? DateTime.SpecifyKind(lastSeen.Value, DateTimeKind.Utc)
                : (DateTime?)null,
        }, ct);

    public Task TypingUpdatedAsync(string phone, bool isTyping, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("TypingUpdated", new { phone, isTyping }, ct);

    public Task MessageStatusUpdatedAsync(string messageId, string phone, string status, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("MessageStatusUpdated", new { messageId, phone, status }, ct);

    public Task ReactionUpdatedAsync(string targetMsgId, string phone, string? emoji, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("ReactionUpdated", new { targetMsgId, phone, emoji }, ct);
}

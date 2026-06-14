using Microsoft.AspNetCore.SignalR;

namespace CalibraHub.Web.Hubs;

/// <summary>
/// WhatsApp mesajlaşma real-time hub'ı.
/// Sunucu → istemci event'leri:
///   MessageReceived(msg)        — yeni mesaj (gelen veya giden)
///   ConversationUpdated(phone)  — sohbet listesini yenile
///   TypingUpdated({phone, isTyping}) — yazıyor göstergesi
///   PresenceUpdated({phone, status, lastSeen}) — çevrimiçi durumu
///   MessageStatusUpdated({messageId, phone, status}) — okundu/iletildi tiki
/// </summary>
public sealed class WhatsAppHub : Hub
{
    // İstemci → sunucu metodları (ileride kullanım için şimdilik boş)
}

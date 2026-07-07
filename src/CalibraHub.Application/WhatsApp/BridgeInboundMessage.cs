using System.Text.Json.Serialization;

namespace CalibraHub.Application.WhatsApp;

/// <summary>
/// Bridge'in urettigi mesaj kaydi — hem GET /messages polling yanitinda
/// hem de bridge'in anlik POST push'unda (bridge-events) ayni schema kullanilir.
/// </summary>
public sealed class BridgeInboundMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>Tam JID: 905...@s.whatsapp.net veya 178...@lid.</summary>
    [JsonPropertyName("jid")]
    public string? Jid { get; set; }

    /// <summary>LID identifier mı (gerçek telefon numarası değil)?</summary>
    [JsonPropertyName("isLid")]
    public bool IsLid { get; set; }

    [JsonPropertyName("fromName")]
    public string? FromName { get; set; }

    [JsonPropertyName("fromMe")]
    public bool FromMe { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>Unix epoch milisaniye.</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("isMedia")]
    public bool IsMedia { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; }

    [JsonPropertyName("mediaMime")]
    public string? MediaMime { get; set; }

    [JsonPropertyName("mediaFileName")]
    public string? MediaFileName { get; set; }

    [JsonPropertyName("mediaSize")]
    public int? MediaSize { get; set; }

    /// <summary>Reaksiyon mesajı: hedef mesajın bridge ID'si.</summary>
    [JsonPropertyName("reactionTargetId")]
    public string? ReactionTargetId { get; set; }

    /// <summary>Alıntılı yanıt: alıntılanan mesajın bridge ID'si.</summary>
    [JsonPropertyName("quotedMsgId")]
    public string? QuotedMsgId { get; set; }

    /// <summary>Grup mesajı: grubun JID'i (@g.us). Null ise 1:1 sohbet.</summary>
    [JsonPropertyName("groupJid")]
    public string? GroupJid { get; set; }

    /// <summary>Grup mesajı: mesajı gönderen üyenin JID'i.</summary>
    [JsonPropertyName("senderJid")]
    public string? SenderJid { get; set; }

    /// <summary>Grup mesajı: mesajı gönderen üyenin adı (pushName).</summary>
    [JsonPropertyName("senderName")]
    public string? SenderName { get; set; }
}

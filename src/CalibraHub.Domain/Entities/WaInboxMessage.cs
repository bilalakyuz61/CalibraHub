using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// WhatsApp Bridge'ten gelen veya cihazimizdan giden birebir mesaj kaydi.
/// Grup mesajlari haric — sadece 1:1 sohbet.
/// </summary>
[Description("WhatsApp inbox/outbox — Bridge'ten poll'lanan birebir mesajlar (gruplar haric).")]
public sealed class WaInboxMessage
{
    public long Id { get; init; }

    /// <summary>whatsapp-web.js msg.id._serialized — dedup icin UNIQUE.</summary>
    public string? BridgeMsgId { get; init; }

    /// <summary>0 = gelen (incoming), 1 = giden (outgoing).</summary>
    public byte Direction { get; init; }

    /// <summary>Karsi tarafin telefonu — sadece rakam, '+' yok (orn: 905321234567).</summary>
    public required string ContactPhone { get; init; }

    /// <summary>Phone -> Contact eslestirmesi. Null ise henuz kayitli musteri degil.</summary>
    public int? ContactId { get; init; }

    /// <summary>WA pushname — sohbet listesinde gosterilecek ad icin fallback.</summary>
    public string? ContactName { get; init; }

    public string? Body { get; init; }

    /// <summary>chat | image | video | audio | document | sticker | location.</summary>
    public string? MediaType { get; init; }

    public bool HasMedia { get; init; }

    /// <summary>Bridge'in verdigi orijinal timestamp (UTC).</summary>
    public DateTime ReceivedAt { get; init; }

    /// <summary>DB'ye yazildigi an (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Kullanici sohbeti acinca isaretlenir; null ise okunmamis.</summary>
    public DateTime? ReadAt { get; init; }

    /// <summary>Sunucu disk path'i (orn. /uploads/whatsapp/2026/04/<id>.jpg). UI bu yolu direkt erisir.</summary>
    public string? MediaPath { get; init; }

    /// <summary>image/jpeg, video/mp4, application/pdf vb.</summary>
    public string? MediaMime { get; init; }

    /// <summary>Dokuman icin orijinal dosya adi (varsa).</summary>
    public string? MediaFileName { get; init; }

    /// <summary>Byte cinsinden boyut.</summary>
    public int? MediaSize { get; init; }

    /// <summary>
    /// contact_phone alanı bir LID identifier içeriyorsa true.
    /// LID'ler gerçek telefon numarası değil; UI'da "Bilinmeyen kişi" gösterilir.
    /// </summary>
    public bool IsLid { get; init; }

    // ── Faz 4: grup alanları ─────────────────────────────────────────────

    /// <summary>Grup JID'i (@g.us). Null ise 1:1 sohbet.</summary>
    public string? GroupJid { get; init; }

    /// <summary>Grupta mesajı gönderen üyenin JID'i.</summary>
    public string? SenderJid { get; init; }

    /// <summary>Grupta mesajı gönderen üyenin adı (pushName).</summary>
    public string? SenderName { get; init; }

    // ── Faz 3 alanları ───────────────────────────────────────────────────

    /// <summary>Alıntı/yanıt: üst mesajın BridgeMsgId'si. Null ise doğrudan mesaj.</summary>
    public string? QuotedMsgId { get; init; }

    /// <summary>Bu mesaja verilen emoji reaksiyonu (varsa). Birden fazla: virgülle ayrılır.</summary>
    public string? ReactionEmoji { get; init; }

    /// <summary>Mesaj karşı taraf tarafından silindiyse true; UI'da tombstone gösterir.</summary>
    public bool IsDeleted { get; init; }

    /// <summary>Gönderilen mesaj için iletim durumu: sent | delivered | read.</summary>
    public string? DeliveryStatus { get; init; }
}

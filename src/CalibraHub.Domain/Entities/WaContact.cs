namespace CalibraHub.Domain.Entities;

/// <summary>
/// WhatsApp contact master kaydı. Her gerçek kişi için bir satır;
/// aynı kişinin farklı JID'leri (telefon JID + LID + diğer cihazlar) WaContactJid tablosunda tutulur.
/// </summary>
public sealed class WaContact
{
    public int Id { get; init; }

    /// <summary>E.164 benzeri normalize telefon (sadece rakam, '+' yok). LID-only ise NULL.</summary>
    public string? PrimaryPhone { get; set; }

    /// <summary>WhatsApp pushName veya CalibraHub kişi rehberinden gelen görünen ad.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Profil fotoğrafı URL'si (Bridge'ten çekilir).</summary>
    public string? ProfilePicUrl { get; set; }

    /// <summary>Son görülme zamanı (UTC).</summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>Anlık durum: online | offline | composing | recording</summary>
    public string? PresenceStatus { get; set; }

    /// <summary>CalibraHub Contact tablosundaki karşılık (müşteri/tedarikçi eşleşmesi).</summary>
    public int? LinkedContactId { get; set; }

    public bool IsBlocked { get; set; }
    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

/// <summary>
/// Bir WaContact'a ait JID'ler (telefon JID, LID, vb.).
/// Aynı kişi birden fazla JID ile gelebilir.
/// </summary>
public sealed class WaContactJid
{
    public int Id { get; init; }
    public int ContactId { get; init; }

    /// <summary>Tam JID: 905xxx@s.whatsapp.net | xxx@lid | xxx@g.us</summary>
    public required string Jid { get; init; }

    /// <summary>phone | lid | group | broadcast</summary>
    public required string JidType { get; init; }

    /// <summary>Bu JID ana JID mi (primary)?</summary>
    public bool IsPrimary { get; set; }

    public DateTime Created { get; init; } = DateTime.UtcNow;
}

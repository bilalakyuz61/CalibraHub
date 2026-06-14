using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Uygulama ici bildirim kaydi. Worker veya servisler tarafindan olusturulur,
/// Shell navbar'daki bell dropdown tarafindan listelenir.
/// </summary>
public sealed class UserNotification : EntityInt
{
    public int CompanyId { get; init; }
    public int UserId { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;

    /// <summary>Basliki (bildirimde kalin kisim).</summary>
    public required string Title { get; init; }

    /// <summary>Aciklama / body metni (opsiyonel).</summary>
    public string? Body { get; init; }

    /// <summary>Kaynak tipi — "NoteReminder", "Share", vb. Filtre/icon icin.</summary>
    public string? SourceType { get; init; }

    /// <summary>Kaynak kayit id'si — notu actirma gibi deep link icin.</summary>
    public int? SourceId { get; init; }

    /// <summary>Opsiyonel deep link URL — tiklayinca gidecegi sayfa.</summary>
    public string? Link { get; init; }

    public bool IsRead { get; private set; }
    public DateTime? ReadAt { get; private set; }

    public void MarkRead(DateTime readAt)
    {
        IsRead = true;
        ReadAt = readAt;
    }
}

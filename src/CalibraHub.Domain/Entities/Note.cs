using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class Note : Entity
{
    public int CompanyId { get; init; }
    public Guid UserId { get; init; }
    public Guid? FolderId { get; set; }
    public required string Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// Mod 2: Not tamamen client-side sifrelenmis mi? true ise <see cref="Content"/>
    /// alani JSON-wrap'li ciphertext'tir (format: {"v":1,"ct":"...","iv":"...","salt":"..."}).
    /// Yalnizca kullanici parolasi ile cozulebilir; sunucu icerigi asla goremez.
    /// </summary>
    public bool IsFullyEncrypted { get; set; }

    /// <summary>
    /// Sifre ipucu (kullanicinin parolasini hatirlamasina yardimci olmak icin).
    /// Plain text olarak saklanir; hassas sey yazilmamasi beklenir.
    /// </summary>
    public string? EncryptionHint { get; set; }

    public void MarkDeleted() => IsDeleted = true;
}

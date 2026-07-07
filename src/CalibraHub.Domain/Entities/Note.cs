using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Kullanici notlari — opsiyonel E2E sifreleme destegi (IsFullyEncrypted=true ise icerigi sadece kullanici parolasi ile cozulebilir, sunucu icerigi asla goremez). Klasorler, hatirlaticilar, paylasimlar ve ekler ayri tablolardadir.")]
public sealed class Note : Entity
{
    public int CompanyId { get; init; }
    public int UserId { get; init; }
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

    /// <summary>
    /// Virgul ile ayrilmis etiketler (orn. "proje,toplanti,onemli").
    /// Max 500 karakter; NULL ise etiket yok.
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>Bagli kayit tipi (orn. "Personnel", "Machine", "Contact", "Document").</summary>
    public string? LinkedEntityType { get; set; }

    /// <summary>Bagli kayit ID'si (entity tablosundaki PK).</summary>
    public int? LinkedEntityId { get; set; }

    /// <summary>Bagli kayitin gorunum etiketi (orn. ad, belge numarasi).</summary>
    public string? LinkedEntityLabel { get; set; }

    /// <summary>Gorunurluk: 0=Private (sadece sahip), 1=Company (sirket geneli).</summary>
    public int Visibility { get; set; } // 0=Private, 1=Company

    /// <summary>
    /// Genel link paylaşımı token'ı — 32 hex char (128-bit random).
    /// NULL ise henüz token üretilmemiş; IsPublic=true ise link aktif.
    /// </summary>
    public string? ShareToken { get; set; }

    /// <summary>true ise login olmadan <see cref="ShareToken"/> ile not okunabilir.</summary>
    public bool ShareIsPublic { get; set; }

    /// <summary>true ise genel link ile paylaşımda ekler de indirilebilir.</summary>
    public bool ShareIncludeAttachments { get; set; }

    /// <summary>
    /// Not gövdesindeki görsellerden Windows OCR ile çıkarılan metin.
    /// Arama sorgularında <see cref="Content"/> ile birlikte taranır.
    /// E2E şifreli notlarda (IsFullyEncrypted=true) her zaman NULL'dır.
    /// </summary>
    public string? OcrText { get; set; }

    /// <summary>
    /// Not listesi kartlarında gösterilen kısa düz-metin özet (~300 karakter).
    /// İçerik at-rest şifreli olduğundan SQL'de üretilemez; kayıt anında HTML'den
    /// çıkarılır ve Protect'li saklanır. E2E şifreli notlarda NULL'dır.
    /// </summary>
    public string? Snippet { get; set; }

    public void MarkDeleted() => IsDeleted = true;
}

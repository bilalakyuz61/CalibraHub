namespace CalibraHub.Domain.Entities;

/// <summary>
/// Sayfa-içi Yorum sistemi — bir ekran (ScreenKey) üzerinde bırakılan geri bildirim/iş kalemi.
/// Katman-4 ajan döngüsünün okuma yüzeyi <see cref="Status"/>='approved' kayıtlardır.
///
/// <see cref="Id"/> istemci tarafında üretilen GUID string'idir (server IDENTITY değil) —
/// çevrimdışı/offline-first akışta istemci kendi kimliğini üretip senkronize edebilir.
/// <see cref="Seq"/> sunucu IDENTITY sıra no'dur, stabil listeleme/sıralama için kullanılır.
///
/// Per-company DB'de yaşar — CompanyId kolonu yoktur (tablo zaten ait olduğu DB'dedir).
/// Şema pc-db tarafından kurulur; bu proje şemaya sadece kodlar (migration YAZMAZ).
/// </summary>
public sealed class PageComment
{
    /// <summary>İstemci tarafında üretilen benzersiz kimlik (GUID string) — PK.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Sunucu IDENTITY sıra no — UNIQUE, stabil sıralama için.</summary>
    public int Seq { get; set; }

    public string ScreenKey { get; set; } = string.Empty;
    public string? PageTitle { get; set; }
    public string? FormCode { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Author { get; set; }

    /// <summary>'approved' | 'applied' | 'abandoned' | 'revised' — DB DEFAULT 'approved'.</summary>
    public string Status { get; set; } = "approved";

    public string? AppliedVersion { get; set; }
    public string? ResolvedBy { get; set; }

    /// <summary>ISO 8601 string — DATETIME DEĞİL (durum çözümleme zaman damgası, sunucu üretir).</summary>
    public string? ResolvedAt { get; set; }

    public string? DevNote { get; set; }

    /// <summary>
    /// İstemcinin kayıt anındaki zaman damgası (ISO string) — sunucunun <see cref="Created"/>
    /// kolonundan bağımsız, offline-first senaryoda gerçek olay zamanını korur.
    /// </summary>
    public string? ClientCreatedAt { get; set; }

    public DateTime Created { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? Updated { get; set; }
    public string? UpdatedBy { get; set; }
}

namespace CalibraHub.Domain.Entities;

public sealed class DocLayout
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// Legacy string code (örn. "satis_teklifi"). Backward-compat icin korunur —
    /// yeni kayitlar DocumentTypeId kullanir; DocType DocumentType.Code'dan turetilir.
    /// "custom" tasarimlar icin NULL (DocumentTypeId NULL ile birlikte) olabilir.
    /// </summary>
    public string? DocType { get; set; }

    /// <summary>
    /// FK to DocumentType. Document.DocumentTypeId ile hizalanan ID-tabanli referans.
    /// NULL = "custom" tasarim (belirli bir tipe bagli degil).
    /// </summary>
    public int? DocumentTypeId { get; set; }

    public string? Description { get; set; }
    public required string LayoutJson { get; set; }
    public decimal PageW { get; set; } = 210m;
    public decimal PageH { get; set; } = 297m;
    public decimal MarginTop { get; set; } = 10m;
    public decimal MarginBot { get; set; } = 10m;
    public decimal MarginLeft { get; set; } = 15m;
    public decimal MarginRight { get; set; } = 10m;
    public int OwnerUserId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>"pdf" (default) | "email" — render hedefi.</summary>
    public string OutputFormat { get; set; } = "pdf";

    /// <summary>Mail sablonlarinda Compose ekraninda otomatik dolan varsayilan konu (legacy).</summary>
    public string? DefaultSubject { get; set; }

    /// <summary>Mail sablonlarinda Compose ekraninda otomatik dolan varsayilan govde metni (legacy).</summary>
    public string? DefaultBody { get; set; }

    /// <summary>Mail sablonu varsayilanlari icin SQL view adi (orn. CBV_MAIL_DEFAULTS_001).</summary>
    public string? DefaultsViewName { get; set; }

    /// <summary>View'da hangi kolonun mail konusu olarak okunacagi.</summary>
    public string? DefaultsSubjectColumn { get; set; }

    /// <summary>View'da hangi kolonun mail govdesi olarak okunacagi.</summary>
    public string? DefaultsBodyColumn { get; set; }

    /// <summary>Opsiyonel WHERE sarti (orn. Lang = 'tr'). Kullanici yazar, validasyondan gecer.</summary>
    public string? DefaultsWhere { get; set; }

    /// <summary>
    /// 2026-05-20: Bu dizayn mail compose ekraninda mail sablonu olarak da listelensin mi?
    /// Eski "OutputFormat=email" akisi kaldirildi — dizayn her zaman belge turune
    /// bagli standart bir DocLayout'tur; mail kullanimi bu bayrakla acilir/kapanir.
    /// </summary>
    public bool UseAsMailTemplate { get; set; }
}

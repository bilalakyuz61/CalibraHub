namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir sablonun kullandigi SQL view data source tanimi.
/// Bir sablonun birden fazla source'u olabilir — primary (belge) + detail (kombinasyon) + sibling (cari bakiye) gibi.
///
/// FastReport Designer'da sol panelde her source ayri bir node olarak gorunur,
/// kullanici [Belge.X], [Kombinasyon.X], [Cari.X] gibi farkli isimlerle alan referansi yazabilir.
///
/// Rendering sirasinda:
///   - Primary source: WHERE [KeyColumn] = @RecordId
///   - Detail source:  WHERE [KeyColumn] IN (primary'den ParentKeyColumn degerleri)
///   - Sibling source: WHERE [KeyColumn] = (primary'den ParentKeyColumn degeri — scalar)
/// </summary>
public sealed class ReportTemplateSource
{
    public int Id { get; init; }

    /// <summary>Parent ReportTemplate.Id.</summary>
    public int TemplateId { get; init; }

    /// <summary>Frx icinde kullanilan isim (source identifier). [Belge.X] icin "Belge", [Kombinasyon.X] icin "Kombinasyon".</summary>
    public required string SourceName { get; init; }

    /// <summary>SQL view adi. Yalnizca vw_ onekli, sadece [A-Za-z0-9_] karakterler.</summary>
    public required string ViewName { get; init; }

    /// <summary>
    /// recordId (veya parent kolon degeri) ile eslesecek view kolonu.
    /// Primary source icin docType.RequiredKeyColumn ile ayni olmali.
    /// </summary>
    public required string KeyColumn { get; init; }

    /// <summary>Master-detail iliski icin: hangi parent source'a bagli (null ise primary).</summary>
    public string? ParentSourceName { get; init; }

    /// <summary>Parent source'taki eslesecek kolon (ornek: Belge.KalemId).</summary>
    public string? ParentKeyColumn { get; init; }

    /// <summary>
    /// Primary mi? Her sablonda tek bir primary olmali — recordId bu source'un KeyColumn'u ile eslesir.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>Listelenme/inject sirasi.</summary>
    public int DisplayOrder { get; init; }

    /// <summary>
    /// Generation sirasinda bu source'un sorgusuna eklenecek opsiyonel siralama kolonu.
    /// View'in INFORMATION_SCHEMA'sindan kullanici secer. NULL ise siralama uygulanmaz.
    /// </summary>
    public string? SortColumn { get; init; }

    /// <summary>"ASC" veya "DESC". SortColumn dolu degilse anlamsiz.</summary>
    public string? SortDirection { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

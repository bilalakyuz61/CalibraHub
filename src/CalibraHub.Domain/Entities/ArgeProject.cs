using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// AR-GE proje yasam dongusu companion kaydi. Bir AR-GE projesi fiziksel olarak
/// 'arge_proje' tipinde bir Document satiridir; bu tablo o belgeye 1-1 baglanir
/// (DocumentId UNIQUE) ve AR-GE'ye ozgu tipli alanlari tutar.
///
/// Statu tek otorite ArgeProject.Status'tedir — Document.Status AR-GE akisinda
/// kullanilmaz. Soft-delete tek otorite Document.IsActive'dir; bu tabloda ayri
/// IsActive YOKTUR (Document soft-delete edilir, companion JOIN ile filtrelenir).
/// </summary>
[Description("AR-GE proje companion kaydi (Document 'arge_proje' ile 1-1). Statu/sorumlu/hedef/ilerleme burada; statu tek otorite ArgeProject.Status'tedir.")]
public sealed class ArgeProject
{
    public int Id { get; init; }

    [Description("Bagli proje belgesi. FK -> Document.Id (1-1, UNIQUE).")]
    public int DocumentId { get; init; }

    [Description("Proje adi (kullaniciya gosterilen baslik). Document'a dokunmamak icin companion'da tutulur.")]
    public required string Name { get; set; }

    [Description("AR-GE yasam dongusu durumu (tek otorite).")]
    public ArgeProjectStatus Status { get; set; } = ArgeProjectStatus.Planning;

    [Description("Proje turu — AR-GE veya ÜR-GE (tek board'da ayrac).")]
    public ArgeProjectType ProjectType { get; set; } = ArgeProjectType.ArGe;

    [Description("Proje sorumlusu. FK -> Personnel.Id.")]
    public int? OwnerPersonnelId { get; set; }

    [Description("Hedef bitis tarihi.")]
    public DateTime? TargetDate { get; set; }

    [Description("Tahmini ilerleme yuzdesi (0-100).")]
    public decimal ProgressPercent { get; set; }

    [Description("Proje aciklamasi / kapsam. Kartta gosterilir.")]
    public string? Description { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

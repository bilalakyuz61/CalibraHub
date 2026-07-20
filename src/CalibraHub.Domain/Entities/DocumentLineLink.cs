using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Birlesik kalem-eslesme kaydi: bir kaynak DocumentLine satirinin ne kadari hangi hedefe (satir/belge/is emri) baglandi. Yon her zaman kaynak -> hedef. FAZ 0 ISKELET (DocumentLineLink-Tasarim.md) — hicbir servis/repo bu tabloyu henuz okumuyor/yazmiyor; sema CalibraDatabaseInitializer'a ayrica ekleniyor.")]
public sealed class DocumentLineLink
{
    public int Id { get; init; }

    [Description("Iliski turu — CalibraHub.Application.Contracts.LinkType enum degerinin sayisal karsiligi (TINYINT). Domain katmani Application'a bagimli olamadigindan byte olarak tutulur; DocumentLine.MovementType ile ayni desen.")]
    public byte LinkType { get; init; }

    [Description("Kaynak satir. FK -> DocumentLine.Id.")]
    public int SourceLineId { get; init; }

    [Description("Kaynak satirin bagli oldugu belge. FK -> Document.Id (denormalize; hizli filtre, join'siz).")]
    public int SourceDocId { get; init; }

    [Description("Hedef satir. FK -> DocumentLine.Id (irsaliye/cikis satiri gibi). Is emri tahsis link'lerinde (WorkOrderAlloc) NULL kalir.")]
    public int? TargetLineId { get; init; }

    [Description("Hedef belge. FK -> Document.Id. Is emri link'lerinde WorkOrder.DocumentId ile doldurulur.")]
    public int? TargetDocId { get; init; }

    [Description("Hedef is emri. FK -> WorkOrder.Id. Yalniz is emri link turlerinde dolu (ayri IDENTITY uzayi).")]
    public int? TargetWorkOrderId { get; init; }

    public decimal Quantity { get; init; }

    public string? Notes { get; init; }

    public bool IsActive { get; init; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
}

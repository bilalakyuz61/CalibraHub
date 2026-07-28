using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Muayene kaydı companion — 'muayene' tipinde bir Document'a 1-1 bağlı (DocumentId UNIQUE).
/// Numara (MUY-...) + audit Document altyapısından gelir; Document.Status hep Draft, otorite
/// companion.Status'te (ArgeProject deseni). Verdict ölçüm satırlarından otomatik türetilir;
/// Disposition uygunsuzlukta girilen KARAR kaydıdır — hiçbir stok hareketi/blok tetiklemez.
/// </summary>
[Description("Muayene kaydı (Document 'muayene' ile 1-1). Verdict ölçümlerden otomatik; Disposition yalnız karar kaydı (stok aksiyonu yok).")]
public sealed class QualityInspection
{
    public int Id { get; init; }

    [Description("Bağlı muayene belgesi. FK -> Document.Id (1-1, UNIQUE).")]
    public int DocumentId { get; init; }

    [Description("Uygulanan plan (varsa). FK -> QualityInspectionPlan.Id.")]
    public int? PlanId { get; set; }

    [Description("Muayene edilen malzeme. FK -> Items.Id.")]
    public int? ItemId { get; set; }

    public InspectionType InspectionType { get; set; }

    [Description("Yaşam döngüsü durumu (tek otorite).")]
    public InspectionStatus Status { get; set; } = InspectionStatus.Draft;

    [Description("Genel karar — ölçüm satırlarından otomatik hesaplanır (Tamamlandı'da dolar).")]
    public InspectionVerdict? Verdict { get; set; }

    [Description("Uygunsuzlukta verilen karar (Kabul/Ret/Yeniden İşlem/Sapma İzni). Yalnız kayıt — stok aksiyonu YOK.")]
    public InspectionDisposition? Disposition { get; set; }

    [Description("İzlenebilirlik referansı türü: 'DeliveryLine' / 'WorkOrderOperation' / 'Lot' (salt-okunur, tetikleme yok).")]
    public string? SourceKind { get; set; }

    [Description("İzlenebilirlik referansı Id'si.")]
    public int? SourceId { get; set; }

    [Description("Muayene edilen miktar.")]
    public decimal? Quantity { get; set; }

    [Description("Muayene eden personel. FK -> Personnel.Id.")]
    public int? InspectedByPersonnelId { get; set; }

    public DateTime? InspectedAt { get; set; }

    public string? Notes { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

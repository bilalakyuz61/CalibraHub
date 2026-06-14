using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Varlık geçmiş kaydı — bir varlığa (AssetId FK) ait bakım, kalibrasyon, onarım, muayene, hareket/zimmet veya durum değişikliği olayı. Master-detail: Asset = master, AssetEvent = detay.")]
public sealed class AssetEvent
{
    public int Id { get; init; }
    public int CompanyId { get; init; }

    /// <summary>FK Asset.Id — bağlı olduğu varlık.</summary>
    public int AssetId { get; init; }

    public AssetEventType EventType { get; init; }
    public DateTime EventDate { get; init; }

    /// <summary>FK Personnel.Id — işlemi yapan personel (opsiyonel, iç kaynak).</summary>
    public int? PerformedByPersonnelId { get; init; }
    /// <summary>İşlemi yapan serbest metin (dış firma/servis sağlayıcı).</summary>
    public string? PerformedByText { get; init; }

    public decimal? Cost { get; init; }
    public AssetEventResult Result { get; init; } = AssetEventResult.None;
    public string? Notes { get; init; }

    /// <summary>Bu olaydan sonraki planlı tarih (örn. sonraki kalibrasyon). Asset.Next*Date güncellemesinde kullanılır.</summary>
    public DateTime? NextDueDate { get; init; }

    /// <summary>Sertifika/belge bağlantısı (opsiyonel URL).</summary>
    public string? DocumentUrl { get; init; }

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
    public int? CreatedById { get; init; }
    public int? UpdatedById { get; init; }
}

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Item kombinasyon kaydi. Eski [ProductConfiguration] tablosunun yeni adi [ItemConfiguration].
/// RecordType artik default 'CONFIG' (hesaplanmis); VALUE/FEATURE_STOCK kayitlari
/// FeatureValue/ItemFeatureMappings tablolarina tasindi. Eski sahalar (RecordCode, DataType,
/// VisibleInDesign) bos/null kalir — service katmaninda kombinasyon ayristirma icin
/// sadece Id/ParentId/ItemId/RecordName kullanilir.
/// </summary>
public sealed class ItemConfiguration
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public int? ItemId { get; init; }
    public string RecordType { get; init; } = "CONFIG";
    public string RecordCode { get; init; } = string.Empty;
    public required string RecordName { get; init; }
    public string? DataType { get; init; }
    /// <summary>Eski kolon — geriye donuk uyumluluk; Items.Code lookup'i ile doldurulur.</summary>
    public string? RelatedMaterialCode { get; init; }
    public bool IsActive { get; init; } = true;
    public bool VisibleInDesign { get; init; } = true;
    public DateTime CreatedDate { get; init; } = DateTime.Now;
}

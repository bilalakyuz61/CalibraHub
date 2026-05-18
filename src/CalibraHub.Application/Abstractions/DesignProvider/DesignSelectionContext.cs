namespace CalibraHub.Application.Abstractions.DesignProvider;

/// <summary>
/// Bir belge basılırken hangi tasarımın seçileceğini etkileyen kriterleri
/// taşıyan değer nesnesi. Yeni kriter eklemek için bu sınıfa nullable property
/// ekle + ilgili IDesignCriterion implementasyonunu DI'a kaydet.
/// </summary>
public sealed class DesignSelectionContext
{
    public required string DocType { get; init; }

    public int?  CustomerId     { get; init; }
    public int?  ContactGroupId { get; init; }
    public Guid? UserId         { get; init; }
    public int?  BranchId       { get; init; }
    public int?  WarehouseId    { get; init; }

    // İleride yeni kriter: public int? RegionId { get; init; } gibi serbestçe eklenebilir.
}

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
    public int?  UserId         { get; init; }
    public int?  BranchId       { get; init; }
    public int?  WarehouseId    { get; init; }
    /// <summary>
    /// Cari tipi kısıtı (ContactType enum'un byte değeri).
    /// NULL → wildcard (tüm cari tipleri için geçerli).
    /// </summary>
    public byte? AccountType    { get; init; }
}

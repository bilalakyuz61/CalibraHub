using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ILogisticsConfigurationService
{
    Task<LogisticsConfigurationSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<ItemDto> Items, int TotalCount)> GetItemsPagedAsync(string? search, int offset, int pageSize, CancellationToken cancellationToken, string? groupCode = null);
    Task<ProductConfigurationSnapshotDto> GetProductConfigurationSnapshotAsync(CancellationToken cancellationToken);
    Task<MaterialCardDynamicSchemaDto> GetMaterialCardDynamicSchemaAsync(CancellationToken cancellationToken);
    Task SaveFieldGroupAsync(
        SaveFieldGroupRequest request,
        CancellationToken cancellationToken);
    Task SaveMaterialCardDynamicFieldAsync(
        SaveMaterialCardDynamicFieldRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FieldDto>> GetFieldsAsync(CancellationToken cancellationToken);
    Task SaveFieldsAsync(
        IReadOnlyCollection<SaveFieldRequest> requests,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<UnitDto>> GetUnitsAsync(CancellationToken cancellationToken);
    Task CreateItemAsync(CreateItemRequest request, CancellationToken cancellationToken);
    Task UpdateItemAsync(UpdateItemRequest request, CancellationToken cancellationToken);
    Task DeactivateItemAsync(int stockCardId, CancellationToken cancellationToken);
    Task CreateLocationAsync(CreateLocationRequest request, CancellationToken cancellationToken);
    Task UpdateLocationAsync(UpdateLocationRequest request, CancellationToken cancellationToken);
    Task DeleteLocationAsync(int locationId, CancellationToken cancellationToken);

    // ── Machine (uretim/depo makineleri) ─────────────────────────────
    Task<IReadOnlyCollection<MachineDto>> GetMachinesAsync(CancellationToken cancellationToken);
    Task<int> CreateMachineAsync(CreateMachineRequest request, CancellationToken cancellationToken);
    Task UpdateMachineAsync(UpdateMachineRequest request, CancellationToken cancellationToken);
    Task DeleteMachineAsync(int machineId, CancellationToken cancellationToken);

    Task CreateUnitAsync(CreateUnitRequest request, CancellationToken cancellationToken);
    Task UpdateUnitAsync(UpdateUnitRequest request, CancellationToken cancellationToken);
    Task DeleteUnitAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemUnitDto>> GetItemUnitsAsync(int itemId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<int>> GetUsedFeatureIdsInCombinationsAsync(int itemId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<(int FeatureId, int ValueId)>> GetUsedFeatureValueIdsInCombinationsAsync(int itemId, CancellationToken cancellationToken);
    Task<int> GetCombinationCountForItemAsync(int itemId, CancellationToken cancellationToken);
    Task SaveItemUnitsAsync(int itemId, IReadOnlyCollection<SaveItemUnitItem> items, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemLocationDto>> GetItemLocationsAsync(int itemId, CancellationToken cancellationToken);
    Task SaveItemLocationsAsync(int itemId, IReadOnlyCollection<SaveItemLocationItem> items, CancellationToken cancellationToken);
    // Planlama: belge bazında malzeme kilidi
    Task<IReadOnlyCollection<string>> GetItemDocumentLocksAsync(int itemId, CancellationToken cancellationToken);
    Task SaveItemDocumentLocksAsync(int itemId, IReadOnlyCollection<string> docTypes, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<int>> GetLockedItemIdsByDocTypeAsync(string docType, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LocationTypeDto>> GetLocationTypesAsync(CancellationToken cancellationToken);
    Task<int> SaveLocationTypeAsync(SaveLocationTypeRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> DeleteLocationTypeAsync(int id, CancellationToken cancellationToken);
    Task ConfigureItemAsync(ConfigureItemRequest request, CancellationToken cancellationToken);
    Task CreatePropertyAsync(CreateFeatureRequest request, CancellationToken cancellationToken);
    Task CreateStockPropertyLinkAsync(CreateItemPropertyLinkRequest request, CancellationToken cancellationToken);
    Task CreatePropertyValueAsync(CreateFeatureValueRequest request, CancellationToken cancellationToken);
    Task CreateStockPropertyMappingAsync(CreateItemFeatureMappingRequest request, CancellationToken cancellationToken);
    Task<int> CreateProductConfigurationFeatureAsync(
        CreateProductConfigurationFeatureRequest request,
        CancellationToken cancellationToken);
    Task<(int Id, string Code)> CreateProductConfigurationValueAsync(
        CreateProductConfigurationValueRequest request,
        CancellationToken cancellationToken);
    Task CreateProductConfigurationItemAsync(
        CreateProductConfigurationItemRequest request,
        CancellationToken cancellationToken);
    Task<(int Id, string Code)> CreateProductConfigurationCombinationAsync(
        CreateProductConfigurationCombinationRequest request,
        CancellationToken cancellationToken);
    Task SaveProductConfigurationFeatureStocksAsync(
        SaveProductConfigurationFeatureStocksRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Bir malzeme kartina bagli ozellik (FEATURE) listesini tamamen yeniden yazar.
    /// Her feature icin opsiyonel AllowedValueIds[] gonderilirse, o (stok, ozellik) pair'i
    /// yalniz secili degerlere kisitlanir; bos ise kisitlama yok (tum degerler gecerli).
    /// </summary>
    Task SetFeaturesForItemAsync(string stockCode, IReadOnlyCollection<(int FeatureId, bool PrintDescriptionInDesign, int[] AllowedValueIds)> items, CancellationToken cancellationToken);
    Task UpdateProductConfigurationFeatureAsync(UpdateProductConfigurationFeatureRequest request, CancellationToken cancellationToken);
    Task DeleteProductConfigurationFeatureAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductConfigurationValueAsync(int id, CancellationToken cancellationToken);
    Task UpdateProductConfigurationValueAsync(int id, string? description, string? aciklama, CancellationToken cancellationToken);
    Task DeleteProductConfigurationItemAsync(int id, CancellationToken cancellationToken);
    Task UpdateProductCombinationDescriptionAsync(int id, string? description, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemDto>> GetItemsForLookupAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsForLookupAsync(
        string materialCode, CancellationToken cancellationToken);

    /// <summary>"Tanımlı Kombinasyonlar" liste ekranı için tüm kombinasyonları döner.</summary>
    Task<IReadOnlyCollection<CombinationListItemDto>> GetAllCombinationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Satış teklifi satırında yeni kombinasyon oluşturma/çözme akışı:
    /// aynı (feature,value) setine sahip mevcut CONFIG varsa onu döndürür;
    /// yoksa yeni bir CONFIG kaydı oluşturur.
    /// </summary>
    Task<ResolveCombinationResponse> ResolveOrCreateCombinationAsync(
        ResolveCombinationRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BOMDto>> GetBOMsAsync(CancellationToken cancellationToken);
    Task<BOMWithNames?> GetBOMByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken);
    Task<BOMWithNames?> GetBOMByIdAsync(int id, CancellationToken cancellationToken);
    /// <summary>
    /// Recete kaydeder. <paramref name="userId"/> CreatedById/UpdatedById audit
    /// alanlarina yazilir (rapor 2026-05-17 madde 3.5). Cycle korumasi (madde 3.1):
    /// kayit oncesi BFS ile dongusel bagimlilik kontrol edilir; tespit edilirse
    /// ArgumentException firlatilir, DB'ye yazilmaz.
    /// <paramref name="allowDuplicateComponents"/>: BOM_ALLOW_DUPLICATE_COMPONENTS
    /// sirket parametresi (Admin → Parametreler → Üretim) — true ise ayni
    /// (ItemId+ConfigId) bilesen farkli satirlarda tekrar edebilir. Caller
    /// (BomController) parametreyi okuyup gecer; import gibi diger cagrilar
    /// default false ile mevcut davranisi korur.
    /// </summary>
    Task<int> SaveBOMAsync(SaveBOMRequest request, int? userId, CancellationToken cancellationToken,
        bool allowDuplicateComponents = false);

    /// <summary>
    /// Soft delete — IsActive=0 (rapor 2026-05-17 madde 3.6). <paramref name="userId"/>
    /// UpdatedById audit alanina yazilir. Lines tablosu fiziksel silinmez.
    /// </summary>
    Task DeleteBOMAsync(int id, int? userId, CancellationToken cancellationToken);

    /// <summary>
    /// Multi-level BOM patlatma (rapor 2026-05-17 madde 3.3): X mamulden
    /// <paramref name="quantity"/> adet uretmek icin tum alt-receteleri gezerek
    /// hammadde + ara mamul ihtiyacini birikmis (aggregated) olarak doner.
    /// BFS, depth cap 20 (cycle korumasi disinda guvenlik smaplosu); cap'e
    /// ulasilirsa <c>Truncated=true</c>. Ayni item farkli yollardan gelirse
    /// quantity toplanir (depth ilk goruldugu seviye).
    /// </summary>
    Task<BOMExplodeResultDto?> ExplodeBOMAsync(
        int parentItemId, decimal quantity, int? configId, CancellationToken cancellationToken);

    /// <summary>
    /// Where-used (ters arama): bir bileseni DOĞRUDAN kullanan parent BOM'larin
    /// 1-seviye listesi. Transitive (Vida→Leg→Masa) bu surumun kapsami disi.
    /// (Rapor 2026-05-17 madde 3.3.)
    /// </summary>
    Task<IReadOnlyCollection<WhereUsedItemDto>> GetWhereUsedAsync(
        int componentItemId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupDto>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken);
    Task CreateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken);
    Task UpdateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken);
    Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken);
    /// <summary>2026-05-24: Batch — Malzeme listeleri icin N+1 onlemek amaciyla.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<MaterialGroupMappingDto>>> GetMaterialGroupMappingsBatchAsync(
        IReadOnlyCollection<int> stockCardIds, CancellationToken cancellationToken);
    Task SaveMaterialGroupMappingsAsync(SaveMaterialGroupMappingsRequest request, CancellationToken cancellationToken);

    /// <summary>2026-05-24: ItemUnit batch — filter panel "Olcu Birimi" alani icin.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<CalibraHub.Domain.Entities.ItemUnit>>> GetItemUnitsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken);

    /// <summary>2026-05-24: ItemFeatureMapping batch — filter panel "Ozellikler" alanlari icin.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<CalibraHub.Domain.Entities.ItemFeatureMapping>>> GetItemFeatureMappingsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken);
}

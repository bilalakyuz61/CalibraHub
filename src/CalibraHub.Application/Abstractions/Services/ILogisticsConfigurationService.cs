using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ILogisticsConfigurationService
{
    Task<LogisticsConfigurationSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<ItemDto> Items, int TotalCount)> GetItemsPagedAsync(string? search, int offset, int pageSize, CancellationToken cancellationToken);
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
    Task CreateUnitAsync(CreateUnitRequest request, CancellationToken cancellationToken);
    Task UpdateUnitAsync(UpdateUnitRequest request, CancellationToken cancellationToken);
    Task DeleteUnitAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StockUnitConversionDto>> GetStockUnitConversionsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveStockUnitConversionsAsync(int stockCardId, IReadOnlyCollection<SaveStockUnitConversionItem> items, CancellationToken cancellationToken);
    Task ConfigureItemAsync(ConfigureItemRequest request, CancellationToken cancellationToken);
    Task CreatePropertyAsync(CreateFeatureRequest request, CancellationToken cancellationToken);
    Task CreateStockPropertyLinkAsync(CreateItemPropertyLinkRequest request, CancellationToken cancellationToken);
    Task CreatePropertyValueAsync(CreateFeatureValueRequest request, CancellationToken cancellationToken);
    Task CreateStockPropertyMappingAsync(CreateItemPropertyMappingRequest request, CancellationToken cancellationToken);
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
    /// Kombinasyon Takibi acik olan stok kartlarinin Ozellikler sekmesinde kullanilir.
    /// </summary>
    Task SetFeaturesForItemAsync(string stockCode, IReadOnlyCollection<int> featureIds, CancellationToken cancellationToken);
    Task UpdateProductConfigurationFeatureAsync(UpdateProductConfigurationFeatureRequest request, CancellationToken cancellationToken);
    Task DeleteProductConfigurationFeatureAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductConfigurationValueAsync(int id, CancellationToken cancellationToken);
    Task UpdateProductConfigurationValueAsync(int id, string? description, string? aciklama, CancellationToken cancellationToken);
    Task DeleteProductConfigurationItemAsync(int id, CancellationToken cancellationToken);
    Task UpdateProductCombinationDescriptionAsync(int id, string? description, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemDto>> GetItemsForLookupAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsForLookupAsync(
        string materialCode, CancellationToken cancellationToken);

    /// <summary>
    /// Satış teklifi satırında yeni kombinasyon oluşturma/çözme akışı:
    /// aynı (feature,value) setine sahip mevcut CONFIG varsa onu döndürür;
    /// yoksa yeni bir CONFIG kaydı oluşturur.
    /// </summary>
    Task<ResolveCombinationResponse> ResolveOrCreateCombinationAsync(
        ResolveCombinationRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BOMDto>> GetBOMsAsync(CancellationToken cancellationToken);
    Task<BOMWithNames?> GetBOMByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken);
    Task<int> SaveBOMAsync(SaveBOMRequest request, CancellationToken cancellationToken);
    Task DeleteBOMAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupDto>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken);
    Task CreateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken);
    Task UpdateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken);
    Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveMaterialGroupMappingsAsync(SaveMaterialGroupMappingsRequest request, CancellationToken cancellationToken);
}

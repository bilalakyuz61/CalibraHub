using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ILogisticsConfigurationService
{
    Task<LogisticsConfigurationSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<ProductConfigurationSnapshotDto> GetProductConfigurationSnapshotAsync(CancellationToken cancellationToken);
    Task<MaterialCardDynamicSchemaDto> GetMaterialCardDynamicSchemaAsync(CancellationToken cancellationToken);
    Task SaveMaterialCardFieldGroupAsync(
        SaveMaterialCardFieldGroupRequest request,
        CancellationToken cancellationToken);
    Task SaveMaterialCardDynamicFieldAsync(
        SaveMaterialCardDynamicFieldRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialCardFieldSettingDto>> GetMaterialCardFieldSettingsAsync(CancellationToken cancellationToken);
    Task SaveMaterialCardFieldSettingsAsync(
        IReadOnlyCollection<SaveMaterialCardFieldSettingRequest> requests,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<WarehouseLocationDto>> GetWarehouseLocationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MeasureUnitDefinitionDto>> GetMeasureUnitDefinitionsAsync(CancellationToken cancellationToken);
    Task CreateStockCardAsync(CreateStockCardRequest request, CancellationToken cancellationToken);
    Task UpdateStockCardAsync(UpdateStockCardRequest request, CancellationToken cancellationToken);
    Task DeactivateStockCardAsync(int stockCardId, CancellationToken cancellationToken);
    Task CreateWarehouseLocationAsync(CreateWarehouseLocationRequest request, CancellationToken cancellationToken);
    Task UpdateWarehouseLocationAsync(UpdateWarehouseLocationRequest request, CancellationToken cancellationToken);
    Task DeleteWarehouseLocationAsync(int locationId, CancellationToken cancellationToken);
    Task CreateMeasureUnitDefinitionAsync(CreateMeasureUnitDefinitionRequest request, CancellationToken cancellationToken);
    Task UpdateMeasureUnitDefinitionAsync(UpdateMeasureUnitDefinitionRequest request, CancellationToken cancellationToken);
    Task DeleteMeasureUnitDefinitionAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StockUnitConversionDto>> GetStockUnitConversionsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveStockUnitConversionsAsync(int stockCardId, IReadOnlyCollection<SaveStockUnitConversionItem> items, CancellationToken cancellationToken);
    Task ConfigureStockCardAsync(ConfigureStockCardRequest request, CancellationToken cancellationToken);
    Task CreatePropertyAsync(CreateConfigurationPropertyRequest request, CancellationToken cancellationToken);
    Task CreateStockPropertyLinkAsync(CreateStockCardPropertyLinkRequest request, CancellationToken cancellationToken);
    Task CreatePropertyValueAsync(CreateConfigurationPropertyValueRequest request, CancellationToken cancellationToken);
    Task CreateStockPropertyMappingAsync(CreateStockCardPropertyMappingRequest request, CancellationToken cancellationToken);
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
    Task UpdateProductConfigurationFeatureAsync(UpdateProductConfigurationFeatureRequest request, CancellationToken cancellationToken);
    Task DeleteProductConfigurationFeatureAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductConfigurationValueAsync(int id, CancellationToken cancellationToken);
    Task UpdateProductConfigurationValueAsync(int id, string? description, string? aciklama, CancellationToken cancellationToken);
    Task DeleteProductConfigurationItemAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StockCardDto>> GetStockCardsForLookupAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsForLookupAsync(
        string materialCode, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ProductTreeDto>> GetProductTreesAsync(CancellationToken cancellationToken);
    Task<ProductTreeWithNames?> GetProductTreeByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken);
    Task<int> SaveProductTreeAsync(SaveProductTreeRequest request, CancellationToken cancellationToken);
    Task DeleteProductTreeAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupDto>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken);
    Task CreateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken);
    Task UpdateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken);
    Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveMaterialGroupMappingsAsync(SaveMaterialGroupMappingsRequest request, CancellationToken cancellationToken);
}

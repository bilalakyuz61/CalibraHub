using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ILogisticsConfigurationRepository
{
    Task<IReadOnlyCollection<StockCard>> GetStockCardsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialCardFieldGroup>> GetMaterialCardFieldGroupsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialCardDynamicFieldDefinition>> GetMaterialCardDynamicFieldDefinitionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialCardFieldOption>> GetMaterialCardFieldOptionsAsync(CancellationToken cancellationToken);
    Task UpsertMaterialCardFieldGroupAsync(MaterialCardFieldGroup group, CancellationToken cancellationToken);
    Task UpsertMaterialCardDynamicFieldAsync(
        MaterialCardDynamicFieldDefinition field,
        IReadOnlyCollection<MaterialCardFieldOption> options,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialCardFieldSetting>> GetMaterialCardFieldSettingsAsync(CancellationToken cancellationToken);
    Task UpsertMaterialCardFieldSettingsAsync(
        IReadOnlyCollection<MaterialCardFieldSetting> settings,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ConfigurationProperty>> GetPropertiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ConfigurationPropertyValue>> GetPropertyValuesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StockCardPropertyMapping>> GetStockPropertyMappingsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductConfigurationRecord>> GetProductConfigurationRecordsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<WarehouseLocation>> GetWarehouseLocationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MeasureUnitDefinition>> GetMeasureUnitDefinitionsAsync(CancellationToken cancellationToken);
    Task<int> AddStockCardAsync(StockCard stockCard, CancellationToken cancellationToken);
    Task UpdateStockCardAsync(StockCard stockCard, CancellationToken cancellationToken);
    Task UpdateStockCardActiveStatusAsync(int stockCardId, bool isActive, CancellationToken cancellationToken);
    Task DeleteStockCardAsync(int stockCardId, CancellationToken cancellationToken);
    Task UpdateStockCardConfigurableStatusAsync(int stockCardId, bool isConfigurable, CancellationToken cancellationToken);
    Task AddWarehouseLocationAsync(WarehouseLocation location, CancellationToken cancellationToken);
    Task UpdateWarehouseLocationAsync(WarehouseLocation location, CancellationToken cancellationToken);
    Task DeleteWarehouseLocationAsync(int locationId, CancellationToken cancellationToken);
    Task AddMeasureUnitDefinitionAsync(MeasureUnitDefinition definition, CancellationToken cancellationToken);
    Task UpdateMeasureUnitDefinitionAsync(MeasureUnitDefinition definition, CancellationToken cancellationToken);
    Task DeleteMeasureUnitDefinitionAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StockUnitConversion>> GetStockUnitConversionsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveStockUnitConversionsAsync(int stockCardId, IReadOnlyCollection<StockUnitConversion> conversions, CancellationToken cancellationToken);
    Task AddPropertyAsync(ConfigurationProperty property, CancellationToken cancellationToken);
    Task AddPropertyValueAsync(ConfigurationPropertyValue propertyValue, CancellationToken cancellationToken);
    Task AddStockPropertyMappingAsync(StockCardPropertyMapping mapping, CancellationToken cancellationToken);
    Task<(int Id, string Code)> AddProductFeatureAsync(
        string name,
        string dataType,
        bool isActive,
        string? unitOfMeasure,
        CancellationToken cancellationToken);
    Task<(int Id, string Code)> AddProductValueAsync(
        int featureId,
        string name,
        bool isActive,
        string? aciklama,
        CancellationToken cancellationToken);
    Task UpdateProductValueAsync(int id, string name, string? aciklama, CancellationToken cancellationToken);
    Task<(int Id, string Code)> AddProductConfigAsync(
        int valueId,
        string relatedMaterialCode,
        string name,
        bool isActive,
        CancellationToken cancellationToken);

    Task<(int Id, string Code)> AddProductConfigurationCombinationAsync(
        string relatedMaterialCode,
        string configName,
        IReadOnlyCollection<int> valueIds,
        bool isActive,
        CancellationToken cancellationToken);

    Task ReplaceProductFeatureStockLinksAsync(
        int featureId,
        string[] stockCodes,
        CancellationToken cancellationToken);
    Task UpdateProductFeatureAsync(int id, string name, string dataType, string? unitOfMeasure, CancellationToken cancellationToken);
    Task DeleteProductFeatureAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductValueAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductConfigAsync(int id, CancellationToken cancellationToken);
    Task UpdateStockPropertyMappingValueAsync(
        Guid mappingId,
        Guid propertyValueId,
        string? configurationCode,
        string? textValue,
        decimal? numericValue,
        DateTime? dateValue,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ProductTree>> GetProductTreesAsync(CancellationToken cancellationToken);
    Task<ProductTreeWithNames?> GetProductTreeByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken);
    Task<int> AddProductTreeAsync(ProductTree tree, CancellationToken cancellationToken);
    Task UpdateProductTreeAsync(ProductTree tree, CancellationToken cancellationToken);
    Task DeleteProductTreeAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsByMaterialCodeAsync(
        string materialCode, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroup>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken);
    Task AddMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken);
    Task UpdateMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken);
    Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveMaterialGroupMappingsAsync(int stockCardId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken);
}

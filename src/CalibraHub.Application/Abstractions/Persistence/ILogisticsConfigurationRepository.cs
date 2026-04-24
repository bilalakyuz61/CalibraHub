using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ILogisticsConfigurationRepository
{
    Task<IReadOnlyCollection<Item>> GetItemsAsync(CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<Item> Items, int TotalCount)> GetItemsPagedAsync(string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FieldGroup>> GetFieldGroupsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialCardDynamicFieldDefinition>> GetMaterialCardDynamicFieldDefinitionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialCardFieldOption>> GetMaterialCardFieldOptionsAsync(CancellationToken cancellationToken);
    Task UpsertFieldGroupAsync(FieldGroup group, CancellationToken cancellationToken);
    Task UpsertMaterialCardDynamicFieldAsync(
        MaterialCardDynamicFieldDefinition field,
        IReadOnlyCollection<MaterialCardFieldOption> options,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Field>> GetFieldsAsync(CancellationToken cancellationToken);
    Task UpsertFieldsAsync(
        IReadOnlyCollection<Field> settings,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Feature>> GetPropertiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FeatureValue>> GetPropertyValuesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemPropertyMapping>> GetStockPropertyMappingsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductConfigurationRecord>> GetProductConfigurationRecordsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Location>> GetLocationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Unit>> GetUnitsAsync(CancellationToken cancellationToken);
    Task<int> AddItemAsync(Item stockCard, CancellationToken cancellationToken);
    Task UpdateItemAsync(Item stockCard, CancellationToken cancellationToken);
    Task UpdateItemActiveStatusAsync(int stockCardId, bool isActive, CancellationToken cancellationToken);
    Task DeleteItemAsync(int stockCardId, CancellationToken cancellationToken);
    Task UpdateItemConfigurableStatusAsync(int stockCardId, bool isConfigurable, CancellationToken cancellationToken);
    Task AddLocationAsync(Location location, CancellationToken cancellationToken);
    Task UpdateLocationAsync(Location location, CancellationToken cancellationToken);
    Task DeleteLocationAsync(int locationId, CancellationToken cancellationToken);
    Task AddUnitAsync(Unit definition, CancellationToken cancellationToken);
    Task UpdateUnitAsync(Unit definition, CancellationToken cancellationToken);
    Task DeleteUnitAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StockUnitConversion>> GetStockUnitConversionsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveStockUnitConversionsAsync(int stockCardId, IReadOnlyCollection<StockUnitConversion> conversions, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemLocation>> GetItemLocationsAsync(int itemId, CancellationToken cancellationToken);
    Task SaveItemLocationsAsync(int itemId, IReadOnlyCollection<ItemLocation> locations, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LocationType>> GetLocationTypesAsync(CancellationToken cancellationToken);
    Task<int> UpsertLocationTypeAsync(LocationType type, CancellationToken cancellationToken);
    Task DeleteLocationTypeAsync(int id, CancellationToken cancellationToken);
    Task<int> CountLocationsOfTypeAsync(string code, CancellationToken cancellationToken);
    Task<int> RenameLocationTypeCodeAsync(string oldCode, string newCode, CancellationToken cancellationToken);
    Task AddPropertyAsync(Feature property, CancellationToken cancellationToken);
    Task AddPropertyValueAsync(FeatureValue propertyValue, CancellationToken cancellationToken);
    Task AddStockPropertyMappingAsync(ItemPropertyMapping mapping, CancellationToken cancellationToken);
    Task<(int Id, string Code)> AddProductFeatureAsync(
        string name,
        string dataType,
        bool isActive,
        string? unitOfMeasure,
        bool visibleInDesign,
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
    /// <summary>Bir stok kartina bagli FEATURE_STOCK kayitlarini full replace eder.
    /// items: (FeatureId, PrintDescriptionInDesign) — PrintDescription VisibleInDesign kolonunda saklanir.</summary>
    Task ReplaceStockFeatureLinksAsync(string stockCode, (int FeatureId, bool PrintDescriptionInDesign)[] items, CancellationToken cancellationToken);
    Task UpdateProductFeatureAsync(int id, string name, string dataType, string? unitOfMeasure, bool visibleInDesign, CancellationToken cancellationToken);
    Task DeleteProductFeatureAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductValueAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductConfigAsync(int id, CancellationToken cancellationToken);
    Task UpdateProductConfigDescriptionAsync(int id, string? description, CancellationToken cancellationToken);
    Task UpdateStockPropertyMappingValueAsync(
        Guid mappingId,
        Guid propertyValueId,
        string? configurationCode,
        string? textValue,
        decimal? numericValue,
        DateTime? dateValue,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BOM>> GetBOMsAsync(CancellationToken cancellationToken);
    Task<BOMWithNames?> GetBOMByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken);
    Task<int> AddBOMAsync(BOM tree, CancellationToken cancellationToken);
    Task UpdateBOMAsync(BOM tree, CancellationToken cancellationToken);
    Task DeleteBOMAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsByMaterialCodeAsync(
        string materialCode, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroup>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken);
    Task AddMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken);
    Task UpdateMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken);
    Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken);
    Task SaveMaterialGroupMappingsAsync(int stockCardId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken);
}

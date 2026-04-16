using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>Kombinasyon arama için zengin DTO — kod, açıklama ve özellik/değer çiftleri</summary>
public sealed record CombinationLookupRow(
    string Code,
    string Name,
    IReadOnlyCollection<(string Feature, string Value)> FeatureValues);

public sealed record LogisticsConfigurationSnapshotDto(
    IReadOnlyCollection<StockCardDto> StockCards,
    IReadOnlyCollection<ConfigurationPropertyDto> Properties,
    IReadOnlyCollection<ConfigurationPropertyValueDto> PropertyValues,
    IReadOnlyCollection<StockCardPropertyMappingDto> StockPropertyMappings);

public sealed record StockCardDto(
    int Id,
    string MaterialCode,
    string MaterialName,
    string? MaterialDescription,
    int? MaterialTypeId,
    bool IsActive,
    DateTime? CreatedDate,
    int? CreatedByUserId,
    DateTime? ModifiedDate,
    int? ModifiedByUserId,
    bool TrackCombinations = false,
    decimal TaxRate = 20m,
    byte[]? ImageData = null,
    string? ImageMimeType = null);

public sealed record ConfigurationPropertyDto(
    Guid Id,
    string Code,
    string Name,
    string DataType,
    bool IsActive);

public sealed record ConfigurationPropertyValueDto(
    Guid Id,
    Guid PropertyId,
    string PropertyName,
    string Code,
    string Description,
    string Value,
    int SortOrder,
    bool IsActive);

public sealed record StockCardPropertyMappingDto(
    Guid Id,
    int StockCardId,
    string MaterialCode,
    Guid PropertyId,
    string PropertyCode,
    string PropertyName,
    string PropertyDataType,
    Guid? PropertyValueId,
    string? PropertyValue,
    string? ConfigurationCode,
    string? TextValue,
    decimal? NumericValue,
    DateTime? DateValue,
    bool IsActive);

public sealed record CreateStockCardRequest(
    string MaterialCode,
    string MaterialName,
    string? MaterialDescription = null,
    int? MaterialTypeId = null,
    int? CreatedByUserId = null,
    bool TrackCombinations = false,
    byte[]? ImageData = null,
    string? ImageMimeType = null);

public sealed record UpdateStockCardRequest(
    int StockCardId,
    string MaterialCode,
    string MaterialName,
    string? MaterialDescription = null,
    int? MaterialTypeId = null,
    int? ModifiedByUserId = null,
    bool TrackCombinations = false,
    byte[]? ImageData = null,
    string? ImageMimeType = null);

public sealed record CreateConfigurationPropertyRequest(
    string Code,
    string Name,
    ConfigurationFieldDataType DataType);

public sealed record CreateStockCardPropertyLinkRequest(
    int StockCardId,
    Guid PropertyId);

public sealed record CreateConfigurationPropertyValueRequest(
    Guid PropertyId,
    string Code,
    string Description,
    string? TextValue,
    decimal? NumericValue,
    DateTime? DateValue,
    int SortOrder);

public sealed record CreateStockCardPropertyMappingRequest(
    int StockCardId,
    Guid PropertyId,
    Guid PropertyValueId);

public sealed record ConfigureStockCardRequest(
    int StockCardId,
    bool IsConfigurable,
    IReadOnlyCollection<Guid> PropertyIds);

public sealed record MaterialCardFieldSettingDto(
    string FieldKey,
    string FieldLabel,
    bool IsVisible,
    bool IsRequired,
    int DisplayOrder);

public sealed record SaveMaterialCardFieldSettingRequest(
    string FieldKey,
    bool IsVisible,
    bool IsRequired);

public sealed record WarehouseLocationDto(
    int Id,
    int? ParentId,
    string LocationTypeCode,
    string LocationCode,
    string? LocationName,
    int SortOrder,
    decimal? MaxWeightCapacity,
    decimal? VolumeCapacity,
    bool IsActive);

public sealed record MeasureUnitDefinitionDto(
    int Id,
    string UnitCode,
    string UnitName,
    string? IntlCode,
    int SortOrder,
    bool IsActive);

public sealed record CreateWarehouseLocationRequest(
    int? ParentId,
    string LocationTypeCode,
    string LocationCode,
    string? LocationName,
    int SortOrder,
    decimal? MaxWeightCapacity,
    decimal? VolumeCapacity,
    bool IsActive);

public sealed record CreateMeasureUnitDefinitionRequest(
    string UnitCode,
    string UnitName,
    string? IntlCode,
    int SortOrder,
    bool IsActive);

public sealed record UpdateWarehouseLocationRequest(
    int Id,
    int? ParentId,
    string LocationTypeCode,
    string LocationCode,
    string? LocationName,
    int SortOrder,
    decimal? MaxWeightCapacity,
    decimal? VolumeCapacity,
    bool IsActive);

public sealed record UpdateMeasureUnitDefinitionRequest(
    int Id,
    string UnitCode,
    string UnitName,
    string? IntlCode,
    int SortOrder,
    bool IsActive);

public sealed record StockUnitConversionDto(
    int Id,
    int StockCardId,
    int LineNo,
    string UnitCode,
    decimal Multiplier);

public sealed record SaveStockUnitConversionItem(
    string UnitCode,
    decimal Multiplier);

public sealed record ProductTreeDto(
    int Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    IReadOnlyCollection<ProductTreeLineDto> Lines);

public sealed record ProductTreeLineDto(
    int Id,
    int ProductTreeId,
    string ComponentMaterialCode,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio,
    Guid LineGuid);

public sealed record CreateProductTreeRequest(
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    IReadOnlyCollection<ProductTreeLineDto> Lines);

public sealed record UpdateProductTreeRequest(
    int Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    IReadOnlyCollection<ProductTreeLineDto> Lines);


public sealed record ProductTreeWithNames(
    int Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    string? ImageFitMode,
    IReadOnlyCollection<ProductTreeLineWithName> Lines);

public sealed record ProductTreeLineWithName(
    string ComponentMaterialCode,
    string ComponentMaterialName,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio);

public sealed record SaveProductTreeRequest(
    int? Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    string? ImageBase64,
    string? ImageMimeType,
    string? ImageFitMode,
    IReadOnlyCollection<SaveProductTreeLineRequest> Lines);

public sealed record SaveProductTreeLineRequest(
    string ComponentMaterialCode,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio);

public sealed record MaterialGroupDto(
    int Id,
    int GroupCategory,
    string GroupCode,
    string? GroupDescription);

public sealed record SaveMaterialGroupRequest(
    int? Id,
    int GroupCategory,
    string GroupCode,
    string? GroupDescription);

public sealed record MaterialGroupMappingDto(
    int SlotOrder,
    string GroupCode,
    string? GroupDescription);

public sealed record SaveMaterialGroupMappingsRequest(
    int StockCardId,
    IReadOnlyCollection<string?> SlotCodes);

public sealed record DeleteMaterialGroupBody(int Id, int Category);

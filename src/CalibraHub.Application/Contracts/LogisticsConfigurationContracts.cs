using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>Kombinasyon arama için zengin DTO — kod, açıklama ve özellik/değer çiftleri</summary>
public sealed record CombinationLookupRow(
    int ConfigId,
    string Code,
    string Name,
    IReadOnlyCollection<(string Feature, string Value)> FeatureValues);

/// <summary>
/// Satış teklifi satırında "yeni kombinasyon oluştur" akışı için request/response.
/// Dedup check: aynı özellik/değer setine sahip mevcut CONFIG varsa onu döner; yoksa yeni CONFIG üretir.
/// </summary>
public sealed record ResolveCombinationRequest(
    string MaterialCode,
    IReadOnlyList<ResolveCombinationSelection> Selections);

public sealed record ResolveCombinationSelection(
    string FeatureName,
    int FeatureId,
    int ValueId,
    string ValueCode,
    string ValueName,
    string? Description);

public sealed record ResolveCombinationResponse(
    bool Matched,
    int ConfigId,
    string Code,
    string Name);

public sealed record LogisticsConfigurationSnapshotDto(
    IReadOnlyCollection<ItemDto> Items,
    IReadOnlyCollection<FeatureDto> Properties,
    IReadOnlyCollection<FeatureValueDto> PropertyValues,
    IReadOnlyCollection<ItemPropertyMappingDto> StockPropertyMappings);

public sealed record ItemDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    int? TypeId,
    bool IsActive,
    DateTime? CreatedDate,
    int? CreatedByUserId,
    DateTime? ModifiedDate,
    int? ModifiedByUserId,
    bool TrackCombinations = false,
    decimal TaxRate = 20m,
    byte[]? ImageData = null,
    string? ImageMimeType = null);

public sealed record FeatureDto(
    Guid Id,
    string Code,
    string Name,
    string DataType,
    bool IsActive);

public sealed record FeatureValueDto(
    Guid Id,
    Guid PropertyId,
    string PropertyName,
    string Code,
    string Description,
    string Value,
    int SortOrder,
    bool IsActive);

public sealed record ItemPropertyMappingDto(
    Guid Id,
    int ItemId,
    string ItemCode,
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

public sealed record CreateItemRequest(
    string Code,
    string Name,
    string? Description = null,
    int? TypeId = null,
    int? CreatedByUserId = null,
    bool TrackCombinations = false,
    decimal TaxRate = 20m,
    byte[]? ImageData = null,
    string? ImageMimeType = null);

public sealed record UpdateItemRequest(
    int ItemId,
    string Code,
    string Name,
    string? Description = null,
    int? TypeId = null,
    int? ModifiedByUserId = null,
    bool TrackCombinations = false,
    decimal TaxRate = 20m,
    byte[]? ImageData = null,
    string? ImageMimeType = null);

public sealed record CreateFeatureRequest(
    string Code,
    string Name,
    ConfigurationFieldDataType DataType);

public sealed record CreateItemPropertyLinkRequest(
    int ItemId,
    Guid PropertyId);

public sealed record CreateFeatureValueRequest(
    Guid PropertyId,
    string Code,
    string Description,
    string? TextValue,
    decimal? NumericValue,
    DateTime? DateValue,
    int SortOrder);

public sealed record CreateItemPropertyMappingRequest(
    int ItemId,
    Guid PropertyId,
    Guid PropertyValueId);

public sealed record ConfigureItemRequest(
    int ItemId,
    bool IsConfigurable,
    IReadOnlyCollection<Guid> PropertyIds);

public sealed record FieldDto(
    string FieldKey,
    string FieldLabel,
    bool IsVisible,
    bool IsRequired,
    int DisplayOrder);

public sealed record SaveFieldRequest(
    string FieldKey,
    bool IsVisible,
    bool IsRequired);

public sealed record LocationDto(
    int Id,
    int? ParentId,
    string LocationTypeCode,
    string LocationCode,
    string? LocationName,
    int SortOrder,
    decimal? MaxWeightCapacity,
    decimal? VolumeCapacity,
    bool IsActive);

public sealed record UnitDto(
    int Id,
    string UnitCode,
    string UnitName,
    string? IntlCode,
    int SortOrder,
    bool IsActive);

public sealed record CreateLocationRequest(
    int? ParentId,
    string LocationTypeCode,
    string LocationCode,
    string? LocationName,
    int SortOrder,
    decimal? MaxWeightCapacity,
    decimal? VolumeCapacity,
    bool IsActive);

public sealed record CreateUnitRequest(
    string UnitCode,
    string UnitName,
    string? IntlCode,
    int SortOrder,
    bool IsActive);

public sealed record UpdateLocationRequest(
    int Id,
    int? ParentId,
    string LocationTypeCode,
    string LocationCode,
    string? LocationName,
    int SortOrder,
    decimal? MaxWeightCapacity,
    decimal? VolumeCapacity,
    bool IsActive);

public sealed record UpdateUnitRequest(
    int Id,
    string UnitCode,
    string UnitName,
    string? IntlCode,
    int SortOrder,
    bool IsActive);

public sealed record StockUnitConversionDto(
    int Id,
    int ItemId,
    int LineNo,
    string UnitCode,
    decimal Multiplier);

public sealed record SaveStockUnitConversionItem(
    string UnitCode,
    decimal Multiplier);

public sealed record ItemLocationDto(
    int Id,
    int ItemId,
    int LocationId,
    string LocationCode,
    string? LocationName,
    string LocationTypeCode,
    bool IsDefault,
    int SortOrder);

public sealed record SaveItemLocationItem(
    int LocationId,
    bool IsDefault);

public sealed record LocationTypeDto(
    int Id,
    string Code,
    string Name,
    int SortOrder,
    bool IsActive);

public sealed record SaveLocationTypeRequest(
    int? Id,
    string Code,
    string Name,
    int SortOrder,
    bool IsActive);

public sealed record BOMDto(
    int Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    IReadOnlyCollection<BOMLineDto> Lines);

public sealed record BOMLineDto(
    int Id,
    int BOMId,
    string ComponentMaterialCode,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio,
    Guid LineGuid);

public sealed record CreateBOMRequest(
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    IReadOnlyCollection<BOMLineDto> Lines);

public sealed record UpdateBOMRequest(
    int Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    IReadOnlyCollection<BOMLineDto> Lines);


public sealed record BOMWithNames(
    int Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    string? ImageFitMode,
    IReadOnlyCollection<BOMLineWithName> Lines);

public sealed record BOMLineWithName(
    string ComponentMaterialCode,
    string ComponentMaterialName,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio);

public sealed record SaveBOMRequest(
    int? Id,
    string ParentMaterialCode,
    string? ConfigurationCode,
    string? Description,
    string? ImageBase64,
    string? ImageMimeType,
    string? ImageFitMode,
    IReadOnlyCollection<SaveBOMLineRequest> Lines);

public sealed record SaveBOMLineRequest(
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
    int ItemId,
    IReadOnlyCollection<string?> SlotCodes);

public sealed record DeleteMaterialGroupBody(int Id, int Category);

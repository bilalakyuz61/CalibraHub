using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record ProductConfigurationSnapshotDto(
    IReadOnlyCollection<ProductConfigurationFeatureDto> Features,
    IReadOnlyCollection<ProductConfigurationValueDto> Values,
    IReadOnlyCollection<ProductConfigurationItemDto> Configurations,
    IReadOnlyCollection<ProductConfigurationFeatureStockLinkDto> FeatureStockLinks);

public sealed record ProductConfigurationFeatureDto(
    int Id,
    string Code,
    string Name,
    string DataType,
    bool IsActive,
    DateTime CreatedDate,
    string? UnitOfMeasure = null,
    bool VisibleInDesign = true);

public sealed record ProductConfigurationValueDto(
    int Id,
    int FeatureId,
    string FeatureCode,
    string FeatureName,
    string Code,
    string Description,
    string Value,
    bool IsActive,
    DateTime CreatedDate,
    string? Aciklama = null);

public sealed record ProductConfigurationItemDto(
    int Id,
    int? ValueId,
    int? FeatureId,
    string ConfigCode,
    string ConfigName,
    string RelatedMaterialCode,
    string FeatureCode,
    string FeatureName,
    string ValueCode,
    string ValueDescription,
    string Value,
    bool IsActive,
    DateTime CreatedDate,
    IReadOnlyCollection<int> ValueIds = null!);

public sealed record ProductConfigurationFeatureStockLinkDto(
    int FeatureId,
    string StockCode,
    bool PrintDescriptionInDesign = true,
    IReadOnlyCollection<int>? AllowedValueIds = null);

public sealed record CreateProductConfigurationFeatureRequest(
    string Name,
    ConfigurationFieldDataType DataType,
    bool IsActive = true,
    string? UnitOfMeasure = null,
    bool VisibleInDesign = true);

public sealed record CreateProductConfigurationValueRequest(
    int FeatureId,
    string? Description,
    string? TextValue,
    decimal? NumericValue,
    DateTime? DateValue,
    bool IsActive = true,
    string? Aciklama = null);

public sealed record CreateProductConfigurationItemRequest(
    string RelatedMaterialCode,
    int FeatureId,
    int ValueId,
    bool IsActive = true);

public sealed record CreateProductConfigurationCombinationRequest(
    string RelatedMaterialCode,
    IReadOnlyCollection<int> ValueIds,
    bool IsActive = true);

public sealed record UpdateProductConfigurationFeatureRequest(
    int Id,
    string Name,
    ConfigurationFieldDataType DataType,
    string? UnitOfMeasure = null,
    bool VisibleInDesign = true);

public sealed record SaveProductConfigurationFeatureStocksRequest(
    int FeatureId,
    IReadOnlyCollection<SaveProductConfigurationFeatureStockItem> Stocks);

public sealed record SaveProductConfigurationFeatureStockItem(
    string StockCode,
    bool PrintDescriptionInDesign = true,
    IReadOnlyCollection<int>? AllowedValueIds = null);



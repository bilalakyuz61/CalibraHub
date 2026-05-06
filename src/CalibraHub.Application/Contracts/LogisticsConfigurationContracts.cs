using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>Tek bir kombinasyon ozelligi. Match/dedup icin FeatureValueId (FK)
/// kullanilir — id tabanli kural (CLAUDE.md). Feature/Value/ValueCode UI display
/// icindir; ID-temelli karsilastirma ad/whitespace farklarina karsi tamamen dirençli.</summary>
public sealed record CombinationFeatureValueDto(int FeatureValueId, string Feature, string Value, string ValueCode);

/// <summary>Kombinasyon arama için zengin DTO — kod, açıklama ve özellik/değer çiftleri</summary>
public sealed record CombinationLookupRow(
    int ConfigId,
    string Code,
    string Name,
    IReadOnlyCollection<CombinationFeatureValueDto> FeatureValues);

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
    IReadOnlyCollection<ItemFeatureMappingDto> StockPropertyMappings);

public sealed record ItemDto(
    int Id,
    string Code,
    string Name,
    int? TypeId,
    bool IsActive,
    DateTime? CreateDate,
    DateTime? ModifyDate,
    int? UnitId = null,
    bool Combinations = false,
    decimal TaxRate = 20m);

public sealed record FeatureDto(
    int Id,
    string Name,
    string DataType,
    bool IsActive);

public sealed record FeatureValueDto(
    int Id,
    int PropertyId,
    string PropertyName,
    string Code,
    string Description,
    string Value,
    int SortOrder,
    bool IsActive);

public sealed record ItemFeatureMappingDto(
    int Id,
    int ItemId,
    string ItemCode,
    int FeatureId,
    string FeatureName,
    string FeatureDataType,
    int? FeatureValueId,
    string? FeatureValue,
    bool IsActive);

public sealed record CreateItemRequest(
    string Code,
    string Name,
    int? TypeId = null,
    int? UnitId = null,
    bool Combinations = false,
    decimal TaxRate = 20m);

public sealed record UpdateItemRequest(
    int ItemId,
    string Code,
    string Name,
    int? TypeId = null,
    int? UnitId = null,
    bool Combinations = false,
    decimal TaxRate = 20m);

public sealed record CreateFeatureRequest(
    string Name,
    ConfigurationFieldDataType DataType);

public sealed record CreateItemPropertyLinkRequest(
    int ItemId,
    int PropertyId);

public sealed record CreateFeatureValueRequest(
    int PropertyId,
    string Code,
    string Description,
    string? TextValue,
    decimal? NumericValue,
    DateTime? DateValue,
    int SortOrder);

public sealed record CreateItemFeatureMappingRequest(
    int ItemId,
    int FeatureId,
    int FeatureValueId);

public sealed record ConfigureItemRequest(
    int ItemId,
    bool IsConfigurable,
    IReadOnlyCollection<int> FeatureIds);

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

// ── MachineType (referans veri — Logo Netsis 9 standart tip + ozel tipler) ──
public sealed record MachineTypeDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    bool IsBuiltIn,
    int SortOrder,
    bool IsActive);

public sealed record SaveMachineTypeRequest(
    int? Id,            // null = create, dolu = update
    string Code,        // create'te zorunlu, update'te kullanilmaz (degistirilemez)
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive);

// ── Machine (uretim/depo makineleri) ────────────────────────────────────────
// LocationId FK ile bir lokasyona bagli; iş emri rotalama, kapasite planlama,
// OEE hesabi bu kayitlardan beslenir.

public sealed record MachineDto(
    int Id,
    int LocationId,
    string? LocationCode,         // join — UI display icin (Repository lookup'ta doldurur)
    string? LocationName,
    string MachineCode,
    string? MachineName,
    string? MachineType,
    decimal? HourlyCapacity,
    int SortOrder,
    bool IsActive);

public sealed record CreateMachineRequest(
    int LocationId,
    string MachineCode,
    string? MachineName,
    string? MachineType,
    decimal? HourlyCapacity,
    int SortOrder,
    bool IsActive);

public sealed record UpdateMachineRequest(
    int Id,
    int LocationId,
    string MachineCode,
    string? MachineName,
    string? MachineType,
    decimal? HourlyCapacity,
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

public sealed record ItemUnitDto(
    int Id,
    int ItemId,
    int LineNo,
    int UnitId,
    decimal Multiplier);

public sealed record SaveItemUnitItem(
    int UnitId,
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

// ── BOM (FK-based: ItemId / ConfigId) ──────────────────────────────────────
// Enriched DTO'lar Items/ItemConfiguration JOIN'i ile ItemCode/ItemName/ConfigCode tasir
// (frontend display icin; iskelet veriyle JOIN gerekmez).
public sealed record BOMDto(
    int Id,
    int ItemId,
    string ItemCode,
    string ItemName,
    int? ConfigId,
    string? ConfigCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    string? ImageFitMode,
    int ImageRotation,
    IReadOnlyCollection<BOMLineDto> Lines);

public sealed record BOMLineDto(
    int Id,
    int BOMId,
    int ItemId,
    string ItemCode,
    string ItemName,
    int? ConfigId,
    string? ConfigCode,
    decimal Quantity,
    decimal ScrapRatio,
    Guid LineGuid);

public sealed record CreateBOMRequest(
    int ItemId,
    int? ConfigId,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    string? ImageFitMode,
    int ImageRotation,
    IReadOnlyCollection<BOMLineDto> Lines);

public sealed record UpdateBOMRequest(
    int Id,
    int ItemId,
    int? ConfigId,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    string? ImageFitMode,
    int ImageRotation,
    IReadOnlyCollection<BOMLineDto> Lines);


// Enriched read DTO'lar (Items.code + Items.name JOIN ile)
public sealed record BOMWithNames(
    int Id,
    int ItemId,
    string ItemCode,
    string ItemName,
    int? ConfigId,
    string? ConfigCode,
    string? Description,
    byte[]? ImageData,
    string? ImageMimeType,
    string? ImageFitMode,
    int ImageRotation,
    IReadOnlyCollection<BOMLineWithName> Lines);

public sealed record BOMLineWithName(
    int ItemId,
    string ComponentMaterialCode,
    string ComponentMaterialName,
    int? ConfigId,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio);

// Frontend submit — backend ItemId/ConfigId ile calisir, ama mevcut UI'lar
// materialCode/configCode kullaniyor olabilir. ItemId 0 gelirse service
// ParentMaterialCode'u Items.code uzerinden lookup eder. Yeni UI'lar
// dogrudan ItemId gondermeli.
public sealed record SaveBOMRequest(
    int? Id,
    int ItemId,
    int? ConfigId,
    string? ParentMaterialCode,    // legacy: ItemId 0 ise lookup icin
    string? ConfigurationCode,     // legacy: ConfigId null ise lookup icin
    string? Description,
    string? ImageBase64,
    string? ImageMimeType,
    string? ImageFitMode,
    int ImageRotation,
    IReadOnlyCollection<SaveBOMLineRequest> Lines);

public sealed record SaveBOMLineRequest(
    int ItemId,
    int? ConfigId,
    string? ComponentMaterialCode, // legacy: ItemId 0 ise lookup icin
    string? ComponentConfigCode,   // legacy: ConfigId null ise lookup icin
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

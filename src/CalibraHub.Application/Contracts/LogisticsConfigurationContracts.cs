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
/// "Tanımlı Kombinasyonlar" liste ekranı için DTO — tüm stok kartlarındaki tüm aktif
/// kombinasyonları parent stok bilgisi ve özellik/değer ayrıntısıyla beraber döner.
/// </summary>
public sealed record CombinationListItemDto(
    int ConfigId,
    string Code,
    string? Name,
    int? ItemId,
    string? ItemCode,
    string? ItemName,
    bool IsActive,
    DateTime CreatedDate,
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
    DateTime? Created,
    DateTime? Updated,
    int? UnitId = null,
    bool Combinations = false,
    decimal TaxRate = 20m,
    int? CreatedById = null,
    int? UpdatedById = null,
    string? TrackingType = "None",
    decimal MinStock = 0m,
    bool AutoSerial = false);

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
    decimal TaxRate = 20m,
    string? TrackingType = "None",
    decimal MinStock = 0m,
    bool AutoSerial = false);

public sealed record UpdateItemRequest(
    int ItemId,
    string Code,
    string Name,
    int? TypeId = null,
    int? UnitId = null,
    bool Combinations = false,
    decimal TaxRate = 20m,
    string? TrackingType = "None",
    decimal MinStock = 0m,
    bool AutoSerial = false);

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
    bool IsActive,
    bool IsMachinePark,
    bool IsStorageArea,
    // Depo bazında eksi bakiye izni (üç durumlu): null=şirket varsayılanını devral,
    // true=izin ver (kontrol kapalı), false=engelle (kontrol açık).
    bool? AllowNegativeBalance = null);

public sealed record UnitDto(
    int Id,
    string Code,
    string Name,
    string? IntlCode,
    int SortOrder,
    bool IsActive);

// ── Machine (uretim/depo makineleri) ────────────────────────────────────────
// LocationId FK ile bir lokasyona bagli; iş emri rotalama, kapasite planlama,
// OEE hesabi bu kayitlardan beslenir. Makine tipi/kategori bilgisi widget
// (form-code: MACHINES) uzerinden parametre olarak yonetilir — entity'de yok.

public sealed record MachineDto(
    int Id,
    int LocationId,
    string? LocationCode,         // join — UI display icin (Repository lookup'ta doldurur)
    string? LocationName,
    string Code,
    string? Name,
    decimal? HourlyCapacity,
    int SortOrder,
    bool IsActive);

public sealed record CreateMachineRequest(
    int LocationId,
    string Code,
    string? Name,
    decimal? HourlyCapacity,
    int SortOrder,
    bool IsActive);

public sealed record UpdateMachineRequest(
    int Id,
    int LocationId,
    string Code,
    string? Name,
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
    bool IsActive,
    bool IsMachinePark,
    bool IsStorageArea,
    bool? AllowNegativeBalance = null);

public sealed record CreateUnitRequest(
    string Code,
    string Name,
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
    bool IsActive,
    bool IsMachinePark,
    bool IsStorageArea,
    bool? AllowNegativeBalance = null);

public sealed record UpdateUnitRequest(
    int Id,
    string Code,
    string Name,
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
    int? LocationId,
    string LocationCode,
    string? LocationName,
    string LocationTypeCode,
    bool IsDefault,
    int SortOrder,
    decimal MinStock = 0m);

public sealed record SaveItemLocationItem(
    int LocationId,
    bool IsDefault,
    decimal MinStock = 0m);

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
    IReadOnlyCollection<BOMLineDto> Lines,
    int? RoutingId = null,         // 2026-05-20: header-level Routing FK
    string? RoutingCode = null,    // display
    string? RoutingName = null);   // display

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
    Guid LineGuid,
    string? Note = null);          // 2026-07-05: satır açıklaması (uçtan uca eklendi)

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
    IReadOnlyCollection<BOMLineWithName> Lines,
    int? RoutingId = null,         // 2026-05-20: header-level Routing FK
    string? RoutingCode = null,    // display (JOIN with Routing.Code)
    string? RoutingName = null);   // display (JOIN with Routing.Name)

public sealed record BOMLineWithName(
    int ItemId,
    string ComponentMaterialCode,
    string ComponentMaterialName,
    int? ConfigId,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio,
    string? Note = null);          // 2026-07-05: satır açıklaması (uçtan uca eklendi)

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
    IReadOnlyCollection<SaveBOMLineRequest> Lines,
    int? RoutingId = null,         // 2026-05-20: opsiyonel rota FK
    string? RoutingCode = null);   // 2026-05-20: RoutingId yoksa Code uzerinden lookup (standart rehber fallback)

public sealed record SaveBOMLineRequest(
    int ItemId,
    int? ConfigId,
    string? ComponentMaterialCode, // legacy: ItemId 0 ise lookup icin
    string? ComponentConfigCode,   // legacy: ConfigId null ise lookup icin
    decimal Quantity,
    decimal ScrapRatio,
    string? Note = null);          // 2026-07-05: satır açıklaması (opsiyonel, max 1000)

/// <summary>
/// Repository → service ham satir tasiyici (ExplodeBOMAsync icinde kullanilir).
/// Items JOIN olmadan tek seviye line bilgisi — Item Code/Name ayri lookup'tan
/// (GetItemsByIdsAsync) zenginlestirilir. Boylece N seviyelik BFS'te N×JOIN
/// yapmak yerine 1 toplu Items okumasi yeterli olur.
/// </summary>
public sealed record BOMComponentLineRow(
    int ItemId,
    int? ConfigId,
    decimal Quantity,
    decimal ScrapRatio);

// ── BOM Explode (multi-level patlatma) sonuclari (rapor 2026-05-17 madde 3.3) ──

/// <summary>
/// "X mamulden Y adet uretmek icin tum hammadde/yari mamulun toplam ihtiyaci".
/// Service recursive BFS ile alt-recete agacini gezerek satirlari aggregate eder.
/// </summary>
public sealed record BOMExplodeResultDto(
    int    ParentItemId,
    string ParentItemCode,
    string ParentItemName,
    int?   ConfigId,
    string? ConfigCode,
    decimal Quantity,                              // patlatma icin istenen mamul adedi
    int    MaxDepth,                               // BFS sirasinda ulasilan en derin seviye
    bool   Truncated,                              // depth cap 20'ye ulasildi mi
    IReadOnlyCollection<BOMExplodeLineDto> Lines); // duzlestirilmis satirlar

/// <summary>
/// Patlatma sonucundaki tek bir bilesen — agacin herhangi bir seviyesinden.
/// IsLeaf=true → kendi recetesi yok (gercek hammadde). false → ara mamul.
/// TotalQuantity = parent qty * line qty * (1 + scrapRatio) zinciri sonucu birikim.
/// </summary>
public sealed record BOMExplodeLineDto(
    int    ItemId,
    string ItemCode,
    string ItemName,
    int?   ConfigId,
    string? ConfigCode,
    decimal TotalQuantity,  // birikmis toplam (parent zincirinin tum quantity carpimlari + scrap)
    int    Depth,           // 1 = parent'in dogrudan bileseni; 2 = bilesenin bileseni; ...
    bool   IsLeaf);         // alt recete yok mu? (true ise gercek hammadde)

// ── Where-Used (ters arama: bu malzeme hangi recetelerde geciyor?) ──

/// <summary>
/// Bir bileseni dogrudan kullanan parent BOM'larin bir satiri. 1-seviye
/// (transitive degil) — "Vida Leg'de geciyor; Leg da Masa'da geciyor" sonucu
/// bu surumun kapsami disinda (V2 icin transitive flag eklenebilir).
/// </summary>
public sealed record WhereUsedItemDto(
    int    BOMId,
    int    ParentItemId,
    string ParentItemCode,
    string ParentItemName,
    int?   ParentConfigId,
    string? ParentConfigCode,
    decimal Quantity,
    decimal ScrapRatio);

// ── BOM Maliyet Hesabi (rapor 2026-05-17 madde 3.8) ──

/// <summary>
/// Multi-level BOM maliyet ozeti. Explosion sonucu duzlestirilmis bilesen
/// listesi + her satira fiyat lookup + leaf satirlarinin toplam maliyeti.
/// Mantik: yalniz IsLeaf=true (gercek hammadde) satirlari TotalCost'a katkida
/// bulunur — ara mamuller alt-recetelerinden zaten roll-up edilmis durumda,
/// onlari da toplamak duplicate sayardi.
/// </summary>
public sealed record BOMCostResultDto(
    int    ParentItemId,
    string ParentItemCode,
    string ParentItemName,
    int?   ConfigId,
    string? ConfigCode,
    decimal Quantity,
    int    PriceGroupId,
    int    CurrencyId,
    string? CurrencyCode,
    string? CurrencySymbol,
    string  PriceType,
    DateTime ValidOn,
    decimal TotalCost,
    int    MissingPriceCount,  // fiyati bulunamamis leaf sayisi (UI uyarisi icin)
    int    MaxDepth,
    bool   Truncated,
    IReadOnlyCollection<BOMCostLineDto> Lines);

/// <summary>
/// Maliyet satiri — explode'daki line + fiyat bilgisi. IsLeaf=false ise
/// LineCost her zaman 0 (intermediate item; alt-recetesindeki leaf'ler
/// kendi satirinda gorunur ve toplama katkida bulunur).
/// </summary>
public sealed record BOMCostLineDto(
    int     ItemId,
    string  ItemCode,
    string  ItemName,
    int?    ConfigId,
    string? ConfigCode,
    decimal TotalQuantity,
    int     Depth,
    bool    IsLeaf,
    decimal UnitPrice,    // leaf icin DB fiyati; intermediate icin 0
    decimal LineCost,     // sadece leaf icin > 0 (TotalQuantity * UnitPrice)
    bool    HasPrice);    // leaf + DB'de fiyat bulundu mu

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

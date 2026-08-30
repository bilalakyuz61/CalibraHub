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
    bool AutoSerial = false,
    string? Barcode = null,
    /// <summary>MRP iş emri kırılımı: "PerOrderLine" | "PerOrder" | "Cumulative".</summary>
    string? WorkOrderSplitPolicy = "PerOrderLine");

/// <summary>Toplu belge kilidi işlemi (Kilitle/Kaldır) sonucu — Malzeme Belge Kilitleri ekranı.</summary>
public sealed record BulkItemDocumentLockResultDto(
    int RequestedItemCount,
    int UpdatedItemCount,
    IReadOnlyCollection<string> AppliedDocTypes);

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
    bool AutoSerial = false,
    string? Barcode = null,
    /// <summary>MRP iş emri kırılımı: "PerOrderLine" | "PerOrder" | "Cumulative".</summary>
    string? WorkOrderSplitPolicy = "PerOrderLine");

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
    bool AutoSerial = false,
    string? Barcode = null,
    /// <summary>MRP iş emri kırılımı: "PerOrderLine" | "PerOrder" | "Cumulative".</summary>
    string? WorkOrderSplitPolicy = "PerOrderLine");

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
    // Sayım referansı: sayımda alt kırılımların bu lokasyon üzerinden sayılması.
    bool IsCountReference,
    // Alt kırılımlar tek türde olmalı (raf/hücre karışamaz).
    bool IsSingleChildType,
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
    bool IsCountReference,
    bool IsSingleChildType,
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
    bool IsCountReference,
    bool IsSingleChildType,
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
    string? RoutingName = null,    // display
    string? VersionCode = null);   // 2026-08-11 versiyonlama: null = baz recete

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
    string? RoutingName = null,    // display (JOIN with Routing.Name)
    // 2026-08-06 reçete versiyonlama: NULL = baz reçete; dolu = kullanıcı-türetimli versiyon.
    string? VersionCode = null,
    int? ParentBomId = null);

public sealed record BOMLineWithName(
    int ItemId,
    string ComponentMaterialCode,
    string ComponentMaterialName,
    int? ConfigId,
    string? ComponentConfigCode,
    decimal Quantity,
    decimal ScrapRatio,
    string? Note = null,           // 2026-07-05: satır açıklaması (uçtan uca eklendi)
    // 2026-08-06: bileşen yarı mamulse sabitlenen reçete/versiyon (NULL = bazı takip et).
    int? ComponentBomId = null,
    string? ComponentBomVersionCode = null,  // display — sabitlenen versiyonun kodu
    bool ComponentHasBom = false);           // display — bileşenin kendi reçetesi var mı (versiyon seçici göster)

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
    string? RoutingCode = null,    // 2026-05-20: RoutingId yoksa Code uzerinden lookup (standart rehber fallback)
    // 2026-08-06 versiyonlama: kaydedilen reçetenin kimliği. NULL = baz reçete.
    // Id verilmemişse upsert kimliği artık (ItemId, ConfigId, VersionCode) üçlüsüdür.
    string? VersionCode = null);

public sealed record SaveBOMLineRequest(
    int ItemId,
    int? ConfigId,
    string? ComponentMaterialCode, // legacy: ItemId 0 ise lookup icin
    string? ComponentConfigCode,   // legacy: ConfigId null ise lookup icin
    decimal Quantity,
    decimal ScrapRatio,
    string? Note = null,           // 2026-07-05: satır açıklaması (opsiyonel, max 1000)
    int? ComponentBomId = null);   // 2026-08-06: yarı mamul bileşende sabitlenen reçete/versiyon

/// <summary>
/// Bir mamul+kombinasyonun reçete versiyon listesi satırı (baz dahil).
/// BOMEdit versiyon seçici + iş emri reçete dropdown + bileşen versiyon seçici besler.
/// </summary>
public sealed record BomVersionSummaryDto(
    int Id,
    string? VersionCode,     // NULL = baz reçete
    string? Description,
    int LineCount,
    DateTime Created,
    DateTime? Updated,
    int? ParentBomId);

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
    decimal ScrapRatio,
    int? ComponentBomId = null);   // 2026-08-06: satırda sabitlenen alt reçete/versiyon (NULL = baz)

// ── Kit / Paket Urun (FK-based: ItemId / ConfigId) ─────────────────────────
// Kit = birden fazla stogu tek kod altinda toplayan phantom bundle. ItemKit (versiyonlu
// baslik) + ItemKitLine (bilesen). BOM deseninin satis klonu — rota/fire yok, versiyon +
// fiyat modu var. Kit'in kendisi Items'ta TypeId=10 (Kit) tipinde bir malzeme kartidir.
// PriceMode 4 deger alir (2026-08-03, PageComment Seq 1078 — KitPriceMode ile birebir):
//   "FixedPackage"   — kit'in tek elle girilen sabit fiyati (FixedPrice). Sunucu ezmez.
//   "FixedComponent" — her bilesenin kit tanimindaki ELLE fiyati (ItemKitLineDto.UnitPrice) x miktar toplami.
//   "ListPackage"    — kit kartinin KENDI fiyat listesi (Genel Liste) fiyati, satis aninda cozulur.
//   "ListComponent"  — bilesenlerin fiyat listesi fiyatlari x miktar toplami, satis aninda (eski "RollUp").
public sealed record ItemKitDto(
    int Id,
    int ItemId,                // kit'in kendisi (Items.id, TypeId=10)
    string ItemCode,
    string ItemName,
    int VersionNo,
    string PriceMode,          // KitPriceMode: yukaridaki 4 deger
    decimal? FixedPrice,       // yalniz FixedPackage modda dolu
    string? Description,
    IReadOnlyCollection<ItemKitLineDto> Lines);

public sealed record ItemKitLineDto(
    int Id,
    int ItemKitId,
    int ItemId,                // bilesen (Items.id)
    string ItemCode,
    string ItemName,
    int? ConfigId,
    string? ConfigCode,
    decimal Quantity,
    Guid LineGuid,
    string? Note = null,
    decimal? UnitPrice = null,   // yalniz kit PriceMode=FixedComponent iken dolu (elle bilesen fiyati)
    int? UnitId = null,          // secili olcu birimi (Unit.Id) — NULL = bilesenin kendi varsayilan birimi
    string? UnitCode = null);    // goruntuleme icin birim kodu (JOIN ile doldurulur)

// Frontend submit — backend ItemId/ConfigId ile calisir. ItemId 0 gelirse service
// materialCode uzerinden lookup eder (legacy UI); yeni UI dogrudan ItemId gonderir.
public sealed record SaveItemKitRequest(
    int? Id,
    int ItemId,
    string? MaterialCode,       // legacy: ItemId 0 ise kit karti lookup'i icin
    string PriceMode,           // KitPriceMode: FixedPackage | FixedComponent | ListPackage | ListComponent
    decimal? FixedPrice,        // yalniz FixedPackage modda anlamli
    string? Description,
    IReadOnlyCollection<SaveItemKitLineRequest> Lines);

public sealed record SaveItemKitLineRequest(
    int ItemId,
    int? ConfigId,
    string? ComponentMaterialCode,  // legacy: ItemId 0 ise lookup icin
    string? ComponentConfigCode,    // legacy: ConfigId null ise lookup icin
    decimal Quantity,
    string? Note = null,
    decimal? UnitPrice = null,      // yalniz kit PriceMode=FixedComponent iken anlamli (elle bilesen fiyati)
    int? UnitId = null);            // secili olcu birimi (Unit.Id) — NULL = bilesenin kendi varsayilan birimi

// ── Kit revizyon gecmisi (ItemKitRevision) ─────────────────────────────────
// Kit her kaydedildiginde o anki icerik JSON snapshot olarak saklanir. RevisionNo,
// snapshot alindigi andaki ItemKit.VersionNo'dur — belgelerdeki
// DocumentLineKitComponent.KitVersionNo dogrudan bu revizyona isaret eder.
// Salt-okunur gecmis: geri yukleme (restore) YOK.
public sealed record ItemKitRevisionSummaryDto(
    int Id,
    int ItemKitId,
    int RevisionNo,
    string PriceMode,
    decimal? FixedPrice,
    int LineCount,
    string? CreatedBy,          // kullanici tam adi (JOIN ile) — null = sistem/bilinmiyor
    DateTime CreatedAt);

public sealed record ItemKitRevisionDetailDto(
    int Id,
    int ItemKitId,
    int RevisionNo,
    string PriceMode,
    decimal? FixedPrice,
    string? Description,
    string? CreatedBy,
    DateTime CreatedAt,
    IReadOnlyList<ItemKitRevisionLineDto> Lines);

// Snapshot JSON'in icindeki bilesen satiri — kod/ad DAHIL saklanir ki gecmis,
// bilesen karti sonradan degisse/silinse bile okunabilir kalsin.
public sealed record ItemKitRevisionLineDto(
    int ItemId,
    string ItemCode,
    string ItemName,
    int? ConfigId,
    string? ConfigCode,
    decimal Quantity,
    int? UnitId,
    string? UnitCode,
    decimal? UnitPrice,
    string? Note);

// Snapshot JSON kok nesnesi (ItemKitRevision.Snapshot icerigi).
public sealed record ItemKitRevisionSnapshot(
    int ItemId,
    string ItemCode,
    string ItemName,
    int VersionNo,
    string PriceMode,
    decimal? FixedPrice,
    string? Description,
    IReadOnlyList<ItemKitRevisionLineDto> Lines);

// ── Kit snapshot kaynagi (Faz 2) — belge kaydinda aktif ItemKit icerigi ────
// Bir kit belge kalemine eklendiginde bu icerik DocumentLineKitComponent'e dondurulur.
// PriceMode (Seq 1073 Part B, genisletildi Seq 1078) — ListComponent/ListPackage modundaki
// kit'lerin birim fiyati DocumentService'te server-truth olarak hesaplanir; FixedPackage
// modda kullanilmaz.
public sealed record KitSnapshotSourceDto(
    int KitItemId,
    int VersionNo,
    string PriceMode,
    IReadOnlyList<KitSnapshotComponentDto> Components);

// UnitPrice — yalniz kaynak kit PriceMode=FixedComponent iken dolu (ItemKitLine.UnitPrice,
// elle bilesen fiyati); ListComponent/ListPackage/FixedPackage'da NULL (fiyat listesinden
// veya kit'in kendi fiyatindan canli cozulur).
public sealed record KitSnapshotComponentDto(
    int ComponentItemId,
    int? ConfigId,
    decimal Quantity,
    decimal? UnitPrice = null);

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

// ═══════════════════════════════════════════════════════════════════════════
// Reçete Ağacı (çok seviyeli tek ekranda düzenleme) — 2026-08-29
//
// ExplodeBOM'dan FARKI: patlatma sonucu DÜZLEŞTİRİR (ItemId'ye göre toplar),
// dolayısıyla ata-çocuk yapısı kaybolur ve düzenleme için kullanılamaz. Burada
// hiyerarşi korunur; her düğüm kendi reçetesini (BomId) ve ata satırındaki
// miktar/fire değerlerini taşır.
//
// YENİ TABLO YOK: ağaç, mevcut BOM/BOMLine kayıtlarının okunma biçimidir.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Ağaçtaki tek düğüm. Kök = mamulün kendisi (ParentLine alanları anlamsızdır).
/// </summary>
public sealed record BomTreeNodeDto(
    // Bu düğümün malzemesi.
    int ItemId,
    string ItemCode,
    string ItemName,
    int? ConfigId,
    string? ConfigCode,
    // Ata satırındaki miktar (kök için 1).
    decimal Quantity,
    decimal ScrapRatio,
    string? Note,
    // Bu düğümün İZLENEN reçetesi. NULL = bu malzemenin reçetesi yok (yaprak).
    int? BomId,
    // Reçete versiyon kodu (NULL = baz reçete).
    string? BomVersionCode,
    // Ata satırında reçete SABİTLENMİŞ mi (BOMLine.ComponentBomId dolu mu).
    bool IsPinned,
    // Bu reçeteyi izleyen ata satırı sayısı. >1 ise dugum PAYLASIMLIDIR: burada
    // yapilan degisiklik kaydedilirken otomatik versiyon turetilir (kullanici karari
    // 2026-08-29), böylece diğer mamuller etkilenmez.
    int ReferenceCount,
    // Döngü nedeniyle kesildi mi (A→B→A). Kesilen dal genişletilemez.
    bool IsCycle,
    IReadOnlyList<BomTreeNodeDto> Children,
    // Malzeme tipi (Items.TypeId) — ağaçta tip ikonu için. Kayıtta tip yoksa null.
    // Listenin SONUNA eklendi: positional record'da araya eklemek mevcut tüm
    // kurulum noktalarını sessizce kaydırırdı.
    int? TypeId = null);

/// <summary>Ağaç okuma sonucu — kök düğüm + gezinme sırasında kesilen dal uyarısı.</summary>
public sealed record BomTreeDto(
    BomTreeNodeDto Root,
    int MaxDepth,
    bool Truncated);

// ── Kaydetme ──

/// <summary>
/// Ağaç kaydetme isteği. İstemci TÜM ağacı gönderir; sunucu her düğümü depodaki
/// haliyle karşılaştırıp yalnız DEĞİŞENLERİ yazar. Değişmeyen düğümün atlanması
/// sadece performans değil DOĞRULUK meselesidir: aksi halde ağacı açıp kaydetmek
/// paylaşımlı her düğüm için gereksiz versiyon türetirdi.
/// </summary>
public sealed record SaveBomTreeRequest(
    SaveBomTreeNode Root);

public sealed record SaveBomTreeNode(
    int ItemId,
    int? ConfigId,
    // Ata satırındaki miktar/fire (kök için yok sayılır).
    decimal Quantity,
    decimal ScrapRatio,
    string? Note,
    // Düzenlenen mevcut reçete. NULL = bu düğüm için henüz reçete yok.
    int? BomId,
    IReadOnlyList<SaveBomTreeNode> Children);

/// <summary>Kaydetme sonrası tek düğüm raporu — hangi reçete yazıldı, versiyon türedi mi.</summary>
public sealed record BomTreeSaveNoteDto(
    int ItemId,
    string ItemCode,
    int BomId,
    // "created" | "updated" | "derived" | "unchanged"
    string Action,
    string? VersionCode,
    // Türetme yapıldıysa: kaç ata satırı paylaşıyordu.
    int ReferenceCount);

public sealed record SaveBomTreeResultDto(
    int RootBomId,
    IReadOnlyList<BomTreeSaveNoteDto> Notes);

/// <summary>
/// Bir reçeteyi İZLEYEN ata satırının sahibi (Reçete Ağacı "Paylaşımlı ×N" detayı).
/// Kullanıcı sayıyı görüp "hangi ürünler?" diye soruyordu; bu liste onu yanıtlar.
/// </summary>
public sealed record BomReferenceDto(
    int BomId,            // ata reçetenin kimliği
    int ItemId,           // ata mamul
    string ItemCode,
    string ItemName,
    string? VersionCode,  // ata reçetenin versiyonu (NULL = baz)
    bool IsPinned);       // satır bu reçeteye SABİTLENMİŞ mi (yoksa bazı izliyor)

/// <summary>
/// Bir malzemenin TÜM reçeteleri (baz + sürümler) ve her birini kullanan ata mamuller.
/// Reçete Ağacında "bu yarı mamulün hangi sürümü hangi ürüne bağlı" sorusunu tek
/// bakışta cevaplar — sürüm sürüm ayrı ekran gezmeye gerek kalmaz.
/// </summary>
public sealed record BomUsageGroupDto(
    int BomId,
    string? VersionCode,      // NULL = baz reçete
    int LineCount,
    bool IsCurrent,           // ağaçta şu an izlenen reçete mi
    IReadOnlyList<BomReferenceDto> References);

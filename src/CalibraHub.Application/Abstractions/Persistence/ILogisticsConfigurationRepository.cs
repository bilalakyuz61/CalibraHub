using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ILogisticsConfigurationRepository
{
    Task<IReadOnlyCollection<Item>> GetItemsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sadece verilen ID set'i icin Items satirlarini doner — full-table snapshot
    /// alternatifi. SaveBOMAsync ve diger Save akislarinda 50K malzemeli bir
    /// kurumda her save'de 50K satir okumamak icin (rapor 2026-05-17 madde 3.10).
    /// Empty/duplicate ID'ler tolere edilir (DISTINCT + bos liste -> bos sonuc).
    /// </summary>
    Task<IReadOnlyCollection<Item>> GetItemsByIdsAsync(
        IEnumerable<int> ids, CancellationToken cancellationToken);

    Task<(IReadOnlyCollection<Item> Items, int TotalCount)> GetItemsPagedAsync(string? search, int offset, int pageSize, CancellationToken cancellationToken, string? groupCode = null);
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
    Task<IReadOnlyCollection<ItemFeature>> GetPropertiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FeatureValue>> GetPropertyValuesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemFeatureMapping>> GetStockPropertyMappingsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemConfiguration>> GetItemConfigurationsAsync(CancellationToken cancellationToken);
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
    Task<IReadOnlyCollection<ItemUnit>> GetItemUnitsAsync(int itemId, CancellationToken cancellationToken);
    Task SaveItemUnitsAsync(int itemId, IReadOnlyCollection<ItemUnit> conversions, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ItemLocation>> GetItemLocationsAsync(int itemId, CancellationToken cancellationToken);
    Task SaveItemLocationsAsync(int itemId, IReadOnlyCollection<ItemLocation> locations, CancellationToken cancellationToken);
    // Planlama: belge bazında malzeme kilidi (DocType kod listesi)
    /// <summary>
    /// Stok kartının aktiflik durumunu değiştirir. Item.IsActive private setter'da olduğu
    /// ve Create/UpdateItemRequest bu alanı taşımadığı için ayrı metod — içe aktarımın
    /// kaynak "Durum" kolonunu yazabilmesi buna bağlı.
    /// </summary>
    Task SetItemActiveAsync(int itemId, bool isActive, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetItemDocumentLocksAsync(int itemId, CancellationToken cancellationToken);
    Task SaveItemDocumentLocksAsync(int itemId, IReadOnlyCollection<string> docTypes, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<int>> GetLockedItemIdsByDocTypeAsync(string docType, CancellationToken cancellationToken);
    // 2026-07-25 — Malzeme Belge Kilitleri ekranı (ItemDocumentLockController):
    /// <summary>Tüm kilitli kayıtları tek sorguda ItemId → DocType[] olarak döner (board liste ekranı N+1 önleme).</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyCollection<string>>> GetAllItemDocumentLocksGroupedAsync(CancellationToken cancellationToken);
    /// <summary>DocType → kilitli (distinct) malzeme sayısı — hızlı filtre çipleri için.</summary>
    Task<IReadOnlyDictionary<string, int>> GetItemDocumentLockCountsByDocTypeAsync(CancellationToken cancellationToken);
    /// <summary>GetItemsPagedAsync ile aynı şekil/sözleşme; ek olarak <paramref name="lockedDocType"/> verilirse
    /// yalnız o belge tipinde kilitli malzemeler döner (null/boş = filtresiz, GetItemsPagedAsync ile eşdeğer sonuç kümesi).</summary>
    Task<(IReadOnlyCollection<Item> Items, int TotalCount)> GetItemsPagedByDocumentLockAsync(
        string? lockedDocType, string? search, int offset, int pageSize, CancellationToken cancellationToken);
    Task NullifyItemLocationsByLocationIdAsync(int locationId, CancellationToken cancellationToken);
    Task NullifyLocationHistoricalFkRefsAsync(int locationId, CancellationToken cancellationToken);
    /// <summary>
    /// Lokasyon silme guard'ı — bu lokasyonu kullanan personel sayısı + örnek isim listesi
    /// (bkz. FK_Personnel_Location). Personelin lokasyonunu sessizce NULL'lamak veri kaybı
    /// sayıldığı için (kimin nerede çalıştığı bilgisi) nullify yerine guard tercih edildi.
    /// </summary>
    Task<(int Count, IReadOnlyCollection<string> SampleNames)> GetPersonnelUsageByLocationIdAsync(int locationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LocationType>> GetLocationTypesAsync(CancellationToken cancellationToken);
    Task<int> UpsertLocationTypeAsync(LocationType type, CancellationToken cancellationToken);
    Task DeleteLocationTypeAsync(int id, CancellationToken cancellationToken);
    Task<int> CountLocationsOfTypeAsync(string code, CancellationToken cancellationToken);
    Task<int> RenameLocationTypeCodeAsync(string oldCode, string newCode, CancellationToken cancellationToken);

    // ── Machine (uretim/depo makineleri) ─────────────────────────────
    Task<IReadOnlyCollection<Machine>> GetMachinesAsync(CancellationToken cancellationToken);
    Task<int> AddMachineAsync(Machine machine, CancellationToken cancellationToken);
    Task UpdateMachineAsync(Machine machine, CancellationToken cancellationToken);
    Task DeleteMachineAsync(int machineId, CancellationToken cancellationToken);

    Task<int> AddPropertyAsync(ItemFeature property, CancellationToken cancellationToken);
    Task ReplaceFeatureStockLinksAsync(
        int featureId,
        (int ItemId, int[] AllowedValueIds, bool PrintDescriptionInDesign)[] items,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<int>> GetUsedFeatureIdsInCombinationsAsync(int itemId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<(int FeatureId, int ValueId)>> GetUsedFeatureValueIdsInCombinationsAsync(int itemId, CancellationToken cancellationToken);
    Task<int> GetCombinationCountForItemAsync(int itemId, CancellationToken cancellationToken);
    Task UpdateItemFeatureAsync(int id, string name, string dataType, string? unitOfMeasure, bool visibleInDesign, bool isActive, CancellationToken cancellationToken);
    Task DeleteItemFeatureAsync(int id, CancellationToken cancellationToken);
    Task AddPropertyValueAsync(FeatureValue propertyValue, CancellationToken cancellationToken);
    Task AddStockPropertyMappingAsync(ItemFeatureMapping mapping, CancellationToken cancellationToken);
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
    /// <summary>Bir stok kartina bagli ozellik linklerini ItemFeatureMappings tablosunda full replace eder.
    /// Her feature icin: 1 header satiri (FeatureValueId NULL) + 0..N value satiri (deger kisitlamasi).</summary>
    Task ReplaceStockFeatureLinksAsync(
        string stockCode,
        (int FeatureId, bool PrintDescriptionInDesign, int[] AllowedValueIds)[] items,
        CancellationToken cancellationToken);
    Task UpdateProductFeatureAsync(int id, string name, string dataType, string? unitOfMeasure, bool visibleInDesign, CancellationToken cancellationToken);
    Task DeleteProductFeatureAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductValueAsync(int id, CancellationToken cancellationToken);
    Task DeleteProductConfigAsync(int id, CancellationToken cancellationToken);
    Task UpdateProductConfigDescriptionAsync(int id, string? description, CancellationToken cancellationToken);
    Task UpdateStockPropertyMappingValueAsync(
        int mappingId,
        int featureValueId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BOM>> GetBOMsAsync(CancellationToken cancellationToken);
    // FK-based lookup: ItemId + opsiyonel ConfigId. Repository, Items + ItemConfiguration JOIN
    // ile enriched BOMWithNames doner (frontend display icin ItemCode/ItemName/ConfigCode tasir).
    Task<BOMWithNames?> GetBOMByItemAsync(int itemId, int? configId, CancellationToken cancellationToken);
    // 2026-08-06 versiyonlama: GetBOMByItemAsync BAZ receteyi doner; belirli versiyon Id ile okunur.
    Task<BOMWithNames?> GetBOMByIdWithNamesAsync(int bomId, CancellationToken cancellationToken);
    /// <summary>Mamul+kombinasyonun tum aktif recete satirlari (baz + versiyonlar), baz once.</summary>
    Task<IReadOnlyCollection<BomVersionSummaryDto>> GetBomVersionsAsync(
        int itemId, int? configId, CancellationToken cancellationToken);
    Task<int> AddBOMAsync(BOM tree, CancellationToken cancellationToken);
    Task UpdateBOMAsync(BOM tree, CancellationToken cancellationToken);

    /// <summary>
    /// 2026-05-20: Routing.Code -> Routing.Id lookup (standart rehber fallback).
    /// SaveBOMRequest.RoutingId null gelir ama RoutingCode dolu ise service bunu
    /// cagirip int FK'yi cozer. Bulunamazsa null doner (service hata mesajini yansitir).
    /// </summary>
    Task<int?> GetRoutingIdByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Soft delete — UPDATE [BOM] SET [IsActive] = 0. Fiziksel silme yapilmaz,
    /// boylece eski iş emirleri orphan kalmaz (rapor 2026-05-17 madde 3.6).
    /// <paramref name="userId"/> UpdatedById audit alanina yazilir.
    /// </summary>
    Task DeleteBOMAsync(int id, int? userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cycle (dongusel bagimlilik) korumasi icin yardimcı:
    /// Verilen parent ItemId'nin AKTİF reçetesinde geçen tüm bileşen ItemId'lerini doner
    /// (1 seviye). BFS gezme caller (Domain.BOM.EnsureNoCycle) icinde yapilir.
    /// Recete yoksa bos liste doner.
    /// </summary>
    Task<IReadOnlyCollection<int>> GetBOMComponentItemIdsAsync(
        int parentItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Multi-level explosion icin: verilen parent ItemId'nin AKTIF recetesindeki
    /// tum bilesen satirlarini Qty + Scrap bilgisiyle birlikte doner (1 seviye).
    /// Service ExplodeBOMAsync recursive BFS sirasinda her dugum icin bu metodu cagirir.
    /// Recete yoksa bos liste doner. (Rapor 2026-05-17 madde 3.3.)
    /// </summary>
    Task<IReadOnlyCollection<BOMComponentLineRow>> GetBOMComponentLinesAsync(
        int parentItemId, CancellationToken cancellationToken);

    /// <summary>2026-08-06: belirli bir recete/versiyonun (BOM.Id) bilesen satirlari —
    /// satirda sabitlenen alt versiyon (ComponentBomId) cozumlemesi icin.</summary>
    Task<IReadOnlyCollection<BOMComponentLineRow>> GetBOMComponentLinesByBomIdAsync(
        int bomId, CancellationToken cancellationToken);

    /// <summary>
    /// 2026-08-29 (Recete Agaci): verilen recetelerin KAC ata satiri tarafindan
    /// izlendigini doner (BomId -> sayi). Iki yol sayilir:
    ///   (a) satir bu receteye SABITLENMIS  -> BOMLine.ComponentBomId = BOM.Id
    ///   (b) satir sabitlenmemis VE bu recete o malzemenin BAZ recetesi
    ///       -> ComponentBomId IS NULL AND ItemId/ConfigId esler AND VersionCode IS NULL
    /// Sonuc 1'den buyukse recete PAYLASIMLIDIR; agacta duzenlenirse otomatik
    /// versiyon turetilir (yoksa duzenleme diger mamulleri de sessizce degistirirdi).
    /// Listede olmayan/hic referansi olmayan Id sonucta YER ALMAZ (caller 0 varsayar).
    /// </summary>
    Task<IReadOnlyDictionary<int, int>> GetBomReferenceCountsAsync(
        IReadOnlyCollection<int> bomIds, CancellationToken cancellationToken);

    /// <summary>
    /// 2026-08-30: <see cref="GetBomReferenceCountsAsync"/> ile AYNI eslesme kuralinin
    /// ayrintili hali — sayiyi degil, receteyi izleyen ATA MAMULLERI doner.
    /// "Paylasimli x3" rozetine tiklayinca hangi urunler oldugunu gostermek icin.
    /// </summary>
    Task<IReadOnlyList<BomReferenceDto>> GetBomReferencesAsync(
        int bomId, CancellationToken cancellationToken);

    /// <summary>
    /// 2026-08-29 (Recete Agaci): verilen malzemelerin BAZ recete Id'si (VersionCode IS NULL,
    /// IsActive=1) — ItemId+ConfigId anahtariyla. Agac gezinirken her dugum icin ayri
    /// sorgu atmamak icin toplu. Recetesi olmayan malzeme sonucta yer almaz.
    /// </summary>
    Task<IReadOnlyDictionary<(int ItemId, int ConfigKey), (int BomId, string? VersionCode)>>
        GetBaseBomIdsAsync(IReadOnlyCollection<(int ItemId, int? ConfigId)> keys, CancellationToken cancellationToken);

    // ── Kit / Paket Urun ─────────────────────────────────────────────
    /// <summary>Kit malzeme kartinin AKTIF icerigini (MAX VersionNo, IsActive=1)
    /// Items/ItemConfiguration JOIN'li enriched doner. Yoksa null.</summary>
    Task<ItemKitDto?> GetKitByItemAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>Yeni kit ekler (VersionNo=1) — header + satirlar transaction icinde. Yeni Id doner.</summary>
    Task<int> AddKitAsync(ItemKit kit, CancellationToken cancellationToken);

    /// <summary>Mevcut kit'i gunceller: VersionNo++ (revizyon), satirlar DELETE+INSERT.</summary>
    Task UpdateKitAsync(ItemKit kit, CancellationToken cancellationToken);

    /// <summary>Kit icerigini soft-delete (ItemKit.IsActive=0). Fiziksel silme yok —
    /// gecmis belgeler snapshot tasir.</summary>
    Task DeleteKitAsync(int itemId, int? userId, CancellationToken cancellationToken);

    /// <summary>Kit kartinin (Items.id) revizyon gecmisi ozeti — RevisionNo azalan.</summary>
    Task<IReadOnlyList<ItemKitRevisionSummaryDto>> GetKitRevisionsAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>Tek bir revizyonun detayi (JSON snapshot'tan cozulmus bilesen listesiyle). Yoksa null.</summary>
    Task<ItemKitRevisionDetailDto?> GetKitRevisionDetailAsync(int revisionId, CancellationToken cancellationToken);

    /// <summary>
    /// Where-used (ters arama): bir bileseni DOĞRUDAN kullanan parent BOM'lari doner.
    /// 1-seviye (transitive degil). Liste ekraninda "bu malzeme hangi recetelerde
    /// geciyor" sorusunu cevaplar. Sadece IsActive=1 BOM'lar dahil.
    /// </summary>
    Task<IReadOnlyCollection<WhereUsedItemDto>> GetWhereUsedAsync(
        int componentItemId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsByMaterialCodeAsync(
        string materialCode, CancellationToken cancellationToken);
    /// <summary>
    /// Tüm aktif kombinasyonlar — "Tanımlı Kombinasyonlar" liste ekranı için.
    /// Parent stok bilgisi (Items JOIN ile ItemId/ItemCode/ItemName) ve özellik/değer
    /// ayrıntısı dahil tek call'da döner.
    /// </summary>
    Task<IReadOnlyCollection<CombinationListItemDto>> GetAllCombinationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroup>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken);
    Task AddMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken);
    Task UpdateMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken);
    Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken);
    /// <summary>
    /// 2026-05-24: Liste/filter ekranlari icin batch query — N+1 onlemek amaciyla.
    /// Doner: ItemId → o item'in mapping listesi (SlotOrder sirali).
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<MaterialGroupMappingDto>>> GetMaterialGroupMappingsBatchAsync(
        IReadOnlyCollection<int> stockCardIds, CancellationToken cancellationToken);
    Task SaveMaterialGroupMappingsAsync(int stockCardId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken);

    /// <summary>
    /// 2026-05-24: Liste/filter ekranlari icin batch — verilen item'larin ItemUnits liste.
    /// Doner: ItemId → o item'e atanmis ItemUnit listesi.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<ItemUnit>>> GetItemUnitsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken);

    /// <summary>
    /// 2026-05-24: Liste/filter ekranlari icin batch — verilen item'larin ItemFeatureMappings.
    /// Doner: ItemId → o item'in feature mapping listesi (header + value satirlari).
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<ItemFeatureMapping>>> GetItemFeatureMappingsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken);
}

package com.calibrahub.app.data

import com.squareup.moshi.JsonClass
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

/**
 * Depo (Warehouse) modülü Retrofit interface'i — /api/mobile/warehouse yolu altında.
 * CalibraApi ile aynı auth deseni: cookie + X-Requested-With (bkz. SessionManager.buildApi()).
 */
interface WarehouseApi {

    @GET("api/mobile/warehouse/locations")
    suspend fun locations(): Response<List<WarehouseLocationDto>>

    // 400 {error} → kod boş; 404 {error} → bulunamadı (WarehouseRepository bu gövdeyi ayrıştırır).
    @GET("api/mobile/warehouse/stock")
    suspend fun stock(@Query("code") code: String): Response<StockQueryDto>

    // Rehber (malzeme arama) — kod VEYA ad ile kısmi eşleşme, backend LIKE ile arar
    // (MobileWarehouseApiController, ILogisticsConfigurationService.GetItemsPagedAsync
    // sarmalayıcısı). Sözleşme koordinatör tarafından KİLİTLİ: 200 [{id,code,name,unit,barcode}]
    // (barcode: 2026-07-16 eklendi — gerçek barkod, tanımlı değilse backend code'a düşürür,
    // asla boş/eksik dönmez). MaterialPickerField bu sonuçları listeleyip seçilen code ile
    // stock(code)'u tetikler; kamera taramasında barcode alanı KESİN eşleşme (case-insensitive)
    // için kullanılır.
    @GET("api/mobile/warehouse/items/search")
    suspend fun searchItems(
        @Query("q") q: String,
        @Query("take") take: Int = 20
    ): Response<List<ItemSearchDto>>

    // Increment 2a — depo giriş/çıkış belgesi oluşturma (sözleşme KİLİTLİ,
    // MobileWarehouseApiController.SaveStockDocAsync). İş kuralı reddi de HTTP 200 döner
    // ({ok:false, error}); yalnız yetki reddi 403 + {ok, message, error} gövdesidir
    // (Retrofit'te errorBody'ye düşer, WarehouseRepository ayrıştırır).
    @POST("api/mobile/warehouse/stock-in")
    suspend fun stockIn(@Body req: StockDocRequest): Response<StockDocResponse>

    @POST("api/mobile/warehouse/stock-out")
    suspend fun stockOut(@Body req: StockDocRequest): Response<StockDocResponse>

    // Increment 2b — depo transfer belgesi (sözleşme KİLİTLİ, koordinatör tarafından verildi;
    // backend paralel ajan tarafından ekleniyor). body: {fromLocationId, toLocationId, lines:
    // [{itemId, quantity}], note?}. Basari: 200 {ok:true, documentNumber}. Is kurali reddi/
    // validasyon: 400 veya 404 {error} (stock-in/out'un "200 ok:false" deseninden FARKLI —
    // sozlesme aynen budur). WarehouseRepository.transfer() 403 dahil tum hata govdelerini
    // parseApiError ile tek kanala normalize eder.
    @POST("api/mobile/warehouse/transfer")
    suspend fun transfer(@Body req: TransferRequest): Response<TransferResponse>

    // Increment 2b — sayim belgesi (sözleşme KİLİTLİ). body: {locationId, lines:[{itemId,
    // countedQuantity}], note?}. Basari: 200 {ok:true, documentNumber, applied, id}. applied=false
    // → belge taslak kalmis (ör. onay akisi parametresi acik); ekran farkli mesaj gosterir.
    // id: taslak sayim belge Id'si — Sayim Yansit akisinda apply endpoint'ine bu Id gonderilir
    // (2026-07-16 sözleşme genişletmesi, koordinatör). Hata: 400 {error}.
    @POST("api/mobile/warehouse/inventory-count")
    suspend fun inventoryCount(@Body req: InventoryCountRequest): Response<InventoryCountResponse>

    // Sayım Yansıt (2026-07-16, koordinatör sözleşmesi KİLİTLİ) — taslak (applied=false) kalmış
    // bir sayım belgesini stoğa uygular. Body yok, yalnızca path'teki belge Id'si. Başarı:
    // 200 {ok:true, writtenCount}. İdempotent DEĞİL sunucu tarafında — ikinci kez çağrılırsa
    // 400 {error} ile reddedilir, mesaj olduğu gibi gösterilir (bkz. CountScreen).
    @POST("api/mobile/warehouse/inventory-count/{id}/apply")
    suspend fun applyInventoryCount(@Path("id") id: Int): Response<InventoryCountApplyResponse>

    // Cari (contact) rehberi — Alış/Satış İrsaliyesi ekranlarının cari seçici alanı.
    // Kod VEYA ad ile kısmi eşleşme (backend LIKE), searchItems ile AYNI arama deseni ama
    // ayrı entity/endpoint. Sözleşme (koordinatör KİLİTLİ, 2026-07-16): 200 [{id,code,name}].
    @GET("api/mobile/warehouse/contacts/search")
    suspend fun searchContacts(
        @Query("q") q: String,
        @Query("take") take: Int = 20
    ): Response<List<ContactSearchDto>>

    // Alış/Satış İrsaliyesi (2026-07-16, koordinatör sözleşmesi KİLİTLİ) — docType "purchase"|
    // "sales" ile TEK endpoint iki yönü de kapsar. Sunucu satırları açık siparişlere FIFO
    // bağlar (parametreye göre); "bağlantısız yasak" parametresi açıksa eşleşmeyen kayıt
    // TÜMDEN 400/404 {error} ile reddedilir (stock-in/out'un "200 ok:false" deseninden FARKLI
    // — transfer/inventory-count ile AYNI desen, WarehouseRepository.delivery() böyle ayrıştırır).
    @POST("api/mobile/warehouse/delivery")
    suspend fun delivery(@Body req: DeliveryRequest): Response<DeliveryResponse>

    // ──────────────────────────────────────────────────────────────────
    // Seri/Lot seçici GET'leri (2026-07-17, koordinatör FINAL kontrat) — DeliverySerialLotSection.kt
    // ──────────────────────────────────────────────────────────────────
    // Müsait seriler (FIFO sıralı, satış seri seçim diyaloğu + kamera KESİN eşleşme doğrulaması).
    // locationId/q opsiyonel — DeliveryScreen'de lokasyon seçimi olmadığından locationId
    // her zaman null gönderilir (Retrofit null @Query'yi URL'den atlar); q dolarsa serialNo'da
    // ara-filtrele.
    @GET("api/mobile/warehouse/items/{itemId}/serials")
    suspend fun itemSerials(
        @Path("itemId") itemId: Int,
        @Query("locationId") locationId: Int? = null,
        @Query("q") q: String? = null,
        @Query("take") take: Int = 50
    ): Response<List<ItemSerialDto>>

    // Müsait lotlar (FEFO sıralı, satış Lot alanı öneri listesi).
    @GET("api/mobile/warehouse/items/{itemId}/lots")
    suspend fun itemLots(
        @Path("itemId") itemId: Int,
        @Query("locationId") locationId: Int? = null,
        @Query("take") take: Int = 20
    ): Response<List<ItemLotDto>>

    // ──────────────────────────────────────────────────────────────────
    // FAZ C(a) — Açık Siparişler → Teslim Et (2026-07-17, koordinatör FINAL kontrat)
    // ──────────────────────────────────────────────────────────────────
    @GET("api/mobile/warehouse/open-orders")
    suspend fun openOrders(
        @Query("docType") docType: String,
        @Query("q") q: String? = null,
        @Query("take") take: Int = 50
    ): Response<List<OpenOrderSummaryDto>>

    @GET("api/mobile/warehouse/open-orders/{id}")
    suspend fun openOrderDetail(@Path("id") id: Int): Response<OpenOrderDetailDto>

    // ──────────────────────────────────────────────────────────────────
    // FAZ C(b) — Taslak Sayımlar (2026-07-17, koordinatör FINAL kontrat)
    // ──────────────────────────────────────────────────────────────────
    // applied=false kalmış sayım belgeleri — DraftCountsScreen listesi; Yansıt aksiyonu
    // MEVCUT applyInventoryCount(id) endpoint'ini (Increment 2b) aynen kullanır.
    @GET("api/mobile/warehouse/inventory-counts")
    suspend fun inventoryCounts(@Query("take") take: Int = 50): Response<List<DraftInventoryCountDto>>
}

// ───────────────────────────────────────────────────────────────────────
// DTO'lar — backend MobileApiController /warehouse/* dönüş şekilleriyle eşleşir
// ───────────────────────────────────────────────────────────────────────

@JsonClass(generateAdapter = true)
data class WarehouseLocationDto(val id: Int, val code: String, val name: String)

/**
 * Rehber arama sonucu satırı — GET items/search yanıtı (id/code/name/unit/barcode,
 * koordinatör sözleşmesi). barcode: gerçek barkod değeri; malzemede tanımlı barkod yoksa
 * backend code'u döner (asla eksik gelmez) — Moshi default'u yalnızca savunma amaçlı
 * (beklenmedik şekilde alan atlanırsa bile non-null kalsın diye).
 *
 * [trackingType]/[autoSerial] (2026-07-17 EK, koordinatör FINAL kontrat) — additive, backend
 * Items.TrackingType ("None"|"Lot"|"Serial") + AutoSerial bayrağının mobile yansıması.
 * DeliveryScreen.resolveTrackingType() bu alanı okur; Seri/Lot iskeleti bu alan sayesinde
 * artık gerçek malzemede aktifleşir (bkz. DeliverySerialLotSection.kt).
 */
@JsonClass(generateAdapter = true)
data class ItemSearchDto(
    val id: Int,
    val code: String,
    val name: String,
    val unit: String,
    val barcode: String = "",
    val trackingType: String = "None",
    val autoSerial: Boolean = false
)

/** [trackingType]/[autoSerial] — bkz. ItemSearchDto üstü KDoc (aynı sözleşme, aynı gerekçe). */
@JsonClass(generateAdapter = true)
data class StockQueryDto(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String? = null,
    val balances: List<StockBalanceDto> = emptyList(),
    val trackingType: String = "None",
    val autoSerial: Boolean = false
)

@JsonClass(generateAdapter = true)
data class StockBalanceDto(
    val locationId: Int,
    val locationName: String,
    val quantity: Double
)

/** stock-in/stock-out kalem — itemId, GET /warehouse/stock yanıtındaki itemId'den gelir. */
@JsonClass(generateAdapter = true)
data class StockDocLineRequest(val itemId: Int, val quantity: Double)

/**
 * stock-in/stock-out istek gövdesi. quantity ANA BİRİMDE ve > 0 (stok sorgu bakiyeleriyle
 * aynı birim); aynı itemId birden fazla satırda gelebilir (backend kabul eder).
 */
@JsonClass(generateAdapter = true)
data class StockDocRequest(
    val locationId: Int,
    val lines: List<StockDocLineRequest>,
    val note: String? = null
)

@JsonClass(generateAdapter = true)
data class StockDocResponse(
    val ok: Boolean,
    val docId: Int? = null,
    val docNumber: String? = null,
    val error: String? = null,   // 200 iş kuralı reddi + 403'te teknik detay
    val message: String? = null  // yalnız 403 gövdesinde (kullanıcı-dostu yetki mesajı)
)

/**
 * Transfer istek gövdesi (Increment 2b) — kaynak/hedef lokasyon + kalemler + opsiyonel not.
 * Kalem şekli stock-in/out ile PAYLAŞILAN [StockDocLineRequest] ({itemId, quantity}) —
 * koordinatör sözleşmesi iki uçta da aynı satır şeklini kullanır.
 */
@JsonClass(generateAdapter = true)
data class TransferRequest(
    val fromLocationId: Int,
    val toLocationId: Int,
    val lines: List<StockDocLineRequest>,
    val note: String? = null
)

/**
 * Transfer yanıtı — sözleşme (koordinatör KİLİTLİ): başarı {ok:true, documentNumber} —
 * stock-in/out'un `docNumber` alanından FARKLI isim (`documentNumber`); DTO alan adı sunucu
 * JSON'ıyla birebir eşleşmek zorunda, buradaki isim bilinçli olarak stock-in/out ile aynı
 * DEĞİL. `error` alanı sözleşmede yalnız 400/404 gövdesinde tanımlı olsa da savunma amaçlı
 * eklendi — WarehouseRepository yine de asıl hata metnini errorBody'den (parseApiError) okur;
 * bu alan yalnızca sunucu 200 ile birlikte ok:false dönerse yedek kanal olarak kullanılır.
 */
@JsonClass(generateAdapter = true)
data class TransferResponse(
    val ok: Boolean,
    val documentNumber: String? = null,
    val error: String? = null
)

/** Sayım kalemi — itemId + SAYILAN miktar (countedQuantity, 0 geçerli: "raf boş" sayımı). */
@JsonClass(generateAdapter = true)
data class InventoryCountLineRequest(val itemId: Int, val countedQuantity: Double)

/** Sayım istek gövdesi (Increment 2b) — tek lokasyon + sayılan kalemler + opsiyonel not. */
@JsonClass(generateAdapter = true)
data class InventoryCountRequest(
    val locationId: Int,
    val lines: List<InventoryCountLineRequest>,
    val note: String? = null
)

/**
 * Sayım yanıtı — sözleşme (koordinatör KİLİTLİ): {ok:true, documentNumber, applied, id}. `applied`
 * sayımın DOĞRUDAN uygulanıp bakiyeyi güncellediğini (true) mi, yoksa taslak/onay bekleyen bir
 * belge mi (false) olduğunu belirtir — ekran bu bayrağa göre farklı snackbar mesajı gösterir.
 * Default false: sunucu alanı hiç döndürmezse (beklenmez ama savunma amaçlı) taslak varsayılır,
 * "uygulandı" gibi yanlış-pozitif bir mesaj asla gösterilmez. `error` alanı TransferResponse
 * ile aynı gerekçeyle (yedek kanal) eklendi. `id` (2026-07-16 sözleşme genişletmesi, koordinatör):
 * taslak sayım belge Id'si — Sayım Yansıt akışında POST inventory-count/{id}/apply çağrısına
 * gider; applied=true ise anlamsızdır (belge zaten uygulanmış), yalnız applied=false dalında kullanılır.
 */
@JsonClass(generateAdapter = true)
data class InventoryCountResponse(
    val ok: Boolean,
    val documentNumber: String? = null,
    val applied: Boolean = false,
    val error: String? = null,
    val id: Int = 0
)

/** Sayım Yansıt (apply) yanıtı — sözleşme KİLİTLİ: {ok:true, writtenCount} | 400 {error}. */
@JsonClass(generateAdapter = true)
data class InventoryCountApplyResponse(
    val ok: Boolean,
    val writtenCount: Int = 0,
    val error: String? = null
)

/** Cari rehberi arama sonucu satırı — GET contacts/search yanıtı (id/code/name, koordinatör sözleşmesi). */
@JsonClass(generateAdapter = true)
data class ContactSearchDto(
    val id: Int,
    val code: String,
    val name: String
)

/**
 * İrsaliye kalemi isteği (2026-07-17 FINAL kontrat, koordinatör) — [StockDocLineRequest]'ten
 * (stock-in/out/transfer ile paylaşılan) BİLİNÇLİ olarak AYRI: yalnızca irsaliyenin ihtiyaç
 * duyduğu seri/lot alanlarını taşır, diğer üç endpoint'i etkilemez.
 *
 * [serials]: SATIŞ'ta OPSİYONEL — null/boş → sunucu sipariş-rezerve/FIFO ile otomatik atar
 * (sonuç [DeliveryLineResultDto.serials]'ta döner); doluysa eleman sayısı [quantity] ile
 * eşleşmelidir (DeliveryScreen client-side kontrol eder, sunucu 400 mesajını da aynen gösterir).
 * ALIŞ'ta girilen/okutulan seri numaraları (adet eşleşmeli VEYA [autoGenerateSerials]=true).
 * [lotCode]: LOT takipli malzeme — alışta yeni lot kodu, satışta seçilen/FEFO uygun lot.
 * [autoGenerateSerials]: yalnız ALIŞ + AutoSerial malzemede anlamlı (bkz. ItemSearchDto.autoSerial).
 */
@JsonClass(generateAdapter = true)
data class DeliveryLineRequest(
    val itemId: Int,
    val quantity: Double,
    val serials: List<String>? = null,
    val lotCode: String? = null,
    val autoGenerateSerials: Boolean? = null
)

/**
 * Alış/Satış İrsaliyesi istek gövdesi (koordinatör KİLİTLİ). docType sunucuya AYNEN gönderilir
 * ("purchase" | "sales" — enum-benzeri sözleşme, DeliveryDocType.apiValue bu string'i üretir).
 * lines şekli (2026-07-17'den itibaren) irsaliyeye ÖZEL [DeliveryLineRequest] — stock-in/out/
 * transfer'ın paylaştığı [StockDocLineRequest]'ten farklı, seri/lot alanları taşır.
 *
 * [externalRefNumber] — ALIŞ modunda "Tedarikçi İrsaliye No" alanından doldurulur; backend artık
 * Document.ExternalRefNumber'a persist ediyor (2026-07-17 koordinatör teyidi) — mobil tarafta
 * değişiklik gerekmedi, alan zaten gönderiliyordu.
 *
 * [preferredOrderId] (2026-07-17 EK, FAZ C) — Açık Siparişler → Teslim Et akışında
 * OpenOrderDetailScreen'in "Teslim Et" çağrısında sipariş Id'si taşınır; sunucu satırları bu
 * siparişe ÖNCELİKLİ bağlar (normal DeliveryScreen akışında her zaman null — FIFO serbest bağlama).
 */
@JsonClass(generateAdapter = true)
data class DeliveryRequest(
    val docType: String,
    val contactId: Int,
    val lines: List<DeliveryLineRequest>,
    val note: String? = null,
    val externalRefNumber: String? = null,
    val preferredOrderId: Int? = null
)

/** Bir irsaliye kaleminin bağlandığı açık sipariş satırı — sipariş no + o siparişe bağlanan miktar. */
@JsonClass(generateAdapter = true)
data class DeliveryLinkedOrderDto(
    val orderNumber: String,
    val quantity: Double
)

/**
 * İrsaliye kaydı sonrası bağlama sonucu (satır bazında) — sunucu FIFO ile açık siparişlere
 * bağladığı miktarları [linked] listesinde, hiçbir siparişe bağlanamayan artık miktarı
 * [unlinkedQuantity] alanında döner. DeliveryScreen bu ikisini "→ SIP-...'e 5, SIP-...'e 3
 * bağlandı; 2 bağlantısız" biçiminde tek satıra özetler.
 *
 * [serials]/[lotCode] (2026-07-17 EK, FINAL kontrat) — sunucunun (otomatik veya manuel) ATADIĞI
 * seri numaraları / lot kodu; başarı diyaloğunda "Seri: SN-001, SN-002 ..." kısaltılmış biçimde
 * gösterilir (bkz. DeliveryLinkResultRow / buildSerialLotSummary).
 */
@JsonClass(generateAdapter = true)
data class DeliveryLineResultDto(
    val itemId: Int,
    val linked: List<DeliveryLinkedOrderDto> = emptyList(),
    val unlinkedQuantity: Double = 0.0,
    val serials: List<String> = emptyList(),
    val lotCode: String? = null
)

/**
 * İrsaliye yanıtı — sözleşme (koordinatör KİLİTLİ): başarı {ok:true, documentNumber, lines}.
 * Hata TRANSFER/SAYIM ile AYNI desen: 200 {ok:false} değil, 400/404 {error} olarak gelir
 * ("bağlantısız yasak" parametresi açıkken eşleşmeyen kayıt da bu kanaldan TÜMDEN reddedilir;
 * "sipariş serisi değiştirilemez" gibi seri/lot iş kuralı redleri de AYNI kanaldan gelir —
 * mesaj olduğu gibi gösterilir). `ok`/`error` alanları yine de savunma amaçlı tutulur
 * (TransferResponse/InventoryCountResponse ile aynı gerekçe) — WarehouseRepository asıl hata
 * metnini errorBody'den okur.
 */
@JsonClass(generateAdapter = true)
data class DeliveryResponse(
    val ok: Boolean,
    val documentNumber: String? = null,
    val lines: List<DeliveryLineResultDto> = emptyList(),
    val error: String? = null
)

// ───────────────────────────────────────────────────────────────────────
// Seri/Lot seçici DTO'ları (2026-07-17, koordinatör FINAL kontrat)
// ───────────────────────────────────────────────────────────────────────

/** Müsait seri numarası — GET items/{itemId}/serials yanıtı satırı (FIFO sıralı). */
@JsonClass(generateAdapter = true)
data class ItemSerialDto(
    val serialNo: String,
    val lotCode: String? = null,
    val entryDate: String? = null
)

/** Müsait lot — GET items/{itemId}/lots yanıtı satırı (FEFO sıralı). */
@JsonClass(generateAdapter = true)
data class ItemLotDto(
    val lotCode: String,
    val quantity: Double,
    val expiry: String? = null
)

// ───────────────────────────────────────────────────────────────────────
// FAZ C(a) — Açık Siparişler DTO'ları (2026-07-17, koordinatör FINAL kontrat)
// ───────────────────────────────────────────────────────────────────────

/** Açık sipariş özeti — GET open-orders yanıtı satırı (OpenOrderListScreen). */
@JsonClass(generateAdapter = true)
data class OpenOrderSummaryDto(
    val id: Int,
    val number: String,
    val contactId: Int,
    val contactName: String,
    val date: String,
    val openLineCount: Int,
    val totalOpenQuantity: Double
)

/**
 * Açık sipariş kalemi — GET open-orders/{id} yanıtındaki lines[] öğesi. [trackingType]/
 * [autoSerial] — bkz. ItemSearchDto üstü KDoc (aynı sözleşme); OpenOrderDetailScreen bu satır
 * için DeliveryScreen'deki AYNI Seri/Lot bölümünü kullanır.
 */
@JsonClass(generateAdapter = true)
data class OpenOrderLineDto(
    val orderLineId: Int,
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String? = null,
    val orderedQuantity: Double,
    val deliveredQuantity: Double,
    val openQuantity: Double,
    val trackingType: String = "None",
    val autoSerial: Boolean = false
)

/** Açık sipariş detayı — GET open-orders/{id} yanıtı (OpenOrderDetailScreen). */
@JsonClass(generateAdapter = true)
data class OpenOrderDetailDto(
    val id: Int,
    val number: String,
    val contactId: Int,
    val contactName: String,
    val date: String,
    val lines: List<OpenOrderLineDto> = emptyList()
)

// ───────────────────────────────────────────────────────────────────────
// FAZ C(b) — Taslak Sayımlar DTO'su (2026-07-17, koordinatör FINAL kontrat)
// ───────────────────────────────────────────────────────────────────────

/** Taslak (applied=false) sayım belgesi özeti — GET inventory-counts yanıtı satırı (DraftCountsScreen). */
@JsonClass(generateAdapter = true)
data class DraftInventoryCountDto(
    val id: Int,
    val documentNumber: String,
    val locationName: String,
    val date: String,
    val lineCount: Int
)

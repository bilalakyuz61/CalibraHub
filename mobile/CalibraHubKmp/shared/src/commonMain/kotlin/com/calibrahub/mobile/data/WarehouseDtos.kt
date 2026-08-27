package com.calibrahub.mobile.data

import kotlinx.serialization.Serializable

/**
 * Depo (Warehouse) modulu sozlesme DTO'lari — CalibraHubAndroid `WarehouseApi.kt` ile BIREBIR
 * alan adi/tip/opsiyonellik eslesir (bkz. CLAUDE.md — sessiz kirilma sinifi 1). `trackingType`
 * ("None"|"Lot"|"Serial") backend'de JsonStringEnumConverter ile STRING doner — Android'deki gibi
 * String olarak AYNEN korunur (enum'a cevrilmez).
 */

@Serializable
data class WarehouseLocationDto(
    val id: Int,
    val code: String,
    val name: String,
)

/** Rehber (malzeme) arama sonucu satiri — GET items/search yaniti. */
@Serializable
data class ItemSearchDto(
    val id: Int,
    val code: String,
    val name: String,
    val unit: String,
    val barcode: String = "",
    val trackingType: String = "None",
    val autoSerial: Boolean = false,
)

@Serializable
data class StockQueryDto(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String? = null,
    /** Gercek barkod (girilmemisse null). items/search'teki `barcode` alanindan farkli —
     * orada `Barcode ?? Code` fallback'i var, burada YOK (bkz. MobileWarehouseApiController.Stock). */
    val barcode: String? = null,
    val balances: List<StockBalanceDto> = emptyList(),
    val trackingType: String = "None",
    val autoSerial: Boolean = false,
)

@Serializable
data class StockBalanceDto(
    val locationId: Int,
    val locationName: String,
    val quantity: Double,
)

/** stock-in/stock-out kalemi — itemId, GET /warehouse/stock yanitindaki itemId'den gelir. */
@Serializable
data class StockDocLineRequest(
    val itemId: Int,
    val quantity: Double,
    /**
     * Lot-takipli malzemede ZORUNLU (sunucu kurali). Takipsizde null birakilir.
     */
    val lotNo: String? = null,
    /**
     * Seri-takipli malzemede ADET KADAR seri. Girişte AutoSerial=1 olan malzemede bos
     * birakilabilir — sunucu uretir. Kural sunucuda (SqlStockDocRepository), istemci
     * yalnizca toplar.
     */
    val serials: List<String>? = null,
)

/** stock-in/stock-out istek govdesi. [extraFields] — dinamik ek saha (WidgetMas) degerleri,
 * key=widget alan anahtari / value=STRING (tip ne olursa olsun). ADDITIVE: null/bos birakilirsa
 * hicbir sey yazilmaz. */
@Serializable
data class StockDocRequest(
    val locationId: Int,
    val lines: List<StockDocLineRequest>,
    val note: String? = null,
    val extraFields: Map<String, String>? = null,
)

/** [extraFieldsError] dolu ise belge YINE DE basarili sayilir (NON-ATOMIC, ok=true kalir). */
@Serializable
data class StockDocResponse(
    val ok: Boolean = false,
    val docId: Int? = null,
    val docNumber: String? = null,
    val error: String? = null,
    val message: String? = null,
    val extraFieldsError: String? = null,
)

/** Transfer istek govdesi — kalem sekli stock-in/out ile PAYLASILAN [StockDocLineRequest]. */
@Serializable
data class TransferRequest(
    val fromLocationId: Int,
    val toLocationId: Int,
    val lines: List<StockDocLineRequest>,
    val note: String? = null,
    val extraFields: Map<String, String>? = null,
)

/** Transfer yaniti — basari {ok:true, documentNumber} (stock-in/out'un `docNumber` alanindan
 * FARKLI isim: `documentNumber`). Hata TRANSFER'de 200 {ok:false} DEGIL, 400/404 {error} gelir. */
@Serializable
data class TransferResponse(
    val ok: Boolean = false,
    val documentNumber: String? = null,
    val error: String? = null,
    val extraFieldsError: String? = null,
)

/** Sayim kalemi — itemId + SAYILAN miktar (0 gecerli: "raf bos" sayimi). */
@Serializable
data class InventoryCountLineRequest(
    val itemId: Int,
    val countedQuantity: Double,
)

@Serializable
data class InventoryCountRequest(
    val locationId: Int,
    val lines: List<InventoryCountLineRequest>,
    val note: String? = null,
    val extraFields: Map<String, String>? = null,
)

/** Sayim yaniti — {ok:true, documentNumber, applied, id}. `applied` false ise taslak/onay
 * bekleyen belge; `id` Sayim Yansit (apply) akisinda kullanilir. */
@Serializable
data class InventoryCountResponse(
    val ok: Boolean = false,
    val documentNumber: String? = null,
    val applied: Boolean = false,
    val error: String? = null,
    val id: Int = 0,
    val extraFieldsError: String? = null,
)

/** Sayim Yansit (apply) yaniti — {ok:true, writtenCount} | 400 {error}. */
@Serializable
data class InventoryCountApplyResponse(
    val ok: Boolean = false,
    val writtenCount: Int = 0,
    val error: String? = null,
)

/** Cari rehberi arama sonucu satiri — GET contacts/search yaniti. */
@Serializable
data class ContactSearchDto(
    val id: Int,
    val code: String,
    val name: String,
)

/**
 * Irsaliye kalemi istegi — [StockDocLineRequest]'ten BILINCLI olarak AYRI: yalniz irsaliyenin
 * ihtiyac duydugu seri/lot alanlarini tasir. [serials] SATIS'ta OPSIYONEL (null/bos -> sunucu
 * otomatik atar); ALIS'ta girilen/okutulan seri numaralari (adet eslesmeli VEYA autoGenerateSerials=true).
 */
@Serializable
data class DeliveryLineRequest(
    val itemId: Int,
    val quantity: Double,
    val serials: List<String>? = null,
    val lotCode: String? = null,
    val autoGenerateSerials: Boolean? = null,
)

/**
 * Alis/Satis Irsaliyesi istek govdesi — docType sunucuya AYNEN gonderilir ("purchase" | "sales" —
 * enum-benzeri sozlesme, backend'deki DeliveryDocType.apiValue ile AYNI string). [preferredOrderId]
 * Acik Siparisler -> Teslim Et akisinda kullanilir (normal akista her zaman null).
 */
@Serializable
data class DeliveryRequest(
    val docType: String,
    val contactId: Int,
    val lines: List<DeliveryLineRequest>,
    val note: String? = null,
    val externalRefNumber: String? = null,
    val preferredOrderId: Int? = null,
    val extraFields: Map<String, String>? = null,
)

/** Bir irsaliye kaleminin baglandigi acik siparis satiri — siparis no + baglanan miktar. */
@Serializable
data class DeliveryLinkedOrderDto(
    val orderNumber: String,
    val quantity: Double,
)

/** Irsaliye kaydi sonrasi baglama sonucu (satir bazinda). [serials]/[lotCode] sunucunun
 * (otomatik veya manuel) ATADIGI seri numaralari / lot kodu. */
@Serializable
data class DeliveryLineResultDto(
    val itemId: Int,
    val linked: List<DeliveryLinkedOrderDto> = emptyList(),
    val unlinkedQuantity: Double = 0.0,
    val serials: List<String> = emptyList(),
    val lotCode: String? = null,
)

/** Irsaliye yaniti — basari {ok:true, documentNumber, lines}. Hata TRANSFER/SAYIM ile AYNI
 * desen: 400/404 {error} (200 {ok:false} DEGIL). */
@Serializable
data class DeliveryResponse(
    val ok: Boolean = false,
    val documentNumber: String? = null,
    val lines: List<DeliveryLineResultDto> = emptyList(),
    val error: String? = null,
    val extraFieldsError: String? = null,
)

/** Musait seri numarasi — GET items/{itemId}/serials yaniti satiri (FIFO sirali). */
@Serializable
data class ItemSerialDto(
    val serialNo: String,
    val lotCode: String? = null,
    val entryDate: String? = null,
)

/** Musait lot — GET items/{itemId}/lots yaniti satiri (FEFO sirali). */
@Serializable
data class ItemLotDto(
    val lotCode: String,
    val quantity: Double,
    val expiry: String? = null,
)

/** Acik siparis ozeti — GET open-orders yaniti satiri. */
@Serializable
data class OpenOrderSummaryDto(
    val id: Int,
    val number: String,
    val contactId: Int,
    val contactName: String,
    val date: String,
    val openLineCount: Int,
    val totalOpenQuantity: Double,
)

/** Acik siparis kalemi — GET open-orders/{id} yanitindaki lines[] ogesi. */
@Serializable
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
    val autoSerial: Boolean = false,
)

/** Acik siparis detayi — GET open-orders/{id} yaniti. */
@Serializable
data class OpenOrderDetailDto(
    val id: Int,
    val number: String,
    val contactId: Int,
    val contactName: String,
    val date: String,
    val lines: List<OpenOrderLineDto> = emptyList(),
)

/** Taslak (applied=false) sayim belgesi ozeti — GET inventory-counts yaniti satiri. */
@Serializable
data class DraftInventoryCountDto(
    val id: Int,
    val documentNumber: String,
    val locationName: String,
    val date: String,
    val lineCount: Int,
)

/**
 * Dinamik alan sema satiri (WidgetMas/EAV). [type] BILINCLI olarak String tutulur ("text"|
 * "number"|"date"|"select"|"bool"|"textarea") — bilinmeyen bir deger enum'da parse hatasi
 * fırlatmasin diye (Android'deki AYNI gerekce).
 */
@Serializable
data class WidgetFieldDto(
    val key: String = "",
    val label: String = "",
    val type: String = "text",
    val required: Boolean = false,
    val options: List<WidgetOptionDto>? = null,
    val order: Int = 0,
)

/** select tipi alanin secenegi — [value] sunucuya gonderilen/saklanan deger (ID-benzeri),
 * [label] kullaniciya gosterilen metin. */
@Serializable
data class WidgetOptionDto(
    val value: String = "",
    val label: String = "",
)

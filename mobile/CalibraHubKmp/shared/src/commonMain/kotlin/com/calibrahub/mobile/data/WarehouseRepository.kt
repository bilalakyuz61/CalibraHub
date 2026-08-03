package com.calibrahub.mobile.data

import com.calibrahub.mobile.session.SessionManager
import io.ktor.client.call.body
import io.ktor.client.statement.HttpResponse
import io.ktor.http.isSuccess

/**
 * Depo modulu icin ince Result<T> repository sarmalayicisi — CalibraHubAndroid
 * `WarehouseRepository.kt` ile BIREBIR ayni desen (fonksiyon adlari/govdeler/hata ayristirma).
 * UI katmani buradan cagirir; HTTP/Ktor detaylari gizli kalir.
 */
class WarehouseRepository(private val session: SessionManager) {

    suspend fun locations(): Result<List<WarehouseLocationDto>> = runCatching {
        val resp = session.warehouseApi().locations()
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<WarehouseLocationDto>>()
    }

    suspend fun stock(itemCode: String): Result<StockQueryDto> = runCatching {
        val resp = session.warehouseApi().stock(itemCode)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<StockQueryDto>()
    }

    /** Rehber (malzeme) arama — debounce'lu cagri (ekran tarafinda uygulanir). */
    suspend fun searchItems(query: String, take: Int = 20): Result<List<ItemSearchDto>> = runCatching {
        val resp = session.warehouseApi().searchItems(query, take)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<ItemSearchDto>>()
    }

    /** Depo giris belgesi (STOCK_IN) — kalemler hedef lokasyona (+) yazilir. */
    suspend fun stockIn(
        locationId: Int,
        lines: List<StockDocLineRequest>,
        note: String?,
        extraFields: Map<String, String>? = null,
    ): Result<StockDocResult> = runCatching {
        unwrapStockDoc(
            session.warehouseApi().stockIn(
                StockDocRequest(locationId = locationId, lines = lines, note = note, extraFields = extraFields),
            ),
        )
    }

    /** Depo cikis belgesi (STOCK_OUT) — kaynak lokasyondan (-) dusulur. */
    suspend fun stockOut(
        locationId: Int,
        lines: List<StockDocLineRequest>,
        note: String?,
        extraFields: Map<String, String>? = null,
    ): Result<StockDocResult> = runCatching {
        unwrapStockDoc(
            session.warehouseApi().stockOut(
                StockDocRequest(locationId = locationId, lines = lines, note = note, extraFields = extraFields),
            ),
        )
    }

    /**
     * Transfer belgesi — sozlesme stock-in/out'tan FARKLI: is kurali reddi 200 {ok:false} degil,
     * 400/404 {error} olarak gelir — bu yuzden [unwrapStockDoc] paylasilmaz, ayri ayristirma.
     */
    suspend fun transfer(
        fromLocationId: Int,
        toLocationId: Int,
        lines: List<StockDocLineRequest>,
        note: String?,
        extraFields: Map<String, String>? = null,
    ): Result<TransferResult> = runCatching {
        val resp = session.warehouseApi().transfer(
            TransferRequest(
                fromLocationId = fromLocationId,
                toLocationId = toLocationId,
                lines = lines,
                note = note,
                extraFields = extraFields,
            ),
        )
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        val body = resp.body<TransferResponse>()
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        TransferResult(documentNumber = body.documentNumber ?: "", extraFieldsError = body.extraFieldsError)
    }

    /** Sayim belgesi — `applied` yanit alani belgenin dogrudan uygulanip uygulanmadigini tasir. */
    suspend fun inventoryCount(
        locationId: Int,
        lines: List<InventoryCountLineRequest>,
        note: String?,
        extraFields: Map<String, String>? = null,
    ): Result<InventoryCountResult> = runCatching {
        val resp = session.warehouseApi().inventoryCount(
            InventoryCountRequest(locationId = locationId, lines = lines, note = note, extraFields = extraFields),
        )
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        val body = resp.body<InventoryCountResponse>()
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        InventoryCountResult(
            documentNumber = body.documentNumber ?: "",
            applied = body.applied,
            id = body.id,
            extraFieldsError = body.extraFieldsError,
        )
    }

    /** Sayim Yansit — taslak (applied=false) kalmis sayim belgesini stoga uygular. Idempotent DEGIL. */
    suspend fun applyInventoryCount(id: Int): Result<InventoryCountApplyResult> = runCatching {
        val resp = session.warehouseApi().applyInventoryCount(id)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        val body = resp.body<InventoryCountApplyResponse>()
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        InventoryCountApplyResult(writtenCount = body.writtenCount)
    }

    /** Cari (contact) rehberi arama — searchItems ile ayni desen. */
    suspend fun searchContacts(query: String, take: Int = 20): Result<List<ContactSearchDto>> = runCatching {
        val resp = session.warehouseApi().searchContacts(query, take)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<ContactSearchDto>>()
    }

    /**
     * Alis/Satis Irsaliyesi — docType "purchase"|"sales" ile TEK endpoint iki yonu de kapsar.
     * Sozlesme TRANSFER/SAYIM ile AYNI: 200 {ok:false} degil, 400/404 {error}.
     */
    suspend fun delivery(
        docType: String,
        contactId: Int,
        lines: List<DeliveryLineRequest>,
        note: String?,
        externalRefNumber: String? = null,
        preferredOrderId: Int? = null,
        extraFields: Map<String, String>? = null,
    ): Result<DeliveryResult> = runCatching {
        val resp = session.warehouseApi().delivery(
            DeliveryRequest(
                docType = docType,
                contactId = contactId,
                lines = lines,
                note = note,
                externalRefNumber = externalRefNumber,
                preferredOrderId = preferredOrderId,
                extraFields = extraFields,
            ),
        )
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        val body = resp.body<DeliveryResponse>()
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        DeliveryResult(documentNumber = body.documentNumber ?: "", lines = body.lines, extraFieldsError = body.extraFieldsError)
    }

    /** Musait seriler (FIFO sirali). `locationId` genelde null gonderilir (sunucu kendi tarafinda cozer). */
    suspend fun availableSerialsForDeliveryLine(
        itemId: Int,
        locationId: Int? = null,
        q: String? = null,
    ): Result<List<ItemSerialDto>> = runCatching {
        val resp = session.warehouseApi().itemSerials(itemId, locationId, q)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<ItemSerialDto>>()
    }

    /** Musait lotlar (FEFO sirali). */
    suspend fun availableLotsForItem(itemId: Int, locationId: Int? = null): Result<List<ItemLotDto>> = runCatching {
        val resp = session.warehouseApi().itemLots(itemId, locationId)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<ItemLotDto>>()
    }

    /** Acik siparis listesi. */
    suspend fun openOrders(docType: String, q: String? = null, take: Int = 50): Result<List<OpenOrderSummaryDto>> =
        runCatching {
            val resp = session.warehouseApi().openOrders(docType, q, take)
            if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
            resp.body<List<OpenOrderSummaryDto>>()
        }

    /** Acik siparis detayi. */
    suspend fun openOrderDetail(id: Int): Result<OpenOrderDetailDto> = runCatching {
        val resp = session.warehouseApi().openOrderDetail(id)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<OpenOrderDetailDto>()
    }

    /** Taslak (applied=false) sayim belgeleri — Yansit aksiyonu MEVCUT [applyInventoryCount]'u kullanir. */
    suspend fun draftInventoryCounts(take: Int = 50): Result<List<DraftInventoryCountDto>> = runCatching {
        val resp = session.warehouseApi().inventoryCounts(take)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<DraftInventoryCountDto>>()
    }

    /**
     * formCode'a tanimli dinamik ek saha semasini getirir. `Result.success(emptyList())` IKI durumu
     * KASITLI olarak AYNI ele alir: (1) sunucu 200 [] -> form icin hic alan tanimlanmamis (NORMAL);
     * (2) 400/403/404 veya ag hatasi -> SESSIZCE bos listeye dusurulur — ek saha bolumu V1'de
     * opsiyonel/best-effort bir katman, asil belge kaydetme akisini ASLA bloklamamali.
     */
    suspend fun getWidgetSchema(formCode: String): Result<List<WidgetFieldDto>> = runCatching {
        val resp = session.warehouseApi().getWidgetSchema(formCode)
        if (resp.status.isSuccess()) resp.body<List<WidgetFieldDto>>() else emptyList()
    }

    /**
     * stock-in/stock-out sozlesmesi hatayi UC kanaldan tasir; hepsi `error(...)` (exception) olarak
     * normalize edilir, [runCatching] Result.failure'a cevirir:
     *  - 200 {ok:false, error}          -> is kurali reddi
     *  - 403 {ok:false, message, error} -> yetki reddi (message oncelikli)
     *  - diger HTTP hatalari            -> generic "HTTP <kod>"
     */
    private suspend fun unwrapStockDoc(resp: HttpResponse): StockDocResult {
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        val body = resp.body<StockDocResponse>()
        if (!body.ok) error(body.error ?: body.message ?: "İşlem başarısız")
        return StockDocResult(docId = body.docId ?: 0, docNumber = body.docNumber ?: "", extraFieldsError = body.extraFieldsError)
    }
}

/** Basarili stok belgesi yazimimin sonucu. [extraFieldsError] dolu ise belge YINE DE basarili
 * sayilir (NON-ATOMIC) — UI onay diyalogunda non-blocking uyari olarak gosterilir. */
data class StockDocResult(val docId: Int, val docNumber: String, val extraFieldsError: String? = null)

data class TransferResult(val documentNumber: String, val extraFieldsError: String? = null)

data class InventoryCountResult(
    val documentNumber: String,
    val applied: Boolean,
    val id: Int,
    val extraFieldsError: String? = null,
)

data class InventoryCountApplyResult(val writtenCount: Int)

data class DeliveryResult(
    val documentNumber: String,
    val lines: List<DeliveryLineResultDto>,
    val extraFieldsError: String? = null,
)

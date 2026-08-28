package com.calibrahub.mobile.data

import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.contentType

/**
 * Depo (Warehouse) modulu Ktor istemcisi — `/api/mobile/warehouse` (+ `/api/mobile/widgets/schema`)
 * yolu altinda. CalibraHubAndroid `WarehouseApi.kt` (Retrofit) ile BIREBIR ayni 16 uc nokta —
 * fonksiyon imzalari/govde sekilleri sozlesmeye sadik birebir port. Auth deseni CalibraApi/
 * ProductionApi ile AYNI: cookie + X-Requested-With (bkz. [com.calibrahub.mobile.net.createHttpClient]).
 */
class WarehouseApi(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val base: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/"
        }

    suspend fun locations(): HttpResponse = client.get("${base}warehouse/locations")

    /** 400 {error} -> kod bos; 404 {error} -> bulunamadi (WarehouseRepository govdeyi ayristirir). */
    suspend fun stock(code: String): HttpResponse =
        client.get("${base}warehouse/stock") { parameter("code", code) }

    /** Rehber (malzeme arama) — kod VEYA ad ile kismi eslesme (backend LIKE ile arar). */
    suspend fun searchItems(q: String, take: Int = 20): HttpResponse =
        client.get("${base}warehouse/items/search") {
            parameter("q", q)
            parameter("take", take)
        }

    /** Is kurali reddi de HTTP 200 doner ({ok:false, error}); yalniz yetki reddi 403 + {ok, message, error}. */
    suspend fun stockIn(req: StockDocRequest): HttpResponse =
        client.post("${base}warehouse/stock-in") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun stockOut(req: StockDocRequest): HttpResponse =
        client.post("${base}warehouse/stock-out") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    /** Is kurali reddi/validasyon 400/404 {error} doner — stock-in/out'un "200 ok:false" deseninden FARKLI. */
    suspend fun transfer(req: TransferRequest): HttpResponse =
        client.post("${base}warehouse/transfer") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun inventoryCount(req: InventoryCountRequest): HttpResponse =
        client.post("${base}warehouse/inventory-count") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    /** Taslak (applied=false) kalmis bir sayim belgesini stoga uygular. Idempotent DEGIL —
     * ikinci kez cagrilirsa 400 {error} ile reddedilir. */
    suspend fun applyInventoryCount(id: Int): HttpResponse =
        client.post("${base}warehouse/inventory-count/$id/apply")

    /** Cari (contact) rehberi — kod VEYA ad ile kismi eslesme. */
    suspend fun searchContacts(q: String, take: Int = 20): HttpResponse =
        client.get("${base}warehouse/contacts/search") {
            parameter("q", q)
            parameter("take", take)
        }

    /** Alis/Satis Irsaliyesi — docType "purchase"|"sales" ile TEK endpoint iki yonu de kapsar. */
    suspend fun delivery(req: DeliveryRequest): HttpResponse =
        client.post("${base}warehouse/delivery") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    /** Musait seriler (FIFO sirali). locationId/q opsiyonel. */
    // ── Kaydedilen belgeler ─────────────────────────────────────────────────
    /** Kaydedilmis depo belgeleri. Yalniz gorme yetkisi olan tipler doner. */
    suspend fun documents(docType: String? = null, days: Int = 30, take: Int = 50): HttpResponse =
        client.get("${base}warehouse/documents") {
            docType?.let { parameter("docType", it) }
            parameter("days", days)
            parameter("take", take)
        }

    suspend fun documentDetail(id: Int): HttpResponse = client.get("${base}warehouse/documents/$id")

    suspend fun itemSerials(itemId: Int, locationId: Int? = null, q: String? = null, take: Int = 50): HttpResponse =
        client.get("${base}warehouse/items/$itemId/serials") {
            locationId?.let { parameter("locationId", it) }
            q?.let { parameter("q", it) }
            parameter("take", take)
        }

    /** Musait lotlar (FEFO sirali). */
    suspend fun itemLots(itemId: Int, locationId: Int? = null, take: Int = 20): HttpResponse =
        client.get("${base}warehouse/items/$itemId/lots") {
            locationId?.let { parameter("locationId", it) }
            parameter("take", take)
        }

    suspend fun openOrders(docType: String, q: String? = null, take: Int = 50): HttpResponse =
        client.get("${base}warehouse/open-orders") {
            parameter("docType", docType)
            q?.let { parameter("q", it) }
            parameter("take", take)
        }

    suspend fun openOrderDetail(id: Int): HttpResponse = client.get("${base}warehouse/open-orders/$id")

    /** applied=false kalmis sayim belgeleri — Yansit aksiyonu MEVCUT applyInventoryCount(id) endpoint'ini kullanir. */
    suspend fun inventoryCounts(take: Int = 50): HttpResponse =
        client.get("${base}warehouse/inventory-counts") { parameter("take", take) }

    /**
     * Dinamik ek saha semasi (WidgetMas/EAV) — "warehouse" alt-yolunda DEGIL (`/api/mobile/widgets/schema`).
     * formCode ekran basina SABIT: STOCK_IN | STOCK_OUT | TRANSFER | INVENTORY_COUNT |
     * SALES_DELIVERY_EDIT | PURCHASE_DELIVERY_EDIT. 200 [] -> alan yok, NORMAL (hata degil).
     */
    suspend fun getWidgetSchema(formCode: String): HttpResponse =
        client.get("${base}widgets/schema") { parameter("formCode", formCode) }
}

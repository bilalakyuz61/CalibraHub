package com.calibrahub.app.data

import org.json.JSONObject
import retrofit2.Response

/**
 * Depo modülü için thin Result<T> repository wrapper — WhatsAppRepository ile aynı desen.
 * ViewModel/Composable'lar buradan çağırır; HTTP/Retrofit detayları gizli kalır.
 */
class WarehouseRepository(private val session: SessionManager) {

    suspend fun locations(): Result<List<WarehouseLocationDto>> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).locations()
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    suspend fun stock(itemCode: String): Result<StockQueryDto> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).stock(itemCode)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: error("Boş yanıt")
    }

    /** Rehber (malzeme) arama — MaterialPickerField'ın debounce'lu çağrısı. Boş/az karakterli
     * sorgu ekran tarafında filtrelenir (bkz. MaterialPickerField); burada doğrudan istek atılır. */
    suspend fun searchItems(query: String, take: Int = 20): Result<List<ItemSearchDto>> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).searchItems(query, take)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    /** Depo giriş belgesi (STOCK_IN) — kalemler hedef lokasyona (+) yazılır. */
    suspend fun stockIn(locationId: Int, lines: List<StockDocLineRequest>, note: String?): Result<StockDocResult> =
        runCatchingApi {
            unwrapStockDoc(
                session.buildApi(WarehouseApi::class.java)
                    .stockIn(StockDocRequest(locationId = locationId, lines = lines, note = note))
            )
        }

    /** Depo çıkış belgesi (STOCK_OUT) — kaynak lokasyondan (-) düşülür; eksi bakiye guard'ı sunucuda çalışır. */
    suspend fun stockOut(locationId: Int, lines: List<StockDocLineRequest>, note: String?): Result<StockDocResult> =
        runCatchingApi {
            unwrapStockDoc(
                session.buildApi(WarehouseApi::class.java)
                    .stockOut(StockDocRequest(locationId = locationId, lines = lines, note = note))
            )
        }

    /**
     * Transfer belgesi (TRANSFER, Increment 2b) — kaynak lokasyondan hedef lokasyona kalem
     * taşır. Sözleşme stock-in/out'tan FARKLI: iş kuralı reddi 200 {ok:false} değil, 400/404
     * {error} olarak gelir (koordinatör sözleşmesi) — bu yüzden [unwrapStockDoc] paylaşılmaz,
     * ayrı ayrıştırma. `body.ok == false` durumu (sunucu yine de 200 dönerse) savunma amaçlı
     * ele alınır.
     */
    suspend fun transfer(
        fromLocationId: Int,
        toLocationId: Int,
        lines: List<StockDocLineRequest>,
        note: String?
    ): Result<TransferResult> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).transfer(
            TransferRequest(fromLocationId = fromLocationId, toLocationId = toLocationId, lines = lines, note = note)
        )
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        TransferResult(documentNumber = body.documentNumber ?: "")
    }

    /**
     * Sayım belgesi (INVENTORY_COUNT, Increment 2b) — sayılan miktarlar sunucuya gönderilir.
     * `applied` yanıt alanı belgenin doğrudan uygulanıp uygulanmadığını taşır; UI bu bayrağa
     * göre farklı mesaj gösterir (bkz. [InventoryCountResult]). `id` (2026-07-16 sözleşme
     * genişletmesi) — applied=false olduğunda Sayım Yansıt akışında [applyInventoryCount]'a gider.
     */
    suspend fun inventoryCount(
        locationId: Int,
        lines: List<InventoryCountLineRequest>,
        note: String?
    ): Result<InventoryCountResult> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).inventoryCount(
            InventoryCountRequest(locationId = locationId, lines = lines, note = note)
        )
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        InventoryCountResult(documentNumber = body.documentNumber ?: "", applied = body.applied, id = body.id)
    }

    /**
     * Sayım Yansıt (2026-07-16) — taslak (applied=false) kalmış sayım belgesini stoğa uygular.
     * Sunucu tarafında idempotent DEĞİL: ikinci kez çağrılırsa 400 {error} ile reddedilir,
     * mesaj CountScreen tarafından olduğu gibi gösterilir (bkz. dosya üstü sözleşme notu).
     */
    suspend fun applyInventoryCount(id: Int): Result<InventoryCountApplyResult> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).applyInventoryCount(id)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        InventoryCountApplyResult(writtenCount = body.writtenCount)
    }

    /** Cari (contact) rehberi arama — ContactPickerField'ın debounce'lu çağrısı (searchItems ile aynı desen). */
    suspend fun searchContacts(query: String, take: Int = 20): Result<List<ContactSearchDto>> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).searchContacts(query, take)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    /**
     * Alış/Satış İrsaliyesi (2026-07-16, satır şekli 2026-07-17'de [DeliveryLineRequest]'e
     * genişledi) — docType "purchase"|"sales" ile TEK endpoint iki yönü de kapsar. Sözleşme
     * TRANSFER/SAYIM ile AYNI: iş kuralı reddi 200 {ok:false} değil, 400/404 {error} olarak gelir
     * ("bağlantısız yasak", seri-adet uyuşmazlığı, "sipariş serisi değiştirilemez" hepsi bu
     * kanaldan TÜMDEN reddedilir) — bu yüzden [unwrapStockDoc] paylaşılmaz, transfer()/
     * inventoryCount() ile aynı ayrıştırma tekrarlanır.
     *
     * [externalRefNumber] — ALIŞ modunda dolu gelir (DeliveryScreen'in "Tedarikçi İrsaliye No"
     * alanı); backend artık Document.ExternalRefNumber'a persist ediyor.
     * [preferredOrderId] (FAZ C) — Açık Siparişler → Teslim Et akışında sipariş Id'si; normal
     * DeliveryScreen çağrısında her zaman null.
     */
    suspend fun delivery(
        docType: String,
        contactId: Int,
        lines: List<DeliveryLineRequest>,
        note: String?,
        externalRefNumber: String? = null,
        preferredOrderId: Int? = null
    ): Result<DeliveryResult> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).delivery(
            DeliveryRequest(
                docType = docType,
                contactId = contactId,
                lines = lines,
                note = note,
                externalRefNumber = externalRefNumber,
                preferredOrderId = preferredOrderId
            )
        )
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: "İşlem başarısız")
        DeliveryResult(documentNumber = body.documentNumber ?: "", lines = body.lines)
    }

    // ──────────────────────────────────────────────────────────────────────
    // Seri/Lot seçici (2026-07-17, koordinatör FINAL kontrat) — bkz. DeliverySerialLotSection.kt
    // ──────────────────────────────────────────────────────────────────────

    /**
     * Müsait seriler (FIFO sıralı) — SerialSelectionDialog'un çağırdığı liste + arama fonksiyonu.
     * `locationId` DeliveryScreen'de her zaman null gönderilir (bu ekranda lokasyon seçimi yok,
     * sunucu kendi tarafında çözer — StockQueryDto/ItemSearchDto'nun locationId'siz aynı deseni).
     */
    suspend fun availableSerialsForDeliveryLine(
        itemId: Int,
        locationId: Int? = null,
        q: String? = null
    ): Result<List<ItemSerialDto>> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).itemSerials(itemId, locationId, q)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    /** Müsait lotlar (FEFO sıralı) — satış Lot alanının öneri listesi (LotInputRow). */
    suspend fun availableLotsForItem(itemId: Int, locationId: Int? = null): Result<List<ItemLotDto>> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).itemLots(itemId, locationId)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    // ──────────────────────────────────────────────────────────────────────
    // FAZ C(a) — Açık Siparişler → Teslim Et (2026-07-17, koordinatör FINAL kontrat)
    // ──────────────────────────────────────────────────────────────────────

    /** Açık sipariş listesi — OpenOrderListScreen'in arama kutusu bu fonksiyonu debounce'lu çağırır. */
    suspend fun openOrders(docType: String, q: String? = null, take: Int = 50): Result<List<OpenOrderSummaryDto>> =
        runCatchingApi {
            val resp = session.buildApi(WarehouseApi::class.java).openOrders(docType, q, take)
            if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
            resp.body() ?: emptyList()
        }

    /** Açık sipariş detayı — OpenOrderDetailScreen açılışında bir kez çağrılır. */
    suspend fun openOrderDetail(id: Int): Result<OpenOrderDetailDto> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).openOrderDetail(id)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: error("Boş yanıt")
    }

    // ──────────────────────────────────────────────────────────────────────
    // FAZ C(b) — Taslak Sayımlar (2026-07-17, koordinatör FINAL kontrat)
    // ──────────────────────────────────────────────────────────────────────

    /** Taslak (applied=false) sayım belgeleri — DraftCountsScreen listesi. Yansıt aksiyonu MEVCUT
     * [applyInventoryCount] fonksiyonunu aynen kullanır (Increment 2b'den beri var, değişmedi). */
    suspend fun draftInventoryCounts(take: Int = 50): Result<List<DraftInventoryCountDto>> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).inventoryCounts(take)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    /**
     * stock-in/stock-out sözleşmesi hatayı üç kanaldan taşır; hepsi Result.failure(mesaj)
     * olarak normalize edilir, UI fold ile tek yoldan gösterir:
     *  - 200 {ok:false, error}          → iş kuralı reddi (yetersiz stok, lot/seri/varyant, validasyon)
     *  - 403 {ok:false, message, error} → yetki reddi (errorBody, parseApiError message'ı tercih eder)
     *  - diğer HTTP hataları            → generic "HTTP <kod>"
     */
    private fun unwrapStockDoc(resp: Response<StockDocResponse>): StockDocResult {
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: body.message ?: "İşlem başarısız")
        return StockDocResult(docId = body.docId ?: 0, docNumber = body.docNumber ?: "")
    }

    /**
     * Hata gövdesinden kullanıcıya gösterilecek mesajı çıkarır. İki gövde şekli var:
     * 400/404 → `{"error":"..."}` (error alanı zaten kullanıcı-dostu);
     * 403     → `{"ok","message","error"}` (message kullanıcı-dostu, error teknik) —
     * bu yüzden önce message, yoksa error okunur. Ayrıştırılamazsa null döner,
     * çağıran taraf generic "HTTP <kod>" mesajına düşer.
     */
    private fun parseApiError(resp: Response<*>): String? = try {
        resp.errorBody()?.string()?.let { raw ->
            val json = JSONObject(raw)
            json.optString("message").takeIf { it.isNotBlank() }
                ?: json.optString("error").takeIf { it.isNotBlank() }
        }
    } catch (e: Exception) {
        null
    }

    /** Network/JSON hatalarını Result.Failure olarak döndürür (throw yok, UI fold ile okur). */
    private inline fun <T> runCatchingApi(block: () -> T): Result<T> = runCatching(block)
}

/** Başarılı stok belgesi yazımının sonucu — UI onay diyaloğunda docNumber gösterilir. */
data class StockDocResult(val docId: Int, val docNumber: String)

/** Başarılı transfer belgesi yazımının sonucu — UI'da documentNumber'lı snackbar gösterilir. */
data class TransferResult(val documentNumber: String)

/**
 * Başarılı sayım belgesi yazımının sonucu — applied bayrağına göre UI farklı snackbar mesajı
 * seçer. `id`: applied=false ise Sayım Yansıt dialoğunun "Yansıt" aksiyonunda kullanılır.
 */
data class InventoryCountResult(val documentNumber: String, val applied: Boolean, val id: Int)

/** Başarılı Sayım Yansıt (apply) sonucu — UI "Yansıtıldı (N satır yazıldı)" snackbar'ında gösterir. */
data class InventoryCountApplyResult(val writtenCount: Int)

/** Başarılı irsaliye kaydının sonucu — documentNumber + satır bazlı sipariş bağlama dökümü. */
data class DeliveryResult(val documentNumber: String, val lines: List<DeliveryLineResultDto>)

package com.calibrahub.app.data

import org.json.JSONObject
import retrofit2.Response

/**
 * Üretim modülü için thin Result<T> repository wrapper — WarehouseRepository ile aynı desen.
 * ViewModel/Composable'lar buradan çağırır; HTTP/Retrofit detayları gizli kalır.
 *
 * Hata sözleşmesi WarehouseRepository'nin stock-in/stock-out'undan FARKLI: burada iş kuralı
 * reddi de HTTP hata kodlarıyla (400/404) gelir — 200 + {ok:false} yok. Bu yüzden tüm
 * metodlar tek tip "isSuccessful değilse errorBody'den mesajı çıkar" akışını kullanır
 * (WarehouseRepository.stock()/locations() ile aynı desen; unwrapStockDoc'un 200-ok:false
 * dalı burada YOK).
 */
class ProductionRepository(private val session: SessionManager) {

    suspend fun workOrders(query: String? = null, take: Int = 50): Result<List<WorkOrderListItemDto>> = runCatchingApi {
        val resp = session.buildApi(ProductionApi::class.java)
            .workOrders(query?.trim()?.takeIf { it.isNotBlank() }, take)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    suspend fun workOrderDetail(id: Int): Result<WorkOrderDetailDto> = runCatchingApi {
        val resp = session.buildApi(ProductionApi::class.java).workOrderDetail(id)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: error("Boş yanıt")
    }

    /**
     * Sicil No + PIN doğrulama — başarıda operatorId+name döner, start/complete
     * çağrılarında kullanılır (2026-07-16: personnelCode zorunlu alan olarak eklendi).
     */
    suspend fun authOperator(personnelCode: String, pin: String): Result<AuthOperatorResponse> = runCatchingApi {
        val resp = session.buildApi(ProductionApi::class.java)
            .authOperator(AuthOperatorRequest(personnelCode = personnelCode, pin = pin))
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: error("Boş yanıt")
    }

    suspend fun startOperation(operationId: Int, operatorId: Int): Result<Unit> = runCatchingApi {
        val resp = session.buildApi(ProductionApi::class.java)
            .startOperation(StartOperationRequest(operationId, operatorId))
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        Unit
    }

    /** goodQuantity/scrapQuantity bu OTURUMDA üretilen EK (delta) miktardır — bkz. CompleteOperationRequest KDoc. */
    suspend fun completeOperation(
        operationId: Int,
        operatorId: Int,
        goodQuantity: Double,
        scrapQuantity: Double,
        note: String?
    ): Result<Unit> = runCatchingApi {
        val resp = session.buildApi(ProductionApi::class.java).completeOperation(
            CompleteOperationRequest(
                operationId = operationId,
                operatorId = operatorId,
                goodQuantity = goodQuantity,
                scrapQuantity = scrapQuantity,
                note = note?.trim()?.takeIf { it.isNotBlank() }
            )
        )
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        Unit
    }

    /**
     * Hata gövdesinden kullanıcıya gösterilecek mesajı çıkarır — WarehouseRepository ile
     * aynı ayrıştırma (message öncelikli, yoksa error). Bu modülde 403 gövdesi (message+error
     * ikilisi) fiilen kullanılmıyor ama aynı iki-alanlı okuma zararsız kalır.
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

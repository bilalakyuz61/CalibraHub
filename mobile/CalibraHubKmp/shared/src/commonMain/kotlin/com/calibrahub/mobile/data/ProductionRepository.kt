package com.calibrahub.mobile.data

import com.calibrahub.mobile.session.SessionManager
import io.ktor.client.call.body
import io.ktor.http.isSuccess

/**
 * Uretim modulu icin ince Result<T> repository sarmalayicisi — CalibraHubAndroid
 * `ProductionRepository.kt` ile BIREBIR ayni desen.
 *
 * Hata sozlesmesi WarehouseRepository'nin stock-in/stock-out'undan FARKLI: burada is kurali
 * reddi de HTTP hata kodlariyla (400/404) gelir — 200 + {ok:false} yok. Bu yuzden tum metodlar
 * tek tip "basarisizsa govdeden mesaji cikar" akisini kullanir.
 */
class ProductionRepository(private val session: SessionManager) {

    suspend fun workOrders(query: String? = null, take: Int = 50): Result<List<WorkOrderListItemDto>> = runCatchingApi {
        val resp = session.productionApi().workOrders(query?.trim()?.takeIf { it.isNotBlank() }, take)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<WorkOrderListItemDto>>()
    }

    suspend fun workOrderDetail(id: Int): Result<WorkOrderDetailDto> = runCatchingApi {
        val resp = session.productionApi().workOrderDetail(id)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<WorkOrderDetailDto>()
    }

    /**
     * Bagli personel + PIN gereksinimi. Hata durumunda "PIN gerekli" varsayilanina duser —
     * ag hatasi yuzunden PIN atlanmamali (fail-safe yon guvenlik lehine).
     */
    suspend fun myOperator(): MyOperatorDto = runCatchingApi {
        val resp = session.productionApi().myOperator()
        if (!resp.status.isSuccess()) error("HTTP ${resp.status.value}")
        resp.body<MyOperatorDto>()
    }.getOrElse { MyOperatorDto(linked = false, pinRequired = true) }

    /** Sicil No + PIN dogrulama — basarida operatorId+name doner, start/complete cagrilarinda kullanilir. */
    suspend fun authOperator(personnelCode: String, pin: String): Result<AuthOperatorResponse> = runCatchingApi {
        val resp = session.productionApi().authOperator(AuthOperatorRequest(personnelCode = personnelCode, pin = pin))
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<AuthOperatorResponse>()
    }

    suspend fun startOperation(operationId: Int, operatorId: Int): Result<Unit> = runCatchingApi {
        val resp = session.productionApi().startOperation(StartOperationRequest(operationId, operatorId))
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        Unit
    }

    /** goodQuantity/scrapQuantity bu OTURUMDA uretilen EK (delta) miktardir — bkz. CompleteOperationRequest KDoc. */
    suspend fun completeOperation(
        operationId: Int,
        operatorId: Int,
        goodQuantity: Double,
        scrapQuantity: Double,
        note: String?,
    ): Result<Unit> = runCatchingApi {
        val resp = session.productionApi().completeOperation(
            CompleteOperationRequest(
                operationId = operationId,
                operatorId = operatorId,
                goodQuantity = goodQuantity,
                scrapQuantity = scrapQuantity,
                note = note?.trim()?.takeIf { it.isNotBlank() },
            ),
        )
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        Unit
    }
}

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
 * Uretim (Production) modulu Ktor istemcisi — `/api/mobile/production` yolu altinda.
 * CalibraHubAndroid `ProductionApi.kt` (Retrofit) ile BIREBIR ayni 5 uc nokta. Operator kimligi:
 * start/complete endpoint'leri operatorId gerektirir; bu deger once auth-operator (Sicil No + PIN
 * dogrulama) ile alinir.
 */
class ProductionApi(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val base: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/"
        }

    /** q bos/null -> tum is emirleri (take ile sinirli); dolu -> numara/malzeme kod/ad LIKE. */
    suspend fun workOrders(q: String? = null, take: Int = 50): HttpResponse =
        client.get("${base}production/work-orders") {
            q?.let { parameter("q", it) }
            parameter("take", take)
        }

    suspend fun workOrderDetail(id: Int): HttpResponse = client.get("${base}production/work-orders/$id")

    /** Giris yapmis kullaniciya bagli personel + mobilde PIN gerekip gerekmedigi. */
    suspend fun myOperator(): HttpResponse = client.get("${base}production/me")

    // ── Durus / aktivite ────────────────────────────────────────────────────
    /** Aktivite tipleri + her tipin sebepleri (tek cagri; tip secilince ikinci tur yok). */
    suspend fun activityTypes(): HttpResponse = client.get("${base}production/activity-types")

    /** Operasyonun an aktif aktivitesi; yoksa govde `null`. */
    suspend fun activeActivity(operationId: Int): HttpResponse =
        client.get("${base}production/operations/$operationId/activity")

    suspend fun startActivity(operationId: Int, req: StartActivityBody): HttpResponse =
        client.post("${base}production/operations/$operationId/activity/start") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun endActivity(operationId: Int, req: EndActivityBody): HttpResponse =
        client.post("${base}production/operations/$operationId/activity/end") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    /** Yanlis sicil no/PIN veya kilitli personel -> 400 {error}. */
    suspend fun authOperator(req: AuthOperatorRequest): HttpResponse =
        client.post("${base}production/auth-operator") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun startOperation(req: StartOperationRequest): HttpResponse =
        client.post("${base}production/operations/start") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun completeOperation(req: CompleteOperationRequest): HttpResponse =
        client.post("${base}production/operations/complete") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }
}

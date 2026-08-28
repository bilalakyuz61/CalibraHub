package com.calibrahub.mobile.data

import com.calibrahub.mobile.session.SessionManager
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import kotlinx.serialization.Serializable

/**
 * İhtiyaç Kaydı (alis_talebi) istemcisi — `/api/mobile/purchase-requests`.
 *
 * Sunucu kaydi web ile AYNI kapidan (DocumentService.SaveQuoteAsync) gecirir; belge numarasi,
 * onay akisi ve audit birebir aynidir (bkz. MobilePurchaseRequestApiController).
 */
class PurchaseRequestApi(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val base: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/purchase-requests"
        }

    suspend fun list(mine: Boolean = true, take: Int = 50): HttpResponse =
        client.get(base) {
            parameter("mine", mine)
            parameter("take", take)
        }

    suspend fun detail(id: Int): HttpResponse = client.get("$base/$id")

    suspend fun create(req: PurchaseRequestCreate): HttpResponse =
        client.post(base) {
            contentType(ContentType.Application.Json)
            setBody(req)
        }
}

/** Talep satiri — fiyat YOK (talep asamasinda bilinmez, web ile ayni). */
@Serializable
data class PurchaseRequestLineCreate(
    val itemId: Int,
    val quantity: Double,
    val note: String? = null,
)

@Serializable
data class PurchaseRequestCreate(
    val lines: List<PurchaseRequestLineCreate>,
    val note: String? = null,
    val locationId: Int? = null,
)

/** Liste satiri. [status] belge durumu ("Draft", "Approved"...) — sunucu string doner. */
@Serializable
data class PurchaseRequestRowDto(
    val id: Int,
    val documentNumber: String = "",
    val documentDate: String? = null,
    val status: String = "",
    val notes: String? = null,
)

@Serializable
data class PurchaseRequestLineDto(
    val id: Int,
    val itemId: Int,
    val materialCode: String? = null,
    val materialName: String? = null,
    val quantity: Double = 0.0,
    /** Karsilanan (teslim edilen) miktar — "ne kadari geldi" gostergesi. */
    val deliveredQuantity: Double = 0.0,
    val notes: String? = null,
)

@Serializable
data class PurchaseRequestDetailDto(
    val id: Int,
    val documentNumber: String = "",
    val documentDate: String? = null,
    val status: String = "",
    val notes: String? = null,
    val lines: List<PurchaseRequestLineDto> = emptyList(),
)

/** Kayit yaniti. [approvalStarted] true ise belge kaydedilir kaydedilmez onaya dustu. */
@Serializable
data class PurchaseRequestCreateResponse(
    val ok: Boolean = false,
    val id: Int = 0,
    val documentNumber: String? = null,
    val status: String? = null,
    val approvalStarted: Boolean = false,
    val error: String? = null,
)

class PurchaseRequestRepository(private val session: SessionManager) {

    suspend fun list(mine: Boolean = true): Result<List<PurchaseRequestRowDto>> = runCatchingApi {
        val resp = session.purchaseRequestApi().list(mine)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<PurchaseRequestRowDto>>()
    }

    suspend fun detail(id: Int): Result<PurchaseRequestDetailDto> = runCatchingApi {
        val resp = session.purchaseRequestApi().detail(id)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<PurchaseRequestDetailDto>()
    }

    suspend fun create(
        lines: List<PurchaseRequestLineCreate>,
        note: String?,
        locationId: Int?,
    ): Result<PurchaseRequestCreateResponse> = runCatchingApi {
        val resp = session.purchaseRequestApi().create(
            PurchaseRequestCreate(
                lines = lines,
                note = note?.trim()?.takeIf { it.isNotBlank() },
                locationId = locationId,
            ),
        )
        // Hata govdesi 400 {ok:false,error} olarak gelir — mesaji cikarip yukari tasi.
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<PurchaseRequestCreateResponse>()
    }
}

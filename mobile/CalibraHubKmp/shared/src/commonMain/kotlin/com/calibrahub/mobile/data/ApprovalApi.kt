package com.calibrahub.mobile.data

import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.contentType
import kotlinx.serialization.Serializable

/**
 * Onay Bekleyenler istemcisi — `/api/mobile/approvals`. Sunucu tarafi web'in
 * /PendingApproval ekraniyla AYNI servisleri kullanir (bkz. MobileApprovalApiController),
 * yani kapsam kontrolu ve adim ilerletme davranisi birebir aynidir.
 */
class ApprovalApi(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val base: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/approvals"
        }

    suspend fun list(): HttpResponse = client.get(base)

    suspend fun approve(instanceId: Int, note: String?): HttpResponse =
        client.post("$base/$instanceId/approve") {
            contentType(ContentType.Application.Json)
            setBody(ApprovalDecisionRequest(note))
        }

    /** Not ZORUNLU — sunucu bos gerekceyi 400 ile reddeder (sessiz "-" doldurmasi yok). */
    suspend fun reject(instanceId: Int, note: String): HttpResponse =
        client.post("$base/$instanceId/reject") {
            contentType(ContentType.Application.Json)
            setBody(ApprovalDecisionRequest(note))
        }
}

@Serializable
data class ApprovalDecisionRequest(val note: String? = null)

/**
 * Bekleyen onay karti. Sunucu bilincli olarak SADELESTIRILMIS bir satir doner (web'in
 * ~20 alanli DTO'sunun tamami degil) — telefonda gosterilmeyen alan gonderilmez.
 */
@Serializable
data class PendingApprovalDto(
    val instanceId: Int,
    val stepName: String = "",
    val flowName: String = "",
    val stepPosition: Int = 0,
    val totalSteps: Int = 0,
    val documentNumber: String = "",
    val documentDate: String? = null,
    val documentTypeName: String? = null,
    val contactName: String? = null,
    val grandTotal: Double = 0.0,
    val currencyCode: String? = null,
    val waitingSince: String? = null,
)

package com.calibrahub.mobile.data

import com.calibrahub.mobile.session.SessionManager
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.statement.HttpResponse
import io.ktor.http.isSuccess
import kotlinx.serialization.Serializable

/**
 * Cari Karti istemcisi — `/api/mobile/contacts`. SALT OKUNUR.
 *
 * DIKKAT: bu, irsaliye ekranindaki cari SECICI'den (WarehouseApi.contactsSearch) FARKLI bir
 * uctur. Secici teslimat yetkisiyle (SALES_DELIVERY/PURCHASE_DELIVERY) korunur ve yalniz
 * secim icin gereken alanlari doner; bu uc CONTACTS yetkisiyle korunur ve kart detayini doner.
 * Ikisini birlestirmek, cari kartini goremeyen bir depocuya kart detayini acardi.
 */
class ContactCardApi(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val base: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/contacts"
        }

    suspend fun search(q: String, take: Int = 20): HttpResponse =
        client.get(base) {
            parameter("q", q)
            parameter("take", take)
        }

    suspend fun detail(id: Int): HttpResponse = client.get("$base/$id")
}

@Serializable
data class ContactCardRowDto(
    val id: Int,
    val code: String = "",
    val title: String = "",
    val city: String? = null,
    val phone: String? = null,
)

/** Kart detayi — bakiye/borc ALANI YOK (bkz. MobileContactApiController KDoc: sistemde
 * tahsilat/odeme modulu olmadigi icin bakiye hesaplanmiyor, uydurulmuyor da). */
@Serializable
data class ContactCardDto(
    val id: Int,
    val code: String = "",
    val title: String = "",
    val accountType: Int = 0,
    val taxNumber: String? = null,
    val identityNumber: String? = null,
    val taxOffice: String? = null,
    val phone: String? = null,
    val mobile: String? = null,
    val email: String? = null,
    val contactPerson: String? = null,
    val address: String? = null,
    val neighborhood: String? = null,
    val district: String? = null,
    val city: String? = null,
    val postalCode: String? = null,
    val isActive: Boolean = true,
)

/** Ince Result<T> sarmalayici — [ProductionRepository] ile ayni hata sozlesmesi. */
class ContactCardRepository(private val session: SessionManager) {

    suspend fun search(query: String, take: Int = 20): Result<List<ContactCardRowDto>> = runCatching {
        val q = query.trim()
        if (q.isEmpty()) return@runCatching emptyList()
        val resp = session.contactCardApi().search(q, take)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<ContactCardRowDto>>()
    }

    suspend fun detail(id: Int): Result<ContactCardDto> = runCatching {
        val resp = session.contactCardApi().detail(id)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<ContactCardDto>()
    }
}

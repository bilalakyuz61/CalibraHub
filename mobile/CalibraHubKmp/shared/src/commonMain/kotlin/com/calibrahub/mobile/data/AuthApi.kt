package com.calibrahub.mobile.data

import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.contentType

/**
 * `api/mobile` altindaki auth/session/whoami uc noktalarini tuketen ince Ktor istemcisi —
 * CalibraHubAndroid `CalibraApi.kt`'nin WhatsApp DISI kismi (WhatsApp/sohbet mobil kapsamdan
 * TAMAMEN CIKARILDI, 2026-08-04 kullanici karari — bkz. CLAUDE.md). CalibraApi/WarehouseApi/
 * ProductionApi ile AYNI auth deseni: cookie + X-Requested-With
 * (bkz. [com.calibrahub.mobile.net.createHttpClient]).
 */
class AuthApi(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val base: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/"
        }

    suspend fun ping(): HttpResponse = client.get("${base}ping")

    suspend fun companies(): HttpResponse = client.get("${base}companies")

    /** Parola dogrulandiktan sonra kullanicinin erisebildigi sirketleri doner (bossa []). */
    suspend fun loginCompanies(req: LoginCompaniesRequest): HttpResponse =
        client.post("${base}login-companies") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun login(req: LoginRequest): HttpResponse =
        client.post("${base}login") {
            contentType(ContentType.Application.Json)
            setBody(req)
        }

    suspend fun logout(): HttpResponse = client.post("${base}logout")

    suspend fun whoAmI(): HttpResponse = client.get("${base}whoami")

    /**
     * "Beni hatirla" otomatik giris probe'u — session-backend kontrati: 200 -> oturum gecerli,
     * govde userId/displayName/email/companyId/companyName ile dolu. 401 -> kimlik yok/expired;
     * kontrat govde URETMEZ, cagiran taraf govdeyi PARSE ETMEDEN yalniz status koduna bakar
     * (bkz. SessionManager.probeSession).
     */
    suspend fun session(): HttpResponse = client.get("${base}session")
}

/**
 * Ince Result sarmalayici — Warehouse/Production repository'lerinin kullandigi kotlin.Result'tan
 * BILINCLI olarak AYRI tutuldu: auth akislari (login/loginCompanies) basarili HTTP yanitinda dahi
 * `ok:false` donebilir (hatali sifre) — bu, "network/HTTP hatasi" ile AYNI kanaldan degil, ayri
 * bir kullanici-mesaji kanalindan gosterilir; POC LoginScreen'in zaten bildigi sozlesme budur.
 */
sealed class ApiResult<out T> {
    data class Success<T>(val data: T) : ApiResult<T>()
    data class Failure(val message: String) : ApiResult<Nothing>()
}

package com.calibrahub.app.data

import com.squareup.moshi.JsonClass
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Query

/**
 * CalibraHub backend için Retrofit interface'i.
 * Mobile endpoint'leri /api/mobile/ yolu altinda — CSRF muaftir,
 * cookie + X-Requested-With header'ı ile origin doğrulanır.
 */
interface CalibraApi {

    // Sunucu doğrulama (anonim) — LoginScreen "Doğrula" akışı SessionManager.ping() üzerinden
    // henüz KAYDEDİLMEMİŞ bir base URL ile çağırır (bkz. SessionManager.ping KDoc); bu interface
    // yalnızca imza/DTO sözleşmesini taşır.
    @GET("api/mobile/ping")
    suspend fun ping(): Response<PingResponse>

    @GET("api/mobile/companies")
    suspend fun companies(): Response<List<CompanyDto>>

    // Parola doğrulandıktan sonra kullanıcının erişebildiği şirketleri döner (boşsa []).
    // Login akışı: önce bu çağrılır, tek şirket dönerse doğrudan login(), birden fazlaysa
    // kullanıcıya seçtirilip seçilen companyId ile login() çağrılır.
    @POST("api/mobile/login-companies")
    suspend fun loginCompanies(@Body req: LoginCompaniesRequest): Response<List<CompanyDto>>

    @POST("api/mobile/login")
    suspend fun login(@Body req: LoginRequest): Response<LoginResponse>

    @POST("api/mobile/logout")
    suspend fun logout(): Response<Unit>

    @GET("api/mobile/whoami")
    suspend fun whoAmI(): Response<WhoAmIResponse>

    // "Beni hatırla" otomatik giriş probe'u (2026-07-17, session-backend kontratı KİLİTLİ —
    // paralel ajan tarafından kuruldu). 200 → oturum geçerli, gövde userId/displayName/email/
    // companyId/companyName ile dolu. 401 → kimlik yok/expired; kontrat gövde ÜRETMEZ, yalnızca
    // status koduna bakılır (bkz. AuthRepository.fetchSession — resp.code() kontrolü).
    @GET("api/mobile/session")
    suspend fun session(): Response<SessionDto>
}

// ───────────────────────────────────────────────────────────────────────
// DTO'lar — backend MobileApiController dönüş şekilleriyle eşleşir
// ───────────────────────────────────────────────────────────────────────

// GET /api/mobile/ping yanıtı — MobileApiController.Ping() ile birebir: { ok, product, version }.
// Tüm alanlar nullable/default'lu tanımlandı (Moshi Kotlin adapter'ı eksik alanda non-null
// alanlarda hata fırlatır); backend her zaman doldurup döner ama savunmacı kalınır.
@JsonClass(generateAdapter = true)
data class PingResponse(val ok: Boolean = false, val product: String? = null, val version: String? = null)

@JsonClass(generateAdapter = true)
data class CompanyDto(val id: Int, val name: String)

@JsonClass(generateAdapter = true)
data class LoginRequest(val email: String, val password: String, val companyId: Int? = null)

@JsonClass(generateAdapter = true)
data class LoginCompaniesRequest(val email: String, val password: String)

@JsonClass(generateAdapter = true)
data class LoginResponse(val ok: Boolean, val displayName: String? = null, val error: String? = null)

@JsonClass(generateAdapter = true)
data class WhoAmIResponse(val ok: Boolean, val userName: String? = null)

// GET /api/mobile/session 200 yanıtı — session-backend kontratı (2026-07-17): { ok, userId,
// displayName, email, companyId, companyName }. PingResponse'taki gibi savunmacı nullable/
// default alanlar — backend kontrat gereği hepsini doldurur ama Moshi'nin eksik-alan
// JsonDataException riskine karşı tümü opsiyonel tanımlandı. 401 durumunda bu tip HİÇ parse
// edilmez (bkz. AuthRepository.fetchSession).
@JsonClass(generateAdapter = true)
data class SessionDto(
    val ok: Boolean = false,
    val userId: Int = 0,
    val displayName: String? = null,
    val email: String? = null,
    val companyId: Int = 0,
    val companyName: String? = null
)

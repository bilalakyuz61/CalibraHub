package com.calibrahub.app.data

import com.squareup.moshi.JsonClass
import okhttp3.MultipartBody
import okhttp3.RequestBody
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Part
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

    @GET("api/mobile/whatsapp/conversations")
    suspend fun conversations(@Query("limit") limit: Int = 200): Response<List<ConversationDto>>

    @GET("api/mobile/whatsapp/messages")
    suspend fun messages(
        @Query("phone") phone: String,
        @Query("limit") limit: Int = 200
    ): Response<List<MessageDto>>

    @POST("api/mobile/whatsapp/send")
    suspend fun sendText(@Body req: SendTextRequest): Response<SendResponse>

    @Multipart
    @POST("api/mobile/whatsapp/send-media")
    suspend fun sendMedia(
        @Part("phone") phone: RequestBody,
        @Part("caption") caption: RequestBody?,
        @Part file: MultipartBody.Part
    ): Response<SendResponse>

    @POST("api/mobile/whatsapp/mark-read")
    suspend fun markRead(@Query("phone") phone: String): Response<Unit>
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

@JsonClass(generateAdapter = true)
data class ConversationDto(
    val phone: String,
    val displayName: String?,
    val contactCode: String?,
    val lastMessage: String?,
    val lastMessageAt: String?,
    val unreadCount: Int = 0,
    val lastMediaType: String? = null
)

@JsonClass(generateAdapter = true)
data class MessageDto(
    val id: Long,
    val direction: Int,          // 0 = incoming, 1 = outgoing
    val body: String?,
    val mediaType: String?,      // chat | image | video | audio | document | sticker
    val mediaPath: String?,      // /uploads/whatsapp/...
    val mediaMime: String?,
    val mediaFilename: String?,
    val mediaSize: Int?,
    val receivedAt: String?
)

@JsonClass(generateAdapter = true)
data class SendTextRequest(val phone: String, val text: String)

@JsonClass(generateAdapter = true)
data class SendResponse(val ok: Boolean, val messageId: String? = null, val error: String? = null)

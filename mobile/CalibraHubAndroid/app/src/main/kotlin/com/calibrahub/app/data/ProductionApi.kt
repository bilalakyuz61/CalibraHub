package com.calibrahub.app.data

import com.squareup.moshi.JsonClass
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

/**
 * Üretim (Production) modülü Retrofit interface'i — /api/mobile/production yolu altında.
 * CalibraApi/WarehouseApi ile aynı auth deseni: cookie + X-Requested-With
 * (bkz. SessionManager.buildApi()). Backend MobileProductionApiController tarafından
 * sağlanır (koordinatör sözleşmesi KİLİTLİ, 2026-07-16).
 *
 * Operatör kimliği: start/complete endpoint'leri operatorId gerektirir; bu değer önce
 * auth-operator (Sicil No + PIN doğrulama) ile alınır. Backend kimliği kilitlenme/deneme
 * sayacıyla korur (ShopFloorLockoutTracker, bkz. CLAUDE.md ShopFloor bölümü — aynı desen);
 * mobil taraf yalnızca dönen {error} mesajını olduğu gibi kullanıcıya gösterir.
 */
interface ProductionApi {

    // q boş/null → tüm iş emirleri (take ile sınırlı); dolu → numara/malzeme kod/ad LIKE.
    // İptal/kapalı iş emirleri de listede döner — backend filtrelemez, görsel ayrım StatusChip'te.
    @GET("api/mobile/production/work-orders")
    suspend fun workOrders(
        @Query("q") q: String? = null,
        @Query("take") take: Int = 50
    ): Response<List<WorkOrderListItemDto>>

    @GET("api/mobile/production/work-orders/{id}")
    suspend fun workOrderDetail(@Path("id") id: Int): Response<WorkOrderDetailDto>

    // Yanlış sicil no/PIN veya kilitli personel → 400 {error} (mesaj kullanıcı-dostu, olduğu gibi gösterilir).
    @POST("api/mobile/production/auth-operator")
    suspend fun authOperator(@Body req: AuthOperatorRequest): Response<AuthOperatorResponse>

    @POST("api/mobile/production/operations/start")
    suspend fun startOperation(@Body req: StartOperationRequest): Response<OperationActionResponse>

    @POST("api/mobile/production/operations/complete")
    suspend fun completeOperation(@Body req: CompleteOperationRequest): Response<OperationActionResponse>
}

// ───────────────────────────────────────────────────────────────────────
// DTO'lar — backend MobileProductionApiController dönüş şekilleriyle birebir eşleşir
// ───────────────────────────────────────────────────────────────────────

/**
 * İş emri liste satırı — GET work-orders yanıtı. statusCode kümesi KİLİTLİ (koordinatör,
 * 2026-07-16): planned / released / in_progress / completed / closed / cancelled. Etiket
 * HER ZAMAN statusLabel'dan gösterilir, statusCode yalnızca renk seçiminde kullanılır
 * (bkz. ekranlardaki StatusChip). plannedDate = planlanan BAŞLANGIÇ tarihi, nullable
 * (planlanmamış olabilir).
 */
@JsonClass(generateAdapter = true)
data class WorkOrderListItemDto(
    val id: Int,
    val number: String,
    val itemCode: String,
    val itemName: String,
    val quantity: Double,
    val unit: String,
    val statusCode: String,
    val statusLabel: String,
    val plannedDate: String? = null
)

/** İş emri detayı — GET work-orders/{id} yanıtı; operations boşsa henüz rota/operasyon tanımlanmamış demektir. */
@JsonClass(generateAdapter = true)
data class WorkOrderDetailDto(
    val id: Int,
    val number: String,
    val itemCode: String,
    val itemName: String,
    val quantity: Double,
    val unit: String,
    val statusCode: String,
    val statusLabel: String,
    val operations: List<WorkOrderOperationDto> = emptyList()
)

/**
 * İş emri operasyon satırı. statusCode kümesi KİLİTLİ: pending / in_progress / completed /
 * skipped. goodQuantity/scrapQuantity burada KÜMÜLATİF toplamdır (operasyonun şu ana kadar
 * ürettiği); complete isteğindeki AYNI ADLI alanlar ise bu OTURUMDA eklenecek DELTA'dır —
 * iki anlam FARKLI, karıştırılmamalı (koordinatör netleştirmesi, 2026-07-16). canStart/
 * canComplete backend'in durum makinesine göre hesaplanır, mobil taraf kendi kuralını
 * türetmez — buton görünürlüğü doğrudan bu bayraklara bağlıdır. machineName manuel/
 * makinesiz operasyonlarda "" (boş string) gelir, null gelmez.
 */
@JsonClass(generateAdapter = true)
data class WorkOrderOperationDto(
    val id: Int,
    val seq: Int,
    val name: String,
    val machineName: String,
    val statusCode: String,
    val statusLabel: String,
    val goodQuantity: Double,
    val scrapQuantity: Double,
    val canStart: Boolean,
    val canComplete: Boolean
)

/** Operatör kimlik doğrulama isteği — sicil no (personnelCode) + PIN birlikte zorunlu (2026-07-16 sözleşme güncellemesi). */
@JsonClass(generateAdapter = true)
data class AuthOperatorRequest(val personnelCode: String, val pin: String)

@JsonClass(generateAdapter = true)
data class AuthOperatorResponse(val operatorId: Int, val name: String)

@JsonClass(generateAdapter = true)
data class StartOperationRequest(val operationId: Int, val operatorId: Int)

/**
 * complete isteği gövdesi. goodQuantity/scrapQuantity bu OTURUMDA üretilen EK (delta)
 * miktardır — WorkOrderOperationDto'daki aynı adlı alanların KÜMÜLATİF toplamıyla
 * KARIŞTIRILMAMALI (koordinatör netleştirmesi, 2026-07-16). Negatif değer backend'de 400
 * ile reddedilir. note boşsa gönderilmez (null).
 */
@JsonClass(generateAdapter = true)
data class CompleteOperationRequest(
    val operationId: Int,
    val operatorId: Int,
    val goodQuantity: Double,
    val scrapQuantity: Double,
    val note: String? = null
)

/** start/complete başarı gövdesi — sözleşme yalnızca {ok:true} döner, hata HTTP 400/404 + {error}. */
@JsonClass(generateAdapter = true)
data class OperationActionResponse(val ok: Boolean = true)

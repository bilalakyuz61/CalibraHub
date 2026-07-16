package com.calibrahub.app.data

import com.squareup.moshi.JsonClass
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Query

/**
 * Depo (Warehouse) modülü Retrofit interface'i — /api/mobile/warehouse yolu altında.
 * CalibraApi ile aynı auth deseni: cookie + X-Requested-With (bkz. SessionManager.buildApi()).
 */
interface WarehouseApi {

    @GET("api/mobile/warehouse/locations")
    suspend fun locations(): Response<List<WarehouseLocationDto>>

    // 400 {error} → kod boş; 404 {error} → bulunamadı (WarehouseRepository bu gövdeyi ayrıştırır).
    @GET("api/mobile/warehouse/stock")
    suspend fun stock(@Query("code") code: String): Response<StockQueryDto>

    // Increment 2a — depo giriş/çıkış belgesi oluşturma (sözleşme KİLİTLİ,
    // MobileWarehouseApiController.SaveStockDocAsync). İş kuralı reddi de HTTP 200 döner
    // ({ok:false, error}); yalnız yetki reddi 403 + {ok, message, error} gövdesidir
    // (Retrofit'te errorBody'ye düşer, WarehouseRepository ayrıştırır).
    @POST("api/mobile/warehouse/stock-in")
    suspend fun stockIn(@Body req: StockDocRequest): Response<StockDocResponse>

    @POST("api/mobile/warehouse/stock-out")
    suspend fun stockOut(@Body req: StockDocRequest): Response<StockDocResponse>
}

// ───────────────────────────────────────────────────────────────────────
// DTO'lar — backend MobileApiController /warehouse/* dönüş şekilleriyle eşleşir
// ───────────────────────────────────────────────────────────────────────

@JsonClass(generateAdapter = true)
data class WarehouseLocationDto(val id: Int, val code: String, val name: String)

@JsonClass(generateAdapter = true)
data class StockQueryDto(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String? = null,
    val balances: List<StockBalanceDto> = emptyList()
)

@JsonClass(generateAdapter = true)
data class StockBalanceDto(
    val locationId: Int,
    val locationName: String,
    val quantity: Double
)

/** stock-in/stock-out kalem — itemId, GET /warehouse/stock yanıtındaki itemId'den gelir. */
@JsonClass(generateAdapter = true)
data class StockDocLineRequest(val itemId: Int, val quantity: Double)

/**
 * stock-in/stock-out istek gövdesi. quantity ANA BİRİMDE ve > 0 (stok sorgu bakiyeleriyle
 * aynı birim); aynı itemId birden fazla satırda gelebilir (backend kabul eder).
 */
@JsonClass(generateAdapter = true)
data class StockDocRequest(
    val locationId: Int,
    val lines: List<StockDocLineRequest>,
    val note: String? = null
)

@JsonClass(generateAdapter = true)
data class StockDocResponse(
    val ok: Boolean,
    val docId: Int? = null,
    val docNumber: String? = null,
    val error: String? = null,   // 200 iş kuralı reddi + 403'te teknik detay
    val message: String? = null  // yalnız 403 gövdesinde (kullanıcı-dostu yetki mesajı)
)

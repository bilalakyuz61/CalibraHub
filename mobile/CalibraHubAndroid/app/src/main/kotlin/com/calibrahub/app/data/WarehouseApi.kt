package com.calibrahub.app.data

import com.squareup.moshi.JsonClass
import retrofit2.Response
import retrofit2.http.GET
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

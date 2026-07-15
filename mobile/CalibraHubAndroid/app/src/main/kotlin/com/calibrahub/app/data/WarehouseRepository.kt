package com.calibrahub.app.data

import org.json.JSONObject
import retrofit2.Response

/**
 * Depo modülü için thin Result<T> repository wrapper — WhatsAppRepository ile aynı desen.
 * ViewModel/Composable'lar buradan çağırır; HTTP/Retrofit detayları gizli kalır.
 */
class WarehouseRepository(private val session: SessionManager) {

    suspend fun locations(): Result<List<WarehouseLocationDto>> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).locations()
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    suspend fun stock(itemCode: String): Result<StockQueryDto> = runCatchingApi {
        val resp = session.buildApi(WarehouseApi::class.java).stock(itemCode)
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        resp.body() ?: error("Boş yanıt")
    }

    /**
     * 400 (kod boş) / 404 (bulunamadı) gibi hatalarda backend'in döndürdüğü
     * `{"error":"..."}` gövdesinden kullanıcıya gösterilecek mesajı çıkarır.
     * Ayrıştırılamazsa null döner, çağıran taraf generic "HTTP <kod>" mesajına düşer.
     */
    private fun parseApiError(resp: Response<*>): String? = try {
        resp.errorBody()?.string()
            ?.let { JSONObject(it).optString("error") }
            ?.takeIf { it.isNotBlank() }
    } catch (e: Exception) {
        null
    }

    /** Network/JSON hatalarını Result.Failure olarak döndürür (throw yok, UI fold ile okur). */
    private inline fun <T> runCatchingApi(block: () -> T): Result<T> = runCatching(block)
}

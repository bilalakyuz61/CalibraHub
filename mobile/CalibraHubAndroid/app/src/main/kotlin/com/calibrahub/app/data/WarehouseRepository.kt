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

    /** Depo giriş belgesi (STOCK_IN) — kalemler hedef lokasyona (+) yazılır. */
    suspend fun stockIn(locationId: Int, lines: List<StockDocLineRequest>, note: String?): Result<StockDocResult> =
        runCatchingApi {
            unwrapStockDoc(
                session.buildApi(WarehouseApi::class.java)
                    .stockIn(StockDocRequest(locationId = locationId, lines = lines, note = note))
            )
        }

    /** Depo çıkış belgesi (STOCK_OUT) — kaynak lokasyondan (-) düşülür; eksi bakiye guard'ı sunucuda çalışır. */
    suspend fun stockOut(locationId: Int, lines: List<StockDocLineRequest>, note: String?): Result<StockDocResult> =
        runCatchingApi {
            unwrapStockDoc(
                session.buildApi(WarehouseApi::class.java)
                    .stockOut(StockDocRequest(locationId = locationId, lines = lines, note = note))
            )
        }

    /**
     * stock-in/stock-out sözleşmesi hatayı üç kanaldan taşır; hepsi Result.failure(mesaj)
     * olarak normalize edilir, UI fold ile tek yoldan gösterir:
     *  - 200 {ok:false, error}          → iş kuralı reddi (yetersiz stok, lot/seri/varyant, validasyon)
     *  - 403 {ok:false, message, error} → yetki reddi (errorBody, parseApiError message'ı tercih eder)
     *  - diğer HTTP hataları            → generic "HTTP <kod>"
     */
    private fun unwrapStockDoc(resp: Response<StockDocResponse>): StockDocResult {
        if (!resp.isSuccessful) error(parseApiError(resp) ?: "HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: body.message ?: "İşlem başarısız")
        return StockDocResult(docId = body.docId ?: 0, docNumber = body.docNumber ?: "")
    }

    /**
     * Hata gövdesinden kullanıcıya gösterilecek mesajı çıkarır. İki gövde şekli var:
     * 400/404 → `{"error":"..."}` (error alanı zaten kullanıcı-dostu);
     * 403     → `{"ok","message","error"}` (message kullanıcı-dostu, error teknik) —
     * bu yüzden önce message, yoksa error okunur. Ayrıştırılamazsa null döner,
     * çağıran taraf generic "HTTP <kod>" mesajına düşer.
     */
    private fun parseApiError(resp: Response<*>): String? = try {
        resp.errorBody()?.string()?.let { raw ->
            val json = JSONObject(raw)
            json.optString("message").takeIf { it.isNotBlank() }
                ?: json.optString("error").takeIf { it.isNotBlank() }
        }
    } catch (e: Exception) {
        null
    }

    /** Network/JSON hatalarını Result.Failure olarak döndürür (throw yok, UI fold ile okur). */
    private inline fun <T> runCatchingApi(block: () -> T): Result<T> = runCatching(block)
}

/** Başarılı stok belgesi yazımının sonucu — UI onay diyaloğunda docNumber gösterilir. */
data class StockDocResult(val docId: Int, val docNumber: String)

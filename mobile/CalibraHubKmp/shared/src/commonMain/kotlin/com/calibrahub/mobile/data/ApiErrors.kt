package com.calibrahub.mobile.data

import io.ktor.client.call.body
import io.ktor.client.statement.HttpResponse

/**
 * Hata govdesinden kullaniciya gosterilecek mesaji cikarir — AuthApi/WarehouseRepository/
 * ProductionRepository PAYLASIR. Android tarafinda WarehouseRepository ve ProductionRepository
 * bu ayni mantigi IKI KEZ kopyalamisti; burada CLAUDE.md "Tekrarlanan mantigi tek kaynaga cek"
 * kuralina uyularak TEK yerde toplandi (bilincli iyilestirme, davranis AYNI).
 *
 * Iki govde sekli: 400/404 `{"error":"..."}` (error alani zaten kullanici-dostu);
 * 403 `{"ok","message","error"}` (message kullanici-dostu, error teknik) — bu yuzden once
 * message, yoksa error okunur. Ayristirilamazsa null doner, cagiran taraf generic
 * "HTTP <kod>" mesajina duser.
 */
internal suspend fun parseApiError(resp: HttpResponse): String? = try {
    val dto = resp.body<ErrorDto>()
    dto.message?.takeIf { it.isNotBlank() } ?: dto.error?.takeIf { it.isNotBlank() }
} catch (e: Exception) {
    null
}

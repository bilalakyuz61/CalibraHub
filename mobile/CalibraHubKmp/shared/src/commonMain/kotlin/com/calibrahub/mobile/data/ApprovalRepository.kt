package com.calibrahub.mobile.data

import com.calibrahub.mobile.session.SessionManager
import io.ktor.client.call.body
import io.ktor.http.isSuccess

/**
 * Onay Bekleyenler repository'si — [ProductionRepository] ile AYNI hata sozlesmesi:
 * is kurali reddi de HTTP hata koduyla (400/404) gelir, 200 + {ok:false} yoktur.
 */
class ApprovalRepository(private val session: SessionManager) {

    suspend fun pending(): Result<List<PendingApprovalDto>> = runCatching {
        val resp = session.approvalApi().list()
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        resp.body<List<PendingApprovalDto>>()
    }

    suspend fun approve(instanceId: Int, note: String?): Result<Unit> = runCatching {
        val resp = session.approvalApi().approve(instanceId, note?.trim()?.takeIf { it.isNotBlank() })
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        Unit
    }

    /** [note] bos olamaz — sunucu da reddeder, burada erken yakalanir (bos ag turu atilmaz). */
    suspend fun reject(instanceId: Int, note: String): Result<Unit> = runCatching {
        val reason = note.trim()
        if (reason.isEmpty()) error("Reddetme gerekçesi zorunludur.")
        val resp = session.approvalApi().reject(instanceId, reason)
        if (!resp.status.isSuccess()) error(parseApiError(resp) ?: "HTTP ${resp.status.value}")
        Unit
    }
}

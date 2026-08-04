package com.calibrahub.mobile.data

import kotlinx.serialization.Serializable

/**
 * `api/mobile` altindaki auth/session sozlesme DTO'lari — CalibraHubAndroid `CalibraApi.kt`'nin
 * WhatsApp DISI (WhatsApp/sohbet mobil kapsamdan TAMAMEN CIKARILDI, 2026-08-04 kullanici karari)
 * kismiyla BIREBIR alan adi/opsiyonellik eslesir (bkz. CLAUDE.md — sessiz kirilma sinifi 1).
 * Enum-benzeri alan yok (auth taraf sade).
 */

@Serializable
data class PingResponse(
    val ok: Boolean = false,
    val product: String? = null,
    val version: String? = null,
)

@Serializable
data class CompanyDto(
    val id: Int,
    val name: String,
)

@Serializable
data class LoginCompaniesRequest(
    val email: String,
    val password: String,
)

@Serializable
data class LoginRequest(
    val email: String,
    val password: String,
    val companyId: Int? = null,
)

@Serializable
data class LoginResponse(
    val ok: Boolean = false,
    val displayName: String? = null,
    val error: String? = null,
)

@Serializable
data class WhoAmIResponse(
    val ok: Boolean = false,
    val userName: String? = null,
)

/**
 * GET /api/mobile/session 200 yaniti — session-backend kontrati: { ok, userId, displayName,
 * email, companyId, companyName }. 401 durumunda bu tip HIC parse edilmez (bkz.
 * SessionManager.probeSession — yalniz HTTP status koduna bakilir).
 */
@Serializable
data class SessionDto(
    val ok: Boolean = false,
    val userId: Int = 0,
    val displayName: String? = null,
    val email: String? = null,
    val companyId: Int = 0,
    val companyName: String? = null,
)

/** Genel hata govdesi — 400/404 `{"error":"..."}` (Warehouse/Production repository'lerinin
 * `parseApiError` yardimcisi tarafindan okunur). */
@Serializable
data class ErrorDto(
    val error: String? = null,
    val message: String? = null,
)

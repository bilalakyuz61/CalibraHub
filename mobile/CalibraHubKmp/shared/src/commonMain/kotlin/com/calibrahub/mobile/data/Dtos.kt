package com.calibrahub.mobile.data

import kotlinx.serialization.Serializable

/**
 * `api/mobile` altindaki sozlesme DTO'lari. Alan adlari sunucunun dondurdugu JSON ile
 * birebir eslesmek zorunda (bkz. CLAUDE.md — sessiz kirilma sinifi 1).
 */

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
data class StockBalanceDto(
    val locationId: Int,
    val locationName: String,
    val quantity: Double,
)

@Serializable
data class StockQueryDto(
    val itemId: Int = 0,
    val itemCode: String = "",
    val itemName: String = "",
    val unit: String? = null,
    val balances: List<StockBalanceDto> = emptyList(),
    val trackingType: String = "None",
    val autoSerial: Boolean = false,
)

@Serializable
data class ErrorDto(
    val error: String? = null,
)

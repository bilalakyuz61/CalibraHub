package com.calibrahub.mobile.data

import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.http.ContentType

/**
 * `api/mobile` altindaki uc noktalarini tuketen ince istemci. Kalici depolama YOK
 * (POC kasitli sadelestirme); cookie yonetimi HttpClient'in HttpCookies plugin'i
 * uzerinden bellek-ici yapilir.
 */
sealed class ApiResult<out T> {
    data class Success<T>(val data: T) : ApiResult<T>()
    data class Failure(val message: String) : ApiResult<Nothing>()
}

class CalibraApiClient(
    private val baseUrl: String,
    private val client: HttpClient,
) {
    private val apiBase: String
        get() {
            val trimmed = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"
            return "${trimmed}api/mobile/"
        }

    suspend fun loginCompanies(email: String, password: String): ApiResult<List<CompanyDto>> {
        return try {
            val response = client.post("${apiBase}login-companies") {
                contentType(ContentType.Application.Json)
                setBody(LoginCompaniesRequest(email = email, password = password))
            }
            if (response.status == HttpStatusCode.OK) {
                ApiResult.Success(response.body<List<CompanyDto>>())
            } else {
                ApiResult.Failure("Sunucu hatasi: ${response.status.value}")
            }
        } catch (e: Exception) {
            ApiResult.Failure(e.message ?: "Baglanti hatasi")
        }
    }

    suspend fun login(email: String, password: String, companyId: Int?): ApiResult<LoginResponse> {
        return try {
            val response = client.post("${apiBase}login") {
                contentType(ContentType.Application.Json)
                setBody(LoginRequest(email = email, password = password, companyId = companyId))
            }
            if (response.status == HttpStatusCode.OK) {
                ApiResult.Success(response.body<LoginResponse>())
            } else {
                ApiResult.Failure("Sunucu hatasi: ${response.status.value}")
            }
        } catch (e: Exception) {
            ApiResult.Failure(e.message ?: "Baglanti hatasi")
        }
    }

    suspend fun queryStock(code: String): ApiResult<StockQueryDto> {
        return try {
            val response = client.get("${apiBase}warehouse/stock") {
                parameter("code", code)
            }
            if (response.status == HttpStatusCode.OK) {
                ApiResult.Success(response.body<StockQueryDto>())
            } else {
                val error = try {
                    response.body<ErrorDto>().error
                } catch (e: Exception) {
                    null
                }
                ApiResult.Failure(error ?: "Sunucu hatasi: ${response.status.value}")
            }
        } catch (e: Exception) {
            ApiResult.Failure(e.message ?: "Baglanti hatasi")
        }
    }
}

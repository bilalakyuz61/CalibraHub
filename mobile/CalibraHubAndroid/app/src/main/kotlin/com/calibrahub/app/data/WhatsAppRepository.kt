package com.calibrahub.app.data

import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.File

/**
 * UI'ya temiz Result<T> arayüzü sunan thin repository wrapper.
 * ViewModel'ler buradan çağırır; HTTP detayları gizli kalır.
 */
class WhatsAppRepository(private val session: SessionManager) {

    suspend fun companies(): Result<List<CompanyDto>> = runCatchingApi {
        val resp = session.buildApi().companies()
        if (!resp.isSuccessful) error("HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    suspend fun login(email: String, password: String, companyId: Int? = null): Result<String> = runCatchingApi {
        val api = session.buildApi()
        val resp = api.login(LoginRequest(email, password, companyId))
        when {
            !resp.isSuccessful -> error("HTTP ${resp.code()}")
            resp.body()?.ok != true -> error(resp.body()?.error ?: "Bilinmeyen hata")
            else -> {
                val name = resp.body()?.displayName ?: email
                session.setDisplayName(name)
                name
            }
        }
    }

    suspend fun whoAmI(): Result<String?> = runCatchingApi {
        val api = session.buildApi()
        val resp = api.whoAmI()
        if (resp.isSuccessful && resp.body()?.ok == true) resp.body()?.userName else null
    }

    suspend fun logout(): Result<Unit> = runCatchingApi {
        runCatching { session.buildApi().logout() }
        session.clearSession()
    }

    suspend fun conversations(): Result<List<ConversationDto>> = runCatchingApi {
        val resp = session.buildApi().conversations()
        if (!resp.isSuccessful) error("HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    suspend fun messages(phone: String): Result<List<MessageDto>> = runCatchingApi {
        val resp = session.buildApi().messages(phone)
        if (!resp.isSuccessful) error("HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    suspend fun sendText(phone: String, text: String): Result<String?> = runCatchingApi {
        val resp = session.buildApi().sendText(SendTextRequest(phone, text))
        if (!resp.isSuccessful) error("HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: "Mesaj gönderilemedi")
        body.messageId
    }

    suspend fun sendMedia(phone: String, file: File, mimeType: String, caption: String?): Result<String?> = runCatchingApi {
        val phoneBody   = phone.toRequestBody("text/plain".toMediaTypeOrNull())
        val captionBody = caption?.toRequestBody("text/plain".toMediaTypeOrNull())
        val filePart    = MultipartBody.Part.createFormData(
            "file", file.name, file.asRequestBody(mimeType.toMediaTypeOrNull())
        )
        val resp = session.buildApi().sendMedia(phoneBody, captionBody, filePart)
        if (!resp.isSuccessful) error("HTTP ${resp.code()}")
        val body = resp.body() ?: error("Boş yanıt")
        if (!body.ok) error(body.error ?: "Dosya gönderilemedi")
        body.messageId
    }

    suspend fun markRead(phone: String): Result<Unit> = runCatchingApi {
        session.buildApi().markRead(phone)
        Unit
    }

    /**
     * Network/JSON hatalarını Result.Failure olarak döndürür.
     * UI sadece result.fold(...) ile sonucu kullanır, exception throw olmaz.
     */
    private inline fun <T> runCatchingApi(block: () -> T): Result<T> =
        runCatching(block)
}

package com.calibrahub.app.data

/**
 * UI'ya temiz Result<T> arayüzü sunan thin repository wrapper — login/session/whoami akışları.
 * ViewModel'ler buradan çağırır; HTTP detayları gizli kalır.
 *
 * 2026-08-04: WhatsApp/sohbet uç noktaları (conversations/messages/sendText/sendMedia/markRead)
 * bu sınıftan (eski adıyla `WhatsAppRepository`) kullanıcı kararıyla TAMAMEN kaldırıldı — mobil
 * kapsamdan WhatsApp çıkarıldı (bkz. CLAUDE.md). Login/session kısmı işlevsel olarak AYNI kaldı,
 * yalnızca sınıf adı `AuthRepository` olarak netleştirildi (isim artık içeriğiyle örtüşüyor).
 */
class AuthRepository(private val session: SessionManager) {

    suspend fun companies(): Result<List<CompanyDto>> = runCatchingApi {
        val resp = session.buildApi().companies()
        if (!resp.isSuccessful) error("HTTP ${resp.code()}")
        resp.body() ?: emptyList()
    }

    /** Parola doğrulandıktan sonra erişilebilir şirket listesini döner — login akışının ilk adımı. */
    suspend fun loginCompanies(email: String, password: String): Result<List<CompanyDto>> = runCatchingApi {
        val resp = session.buildApi().loginCompanies(LoginCompaniesRequest(email, password))
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

    /**
     * "Beni hatırla" otomatik giriş probe'u — GET /api/mobile/session (bkz. MainActivity.AppNav
     * açılış akışı). 401 kontrat gereği "kimlik yok/expired" anlamına gelir; bu bir HATA DEĞİL,
     * normal bir durumdur → Result.success(null) (çağıran taraf login'e düşer + kalıcı çerezi
     * temizler). Network/diğer HTTP hataları Result.failure (çağıran taraf yine login'e düşer
     * ama kalıcı çerezi SİLMEZ — geçici bağlantı sorunu olabilir, bkz. AppNav yorumu).
     */
    suspend fun fetchSession(): Result<SessionDto?> = runCatchingApi {
        val resp = session.buildApi().session()
        when {
            resp.code() == 401 -> null
            resp.isSuccessful && resp.body()?.ok == true -> resp.body()
            else -> error("HTTP ${resp.code()}")
        }
    }

    suspend fun logout(): Result<Unit> = runCatchingApi {
        runCatching { session.buildApi().logout() }
        session.clearSession()
    }

    /**
     * Network/JSON hatalarını Result.Failure olarak döndürür.
     * UI sadece result.fold(...) ile sonucu kullanır, exception throw olmaz.
     */
    private inline fun <T> runCatchingApi(block: () -> T): Result<T> =
        runCatching(block)
}

package com.calibrahub.mobile.session

import com.calibrahub.mobile.data.ApiResult
import com.calibrahub.mobile.data.ApprovalApi
import com.calibrahub.mobile.data.ApprovalRepository
import com.calibrahub.mobile.data.AuthApi
import com.calibrahub.mobile.data.ContactCardApi
import com.calibrahub.mobile.data.ContactCardRepository
import com.calibrahub.mobile.data.CompanyDto
import com.calibrahub.mobile.data.LoginCompaniesRequest
import com.calibrahub.mobile.data.LoginRequest
import com.calibrahub.mobile.data.LoginResponse
import com.calibrahub.mobile.data.PingResponse
import com.calibrahub.mobile.data.ProductionApi
import com.calibrahub.mobile.data.ProductionRepository
import com.calibrahub.mobile.data.SessionDto
import com.calibrahub.mobile.data.WarehouseApi
import com.calibrahub.mobile.data.WarehouseRepository
import com.calibrahub.mobile.data.WhoAmIResponse
import com.calibrahub.mobile.data.parseApiError
import com.calibrahub.mobile.net.PersistentCookiesStorage
import com.calibrahub.mobile.net.createHttpClient
import com.calibrahub.mobile.net.httpEngineFactory
import com.calibrahub.mobile.storage.SecureStorage
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.defaultRequest
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.http.HttpStatusCode
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json

/**
 * Kullanicinin koyu/acik tema tercihi — CalibraHubAndroid `SessionManager.ThemeMode` ile AYNI
 * uc degerli model + storageKey deseni. [SYSTEM] (varsayilan) cihaz ayarini izler.
 *
 * Faz 2a guncellemesi: [SecureStorage] hala suspend get/put'tan ibaret (DataStore Flow
 * multiplatform'da yok), bu yuzden CANLI reaktiflik [SessionManager.themeMode] alaninda bir
 * [MutableStateFlow] ile SIMULE edilir — `init` bloğunda [storage]'dan bir kez okunup tohumlanir,
 * [SessionManager.setThemeMode] hem storage'a yazar hem StateFlow'u gunceller. Boylece
 * [com.calibrahub.mobile.ui.AppRoot] `collectAsState` ile dinleyip SettingsScreen'deki degisikligi
 * Activity/ComposeUIViewController restart OLMADAN aninda yansitir — Android SessionManager'daki
 * DataStore Flow'un davranissal esdegeri (bkz. CalibraHubAndroid SessionManager.themeMode KDoc).
 */
enum class ThemeMode(val storageKey: String) {
    SYSTEM("system"),
    LIGHT("light"),
    DARK("dark");

    companion object {
        fun fromStorageKey(key: String?): ThemeMode = entries.firstOrNull { it.storageKey == key } ?: SYSTEM
    }
}

/** GET /api/mobile/session sonucu — 200/401/diger ayrimi CalibraHubAndroid'deki AYNI kontratla
 * (bkz. AuthApi.session KDoc): 401'de govde HIC parse edilmez. */
sealed class SessionProbeResult {
    data class Success(val dto: SessionDto) : SessionProbeResult()
    data object Unauthorized : SessionProbeResult()
    data class Error(val message: String) : SessionProbeResult()
}

/**
 * Platform-bagimsiz oturum yoneticisi — CalibraHubAndroid `SessionManager.kt`'nin (Retrofit/OkHttp/
 * Context tabanli) KMP karsiligi: Ktor + [SecureStorage] uzerine kurulu, Android Context'e bagimli
 * DEGIL. Gorevleri Android ile AYNI: base URL persistence, cached display name, HttpClient factory
 * (kalici cookie jar ile), "beni hatirla" tercihi + cookie yaziminin bu tercihe gate'lenmesi, global
 * 401 yakalama ([sessionExpiredEvents]), tema tercihi persistence.
 *
 * KMP karsiligi olarak `buildApi(Class<T>)` yerine [authApi]/[warehouseApi]/[productionApi] TIPLI
 * factory metodlari kullanilir (Ktor'da reflection tabanli servis uretimi yok — annotation
 * islemcisi gerektirmez, bu yuzden Class<T> parametresine gerek kalmadi).
 */
class SessionManager(private val storage: SecureStorage) {

    private val cookiesStorage = PersistentCookiesStorage(storage)

    /** Uygulama yasam suresince TEK HttpClient — cookie state'i (persistent jar) korunur. */
    private val httpClient: HttpClient by lazy {
        createHttpClient(cookiesStorage = cookiesStorage, onUnauthorized = ::handleUnauthorized)
    }

    private val _sessionExpiredEvents = MutableSharedFlow<Unit>(
        extraBufferCapacity = 1,
        onBufferOverflow = BufferOverflow.DROP_OLDEST,
    )

    /** Herhangi bir authed `/api/mobile/` cagrisi 401 donerse bir event basilir — UI katmani
     * (Faz 2) bunu dinleyip login ekranina yonlendirir (Android AppNav'in AYNI rolu). */
    val sessionExpiredEvents: SharedFlow<Unit> = _sessionExpiredEvents.asSharedFlow()

    val warehouseRepository: WarehouseRepository by lazy { WarehouseRepository(this) }
    val productionRepository: ProductionRepository by lazy { ProductionRepository(this) }
    val approvalRepository: ApprovalRepository by lazy { ApprovalRepository(this) }
    val contactCardRepository: ContactCardRepository by lazy { ContactCardRepository(this) }

    private val _themeMode = MutableStateFlow(ThemeMode.SYSTEM)

    /** Reaktif tema tercihi — bkz. [ThemeMode] sinifi ustundeki KDoc. */
    val themeMode: StateFlow<ThemeMode> = _themeMode.asStateFlow()

    private val _pinnedRoutes = MutableStateFlow<Set<String>>(emptySet())

    /** Ana ekrana sabitlenen (pinned) drawer route'lari — CalibraHubAndroid
     * `SessionManager.pinnedRoutes` DataStore Flow'unun KMP karsiligi (bkz. [themeMode] ile AYNI
     * StateFlow-simulasyon deseni). [com.calibrahub.mobile.ui.home.HomeScreen] kisayol izgarasini
     * BUNDAN besler, [com.calibrahub.mobile.ui.nav.AppDrawerContent] pin ikonuyla degistirir. */
    val pinnedRoutes: StateFlow<Set<String>> = _pinnedRoutes.asStateFlow()

    private val _permissions = MutableStateFlow<Set<String>?>(null)

    /**
     * Kullanicinin gorebilecegi ekranlarin form kodlari — GET /api/mobile/session'dan gelir.
     * `null` = HENUZ BILINMIYOR ya da sunucu bu alani gondermiyor (eski surum) -> menu HICBIR
     * SEYI gizlemez (fail-open). Bos kume = gercekten hicbir ekrana yetki yok.
     *
     * Bu YALNIZCA gorunurluk katmanidir: yetkisiz bir ekrana yine de gidilse endpoint 403
     * doner. Yani buradaki bir hata veri sizdirmaz, olsa olsa kart gosterir/gizler.
     */
    val permissions: StateFlow<Set<String>?> = _permissions.asStateFlow()

    /** Acilis tohumlamasi icin uygulama-omurlu scope — bkz. [init]. */
    private val initScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    init {
        // Kalici tercihleri (beni-hatirla / tema / pinned) depodan oku ve StateFlow'lari tohumla.
        //
        // ASENKRON olmasi ZORUNLU: SessionManager Compose kompozisyonu icinde kurulur
        // (AppRoot: `remember { SessionManager(...) }`), yani ANA THREAD'de. Burada `runBlocking`
        // kullanmak iOS'ta ana thread'i acilis aninda BLOKLAR; iOS watchdog bunu "yanit vermeyen
        // uygulama" sayip sureci OLDURUR -> kullanici "uygulama coktu" gorur (UI hic cizilmeden).
        // Android'de ayni kod zararsizdi (boyle bir watchdog yok) — bu yuzden Android'de calisip
        // iOS'ta aninda cokuyordu. DERS: ortak kodda ana thread'i bloklayan cagri YAPMA.
        //
        // Varsayilanlar (SYSTEM tema / bos pin) alan tanimlarinda zaten mevcut; tohumlama bitince
        // StateFlow'lar guncellenir ve Compose otomatik yeniden cizer. Acilis ile tohumlama
        // arasindaki pencerede ag cagrisi olmaz (kullanici henuz giris yapmadi), dolayisiyla
        // cookie persistence tercihinin gec uygulanmasi risk yaratmaz.
        initScope.launch {
            val remembered = storage.getString(REMEMBER_ME_KEY)?.toBooleanStrictOrNull() ?: true
            cookiesStorage.setPersistenceEnabled(remembered)
            _themeMode.value = ThemeMode.fromStorageKey(storage.getString(THEME_MODE_KEY))
            _pinnedRoutes.value = storage.getStringSet(PINNED_ROUTES_KEY) ?: emptySet()
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Kalici alanlar — base URL / display / remember-me / tema
    // ─────────────────────────────────────────────────────────────────

    suspend fun currentBaseUrl(): String = storage.getString(BASE_URL_KEY) ?: DEFAULT_BASE_URL

    suspend fun setBaseUrl(url: String) {
        val normalized = if (url.endsWith("/")) url else "$url/"
        storage.putString(BASE_URL_KEY, normalized)
    }

    suspend fun currentDisplayName(): String? = storage.getString(DISPLAY_NAME_KEY)

    suspend fun setDisplayName(name: String?) {
        if (name == null) storage.remove(DISPLAY_NAME_KEY) else storage.putString(DISPLAY_NAME_KEY, name)
    }

    suspend fun isRememberMeEnabled(): Boolean = storage.getString(REMEMBER_ME_KEY)?.toBooleanStrictOrNull() ?: true

    /**
     * "Beni hatirla" tercihini kaydeder + cookie jar'in SecureStorage yazimini (persistenceEnabled)
     * HEMEN senkronlar. LoginScreen'in login istegi ONCESINDE cagirmasi ZORUNLU — Android
     * SessionManager.setRememberMe ile AYNI sozlesme (bkz. o KDoc).
     */
    suspend fun setRememberMe(enabled: Boolean) {
        storage.putString(REMEMBER_ME_KEY, enabled.toString())
        cookiesStorage.setPersistenceEnabled(enabled)
    }

    suspend fun rememberedEmail(): String? = storage.getString(REMEMBERED_EMAIL_KEY)

    /**
     * "Parolayi hatirla" tercihi — [setRememberMe]'den AYRI ve varsayilani KAPALI. Parolayi
     * saklamak oturum cerezini saklamaktan farkli bir risk sinifidir (cerez suresi dolar ve
     * iptal edilebilir, parola edilemez), bu yuzden kullanici ayrica ve bilerek acmalidir.
     */
    suspend fun isRememberPasswordEnabled(): Boolean =
        storage.getString(REMEMBER_PASSWORD_KEY)?.toBooleanStrictOrNull() ?: false

    /**
     * Tercihi kaydeder; KAPATILDIGINDA saklanan parolayi da SILER — "kapattim ama diskte
     * duruyor" durumu olusmasin.
     */
    suspend fun setRememberPassword(enabled: Boolean) {
        storage.putString(REMEMBER_PASSWORD_KEY, enabled.toString())
        if (!enabled) storage.remove(REMEMBERED_PASSWORD_KEY)
    }

    /** Saklanan parola — yalniz tercih ACIKKEN doner (kapaliyken artik kayit da yok). */
    suspend fun rememberedPassword(): String? =
        if (isRememberPasswordEnabled()) storage.getString(REMEMBERED_PASSWORD_KEY) else null

    /** Basarili giristen SONRA cagrilir; tercih kapaliysa hicbir sey yazmaz. */
    suspend fun persistPasswordIfEnabled(password: String) {
        if (password.isBlank()) return
        if (isRememberPasswordEnabled()) storage.putString(REMEMBERED_PASSWORD_KEY, password)
    }

    suspend fun currentCompanyName(): String? = storage.getString(COMPANY_NAME_KEY)

    suspend fun currentCompanyId(): Int? = storage.getString(COMPANY_ID_KEY)?.toIntOrNull()

    /** Tema tercihini kaydeder — [themeMode] StateFlow'u ayni cagrida gunceller (bkz. yukarida
     * ThemeMode KDoc'u); AppRoot bunu dinledigi icin degisiklik ANINDA tum agaca yansir. */
    suspend fun setThemeMode(mode: ThemeMode) {
        storage.putString(THEME_MODE_KEY, mode.storageKey)
        _themeMode.value = mode
    }

    /** Bir drawer route'unu ana ekran kisayol izgarasina sabitler/kaldirir — [pinnedRoutes]
     * StateFlow'u ayni cagrida gunceller (bkz. [pinnedRoutes] KDoc'u). */
    suspend fun togglePinnedRoute(route: String) {
        val current = _pinnedRoutes.value
        val updated = if (route in current) current - route else current + route
        storage.putStringSet(PINNED_ROUTES_KEY, updated)
        _pinnedRoutes.value = updated
    }

    /** Acilista "beni hatirla" otomatik-giris karari icin — CalibraHubAndroid
     * `session.cookieJar.hasStoredCookies()` ile AYNI sozlesme (bkz. [AppNavHost] KDoc'u,
     * com.calibrahub.mobile.ui.nav paketi). */
    suspend fun hasStoredCookies(): Boolean = cookiesStorage.hasStoredCookies()

    /** Basarili login/acilis probe'u sonrasi oturum goruntu alanlarini kaydeder — cookie'nin
     * kendisi bu fonksiyonun isi DEGIL (bkz. Android KDoc, ayni gerekce). */
    suspend fun persistSessionDisplay(email: String?, displayName: String?, companyId: Int, companyName: String?) {
        if (!email.isNullOrBlank()) storage.putString(REMEMBERED_EMAIL_KEY, email)
        if (!displayName.isNullOrBlank()) storage.putString(DISPLAY_NAME_KEY, displayName)
        storage.putString(COMPANY_ID_KEY, companyId.toString())
        if (!companyName.isNullOrBlank()) storage.putString(COMPANY_NAME_KEY, companyName)
    }

    /** Logout VEYA 401-tetiklemeli oturum sonlandirma. [REMEMBERED_EMAIL_KEY]/[REMEMBER_ME_KEY]
     * BILEREK silinmez (Android ile AYNI karar — bir sonraki LoginScreen'de hatirlanmaya devam eder). */
    suspend fun clearSession() {
        cookiesStorage.clear()
        storage.remove(DISPLAY_NAME_KEY)
        storage.remove(COMPANY_ID_KEY)
        storage.remove(COMPANY_NAME_KEY)
        // Izinler oturuma baglidir: baska kullanici giris yaparsa onceki kullanicinin
        // menu suzgeci kalmamali (null = bilinmiyor -> yeni probe/refresh gelene kadar
        // hicbir sey gizlenmez, fail-open).
        _permissions.value = null
    }

    private suspend fun handleUnauthorized() {
        cookiesStorage.clear()
        storage.remove(DISPLAY_NAME_KEY)
        storage.remove(COMPANY_ID_KEY)
        storage.remove(COMPANY_NAME_KEY)
        _permissions.value = null
        _sessionExpiredEvents.tryEmit(Unit)
    }

    // ─────────────────────────────────────────────────────────────────
    // Tipli Api factory'leri — Android'deki `buildApi(Class<T>)`'in KMP karsiligi
    // ─────────────────────────────────────────────────────────────────

    suspend fun authApi(): AuthApi = AuthApi(baseUrl = currentBaseUrl(), client = httpClient)
    suspend fun warehouseApi(): WarehouseApi = WarehouseApi(baseUrl = currentBaseUrl(), client = httpClient)
    suspend fun productionApi(): ProductionApi = ProductionApi(baseUrl = currentBaseUrl(), client = httpClient)
    suspend fun approvalApi(): ApprovalApi = ApprovalApi(baseUrl = currentBaseUrl(), client = httpClient)
    suspend fun contactCardApi(): ContactCardApi = ContactCardApi(baseUrl = currentBaseUrl(), client = httpClient)

    // ─────────────────────────────────────────────────────────────────
    // Auth akislari
    // ─────────────────────────────────────────────────────────────────

    /**
     * Sunucu dogrulama (LoginScreen "Dogrula" akisi) — kullanicinin o an yazdigi, HENUZ
     * [setBaseUrl] ile KAYDEDILMEMIS [rawBaseUrl] degeriyle bagimsiz/tek-seferlik bir cagri yapar.
     * [authApi] BILEREK KULLANILMAZ (o [currentBaseUrl] / kalici cookie jar okur); burada ayri,
     * cookie'siz, kisa timeout'lu (5 sn) bir HttpClient kurulur — Android `ping(rawBaseUrl)` ile
     * AYNI gerekce.
     */
    suspend fun ping(rawBaseUrl: String): Result<PingResponse> = runCatching {
        val trimmed = rawBaseUrl.trim()
        require(trimmed.isNotEmpty()) { "Sunucu adresi boş" }
        val normalized = if (trimmed.endsWith("/")) trimmed else "$trimmed/"

        val probeClient = HttpClient(httpEngineFactory) {
            install(ContentNegotiation) { json(Json { ignoreUnknownKeys = true }) }
            install(HttpTimeout) {
                requestTimeoutMillis = 5_000
                connectTimeoutMillis = 5_000
            }
            defaultRequest { header("X-Requested-With", "XMLHttpRequest") }
            expectSuccess = false
        }
        try {
            val resp = probeClient.get("${normalized}api/mobile/ping")
            if (!resp.status.isSuccess()) error("HTTP ${resp.status.value}")
            resp.body<PingResponse>()
        } finally {
            probeClient.close()
        }
    }

    suspend fun loginCompanies(email: String, password: String): ApiResult<List<CompanyDto>> = try {
        val resp = authApi().loginCompanies(LoginCompaniesRequest(email = email, password = password))
        if (resp.status == HttpStatusCode.OK) {
            ApiResult.Success(resp.body<List<CompanyDto>>())
        } else {
            ApiResult.Failure(parseApiError(resp) ?: "Sunucu hatası: ${resp.status.value}")
        }
    } catch (e: Exception) {
        ApiResult.Failure(e.message ?: "Bağlantı hatası")
    }

    suspend fun login(email: String, password: String, companyId: Int?): ApiResult<LoginResponse> = try {
        val resp = authApi().login(LoginRequest(email = email, password = password, companyId = companyId))
        if (resp.status == HttpStatusCode.OK) {
            ApiResult.Success(resp.body<LoginResponse>())
        } else {
            ApiResult.Failure(parseApiError(resp) ?: "Sunucu hatası: ${resp.status.value}")
        }
    } catch (e: Exception) {
        ApiResult.Failure(e.message ?: "Bağlantı hatası")
    }

    suspend fun logout(): ApiResult<Unit> = try {
        authApi().logout()
        clearSession()
        ApiResult.Success(Unit)
    } catch (e: Exception) {
        ApiResult.Failure(e.message ?: "Bağlantı hatası")
    }

    suspend fun whoAmI(): ApiResult<WhoAmIResponse> = try {
        val resp = authApi().whoAmI()
        if (resp.status.isSuccess()) {
            ApiResult.Success(resp.body<WhoAmIResponse>())
        } else {
            ApiResult.Failure(parseApiError(resp) ?: "Sunucu hatası: ${resp.status.value}")
        }
    } catch (e: Exception) {
        ApiResult.Failure(e.message ?: "Bağlantı hatası")
    }

    /** Acilis probe'u (auto-login) — bkz. [SessionProbeResult] KDoc. Yan etki: yanittaki
     * izin listesi [permissions] akisina yazilir (menu suzgeci bundan beslenir). */
    suspend fun probeSession(): SessionProbeResult = try {
        val resp = authApi().session()
        when {
            resp.status == HttpStatusCode.OK -> {
                val dto = resp.body<SessionDto>()
                _permissions.value = dto.permissions?.toSet()
                SessionProbeResult.Success(dto)
            }
            resp.status == HttpStatusCode.Unauthorized -> SessionProbeResult.Unauthorized
            else -> SessionProbeResult.Error("HTTP ${resp.status.value}")
        }
    } catch (e: Exception) {
        SessionProbeResult.Error(e.message ?: "Bağlantı hatası")
    }

    /**
     * Izin listesini tazeler. Giris BASARILI olduktan hemen sonra cagrilir: /login yaniti
     * izin tasimaz, otomatik-giris probe'u ise yalniz uygulama acilisinda calisir — bu cagri
     * olmadan elle giris yapan kullanicinin menusu o oturum boyunca suzulmeden kalirdi.
     *
     * Sessizce basarisiz olur (izinler `null` kalir -> menu hicbir seyi gizlemez): agdaki
     * gecici bir sorun kullaniciyi bos menuyle kilitlememeli.
     */
    suspend fun refreshPermissions() {
        runCatching {
            val resp = authApi().session()
            if (resp.status == HttpStatusCode.OK) {
                _permissions.value = resp.body<SessionDto>().permissions?.toSet()
            }
        }
    }

    companion object {
        private const val BASE_URL_KEY = "base_url"
        private const val DISPLAY_NAME_KEY = "display_name"
        private const val REMEMBER_ME_KEY = "remember_me"
        private const val REMEMBERED_EMAIL_KEY = "remembered_email"
        private const val REMEMBER_PASSWORD_KEY = "remember_password"
        private const val REMEMBERED_PASSWORD_KEY = "remembered_password"
        private const val COMPANY_ID_KEY = "company_id"
        private const val COMPANY_NAME_KEY = "company_name"
        private const val THEME_MODE_KEY = "theme_mode"
        private const val PINNED_ROUTES_KEY = "pinned_routes"

        /** Emulator varsayilani (10.0.2.2 host loopback) — fiziksel cihazda kullanici LAN IP'sine
         * degistirir (bkz. gorev talimati). */
        private const val DEFAULT_BASE_URL = "http://10.0.2.2:61001/"
    }
}

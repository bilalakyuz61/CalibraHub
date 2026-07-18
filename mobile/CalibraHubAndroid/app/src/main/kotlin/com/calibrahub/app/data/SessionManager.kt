package com.calibrahub.app.data

import android.content.Context
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.calibrahub.app.BuildConfig
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.runBlocking
import okhttp3.Interceptor
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.moshi.MoshiConverterFactory
import java.util.concurrent.TimeUnit

/**
 * Kullanıcının koyu/açık tema tercihi (2026-07-19). [SYSTEM] (varsayılan) cihaz ayarını izler —
 * bkz. [androidx.compose.foundation.isSystemInDarkTheme]; [LIGHT]/[DARK] cihaz ayarından bağımsız
 * sabit tema zorlar. [storageKey] DataStore'a yazılan ham string'tir — [DeliveryDocType.apiValue]
 * (bkz. `ui/warehouse/DeliveryScreen.kt`) ile AYNI "enum + sabit string key" deseni.
 */
enum class ThemeMode(val storageKey: String) {
    SYSTEM("system"),
    LIGHT("light"),
    DARK("dark");

    companion object {
        /** DataStore'dan okunan ham string'i [ThemeMode]'a çevirir; tanınmayan/eksik değer → [SYSTEM]. */
        fun fromStorageKey(key: String?): ThemeMode = values().firstOrNull { it.storageKey == key } ?: SYSTEM
    }
}

/**
 * Singleton-ish session yöneticisi.
 *
 * Görevleri:
 *  - Base URL persistence (kullanıcı LAN IP gireceği zaman değiştirebilir)
 *  - Cached display name (login sonrası gösterim)
 *  - Retrofit + OkHttp factory (cookie jar + interceptor'larla)
 *  - "Beni hatırla" tercihi + kalıcı oturum çerezinin DataStore yazımını gate'lemesi (2026-07-17)
 *  - Global 401 yakalama → [sessionExpiredEvents]
 *  - Tema tercihi ([ThemeMode]) persistence — bkz. [themeMode] / [setThemeMode] (2026-07-19)
 *
 * Application-scoped: CalibraApp.kt içinden init edilir.
 */
class SessionManager(private val context: Context) {

    val cookieJar = PersistentCookieJar(context)

    init {
        // Cookie jar'ın DataStore'a yazıp yazmayacağını (persistenceEnabled) uygulama açılışında
        // son kaydedilen "beni hatırla" tercihiyle senkronlar — [PersistentCookieJar.init] ile
        // AYNI desen (senkron, tek seferlik runBlocking; SessionManager application-scoped tek
        // instance olduğu için maliyeti düşük). Tercih hiç kaydedilmemişse (ilk kurulum) varsayılan
        // AÇIK — henüz hiç cookie yok, bu yüzden pratikte fark etmez.
        val remembered = runBlocking {
            context.sessionStore.data.map { it[REMEMBER_ME_KEY] ?: true }.first()
        }
        cookieJar.setPersistenceEnabled(remembered)
    }

    private val baseUrlFlow: Flow<String> = context.sessionStore.data
        .map { it[BASE_URL_KEY] ?: BuildConfig.DEFAULT_BASE_URL }

    private val displayNameFlow: Flow<String?> = context.sessionStore.data
        .map { it[DISPLAY_NAME_KEY] }

    private val rememberMeFlow: Flow<Boolean> = context.sessionStore.data
        .map { it[REMEMBER_ME_KEY] ?: true }   // varsayılan AÇIK (LoginScreen switch varsayılanıyla aynı)

    private val rememberedEmailFlow: Flow<String?> = context.sessionStore.data
        .map { it[REMEMBERED_EMAIL_KEY] }

    private val companyNameFlow: Flow<String?> = context.sessionStore.data
        .map { it[COMPANY_NAME_KEY] }

    private val companyIdFlow: Flow<Int?> = context.sessionStore.data
        .map { it[COMPANY_ID_KEY] }

    /**
     * Tema tercihi — CANLI [Flow] olarak dışa açılır; diğer alanlardaki private-flow +
     * `suspend fun current...()` tek-seferlik okuma deseninin AKSİNE. Sebep: tema tercihi
     * [com.calibrahub.app.MainActivity]'nin en üst composable'ında `collectAsState` ile SÜREKLİ
     * dinlenir — kullanıcı [com.calibrahub.app.ui.settings.SettingsScreen]'de tercihi değiştirdiği
     * anda TÜM uygulama (MaterialTheme'in colorScheme'i üzerinden) Activity restart OLMADAN
     * yeniden renklenir. Diğer alanlar (displayName, companyName vb.) yalnız belirli tetikleyici
     * noktalarda (ekran girişi, login sonrası) bir kez okunduğu için suspend getter yeterliydi;
     * tema tercihinde bu YETERSİZ kalır.
     */
    val themeMode: Flow<ThemeMode> = context.sessionStore.data
        .map { ThemeMode.fromStorageKey(it[THEME_MODE_KEY]) }

    suspend fun currentBaseUrl(): String = baseUrlFlow.first()
    suspend fun currentDisplayName(): String? = displayNameFlow.first()
    suspend fun isRememberMeEnabled(): Boolean = rememberMeFlow.first()
    suspend fun rememberedEmail(): String? = rememberedEmailFlow.first()
    suspend fun currentCompanyName(): String? = companyNameFlow.first()
    suspend fun currentCompanyId(): Int? = companyIdFlow.first()

    suspend fun setBaseUrl(url: String) {
        val normalized = if (url.endsWith("/")) url else "$url/"
        context.sessionStore.edit { it[BASE_URL_KEY] = normalized }
    }

    suspend fun setDisplayName(name: String?) {
        context.sessionStore.edit {
            if (name == null) it.remove(DISPLAY_NAME_KEY)
            else it[DISPLAY_NAME_KEY] = name
        }
    }

    /** Tema tercihini kaydeder — [themeMode] Flow'u DataStore üzerinden otomatik yeni değeri yayınlar. */
    suspend fun setThemeMode(mode: ThemeMode) {
        context.sessionStore.edit { it[THEME_MODE_KEY] = mode.storageKey }
    }

    /**
     * "Beni hatırla" tercihini kaydeder + cookie jar'ın DataStore yazımını (persistenceEnabled)
     * HEMEN senkronlar. LoginScreen'in login isteğinden ÖNCE çağırması ZORUNLU — login yanıtının
     * Set-Cookie'si [PersistentCookieJar.saveFromResponse] içinde SENKRON işlendiği için bu
     * flag'in istek anında zaten doğru değerde olması gerekir (bkz. LoginScreen.doLogin).
     */
    suspend fun setRememberMe(enabled: Boolean) {
        context.sessionStore.edit { it[REMEMBER_ME_KEY] = enabled }
        cookieJar.setPersistenceEnabled(enabled)
    }

    /**
     * Oturum görüntü alanlarını (hatırlanan e-posta + displayName + companyId/companyName)
     * DataStore'a yazar. İki çağrı noktası: (1) [com.calibrahub.app.ui.login.LoginScreen]
     * başarılı login sonrası "beni hatırla" AÇIKSA, (2) MainActivity açılış probe'u
     * (GET /api/mobile/session) 200 döndüğünde. Oturum çerezinin kendisi bu fonksiyonun İŞİ
     * DEĞİL — o zaten [PersistentCookieJar] tarafından persistenceEnabled=true iken otomatik
     * yazılır. email/displayName/companyName boş/null gelirse o alana DOKUNULMAZ (savunmacı —
     * kontrat her zaman doldursa da).
     */
    suspend fun persistSessionDisplay(email: String?, displayName: String?, companyId: Int, companyName: String?) {
        context.sessionStore.edit { prefs ->
            if (!email.isNullOrBlank()) prefs[REMEMBERED_EMAIL_KEY] = email
            if (!displayName.isNullOrBlank()) prefs[DISPLAY_NAME_KEY] = displayName
            prefs[COMPANY_ID_KEY] = companyId
            if (!companyName.isNullOrBlank()) prefs[COMPANY_NAME_KEY] = companyName
        }
    }

    /**
     * Logout VEYA 401-tetiklemeli oturum sonlandırma — kalıcı oturum çerezi + oturum-bazlı
     * alanlar (displayName/companyId/companyName) temizlenir. [REMEMBERED_EMAIL_KEY] ve
     * [REMEMBER_ME_KEY] BİLEREK silinmez: bir sonraki LoginScreen açılışında e-posta alanı yine
     * otomatik dolsun + kullanıcının "beni hatırla" tercihi hatırlansın diye (kullanıcı kararı,
     * 2026-07-17).
     */
    suspend fun clearSession() {
        cookieJar.clear()
        context.sessionStore.edit {
            it.remove(DISPLAY_NAME_KEY)
            it.remove(COMPANY_ID_KEY)
            it.remove(COMPANY_NAME_KEY)
        }
    }

    private val _sessionExpiredEvents = MutableSharedFlow<Unit>(
        extraBufferCapacity = 1,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )

    /**
     * Herhangi bir authed `/api/mobile/` altındaki çağrı 401 dönerse (bkz. [buildRetrofit] içindeki
     * authExpiryInterceptor) bir event basılır. [com.calibrahub.app.MainActivity]'nin AppNav'ı
     * bunu dinleyip login ekranına yönlendirir — KRİTİK: bu akışı dinlemeye yalnız NavHost en az
     * bir kez composed olduktan SONRA başlanmalı (bkz. MainActivity yorumu) — erken bir emisyon
     * navController.graph henüz set edilmemişken navigate() çağrısını çökertir.
     */
    val sessionExpiredEvents: SharedFlow<Unit> = _sessionExpiredEvents.asSharedFlow()

    /**
     * Yeni bir Retrofit instance üretir. Pahalı değil ama her API çağrısı için
     * yeniden yaratmamak adına ApplicationScope'da cache'lenmesi önerilir.
     */
    suspend fun buildApi(): CalibraApi = buildRetrofit().create(CalibraApi::class.java)

    /**
     * Diğer /api/mobile/ altındaki Retrofit interface'leri (WarehouseApi gibi) için aynı
     * cookie + X-Requested-With + timeout deseniyle client üretir.
     * Kullanım: `session.buildApi(WarehouseApi::class.java)`.
     *
     * Not: reified generic (`buildApi<T>()`) yerine bilinçli olarak `Class<T>` parametresi
     * tercih edildi — aynı isim+arity'de reified generic overload, üstteki no-arg `buildApi()`
     * ile Kotlin tip çıkarımında (özellikle zincirleme çağrılarda) belirsizliğe yol açabiliyor.
     */
    suspend fun <T> buildApi(service: Class<T>): T = buildRetrofit().create(service)

    /**
     * Sunucu doğrulama (LoginScreen "Doğrula" akışı) — kullanıcının o an yazdığı, henüz
     * [setBaseUrl] ile KAYDEDİLMEMİŞ [rawBaseUrl] değeriyle bağımsız/tek-seferlik bir Retrofit
     * çağrısı yapar. [buildApi]/[buildRetrofit] bilerek KULLANILMAZ — onlar [currentBaseUrl]
     * (persisted) okur; burada kullanıcının henüz kaydetmediği aday adres denenir. Kısa timeout
     * (5 sn): yanlış/ulaşılamaz bir adres login denenmeden önce erken ve net teşhis edilsin diye.
     * Cookie jar BİLEREK takılmaz (ping anonim, oturumla ilgisi yok).
     *
     * Result.success → sunucu 2xx döndü ve gövde parse edildi (`ok`/`product`/`version` — 200
     * dönüp product "CalibraHub" olmaması da BAŞARI sayılır, "beklenmeyen sunucu" yorumu
     * çağıran tarafta yapılır). Result.failure → bağlantı hatası, timeout ya da HTTP hata kodu
     * (sunucuya hiç ulaşılamadı ya da geçersiz adres).
     */
    suspend fun ping(rawBaseUrl: String): Result<PingResponse> = runCatching {
        val trimmed = rawBaseUrl.trim()
        require(trimmed.isNotEmpty()) { "Sunucu adresi boş" }
        val normalized = if (trimmed.endsWith("/")) trimmed else "$trimmed/"

        val moshi = Moshi.Builder().add(KotlinJsonAdapterFactory()).build()
        val mobileHeader = Interceptor { chain ->
            val req = chain.request().newBuilder()
                .header("X-Requested-With", "CalibraHubAndroid")
                .header("Accept", "application/json")
                .build()
            chain.proceed(req)
        }
        val client = OkHttpClient.Builder()
            .addInterceptor(mobileHeader)
            .connectTimeout(5, TimeUnit.SECONDS)
            .readTimeout(5, TimeUnit.SECONDS)
            .writeTimeout(5, TimeUnit.SECONDS)
            .build()
        val retrofit = Retrofit.Builder()
            .baseUrl(normalized)
            .client(client)
            .addConverterFactory(MoshiConverterFactory.create(moshi))
            .build()

        val resp = retrofit.create(CalibraApi::class.java).ping()
        if (!resp.isSuccessful) error("HTTP ${resp.code()}")
        resp.body() ?: error("Boş yanıt")
    }

    private suspend fun buildRetrofit(): Retrofit {
        val baseUrl = currentBaseUrl()
        val moshi = Moshi.Builder().add(KotlinJsonAdapterFactory()).build()

        val logger = HttpLoggingInterceptor().apply {
            level = if (BuildConfig.DEBUG) HttpLoggingInterceptor.Level.HEADERS
                    else HttpLoggingInterceptor.Level.NONE
        }

        // Origin doğrulama header'ı — backend'de [IgnoreAntiforgeryToken] olan
        // mobile endpoint'leri bu header'ı kontrol ederek CSRF korumasını sağlar.
        val mobileHeader = Interceptor { chain ->
            val req = chain.request().newBuilder()
                .header("X-Requested-With", "CalibraHubAndroid")
                .header("Accept", "application/json")
                .build()
            chain.proceed(req)
        }

        // Global 401 yakalama (2026-07-17) — /api/mobile/ altındaki HERHANGİ bir authed çağrı
        // (WhatsApp/Warehouse/Production repository'lerinin tümü [buildApi]/[buildRetrofit]
        // üzerinden BURAYA akar) 401 dönerse: kalıcı oturum çerezi + oturum-bazlı alanlar
        // temizlenir ve [sessionExpiredEvents]'e bir event basılır (AppNav dinleyip login'e
        // yönlendirir). OkHttp interceptor'ları arka plan thread'inde çalışır (UI thread'i
        // bloklanmaz) ama suspend context DEĞİLDİR — [clearSession] (suspend) burada runBlocking
        // ile SARILMAZ, çünkü o zaten [PersistentCookieJar.clear]'ın KENDİ runBlocking'ini iç içe
        // (nested) çağırmış olurdu. Bunun yerine SIRALI (nested değil) iki adım: önce senkron
        // [PersistentCookieJar.clear] çağrılır, SONRA ayrı/düz bir runBlocking ile sessionStore
        // alanları temizlenir — [clearSession] ile AYNI netice, iç içe runBlocking riski olmadan.
        // /api/mobile/login, /api/mobile/login-companies, /api/mobile/ping gibi [AllowAnonymous]
        // endpoint'ler kimlik hatasını HTTP 401 ile DEĞİL (200 + ok:false gövdesiyle) döndürür —
        // bu interceptor onlarla ÇAKIŞMAZ (bkz. WhatsAppRepository.login/loginCompanies).
        val authExpiryInterceptor = Interceptor { chain ->
            val response = chain.proceed(chain.request())
            if (response.code == 401) {
                cookieJar.clear()
                runBlocking {
                    context.sessionStore.edit {
                        it.remove(DISPLAY_NAME_KEY)
                        it.remove(COMPANY_ID_KEY)
                        it.remove(COMPANY_NAME_KEY)
                    }
                }
                _sessionExpiredEvents.tryEmit(Unit)
            }
            response
        }

        val client = OkHttpClient.Builder()
            .cookieJar(cookieJar)
            .addInterceptor(mobileHeader)
            .addInterceptor(authExpiryInterceptor)
            .addInterceptor(logger)
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(120, TimeUnit.SECONDS)   // SendMedia 60 MB için bol
            .build()

        return Retrofit.Builder()
            .baseUrl(baseUrl)
            .client(client)
            .addConverterFactory(MoshiConverterFactory.create(moshi))
            .build()
    }

    companion object {
        private val BASE_URL_KEY         = stringPreferencesKey("base_url")
        private val DISPLAY_NAME_KEY     = stringPreferencesKey("display_name")
        private val REMEMBER_ME_KEY      = booleanPreferencesKey("remember_me")
        private val REMEMBERED_EMAIL_KEY = stringPreferencesKey("remembered_email")
        private val COMPANY_ID_KEY       = intPreferencesKey("company_id")
        private val COMPANY_NAME_KEY     = stringPreferencesKey("company_name")
        private val THEME_MODE_KEY       = stringPreferencesKey("theme_mode")
    }
}

private val Context.sessionStore by preferencesDataStore(name = "calibra_session")

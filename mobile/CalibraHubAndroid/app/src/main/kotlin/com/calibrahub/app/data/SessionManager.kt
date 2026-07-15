package com.calibrahub.app.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.calibrahub.app.BuildConfig
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import okhttp3.Interceptor
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.moshi.MoshiConverterFactory
import java.util.concurrent.TimeUnit

/**
 * Singleton-ish session yöneticisi.
 *
 * Görevleri:
 *  - Base URL persistence (kullanıcı LAN IP gireceği zaman değiştirebilir)
 *  - Cached display name (login sonrası gösterim)
 *  - Retrofit + OkHttp factory (cookie jar + interceptor'larla)
 *
 * Application-scoped: CalibraApp.kt içinden init edilir.
 */
class SessionManager(private val context: Context) {

    val cookieJar = PersistentCookieJar(context)

    private val baseUrlFlow: Flow<String> = context.sessionStore.data
        .map { it[BASE_URL_KEY] ?: BuildConfig.DEFAULT_BASE_URL }

    private val displayNameFlow: Flow<String?> = context.sessionStore.data
        .map { it[DISPLAY_NAME_KEY] }

    suspend fun currentBaseUrl(): String = baseUrlFlow.first()
    suspend fun currentDisplayName(): String? = displayNameFlow.first()

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

    suspend fun clearSession() {
        cookieJar.clear()
        context.sessionStore.edit { it.remove(DISPLAY_NAME_KEY) }
    }

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

        val client = OkHttpClient.Builder()
            .cookieJar(cookieJar)
            .addInterceptor(mobileHeader)
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
        private val BASE_URL_KEY     = stringPreferencesKey("base_url")
        private val DISPLAY_NAME_KEY = stringPreferencesKey("display_name")
    }
}

private val Context.sessionStore by preferencesDataStore(name = "calibra_session")

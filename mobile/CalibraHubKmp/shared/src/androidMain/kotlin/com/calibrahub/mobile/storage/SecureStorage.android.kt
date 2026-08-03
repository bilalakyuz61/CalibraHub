package com.calibrahub.mobile.storage

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

/**
 * Android DataStore Preferences tabanli [SecureStorage] — CalibraHubAndroid'in
 * `SessionManager.sessionStore` / `PersistentCookieJar.cookieStore` DataStore kullanimi ile
 * AYNI mekanizma, tek dosyada birlestirildi ("calibra_kmp_store").
 */
private val Context.calibraKmpDataStore by preferencesDataStore(name = "calibra_kmp_store")

internal class AndroidSecureStorage(private val context: Context) : SecureStorage {

    override suspend fun getString(key: String): String? =
        context.calibraKmpDataStore.data.map { it[stringPreferencesKey(key)] }.first()

    override suspend fun putString(key: String, value: String) {
        context.calibraKmpDataStore.edit { it[stringPreferencesKey(key)] = value }
    }

    override suspend fun getStringSet(key: String): Set<String>? =
        context.calibraKmpDataStore.data.map { it[stringSetPreferencesKey(key)] }.first()

    override suspend fun putStringSet(key: String, value: Set<String>) {
        context.calibraKmpDataStore.edit { it[stringSetPreferencesKey(key)] = value }
    }

    override suspend fun remove(key: String) {
        context.calibraKmpDataStore.edit {
            it.remove(stringPreferencesKey(key))
            it.remove(stringSetPreferencesKey(key))
        }
    }
}

/**
 * androidApp'in (Application.onCreate) [init] ile doldurdugu global Context tutucu —
 * [SecureStorageFactory] Context parametresi ALAMAZ (expect object, commonMain sozlesmesi
 * parametre gerektirmiyor), bu yuzden gorev talimatindaki "gerekirse init(context) deseni"
 * burada uygulanir. [init] app surecinde YALNIZ BIR KEZ (Application.onCreate) cagrilmalidir.
 */
object AndroidContextHolder {
    private var appContext: Context? = null

    fun init(context: Context) {
        appContext = context.applicationContext
    }

    internal fun require(): Context =
        appContext ?: error(
            "AndroidContextHolder.init(context) hic cagrilmadi — CalibraApplication.onCreate " +
                "icinde cagrilmasi zorunlu (bkz. androidApp CalibraApplication.kt).",
        )
}

actual object SecureStorageFactory {
    actual fun create(): SecureStorage = AndroidSecureStorage(AndroidContextHolder.require())
}

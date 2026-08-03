package com.calibrahub.mobile.storage

/**
 * Platform-bagimsiz kalici anahtar-deger deposu. Android'deki DataStore Preferences +
 * PersistentCookieJar'in DataStore kullanimi ile AYNI rolu tek ortak arayuzde toplar
 * (bkz. CalibraHubAndroid SessionManager.kt / PersistentCookieJar.kt).
 *
 * androidMain -> DataStore Preferences (bkz. AndroidSecureStorage).
 * iosMain     -> NSUserDefaults (bkz. IosSecureStorage) — TODO: prod'da Keychain kullanilmali,
 * NSUserDefaults duz metin saklar (Faz 1 kasitli sadelestirme, KMP_GOC_PLANI ile tutarli).
 */
interface SecureStorage {
    suspend fun getString(key: String): String?
    suspend fun putString(key: String, value: String)
    suspend fun getStringSet(key: String): Set<String>?
    suspend fun putStringSet(key: String, value: Set<String>)
    suspend fun remove(key: String)
}

/** Platforma ozgu [SecureStorage] uretimi — androidMain Context gerektirir (bkz. AndroidSecureStorage
 * + AndroidContextHolder.init), iosMain parametresiz calisir (NSUserDefaults.standardUserDefaults). */
expect object SecureStorageFactory {
    fun create(): SecureStorage
}

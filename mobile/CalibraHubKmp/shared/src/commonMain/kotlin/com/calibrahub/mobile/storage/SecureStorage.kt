package com.calibrahub.mobile.storage

/**
 * Platform-bagimsiz kalici anahtar-deger deposu. Android'deki DataStore Preferences +
 * PersistentCookieJar'in DataStore kullanimi ile AYNI rolu tek ortak arayuzde toplar
 * (bkz. CalibraHubAndroid SessionManager.kt / PersistentCookieJar.kt).
 *
 * androidMain -> DataStore Preferences (bkz. AndroidSecureStorage).
 * iosMain     -> Keychain, kSecClassGenericPassword (bkz. IosSecureStorage). Faz 1'de kasitli
 * olarak NSUserDefaults kullaniliyordu (duz metin plist); oturum cookie'si hassas oldugu icin
 * Keychain'e tasindi — eski defaults degerleri ilk okumada tembel gecisle aktarilir.
 *
 * NOT (Android tarafi hala acik): DataStore Preferences de SIFRELENMEMIS saklar. iOS ile simetri
 * icin EncryptedSharedPreferences / DataStore + Tink'e tasinmasi ayri bir is olarak duruyor.
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

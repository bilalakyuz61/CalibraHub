package com.calibrahub.mobile.storage

import platform.Foundation.NSUserDefaults

/**
 * iOS `NSUserDefaults` tabanli [SecureStorage] — Faz 1 kasitli sadelestirme (gorev talimati).
 *
 * TODO: prod'da Keychain kullanilmali. NSUserDefaults duz metin (plist) olarak diske yazar;
 * oturum cookie'si gibi hassas degerler icin Keychain (kSecClassGenericPassword) daha guvenlidir.
 * Bu POC/Faz-1 asamasinda Android tarafi da benzer sekilde DataStore Preferences (sifrelenmemis)
 * kullandigi icin simetrik bir basit-baslangic kabul edildi — Faz 2/3'te ikisi de sifreli
 * depoya (EncryptedSharedPreferences / Keychain) tasinabilir.
 */
internal class IosSecureStorage : SecureStorage {

    private val defaults get() = NSUserDefaults.standardUserDefaults

    override suspend fun getString(key: String): String? = defaults.stringForKey(key)

    override suspend fun putString(key: String, value: String) {
        defaults.setObject(value, forKey = key)
    }

    override suspend fun getStringSet(key: String): Set<String>? {
        @Suppress("UNCHECKED_CAST")
        val stored = defaults.arrayForKey(key) as? List<String> ?: return null
        return stored.toSet()
    }

    override suspend fun putStringSet(key: String, value: Set<String>) {
        defaults.setObject(value.toList(), forKey = key)
    }

    override suspend fun remove(key: String) {
        defaults.removeObjectForKey(key)
    }
}

actual object SecureStorageFactory {
    actual fun create(): SecureStorage = IosSecureStorage()
}

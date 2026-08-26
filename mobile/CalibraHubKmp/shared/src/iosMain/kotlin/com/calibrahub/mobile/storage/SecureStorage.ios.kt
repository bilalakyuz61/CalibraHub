package com.calibrahub.mobile.storage

import kotlinx.cinterop.ExperimentalForeignApi
import kotlinx.cinterop.alloc
import kotlinx.cinterop.memScoped
import kotlinx.cinterop.ptr
import kotlinx.cinterop.value
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.builtins.serializer
import kotlinx.serialization.json.Json
import platform.CoreFoundation.CFDictionaryAddValue
import platform.CoreFoundation.CFDictionaryCreateMutable
import platform.CoreFoundation.CFDictionaryRef
import platform.CoreFoundation.CFMutableDictionaryRef
import platform.CoreFoundation.CFRelease
import platform.CoreFoundation.CFStringRef
import platform.CoreFoundation.CFTypeRefVar
import platform.CoreFoundation.kCFAllocatorDefault
import platform.CoreFoundation.kCFBooleanTrue
import platform.CoreFoundation.kCFTypeDictionaryKeyCallBacks
import platform.CoreFoundation.kCFTypeDictionaryValueCallBacks
import platform.Foundation.CFBridgingRelease
import platform.Foundation.CFBridgingRetain
import platform.Foundation.NSData
import platform.Foundation.NSString
import platform.Foundation.NSUTF8StringEncoding
import platform.Foundation.NSUserDefaults
import platform.Foundation.create
import platform.Foundation.dataUsingEncoding
import platform.Security.SecItemAdd
import platform.Security.SecItemCopyMatching
import platform.Security.SecItemDelete
import platform.Security.kSecAttrAccessible
import platform.Security.kSecAttrAccessibleAfterFirstUnlock
import platform.Security.kSecAttrAccount
import platform.Security.kSecAttrService
import platform.Security.kSecClass
import platform.Security.kSecClassGenericPassword
import platform.Security.kSecMatchLimit
import platform.Security.kSecMatchLimitOne
import platform.Security.kSecReturnData
import platform.Security.kSecValueData

/**
 * iOS Keychain (`kSecClassGenericPassword`) tabanli [SecureStorage].
 *
 * Onceki surum NSUserDefaults kullaniyordu (Faz 1 kasitli sadelestirmesi) — oturum cookie'si
 * duz metin plist olarak diske yaziliyordu. Keychain hem donanim destekli sifreleme hem de
 * `kSecAttrAccessibleAfterFirstUnlock` ile "cihaz ilk kez acildiktan sonra erisilebilir"
 * garantisi verir.
 *
 * Iki tuzak ele alindi:
 *  1. **Keychain uygulama silinince TEMIZLENMEZ.** Uygulama kaldirilip yeniden kurulunca eski
 *     oturum cookie'si geri gelir ve kullanici hic giris yapmadigi halde acilmis oturum bulur.
 *     NSUserDefaults ise silinir — bu yuzden orada bir "kuruldu" bayragi tutulur; bayrak yoksa
 *     (= temiz kurulum) SERVICE altindaki tum Keychain kayitlari silinir.
 *  2. **Eskiden NSUserDefaults'ta duran degerler.** Guncelleyen kullanicilar oturumunu
 *     kaybetmesin diye okuma sirasinda tembel gecis yapilir: Keychain'de yoksa defaults'a
 *     bakilir, bulunursa Keychain'e tasinip defaults'tan SILINIR (duz metin kalinti kalmaz).
 */
@OptIn(ExperimentalForeignApi::class)
internal class IosSecureStorage : SecureStorage {

    init {
        Keychain.wipeIfFreshInstall()
    }

    override suspend fun getString(key: String): String? =
        Keychain.get(key) ?: migrateLegacyString(key)

    override suspend fun putString(key: String, value: String) {
        Keychain.put(key, value)
    }

    override suspend fun getStringSet(key: String): Set<String>? {
        val raw = getString(key) ?: return null
        return runCatching { json.decodeFromString(stringListSerializer, raw).toSet() }.getOrNull()
    }

    override suspend fun putStringSet(key: String, value: Set<String>) {
        Keychain.put(key, json.encodeToString(stringListSerializer, value.toList()))
    }

    override suspend fun remove(key: String) {
        Keychain.delete(key)
        NSUserDefaults.standardUserDefaults.removeObjectForKey(key)
    }

    /** NSUserDefaults'taki eski degeri Keychain'e tasir ve duz metin kopyayi siler. */
    private fun migrateLegacyString(key: String): String? {
        val defaults = NSUserDefaults.standardUserDefaults
        val legacy = defaults.stringForKey(key)
            ?: (defaults.arrayForKey(key) as? List<*>)
                ?.filterIsInstance<String>()
                ?.let { json.encodeToString(stringListSerializer, it) }
            ?: return null
        Keychain.put(key, legacy)
        defaults.removeObjectForKey(key)
        return legacy
    }

    private companion object {
        val json = Json { ignoreUnknownKeys = true }
        val stringListSerializer = ListSerializer(String.serializer())
    }
}

/**
 * Ince Keychain sarmalayicisi. Tum kayitlar tek bir `service` altinda, anahtar = `account`.
 * Hata durumunda sessizce null doner — depolama hatasi uygulamayi cokertmemeli (oturum kaybi
 * tolere edilebilir, cokme edilemez).
 */
@OptIn(ExperimentalForeignApi::class)
private object Keychain {

    private const val SERVICE = "com.calibrahub.mobile"
    private const val FRESH_INSTALL_FLAG = "calibrahub.keychain.initialized"

    /** OSStatus basarili degeri (errSecSuccess). */
    private const val STATUS_OK = 0

    fun wipeIfFreshInstall() {
        val defaults = NSUserDefaults.standardUserDefaults
        if (defaults.boolForKey(FRESH_INSTALL_FLAG)) return
        // Uygulama silinip yeniden kuruldu (ya da ilk kurulum): Keychain'de kalmis olabilecek
        // eski oturum kalintilarini temizle.
        val query = newQuery(account = null)
        if (query != null) {
            SecItemDelete(query)
            CFRelease(query)
        }
        defaults.setBool(true, FRESH_INSTALL_FLAG)
    }

    fun get(key: String): String? = memScoped {
        val query = newQuery(key) { dict ->
            CFDictionaryAddValue(dict, kSecReturnData, kCFBooleanTrue)
            CFDictionaryAddValue(dict, kSecMatchLimit, kSecMatchLimitOne)
        } ?: return@memScoped null

        val out = alloc<CFTypeRefVar>()
        val status = SecItemCopyMatching(query, out.ptr)
        CFRelease(query)
        if (status != STATUS_OK) return@memScoped null

        val data = CFBridgingRelease(out.value) as? NSData ?: return@memScoped null
        NSString.create(data = data, encoding = NSUTF8StringEncoding) as String?
    }

    fun put(key: String, value: String) {
        // Once sil sonra ekle: SecItemUpdate'in ayri sorgu/oznitelik ciftini yonetmekten daha
        // az kirilgan ve ayni sonucu verir.
        delete(key)
        val data = (value as NSString).dataUsingEncoding(NSUTF8StringEncoding) ?: return
        val cfData = CFBridgingRetain(data) ?: return
        val query = newQuery(key) { dict ->
            CFDictionaryAddValue(dict, kSecValueData, cfData)
            CFDictionaryAddValue(dict, kSecAttrAccessible, kSecAttrAccessibleAfterFirstUnlock)
        }
        if (query != null) {
            SecItemAdd(query, null)
            CFRelease(query)
        }
        CFRelease(cfData)
    }

    fun delete(key: String) {
        val query = newQuery(key) ?: return
        SecItemDelete(query)
        CFRelease(query)
    }

    /**
     * Ortak sorgu iskeleti: class + service (+ varsa account). [account] null verilirse sorgu
     * SERVICE altindaki TUM kayitlari kapsar (yalniz temiz-kurulum silmesinde kullanilir).
     */
    private inline fun newQuery(
        account: String?,
        extra: (CFMutableDictionaryRef?) -> Unit = {},
    ): CFDictionaryRef? {
        val dict = CFDictionaryCreateMutable(
            kCFAllocatorDefault,
            0,
            kCFTypeDictionaryKeyCallBacks.ptr,
            kCFTypeDictionaryValueCallBacks.ptr,
        ) ?: return null

        CFDictionaryAddValue(dict, kSecClass, kSecClassGenericPassword)
        addString(dict, kSecAttrService, SERVICE)
        if (account != null) addString(dict, kSecAttrAccount, account)
        extra(dict)
        return dict
    }

    /** CFString'i sozluge ekler ve YEREL referansi birakir (sozluk kendi retain'ini alir). */
    private fun addString(dict: CFMutableDictionaryRef?, key: CFStringRef?, value: String) {
        val cf = CFBridgingRetain(value as NSString) ?: return
        CFDictionaryAddValue(dict, key, cf)
        CFRelease(cf)
    }
}

actual object SecureStorageFactory {
    actual fun create(): SecureStorage = IosSecureStorage()
}

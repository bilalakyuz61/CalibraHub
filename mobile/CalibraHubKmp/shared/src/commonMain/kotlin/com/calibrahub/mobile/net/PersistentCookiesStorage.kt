package com.calibrahub.mobile.net

import com.calibrahub.mobile.storage.SecureStorage
import io.ktor.client.plugins.cookies.CookiesStorage
import io.ktor.http.Cookie
import io.ktor.http.Url
import io.ktor.util.date.getTimeMillis
import kotlin.concurrent.Volatile
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * Ktor [CookiesStorage] implementasyonu — CalibraHubAndroid'in `PersistentCookieJar.kt`
 * (OkHttp CookieJar) KMP karsiligi. Ayni davranis: bellek-ici cache HER ZAMAN guncellenir,
 * [SecureStorage]'a YAZMA yalniz [persistenceEnabled] ACIKKEN olur ("beni hatirla" flag'i —
 * bkz. SessionManager.setRememberMe). Kapaliyken mevcut app run'i icin oturum calismaya devam
 * eder ama surec oldugunde cookie kaybolur (Android'deki AYNI tasarim kararı).
 *
 * BILINCLI SADELESTIRME (Android'den sapma): eslesme host-bazli yapilir (istek URL'sinin host'u
 * ile cookie'nin alindigi host'un ESITLIGI) — OkHttp'nin domain-suffix/path-prefix/secure
 * eslestirmesi PORT EDILMEDI. Gerekce: bu uygulama TEK bir CalibraHub sunucusuyla konusur (kullanici
 * ayarlarindan degistirilen tek base URL); path/secure ayrimi pratikte fark yaratmiyor (KISS).
 * Cookie'nin kendi `expires`/`maxAge` alanlarina da GUVENILMEZ (Ktor'un GMTDate'ini yeniden
 * kurmak Long.MAX_VALUE gibi sinir degerlerde risk tasir) — bunun yerine [StoredCookie.expiresAtMillis]
 * ayri bir yan-alanda tutulur, sunucuya YALNIZ name=value cifti gonderilir (bkz. HttpCookiesKt
 * render — domain/path/secure zaten disariya sizmiyor).
 */
class PersistentCookiesStorage(private val storage: SecureStorage) : CookiesStorage {

    private data class StoredCookie(val cookie: Cookie, val expiresAtMillis: Long)

    private val mutex = Mutex()
    private val cache = mutableMapOf<String, MutableList<StoredCookie>>()
    private var loaded = false

    @Volatile
    private var persistenceEnabled: Boolean = false

    /** [com.calibrahub.mobile.session.SessionManager] tarafindan cagrilir — SecureStorage'a yazimi ac/kapat. */
    fun setPersistenceEnabled(enabled: Boolean) {
        persistenceEnabled = enabled
    }

    /** [cache] DataStore/NSUserDefaults'tan yuklenmis en az bir cookie iceriyor mu — acilis
     * probe'unun ("otomatik giris denensin mi") karari icin kullanilir. */
    suspend fun hasStoredCookies(): Boolean {
        ensureLoaded()
        return mutex.withLock { cache.values.any { it.isNotEmpty() } }
    }

    override suspend fun get(requestUrl: Url): List<Cookie> {
        ensureLoaded()
        val now = getTimeMillis()
        return mutex.withLock {
            val list = cache[requestUrl.host] ?: return@withLock emptyList()
            list.removeAll { it.expiresAtMillis < now }
            list.map { it.cookie }
        }
    }

    override suspend fun addCookie(requestUrl: Url, cookie: Cookie) {
        ensureLoaded()
        val now = getTimeMillis()
        mutex.withLock {
            val list = cache.getOrPut(requestUrl.host) { mutableListOf() }
            val expiresAt = computeExpiresAt(cookie, now)
            val idx = list.indexOfFirst { it.cookie.name == cookie.name }
            val entry = StoredCookie(cookie, expiresAt)
            if (idx >= 0) list[idx] = entry else list.add(entry)
            list.removeAll { it.expiresAtMillis < now }
        }
        if (persistenceEnabled) persist()
    }

    /** Logout VEYA 401-tetiklemeli oturum sonlandirma — SessionManager.clearSession bunu cagirir. */
    suspend fun clear() {
        mutex.withLock { cache.clear() }
        storage.remove(COOKIE_KEY)
    }

    override fun close() {
        // Kapatilacak bir kaynak (soket/dosya handle) yok — SecureStorage kendi yasam
        // dongusunu yonetir (DataStore/NSUserDefaults singleton).
    }

    private suspend fun ensureLoaded() {
        if (loaded) return
        mutex.withLock {
            if (loaded) return@withLock
            val stored = storage.getStringSet(COOKIE_KEY) ?: emptySet()
            stored.mapNotNull { deserialize(it) }.forEach { (host, entry) ->
                cache.getOrPut(host) { mutableListOf() }.add(entry)
            }
            loaded = true
        }
    }

    private suspend fun persist() {
        val serialized = mutex.withLock {
            cache.entries.flatMap { (host, list) -> list.map { serialize(host, it) } }.toSet()
        }
        storage.putStringSet(COOKIE_KEY, serialized)
    }

    /** Sunucunun Set-Cookie'sinde `Expires`/`Max-Age` verilmemisse (oturum cookie'si) — OkHttp'nin
     * session-cookie davranisiyla AYNI: "beni hatirla" acikken sinirsiz kalir (Long.MAX_VALUE),
     * yalnizca logout/401/clear ile sonlanir. */
    private fun computeExpiresAt(cookie: Cookie, nowMillis: Long): Long {
        cookie.expires?.let { return it.timestamp }
        val maxAge = cookie.maxAge
        if (maxAge != null && maxAge > 0) return nowMillis + maxAge.toLong() * 1000L
        return Long.MAX_VALUE
    }

    private fun serialize(host: String, entry: StoredCookie): String =
        listOf(host, entry.cookie.name, entry.cookie.value, entry.expiresAtMillis.toString())
            .joinToString("|") { it.replace("|", "%7C") }

    private fun deserialize(raw: String): Pair<String, StoredCookie>? {
        val parts = raw.split("|").map { it.replace("%7C", "|") }
        if (parts.size < 4) return null
        return try {
            val host = parts[0]
            val name = parts[1]
            val value = parts[2]
            val expiresAt = parts[3].toLong()
            host to StoredCookie(cookie = Cookie(name = name, value = value), expiresAtMillis = expiresAt)
        } catch (e: Exception) {
            null
        }
    }

    companion object {
        private const val COOKIE_KEY = "calibra_cookies_v1"
    }
}

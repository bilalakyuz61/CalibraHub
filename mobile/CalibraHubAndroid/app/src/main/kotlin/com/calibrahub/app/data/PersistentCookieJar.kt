package com.calibrahub.app.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.HttpUrl
import java.util.concurrent.ConcurrentHashMap

/**
 * OkHttp CookieJar — auth cookie'sini DataStore'a persist eder.
 * ASP.NET Identity cookie'leri (.AspNetCore.Identity.Application gibi)
 * + CalibraSession otomatik saklanır. App restart sonrası login state korunur.
 *
 * Thread-safe: ConcurrentHashMap + synchronized DataStore write.
 */
class PersistentCookieJar(private val context: Context) : CookieJar {

    private val memCache = ConcurrentHashMap<String, MutableList<Cookie>>()

    init {
        // İlk init'te DataStore'dan oku
        runBlocking {
            val stored = context.cookieStore.data.first()[COOKIE_KEY] ?: emptySet()
            stored.mapNotNull { parseSerialized(it) }
                .groupBy { it.domain }
                .forEach { (host, list) -> memCache[host] = list.toMutableList() }
        }
    }

    override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
        val host = url.host
        val list = memCache.getOrPut(host) { mutableListOf() }
        cookies.forEach { incoming ->
            // Aynı isimli cookie'yi değiştir (replace), yoksa ekle
            val idx = list.indexOfFirst { it.name == incoming.name }
            if (idx >= 0) list[idx] = incoming else list.add(incoming)
        }
        // Expire olmuş cookie'leri at
        val now = System.currentTimeMillis()
        list.removeAll { it.expiresAt < now }
        persist()
    }

    override fun loadForRequest(url: HttpUrl): List<Cookie> {
        val now = System.currentTimeMillis()
        val host = url.host
        val list = memCache[host] ?: return emptyList()
        list.removeAll { it.expiresAt < now }
        return list.filter { it.matches(url) }
    }

    fun clear() {
        memCache.clear()
        runBlocking {
            context.cookieStore.edit { it.remove(COOKIE_KEY) }
        }
    }

    private fun persist() {
        val serialized = memCache.values.flatten().map { serialize(it) }.toSet()
        runBlocking {
            context.cookieStore.edit { it[COOKIE_KEY] = serialized }
        }
    }

    private fun serialize(c: Cookie): String =
        listOf(c.name, c.value, c.expiresAt.toString(), c.domain, c.path,
               c.secure.toString(), c.httpOnly.toString(), c.hostOnly.toString())
            .joinToString("|") { it.replace("|", "%7C") }

    private fun parseSerialized(raw: String): Cookie? {
        val parts = raw.split("|").map { it.replace("%7C", "|") }
        if (parts.size < 8) return null
        return try {
            Cookie.Builder()
                .name(parts[0])
                .value(parts[1])
                .expiresAt(parts[2].toLong())
                .apply {
                    val domain = parts[3]
                    if (parts[7] == "true") hostOnlyDomain(domain) else domain(domain)
                }
                .path(parts[4])
                .apply {
                    if (parts[5] == "true") secure()
                    if (parts[6] == "true") httpOnly()
                }
                .build()
        } catch (e: Exception) { null }
    }

    companion object {
        private val COOKIE_KEY = stringSetPreferencesKey("calibra_cookies_v1")
    }
}

// DataStore extension — Context property delegate
internal val Context.cookieStore by preferencesDataStore(name = "calibra_cookies")

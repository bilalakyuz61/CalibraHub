package com.calibrahub.mobile.net

import io.ktor.client.HttpClient
import io.ktor.client.engine.HttpClientEngineFactory
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.cookies.CookiesStorage
import io.ktor.client.plugins.cookies.HttpCookies
import io.ktor.client.plugins.defaultRequest
import io.ktor.client.plugins.observer.ResponseObserver
import io.ktor.client.request.header
import io.ktor.http.HttpStatusCode
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.json.Json

/**
 * Tek platform seam: HTTP engine (expect/actual). Android -> OkHttp, iOS -> Darwin.
 */
expect val httpEngineFactory: HttpClientEngineFactory<*>

/**
 * Uygulama genelinde TEK kez olusturulup [com.calibrahub.mobile.session.SessionManager] icinde
 * saklanan HttpClient. [cookiesStorage] kalici (SecureStorage-destekli) cookie jar'dir — bkz.
 * [PersistentCookiesStorage]; [onUnauthorized] ise CalibraHubAndroid SessionManager'daki global
 * 401-yakalama interceptor'inin (authExpiryInterceptor) KMP karsiligidir. Ktor'da bu is
 * OkHttp Interceptor yerine [ResponseObserver] plugin'i ile yapilir — yalniz response.status
 * okunur, govde HIC tuketilmez (double-read riski yok).
 */
fun createHttpClient(
    cookiesStorage: CookiesStorage,
    onUnauthorized: suspend () -> Unit = {},
): HttpClient = HttpClient(httpEngineFactory) {
    install(ContentNegotiation) {
        json(Json { ignoreUnknownKeys = true })
    }
    install(HttpCookies) {
        storage = cookiesStorage
    }
    install(HttpTimeout) {
        requestTimeoutMillis = 20_000
        connectTimeoutMillis = 15_000
    }
    install(ResponseObserver) {
        onResponse { response ->
            if (response.status == HttpStatusCode.Unauthorized) onUnauthorized()
        }
    }
    defaultRequest {
        header("X-Requested-With", "XMLHttpRequest")
    }
    expectSuccess = false
}

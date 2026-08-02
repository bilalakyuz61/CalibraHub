package com.calibrahub.mobile.net

import io.ktor.client.HttpClient
import io.ktor.client.engine.HttpClientEngineFactory
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.cookies.HttpCookies
import io.ktor.client.plugins.defaultRequest
import io.ktor.client.request.header
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.json.Json

/**
 * Tek platform seam: HTTP engine (expect/actual). Android -> OkHttp, iOS -> Darwin.
 * POC kasitli olarak baska bir expect/actual eklemez (bkz. gorev talimati).
 */
expect val httpEngineFactory: HttpClientEngineFactory<*>

fun createHttpClient(): HttpClient = HttpClient(httpEngineFactory) {
    install(ContentNegotiation) {
        json(Json { ignoreUnknownKeys = true })
    }
    install(HttpCookies)
    install(HttpTimeout) {
        requestTimeoutMillis = 20_000
    }
    defaultRequest {
        header("X-Requested-With", "XMLHttpRequest")
    }
    expectSuccess = false
}

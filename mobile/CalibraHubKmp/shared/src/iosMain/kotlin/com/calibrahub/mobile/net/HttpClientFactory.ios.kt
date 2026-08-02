package com.calibrahub.mobile.net

import io.ktor.client.engine.HttpClientEngineFactory
import io.ktor.client.engine.darwin.Darwin

actual val httpEngineFactory: HttpClientEngineFactory<*> = Darwin

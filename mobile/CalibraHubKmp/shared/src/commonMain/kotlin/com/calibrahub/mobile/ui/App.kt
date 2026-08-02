package com.calibrahub.mobile.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import com.calibrahub.mobile.data.CalibraApiClient
import com.calibrahub.mobile.net.createHttpClient

/**
 * POC navigasyonu: kutuphane YOK, basit sealed-state + when. Yeni ekran eklenirse
 * bu enum/sealed genisletilir (bkz. gorev talimati — Navigation kutuphanesi YOK).
 */
private sealed class Screen {
    data class Login(val baseUrl: String = "http://10.0.2.2:61001/") : Screen()
    data class Stock(val baseUrl: String, val client: CalibraApiClient, val displayName: String?) : Screen()
}

@Composable
fun App() {
    MaterialTheme {
        Surface(modifier = Modifier) {
            var screen by remember { mutableStateOf<Screen>(Screen.Login()) }
            // HttpClient uygulama yasam suresince tek instance — login cookie'sini
            // Stock ekranina tasimak icin ayni client + CalibraApiClient reuse edilir.
            val httpClient = remember { createHttpClient() }

            when (val current = screen) {
                is Screen.Login -> LoginScreen(
                    initialBaseUrl = current.baseUrl,
                    httpClient = httpClient,
                    onLoginSuccess = { baseUrl, client, displayName ->
                        screen = Screen.Stock(baseUrl = baseUrl, client = client, displayName = displayName)
                    },
                )
                is Screen.Stock -> StockScreen(
                    apiClient = current.client,
                    displayName = current.displayName,
                    onLogout = {
                        screen = Screen.Login(baseUrl = current.baseUrl)
                    },
                )
            }
        }
    }
}

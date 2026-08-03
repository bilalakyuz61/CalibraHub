package com.calibrahub.mobile.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.storage.SecureStorageFactory

/**
 * POC navigasyonu: kutuphane YOK, basit sealed-state + when. Yeni ekran eklenirse
 * bu enum/sealed genisletilir (bkz. gorev talimati — Navigation kutuphanesi YOK).
 */
private sealed class Screen {
    data object Login : Screen()
    data class Stock(val displayName: String?) : Screen()
}

@Composable
fun App() {
    MaterialTheme {
        Surface(modifier = Modifier) {
            var screen by remember { mutableStateOf<Screen>(Screen.Login) }
            // SessionManager uygulama yasam suresince TEK instance — kalici cookie jar'i +
            // baseUrl/tema/beni-hatirla tercihlerini Login<->Stock arasinda tasir (bkz.
            // com.calibrahub.mobile.session.SessionManager).
            val sessionManager = remember { SessionManager(SecureStorageFactory.create()) }

            when (val current = screen) {
                is Screen.Login -> LoginScreen(
                    sessionManager = sessionManager,
                    onLoginSuccess = { displayName ->
                        screen = Screen.Stock(displayName = displayName)
                    },
                )
                is Screen.Stock -> StockScreen(
                    sessionManager = sessionManager,
                    displayName = current.displayName,
                    onLogout = {
                        screen = Screen.Login
                    },
                )
            }
        }
    }
}

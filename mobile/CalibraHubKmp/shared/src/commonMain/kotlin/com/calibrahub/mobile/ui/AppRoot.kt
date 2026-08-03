package com.calibrahub.mobile.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.session.ThemeMode
import com.calibrahub.mobile.storage.SecureStorageFactory
import com.calibrahub.mobile.ui.nav.AppNavHost
import com.calibrahub.mobile.ui.theme.CalibraTheme

/**
 * Uygulama kökü — CalibraHubAndroid `MainActivity.CalibraRoot` ile BİREBİR aynı karar (sadik
 * port): tema tercihini ([ThemeMode] Sistem/Açık/Koyu) [SessionManager.themeMode] StateFlow'undan
 * `collectAsState` ile CANLI okur ve [CalibraTheme]'e besler. Kullanıcı [com.calibrahub.mobile.
 * ui.settings.SettingsScreen]'de tercihi değiştirdiği anda BU composable yeniden çalışır —
 * Activity/ComposeUIViewController restart YOK, MaterialTheme'in colorScheme'i üzerinden TÜM alt
 * ağaç (AppNavHost → her ekran) anında yeniden renklenir.
 *
 * [androidApp]/[iosApp] host'ları BU composable'ı çağırır (`setContent { AppRoot() }` /
 * `ComposeUIViewController { AppRoot() }`) — [SessionManager] TEK instance burada kurulur/
 * hatırlanır (uygulama yaşam süresince tek kalıcı cookie jar + baseUrl/tema/beni-hatırla
 * tercihlerini Login↔Home↔Settings arasında taşır, bkz. [SessionManager] KDoc'u).
 *
 * @param onDarkThemeResolved Android-özel durum/gezinme çubuğu (status/navigation bar) ikon
 * kontrastı senkronu İÇİN — bu native Android chrome'dur, Compose'un MaterialTheme'inin KAPSAMI
 * DIŞINDADIR (bkz. gorev talimati). [androidApp.MainActivity] bu callback'i dinleyip
 * `WindowCompat.getInsetsController` ile ikon kontrastını senkronlar; iOS/varsayılan çağıran
 * taraf hiçbir şey yapmaz (no-op varsayılan).
 */
@Composable
fun AppRoot(onDarkThemeResolved: (Boolean) -> Unit = {}) {
    val sessionManager = remember { SessionManager(SecureStorageFactory.create()) }
    val themeMode by sessionManager.themeMode.collectAsState()
    val useDarkTheme = when (themeMode) {
        ThemeMode.SYSTEM -> isSystemInDarkTheme()
        ThemeMode.LIGHT -> false
        ThemeMode.DARK -> true
    }

    SideEffect { onDarkThemeResolved(useDarkTheme) }

    CalibraTheme(darkTheme = useDarkTheme) {
        Surface(modifier = Modifier.fillMaxSize()) {
            AppNavHost(session = sessionManager)
        }
    }
}

package com.calibrahub.mobile.android

import android.app.Activity
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat
import com.calibrahub.mobile.ui.AppRoot

/**
 * İNCE host — CalibraHubAndroid `MainActivity.kt` ile AYNI rol: tüm navigasyon/tema/ekran
 * mantığı `shared` modülünde ([com.calibrahub.mobile.ui.AppRoot], commonMain). Bu Activity yalnız
 * [AppRoot]'u `setContent` ile bağlar + Android-özel durum/gezinme çubuğu (status/navigation bar)
 * ikon kontrastını [AppRoot]'un çözdüğü `onDarkThemeResolved` geri çağrısıyla senkronlar — bu
 * native Android chrome'dur, Compose'un MaterialTheme'inin KAPSAMI DIŞINDADIR (bu yüzden
 * commonMain'de DEĞİL burada) — CalibraHubAndroid `MainActivity.CalibraRoot`'taki AYNI
 * `SideEffect` deseni (sadik port).
 *
 * XML temasındaki sabit `windowLightStatusBar` yalnız ilk-çizim varsayılanıdır; kullanıcı
 * içeriği koyulaştırdığında (cihaz sistemi açık kalsa bile) durum çubuğu ikonları da senkron
 * koyulaşmazsa koyu ikonlar koyu TopAppBar üzerinde görünmez kalırdı.
 */
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            var isDark by remember { mutableStateOf(false) }

            val view = LocalView.current
            if (!view.isInEditMode) {
                SideEffect {
                    val window = (view.context as Activity).window
                    val controller = WindowCompat.getInsetsController(window, view)
                    controller.isAppearanceLightStatusBars = !isDark
                    controller.isAppearanceLightNavigationBars = !isDark
                }
            }

            AppRoot(onDarkThemeResolved = { isDark = it })
        }
    }
}

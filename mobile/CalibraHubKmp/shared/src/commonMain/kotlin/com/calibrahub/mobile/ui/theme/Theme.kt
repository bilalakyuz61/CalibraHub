package com.calibrahub.mobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/**
 * CalibraHubAndroid `ui/theme/Theme.kt` ile BIREBIR ayni renk paleti (sadik port) — WhatsApp
 * yesili birincil vurgu, ayni light/dark hex degerleri.
 *
 * BILINCLI SADELESTIRME (Faz 2a — commonMain kisitlamasi): Android'deki Material You dinamik
 * renk (`dynamicLightColorScheme`/`dynamicDarkColorScheme`, Android 12+ duvar kagidi tabanli)
 * BURADA YOK — bu API'ler `android.content.Context` + Android 12 SDK'ya bagimlidir, commonMain'de
 * (ve iOS'ta) karsiligi yoktur. Faz 2a kapsaminda YALNIZ SABIT Light/Dark semalari kullanilir; bu
 * gercek bir ozellik kaybi degil, kozmetik bir kisitlamadir (marka rengi zaten sabit hex'lerle
 * ayni kalir). Android'e ozel dinamik renk istenirse ileride androidApp katmaninda ayrica
 * eklenebilir (bu Faz'in KAPSAMI DISINDA, koordinatore raporlanir).
 */
private val LightScheme = lightColorScheme(
    primary = Color(0xFF25D366), // WhatsApp green
    onPrimary = Color.White,
    primaryContainer = Color(0xFFDCF8C6),
    onPrimaryContainer = Color(0xFF052E16),
    secondary = Color(0xFF6366F1),
    onSecondary = Color.White,
    background = Color(0xFFF7F8FA),
    surface = Color.White,
    onSurface = Color(0xFF111827),
    onSurfaceVariant = Color(0xFF6B7280),
    surfaceVariant = Color(0xFFF3F4F6),
    outline = Color(0xFFE5E7EB),
)

private val DarkScheme = darkColorScheme(
    primary = Color(0xFF25D366),
    onPrimary = Color(0xFF052E16),
    primaryContainer = Color(0xFF1F4D2D),
    onPrimaryContainer = Color(0xFFD1FAE5),
    secondary = Color(0xFFA5B4FC),
    onSecondary = Color(0xFF1E1B4B),
    background = Color(0xFF080C17),
    surface = Color(0xFF0D1323),
    onSurface = Color(0xFFE2E8F0),
    onSurfaceVariant = Color(0xFF94A3B8),
    surfaceVariant = Color(0xFF1E293B),
    outline = Color(0x14FFFFFF),
)

@Composable
fun CalibraTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val scheme = if (darkTheme) DarkScheme else LightScheme
    MaterialTheme(colorScheme = scheme, content = content)
}

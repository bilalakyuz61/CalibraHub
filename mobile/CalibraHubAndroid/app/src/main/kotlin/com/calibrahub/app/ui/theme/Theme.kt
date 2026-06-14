package com.calibrahub.app.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext

private val LightScheme = lightColorScheme(
    primary           = Color(0xFF25D366),   // WhatsApp green
    onPrimary         = Color.White,
    primaryContainer  = Color(0xFFDCF8C6),
    onPrimaryContainer= Color(0xFF052E16),
    secondary         = Color(0xFF6366F1),
    onSecondary       = Color.White,
    background        = Color(0xFFF7F8FA),
    surface           = Color.White,
    onSurface         = Color(0xFF111827),
    onSurfaceVariant  = Color(0xFF6B7280),
    surfaceVariant    = Color(0xFFF3F4F6),
    outline           = Color(0xFFE5E7EB)
)

private val DarkScheme = darkColorScheme(
    primary           = Color(0xFF25D366),
    onPrimary         = Color(0xFF052E16),
    primaryContainer  = Color(0xFF1F4D2D),
    onPrimaryContainer= Color(0xFFD1FAE5),
    secondary         = Color(0xFFA5B4FC),
    onSecondary       = Color(0xFF1E1B4B),
    background        = Color(0xFF080C17),
    surface           = Color(0xFF0D1323),
    onSurface         = Color(0xFFE2E8F0),
    onSurfaceVariant  = Color(0xFF94A3B8),
    surfaceVariant    = Color(0xFF1E293B),
    outline           = Color(0x14FFFFFF)
)

@Composable
fun CalibraTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    dynamicColor: Boolean = true,   // Android 12+ wallpaper-based colors
    content: @Composable () -> Unit
) {
    val context = LocalContext.current
    val scheme = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ->
            if (darkTheme) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        darkTheme -> DarkScheme
        else      -> LightScheme
    }
    MaterialTheme(colorScheme = scheme, content = content)
}

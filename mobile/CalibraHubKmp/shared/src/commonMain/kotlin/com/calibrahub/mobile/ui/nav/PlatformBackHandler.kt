package com.calibrahub.mobile.ui.nav

import androidx.compose.runtime.Composable

/**
 * Platform geri-tuşu yakalayıcı. Android'de sistem geri tuşunu yakalar (androidx.activity
 * BackHandler); iOS'ta hiçbir şeyi yakalamaz (iOS'ta donanım geri tuşu yoktur; kök ekranda
 * geri-jesti zaten no-op'tur — çift-geri ile çıkış Android'e özgü bir desendir).
 *
 * Neden expect/actual: Compose Multiplatform 1.7.x'te ORTAK bir BackHandler yoktur
 * (`androidx.compose.ui.backhandler.BackHandler` CMP 1.8+ ile gelir). 1.7.3'te kaldığımız
 * sürece bu ince expect/actual köprüsü kullanılır.
 */
@Composable
expect fun PlatformBackHandler(enabled: Boolean, onBack: () -> Unit)

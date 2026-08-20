package com.calibrahub.mobile.ui.nav

import androidx.compose.runtime.Composable

// iOS'ta donanim geri tusu yoktur; cift-geri ile cikis Android'e ozgu bir desendir. No-op.
@Composable
actual fun PlatformBackHandler(enabled: Boolean, onBack: () -> Unit) {
}

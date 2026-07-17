package com.calibrahub.app.ui.home

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * Login sonrası ilk ekran — kısa karşılama.
 *
 * 2026-07-17 drawer migration: modül seçimi (eskiden burada [HomeModuleCard] kart ızgarası —
 * Sohbetler/Depo/Üretim) artık sol menüde ([com.calibrahub.app.ui.nav.AppDrawerContent], 7 kök
 * modül). Bu ekran sadeleşti: yalnız hoş geldin metni + TopAppBar hamburger (drawer'ı açar).
 * Küçük karar: "hızlı erişim" kartları BİLEREK eklenmedi — modül listesi artık tek yerde
 * (drawer'da) yaşıyor, burada ikinci bir kopyası bakım yükü + iki yer arasında tutarsızlık riski
 * doğururdu. Gerekirse ileride buraya widget/özet eklenebilir.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    displayName: String?,
    onOpenDrawer: () -> Unit
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("CalibraHub") },
                navigationIcon = {
                    IconButton(onClick = onOpenDrawer) {
                        Icon(Icons.Default.Menu, contentDescription = "Menü")
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            Text(
                text = if (!displayName.isNullOrBlank()) "Hoş geldin, $displayName" else "Hoş geldiniz",
                style = MaterialTheme.typography.headlineSmall
            )
            Text(
                text = "Sol menüden bir modül seçerek başlayın.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

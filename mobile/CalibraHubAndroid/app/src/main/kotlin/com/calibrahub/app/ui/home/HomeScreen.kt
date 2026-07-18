package com.calibrahub.app.ui.home

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.ui.nav.DrawerLeaf
import com.calibrahub.app.ui.nav.pinnableDrawerLeaves

/**
 * Login sonrası ilk ekran — kısa karşılama + kullanıcının sabitlediği modül kısayollarının
 * ızgarası.
 *
 * 2026-07-17 drawer migration: modül seçimi (eskiden burada [HomeModuleCard] kart ızgarası —
 * Sohbetler/Depo/Üretim) sol menüye taşındı ([com.calibrahub.app.ui.nav.AppDrawerContent], 7 kök
 * modül).
 *
 * 2026-07-19 Ayarlar tekilleştirme: sağ üst köşedeki dişli ikonu ([onOpenSettings]) TEK giriş
 * noktasıdır (kullanıcı kararı: "iki yerde ayarlar simgesi olmasın, sağ üstteki kalabilir") —
 * drawer'daki eski "Ayarlar" girişi kaldırıldı.
 *
 * 2026-07-19 kısayol ızgarası: sol menüde bir yaprağın yanındaki raptiye (pin) ikonuyla
 * sabitlenen modüller ([com.calibrahub.app.data.SessionManager.pinnedRoutes]) BURADA kare
 * kutucuklar halinde bir ızgarada ([ModuleShortcutTile], [androidx.compose.foundation.lazy.grid.
 * LazyVerticalGrid]) gösterilir; bir kutucuğa dokunmak [onNavigate] üzerinden o rotaya gider. Hiç
 * sabitlenmiş yoksa eski boş-durum metni + kısa bir ipucu satırı gösterilir. Route+etiket+ikon
 * tanımı KOPYALANMAZ — tek kaynak [com.calibrahub.app.ui.nav.pinnableDrawerLeaves] (drawer ile
 * PAYLAŞILIR, bkz. o KDoc). Izgara sıralaması bu listenin (drawer'daki) doğal sırasına göre
 * yapılır, sabitleme sırasına GÖRE DEĞİL.
 *
 * Layout notu: dış [Column] `verticalScroll` SARILMAMIŞ (bounded height) — [LazyVerticalGrid]
 * `Modifier.weight(1f)` ile kalan alanı doldurur (aynı desen: bkz. `ContactPickerDialog.kt`'deki
 * `LazyColumn(...weight(1f))` kullanımı), "unbounded height" crash riski YOK.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    displayName: String?,
    onOpenDrawer: () -> Unit,
    onOpenSettings: () -> Unit,
    onNavigate: (String) -> Unit
) {
    val session = LocalContext.current.app.session
    val pinnedRoutes by session.pinnedRoutes.collectAsState(initial = emptySet<String>())

    // pinnableDrawerLeaves (tek kaynak, bkz. AppDrawer.kt) ile pinnedRoutes'un kesişimi — sıra
    // pinnableDrawerLeaves'in doğal (drawer'daki) sırası, sabitleme sırası DEĞİL. Tanınmayan/
    // silinmiş bir route pinnedRoutes'ta kalsa bile (ör. ileride bir ekran kaldırılırsa)
    // pinnableDrawerLeaves'te karşılığı olmadığından burada sessizce elenir.
    val pinnedLeaves = remember(pinnedRoutes) {
        pinnableDrawerLeaves.filter { it.route in pinnedRoutes }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("CalibraHub") },
                navigationIcon = {
                    IconButton(onClick = onOpenDrawer) {
                        Icon(Icons.Default.Menu, contentDescription = "Menü")
                    }
                },
                actions = {
                    IconButton(onClick = onOpenSettings) {
                        Icon(Icons.Default.Settings, contentDescription = "Ayarlar")
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
            if (pinnedLeaves.isEmpty()) {
                Text(
                    text = "Sol menüden bir modül seçerek başlayın.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Text(
                    text = "İpucu: Sol menüdeki bir modülün yanındaki raptiye simgesine dokunarak buraya kısayol ekleyebilirsiniz.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            } else {
                LazyVerticalGrid(
                    columns = GridCells.Fixed(3),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .padding(top = 8.dp)
                ) {
                    items(pinnedLeaves, key = { it.route }) { leaf ->
                        ModuleShortcutTile(leaf = leaf, onClick = { onNavigate(leaf.route) })
                    }
                }
            }
        }
    }
}

/**
 * Ana ekran ızgarasındaki tek bir kare kısayol kutucuğu (2026-07-19) — [leaf]'in ikonu + etiketi,
 * [Modifier.aspectRatio] ile kare oranı zorlanır. Tema uyumlu varsayılan [Card] (hardcoded renk
 * YOK): container/content renkleri [MaterialTheme.colorScheme]'den — koyu/açık ikisinde de aynı
 * default `surface`/`onSurface` çiftini kullanır, ikon [MaterialTheme.colorScheme.primary] ile
 * hafif vurgulanır.
 */
@Composable
private fun ModuleShortcutTile(leaf: DrawerLeaf, onClick: () -> Unit) {
    Card(
        onClick = onClick,
        modifier = Modifier.aspectRatio(1f)
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(8.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            Icon(
                imageVector = leaf.icon,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(28.dp)
            )
            Spacer(Modifier.height(6.dp))
            Text(
                text = leaf.label,
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurface,
                textAlign = TextAlign.Center,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

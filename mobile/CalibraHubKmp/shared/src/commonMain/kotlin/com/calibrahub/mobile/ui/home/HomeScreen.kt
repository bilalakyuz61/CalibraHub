package com.calibrahub.mobile.ui.home

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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.nav.DrawerLeaf
import com.calibrahub.mobile.ui.nav.leafGroupLabels
import com.calibrahub.mobile.ui.nav.pinnableDrawerLeaves

/**
 * Login sonrasi ilk ekran — CalibraHubAndroid `ui/home/HomeScreen.kt` ile BIREBIR ayni yapi/
 * davranis (sadik port): kisa karsilama + kullanicinin sabitledigi (pinned) modul kisayollarinin
 * izgarasi. Tek fark: [session] parametre olarak alinir (LocalContext.current.app.session yerine
 * — bkz. AppDrawer.kt AppDrawerContent KDoc'undaki AYNI gerekce).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    session: SessionManager,
    displayName: String?,
    onOpenDrawer: () -> Unit,
    onOpenSettings: () -> Unit,
    onNavigate: (String) -> Unit,
) {
    val pinnedRoutes by session.pinnedRoutes.collectAsState()

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
                },
            )
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Text(
                text = if (!displayName.isNullOrBlank()) "Hoş geldin, $displayName" else "Hoş geldiniz",
                style = MaterialTheme.typography.headlineSmall,
            )
            if (pinnedLeaves.isEmpty()) {
                Text(
                    text = "Sol menüden bir modül seçerek başlayın.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Text(
                    text = "İpucu: Sol menüdeki bir modülün yanındaki raptiye simgesine dokunarak buraya kısayol ekleyebilirsiniz.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                LazyVerticalGrid(
                    columns = GridCells.Fixed(3),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .padding(top = 8.dp),
                ) {
                    items(pinnedLeaves, key = { it.route }) { leaf ->
                        ModuleShortcutTile(
                            leaf = leaf,
                            groupLabel = leafGroupLabels[leaf.route],
                            onClick = { onNavigate(leaf.route) },
                        )
                    }
                }
            }
        }
    }
}

/** Ana ekran izgarasindaki tek bir kare kisayol kutucugu — CalibraHubAndroid `ModuleShortcutTile`
 * ile ayni (sadik port). */
@Composable
private fun ModuleShortcutTile(leaf: DrawerLeaf, groupLabel: String?, onClick: () -> Unit) {
    Card(
        onClick = onClick,
        modifier = Modifier.aspectRatio(1f),
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(8.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            Icon(
                imageVector = leaf.icon,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(26.dp),
            )
            Spacer(Modifier.height(4.dp))
            if (groupLabel != null) {
                Text(
                    text = groupLabel,
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Text(
                text = leaf.label,
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurface,
                textAlign = TextAlign.Center,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

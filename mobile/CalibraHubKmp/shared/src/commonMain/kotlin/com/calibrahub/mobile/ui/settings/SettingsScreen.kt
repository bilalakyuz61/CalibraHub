package com.calibrahub.mobile.ui.settings

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.DarkMode
import androidx.compose.material.icons.filled.LightMode
import androidx.compose.material.icons.filled.SettingsBrightness
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.session.ThemeMode
import kotlinx.coroutines.launch

/**
 * Ayarlar ekrani — CalibraHubAndroid `ui/settings/SettingsScreen.kt` ile BIREBIR ayni yapi/
 * davranis (sadik port): su an tek bolum "Görünüm" (tema tercihi Sistem/Açık/Koyu).
 * [com.calibrahub.mobile.ui.home.HomeScreen] ust cubugundaki disli ikonundan erisilir.
 *
 * Tema tercihi [SessionManager.themeMode] StateFlow'undan `collectAsState` ile okunur, degisiklik
 * [SessionManager.setThemeMode] ile kaydedilir — AppRoot bu AYNI StateFlow'u dinledigi icin secim
 * ANINDA (restart olmadan) tum uygulamaya yansir (bkz. SessionManager.themeMode KDoc'u).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(session: SessionManager, onBack: () -> Unit) {
    val scope = rememberCoroutineScope()
    val themeMode by session.themeMode.collectAsState()

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Ayarlar") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(top = 12.dp),
        ) {
            SettingsSection(title = "Görünüm") {
                ThemeModeRow(
                    icon = Icons.Default.SettingsBrightness,
                    label = "Sistem",
                    selected = themeMode == ThemeMode.SYSTEM,
                    onClick = { scope.launch { session.setThemeMode(ThemeMode.SYSTEM) } },
                )
                HorizontalDivider()
                ThemeModeRow(
                    icon = Icons.Default.LightMode,
                    label = "Açık",
                    selected = themeMode == ThemeMode.LIGHT,
                    onClick = { scope.launch { session.setThemeMode(ThemeMode.LIGHT) } },
                )
                HorizontalDivider()
                ThemeModeRow(
                    icon = Icons.Default.DarkMode,
                    label = "Koyu",
                    selected = themeMode == ThemeMode.DARK,
                    onClick = { scope.launch { session.setThemeMode(ThemeMode.DARK) } },
                )
            }
        }
    }
}

/** Ayarlar ekranindaki tek bir grup/bolum — CalibraHubAndroid `SettingsSection` ile ayni. */
@Composable
private fun SettingsSection(
    title: String,
    content: @Composable () -> Unit,
) {
    Column(modifier = Modifier.padding(bottom = 8.dp)) {
        Text(
            text = title,
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.primary,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier.padding(start = 20.dp, bottom = 8.dp),
        )
        Card(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp),
        ) {
            Column { content() }
        }
    }
}

/** Tema secim satiri — ikon + etiket + sagda secili durumu gosteren [RadioButton]. */
@Composable
private fun ThemeModeRow(
    icon: ImageVector,
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(
            imageVector = icon,
            contentDescription = null,
            tint = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.width(16.dp))
        Text(
            text = label,
            style = MaterialTheme.typography.bodyLarge,
            modifier = Modifier.weight(1f),
        )
        RadioButton(selected = selected, onClick = onClick)
    }
}

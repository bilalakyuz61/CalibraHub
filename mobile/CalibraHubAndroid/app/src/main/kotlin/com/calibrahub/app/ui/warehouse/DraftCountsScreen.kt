package com.calibrahub.app.ui.warehouse

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.DraftInventoryCountDto
import kotlinx.coroutines.launch

/**
 * FAZ C(b) — Taslak Sayımlar → Yansıt (2026-07-17, koordinatör FINAL kontrat).
 *
 * Liste GET inventory-counts (applied=false kalmış belgeler) — Yansıt aksiyonu MEVCUT
 * [WarehouseRepository.applyInventoryCount] fonksiyonunu (Increment 2b'den beri var,
 * DEĞİŞMEDİ) aynen çağırır; bu ekran yalnızca listeleme + onay katmanı ekler.
 *
 * Onay diyaloğu CLAUDE.md "Silme onay standardı"na PARALEL bir desendir (destrüktif olmasa da
 * GERİ ALINAMAZ bir işlem — stok bakiyesini kalıcı değiştirir): ortalanmış AlertDialog, iki buton
 * (Vazgeç/Yansıt), başarı sonrası satır listeden düşer + snackbar.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DraftCountsScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository
    val scope = rememberCoroutineScope()

    var counts by remember { mutableStateOf<List<DraftInventoryCountDto>?>(null) }
    var loading by remember { mutableStateOf(true) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var reloadTick by remember { mutableStateOf(0) }

    var confirmTarget by remember { mutableStateOf<DraftInventoryCountDto?>(null) }
    var applying by remember { mutableStateOf(false) }
    var applyError by remember { mutableStateOf<String?>(null) }
    val snackbarHostState = remember { SnackbarHostState() }

    LaunchedEffect(reloadTick) {
        loading = true
        errorMessage = null
        repo.draftInventoryCounts(50).fold(
            onSuccess = { counts = it },
            onFailure = { errorMessage = it.message ?: "Taslak sayımlar yüklenemedi" }
        )
        loading = false
    }

    fun applyConfirmed() {
        val target = confirmTarget ?: return
        scope.launch {
            applying = true
            applyError = null
            repo.applyInventoryCount(target.id).fold(
                onSuccess = { result ->
                    counts = counts?.filterNot { it.id == target.id }
                    confirmTarget = null
                    snackbarHostState.showSnackbar("Yansıtıldı (${result.writtenCount} satır yazıldı) — ${target.documentNumber}")
                },
                onFailure = { applyError = it.message ?: "Yansıtma başarısız" }
            )
            applying = false
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Taslak Sayımlar") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                }
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) }
    ) { padding ->
        Column(modifier = Modifier.padding(padding).fillMaxSize()) {
            when {
                errorMessage != null -> DraftCountsMessage(
                    icon = Icons.Default.ErrorOutline,
                    text = errorMessage!!,
                    tint = MaterialTheme.colorScheme.error,
                    onRetry = { reloadTick++ }
                )
                loading && counts == null -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center
                ) { CircularProgressIndicator() }
                counts.isNullOrEmpty() -> DraftCountsMessage(
                    icon = Icons.Default.Checklist,
                    text = "Taslak (yansıtılmamış) sayım yok.",
                    tint = MaterialTheme.colorScheme.onSurfaceVariant
                )
                else -> LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(start = 16.dp, top = 8.dp, end = 16.dp, bottom = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    items(counts!!, key = { it.id }) { count ->
                        DraftCountCard(count, onClick = { confirmTarget = count })
                    }
                }
            }
        }
    }

    // ── Onay diyaloğu — "BelgeNo yansıtılsın mı? [Vazgeç][Yansıt]" ─────────────────────────
    if (confirmTarget != null) {
        val target = confirmTarget!!
        AlertDialog(
            onDismissRequest = { if (!applying) { confirmTarget = null; applyError = null } },
            icon = {
                Icon(Icons.Default.Checklist, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            },
            title = { Text("Sayımı Yansıt") },
            text = {
                Column {
                    Text("${target.documentNumber} yansıtılsın mı?")
                    Spacer(Modifier.height(6.dp))
                    Text(
                        text = "${target.locationName} lokasyonundaki bu sayımın farkları stok bakiyesine işlenir. Bu işlem geri alınamaz.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    if (applyError != null) {
                        Spacer(Modifier.height(10.dp))
                        Text(
                            text = applyError!!,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error
                        )
                    }
                }
            },
            confirmButton = {
                Button(onClick = { applyConfirmed() }, enabled = !applying) {
                    if (applying) CircularProgressIndicator(
                        modifier = Modifier.size(18.dp),
                        color = MaterialTheme.colorScheme.onPrimary
                    ) else Text("Yansıt")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { confirmTarget = null; applyError = null },
                    enabled = !applying
                ) { Text("Vazgeç") }
            }
        )
    }
}

@Composable
private fun DraftCountCard(count: DraftInventoryCountDto, onClick: () -> Unit) {
    Card(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = count.documentNumber,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f)
                )
                Text(
                    text = formatIsoDate(count.date),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Spacer(Modifier.height(6.dp))
            Text(count.locationName, style = MaterialTheme.typography.bodyLarge)
            Spacer(Modifier.height(8.dp))
            InfoBadge(text = "${count.lineCount} kalem", icon = Icons.Default.Checklist)
        }
    }
}

@Composable
private fun DraftCountsMessage(icon: ImageVector, text: String, tint: Color, onRetry: (() -> Unit)? = null) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 48.dp, start = 24.dp, end = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(40.dp))
        Spacer(Modifier.height(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
        if (onRetry != null) {
            Spacer(Modifier.height(12.dp))
            OutlinedButton(onClick = onRetry) { Text("Tekrar Dene") }
        }
    }
}

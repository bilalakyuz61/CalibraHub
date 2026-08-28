package com.calibrahub.mobile.ui.warehouse

import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ReceiptLong
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.SavedDocDetailDto
import com.calibrahub.mobile.data.SavedDocRowDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.common.isoDateToDisplay
import kotlinx.coroutines.launch

/**
 * Kayıtlı Belgeler — mobilden (veya web'den) kaydedilmiş depo belgeleri: Giriş, Çıkış,
 * Transfer, Sayım.
 *
 * NEDEN VAR: mobil bugune kadar belge YAZIYOR ama YAZDIGINI GERI GOSTERMIYORDU. Kullanici
 * fisi kaydettikten sonra "gercekten kaydoldu mu, ne yazdim" sorusunu ancak web'den
 * cevaplayabiliyordu.
 *
 * Yetki: sunucu yalniz kullanicinin GORME yetkisi olan belge tiplerini doner; hicbirine
 * yetkisi yoksa bos liste gelir (hata ekrani degil).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SavedDocsScreen(session: SessionManager, onBack: () -> Unit) {
    val repo = session.warehouseRepository
    val scope = rememberCoroutineScope()

    var rows by remember { mutableStateOf<List<SavedDocRowDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var filter by remember { mutableStateOf<String?>(null) }
    var detail by remember { mutableStateOf<SavedDocDetailDto?>(null) }
    var pendingDetailId by remember { mutableStateOf<Int?>(null) }

    suspend fun reload() {
        loading = true
        repo.savedDocuments(docType = filter).fold(
            onSuccess = { rows = it; errorMessage = null },
            onFailure = { errorMessage = it.message ?: "Belgeler alınamadı." },
        )
        loading = false
    }

    LaunchedEffect(filter) { reload() }

    LaunchedEffect(pendingDetailId) {
        val id = pendingDetailId ?: return@LaunchedEffect
        repo.savedDocumentDetail(id).fold(
            onSuccess = { detail = it },
            onFailure = { errorMessage = it.message ?: "Belge açılamadı." },
        )
        pendingDetailId = null
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (detail == null) "Kayıtlı Belgeler" else detail!!.docNo) },
                navigationIcon = {
                    IconButton(onClick = { if (detail != null) detail = null else onBack() }) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
    ) { padding ->
        Box(modifier = Modifier.padding(padding).fillMaxSize()) {
            val d = detail
            if (d != null) {
                DocDetailView(d)
            } else {
                Column(modifier = Modifier.fillMaxSize()) {
                    // Tip suzgeci — "Tümü" varsayilan. Kullanicinin yetkisi olmayan tip
                    // sunucudan zaten gelmez; secilse bile bos liste doner.
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        listOf(
                            null to "Tümü",
                            "STOCK_IN" to "Giriş",
                            "STOCK_OUT" to "Çıkış",
                            "TRANSFER" to "Transfer",
                            "INVENTORY_COUNT" to "Sayım",
                        ).forEach { (value, label) ->
                            FilterChip(
                                selected = filter == value,
                                onClick = { filter = value },
                                label = { Text(label) },
                            )
                        }
                    }

                    when {
                        loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                            CircularProgressIndicator()
                        }
                        errorMessage != null -> InfoBlock(errorMessage!!, isError = true) {
                            scope.launch { reload() }
                        }
                        rows.isEmpty() -> InfoBlock("Son 30 günde kayıtlı belge yok.", isError = false) {
                            scope.launch { reload() }
                        }
                        else -> LazyColumn(
                            modifier = Modifier.fillMaxSize(),
                            contentPadding = PaddingValues(16.dp),
                            verticalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            items(rows, key = { it.id }) { row ->
                                DocRow(row) { pendingDetailId = row.id }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun DocRow(row: SavedDocRowDto, onClick: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth().clickable { onClick() }) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = row.docTypeLabel,
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.primary,
                    )
                    Text(row.docNo, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                }
                Surface(
                    shape = MaterialTheme.shapes.small,
                    color = MaterialTheme.colorScheme.surfaceVariant,
                    contentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                ) {
                    Text(
                        text = displayDate(row.docDate),
                        style = MaterialTheme.typography.labelMedium,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp),
                    )
                }
            }
            Spacer(Modifier.height(6.dp))
            Text(
                text = buildString {
                    row.locationName?.takeIf { it.isNotBlank() }?.let { append(it) }
                    row.toLocationName?.takeIf { it.isNotBlank() && it != row.locationName }?.let {
                        if (isNotEmpty()) append(" → ")
                        append(it)
                    }
                    if (isNotEmpty()) append(" · ")
                    append("${row.lineCount} kalem")
                },
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun DocDetailView(d: SavedDocDetailDto) {
    Column(modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
        Text(d.docTypeLabel, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary)
        Text(d.docNo, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
        Text(displayDate(d.docDate), style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant)

        val loc = buildString {
            d.fromLocationName?.takeIf { it.isNotBlank() }?.let { append(it) }
            d.toLocationName?.takeIf { it.isNotBlank() }?.let {
                if (isNotEmpty()) append(" → ")
                append(it)
            }
        }
        if (loc.isNotEmpty()) {
            Spacer(Modifier.height(4.dp))
            Text(loc, style = MaterialTheme.typography.bodyMedium)
        }
        d.notes?.takeIf { it.isNotBlank() }?.let {
            Spacer(Modifier.height(8.dp))
            Text(it, style = MaterialTheme.typography.bodyMedium)
        }

        Spacer(Modifier.height(16.dp))
        Text("Kalemler (${d.lines.size})", style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(6.dp))

        Card(modifier = Modifier.fillMaxWidth()) {
            Column {
                d.lines.forEachIndexed { i, l ->
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(l.materialName ?: "—", style = MaterialTheme.typography.bodyMedium)
                            Text(
                                text = listOfNotNull(
                                    l.materialCode?.takeIf { it.isNotBlank() },
                                    l.lotNo?.takeIf { it.isNotBlank() }?.let { "Lot: $it" },
                                ).joinToString(" · "),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        Text(formatQty(l.quantity), fontWeight = FontWeight.SemiBold)
                    }
                    if (i < d.lines.lastIndex) HorizontalDivider()
                }
            }
        }
        Spacer(Modifier.height(24.dp))
    }
}

/** Sunucu ISO tarih/zaman doner ("2026-08-28T00:00:00"); ilk 10 karakter gun kismidir. */
private fun displayDate(raw: String?): String {
    val iso = raw?.take(10).orEmpty()
    return isoDateToDisplay(iso) ?: "—"
}

@Composable
private fun InfoBlock(text: String, isError: Boolean, onRetry: () -> Unit) {
    val tint = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
    Column(
        modifier = Modifier.fillMaxSize().padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(Icons.AutoMirrored.Filled.ReceiptLong, contentDescription = null, tint = tint,
            modifier = Modifier.size(42.dp))
        Spacer(Modifier.height(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
        Spacer(Modifier.height(16.dp))
        OutlinedButton(onClick = onRetry) { Text("Yenile") }
    }
}

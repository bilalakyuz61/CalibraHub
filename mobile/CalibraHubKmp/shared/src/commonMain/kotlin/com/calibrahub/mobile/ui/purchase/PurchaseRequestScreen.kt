package com.calibrahub.mobile.ui.purchase

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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.automirrored.filled.Assignment
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.text.KeyboardOptions
import com.calibrahub.mobile.data.PurchaseRequestLineCreate
import com.calibrahub.mobile.data.PurchaseRequestRowDto
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.warehouse.MaterialPickerField
import com.calibrahub.mobile.ui.warehouse.formatQty
import kotlinx.coroutines.launch

/** Yeni talep formundaki satirin UI modeli. */
private data class RequestLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val quantity: Double,
)

/**
 * İhtiyaç Kaydı — "benim taleplerim" listesi + yeni talep formu.
 *
 * KAPSAM (bilincli): fiyat / cari / para birimi / iskonto YOK. Talep asamasinda fiyat bilinmez;
 * fiyatlandirma satin alma tarafinda (web) yapilir — web'in İhtiyaç Kaydı ekrani da fiyat
 * beklemez. "Talep Eden" de sorulmaz: sunucu bunu giris yapmis kullanicinin bagli personel
 * kaydindan cozer (bkz. MobilePurchaseRequestApiController).
 *
 * Liste VARSAYILAN olarak yalniz kendi taleplerini gosterir — telefondaki birincil soru
 * "benim talebim ne durumda".
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PurchaseRequestScreen(session: SessionManager, onBack: () -> Unit) {
    val repo = session.purchaseRequestRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var rows by remember { mutableStateOf<List<PurchaseRequestRowDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var showForm by remember { mutableStateOf(false) }

    suspend fun reload() {
        loading = true
        repo.list(mine = true).fold(
            onSuccess = { rows = it; errorMessage = null },
            onFailure = { errorMessage = it.message ?: "Talepler alınamadı." },
        )
        loading = false
    }

    LaunchedEffect(Unit) { reload() }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (showForm) "Yeni İhtiyaç Kaydı" else "İhtiyaç Kaydı") },
                navigationIcon = {
                    IconButton(onClick = { if (showForm) showForm = false else onBack() }) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        floatingActionButton = {
            if (!showForm) {
                ExtendedFloatingActionButton(
                    onClick = { showForm = true },
                    icon = { Icon(Icons.Default.Add, contentDescription = null) },
                    text = { Text("Yeni Talep") },
                )
            }
        },
    ) { padding ->
        Box(modifier = Modifier.padding(padding).fillMaxSize()) {
            if (showForm) {
                NewRequestForm(
                    session = session,
                    onCancel = { showForm = false },
                    onSaved = { message ->
                        showForm = false
                        scope.launch {
                            snackbarHostState.showSnackbar(message)
                            reload()
                        }
                    },
                )
            } else {
                when {
                    loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator()
                    }
                    errorMessage != null -> MessageBlock(errorMessage!!, isError = true) {
                        scope.launch { reload() }
                    }
                    rows.isEmpty() -> MessageBlock("Henüz bir talebiniz yok.", isError = false) {
                        scope.launch { reload() }
                    }
                    else -> LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(16.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        items(rows, key = { it.id }) { row -> RequestRow(row) }
                    }
                }
            }
        }
    }
}

@Composable
private fun RequestRow(row: PurchaseRequestRowDto) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = row.documentNumber,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                )
                StatusBadge(row.status)
            }
            row.documentDate?.take(10)?.takeIf { it.isNotBlank() }?.let {
                Text(it, style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            row.notes?.takeIf { it.isNotBlank() }?.let {
                Spacer(Modifier.height(6.dp))
                Text(it, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}

/** Durum rozeti — bilinmeyen durum ham metin olarak gosterilir (sessizce gizlenmez). */
@Composable
private fun StatusBadge(status: String) {
    val label = when (status) {
        "Draft" -> "Taslak"
        "Sent" -> "Gönderildi"
        "Approved" -> "Onaylandı"
        "Rejected" -> "Reddedildi"
        "Cancelled" -> "İptal"
        "Converted" -> "Karşılandı"
        else -> status
    }
    val container = when (status) {
        "Approved", "Converted" -> MaterialTheme.colorScheme.secondaryContainer
        "Rejected", "Cancelled" -> MaterialTheme.colorScheme.errorContainer
        else -> MaterialTheme.colorScheme.surfaceVariant
    }
    val content = when (status) {
        "Approved", "Converted" -> MaterialTheme.colorScheme.onSecondaryContainer
        "Rejected", "Cancelled" -> MaterialTheme.colorScheme.onErrorContainer
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    Surface(shape = MaterialTheme.shapes.small, color = container, contentColor = content) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelSmall,
            modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp),
        )
    }
}

@Composable
private fun NewRequestForm(
    session: SessionManager,
    onCancel: () -> Unit,
    onSaved: (String) -> Unit,
) {
    val repo = session.purchaseRequestRepository
    val scope = rememberCoroutineScope()

    var code by remember { mutableStateOf("") }
    var resolved by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by remember { mutableStateOf("") }
    var lines by remember { mutableStateOf<List<RequestLineUi>>(emptyList()) }
    var note by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<String?>(null) }

    val qty = qtyText.trim().replace(',', '.').toDoubleOrNull()
    val qtyValid = qty != null && qty > 0.0

    fun addLine() {
        val item = resolved ?: return
        val q = qty ?: return
        if (q <= 0.0 || saving) return
        lines = lines + RequestLineUi(item.itemId, item.itemCode, item.itemName, item.unit, q)
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        saveError = null
    }

    Column(
        modifier = Modifier.fillMaxSize().padding(16.dp),
    ) {
        MaterialPickerField(
            query = code,
            onQueryChange = { code = it; resolved = null; resolveError = null },
            onResolved = { resolved = it; resolveError = null },
            onResolveError = { resolved = null; resolveError = it },
            repo = session.warehouseRepository,
            enabled = !saving,
            modifier = Modifier.fillMaxWidth(),
        )
        resolveError?.let {
            Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
        }

        if (resolved != null) {
            Spacer(Modifier.height(10.dp))
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = qtyText,
                    onValueChange = { qtyText = it },
                    label = { Text("Miktar" + (resolved?.unit?.let { u -> " ($u)" } ?: "")) },
                    singleLine = true,
                    isError = qtyText.isNotBlank() && !qtyValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    modifier = Modifier.weight(1f),
                )
                Spacer(Modifier.size(8.dp))
                FilledIconButton(onClick = { addLine() }, enabled = qtyValid && !saving) {
                    Icon(Icons.Default.Add, contentDescription = "Satıra ekle")
                }
            }
        }

        Spacer(Modifier.height(14.dp))
        Text("Talep Satırları (${lines.size})", style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(6.dp))

        if (lines.isEmpty()) {
            Text(
                "Malzeme arayıp miktar girerek satır ekleyin.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            LazyColumn(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                items(lines, key = { it.itemId.toString() + "_" + it.quantity }) { line ->
                    Card(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier.fillMaxWidth().padding(12.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(line.itemName, fontWeight = FontWeight.Medium)
                                Text(
                                    text = line.itemCode + " · " + formatQty(line.quantity) +
                                        (line.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                            IconButton(
                                onClick = { lines = lines - line },
                                enabled = !saving,
                            ) {
                                Icon(Icons.Default.Delete, contentDescription = "Satırı sil",
                                    tint = MaterialTheme.colorScheme.error)
                            }
                        }
                    }
                }
            }
        }

        Spacer(Modifier.height(10.dp))
        OutlinedTextField(
            value = note,
            onValueChange = { note = it },
            label = { Text("Açıklama (isteğe bağlı)") },
            enabled = !saving,
            modifier = Modifier.fillMaxWidth(),
        )

        saveError?.let {
            Spacer(Modifier.height(8.dp))
            Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
        }

        Spacer(Modifier.height(12.dp))
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = onCancel, enabled = !saving, modifier = Modifier.weight(1f)) {
                Text("Vazgeç")
            }
            Button(
                onClick = {
                    if (lines.isEmpty() || saving) return@Button
                    saving = true
                    saveError = null
                    scope.launch {
                        repo.create(
                            lines = lines.map { PurchaseRequestLineCreate(it.itemId, it.quantity) },
                            note = note,
                            locationId = null,
                        ).fold(
                            onSuccess = { res ->
                                saving = false
                                // Onay akisi tanimliysa belge kaydedilir kaydedilmez onaya duser —
                                // kullaniciya bunu SOYLE, yoksa "kaydettim, kimse gormedi mi?" olur.
                                val extra = if (res.approvalStarted) " — onaya gönderildi" else ""
                                onSaved("Talep oluşturuldu: ${res.documentNumber ?: res.id}$extra")
                            },
                            onFailure = {
                                saving = false
                                saveError = it.message ?: "Talep kaydedilemedi."
                            },
                        )
                    }
                },
                enabled = lines.isNotEmpty() && !saving,
                modifier = Modifier.weight(1f),
            ) {
                if (saving) CircularProgressIndicator(modifier = Modifier.size(18.dp))
                else Text("Kaydet")
            }
        }
    }
}

@Composable
private fun MessageBlock(text: String, isError: Boolean, onRetry: () -> Unit) {
    val tint = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
    Column(
        modifier = Modifier.fillMaxSize().padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(Icons.AutoMirrored.Filled.Assignment, contentDescription = null, tint = tint,
            modifier = Modifier.size(42.dp))
        Spacer(Modifier.height(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
        Spacer(Modifier.height(16.dp))
        OutlinedButton(onClick = onRetry) { Text("Yenile") }
    }
}

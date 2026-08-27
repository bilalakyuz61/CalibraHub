package com.calibrahub.mobile.ui.approval

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.TaskAlt
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.PendingApprovalDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.warehouse.formatQty
import kotlinx.coroutines.launch

/**
 * Onay Bekleyenler — web'deki /PendingApproval ekraninin mobil karsiligi.
 *
 * KAPSAM FARKI (bilincli): web'de kapsam secici var (bana/departman/tumu); mobil YALNIZCA
 * "bana atanmis" onaylari gosterir. Telefonda baskasinin kuyrugunu gezmek bir ihtiyac degil
 * ve yetki yuzeyini gereksiz buyuturdu (bkz. MobileApprovalApiController KDoc).
 *
 * Karar sonrasi liste YENIDEN CEKILIR (yerel olarak satir silinmez): onaylanan adim akisi
 * ilerletebilir ve ayni belge bir SONRAKI adimda yine bana dusebilir — yerel silme bu durumu
 * yanlis gosterirdi.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PendingApprovalScreen(session: SessionManager, onBack: () -> Unit) {
    val repo = session.approvalRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var items by remember { mutableStateOf<List<PendingApprovalDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    // Karar diyalogu — null ise kapali. Bosluk: onay/red ayrimi [approving] ile tasinir.
    var decisionFor by remember { mutableStateOf<PendingApprovalDto?>(null) }
    var decisionIsApprove by remember { mutableStateOf(true) }
    var decisionNote by remember { mutableStateOf("") }
    var submitting by remember { mutableStateOf(false) }

    suspend fun reload() {
        loading = true
        repo.pending().fold(
            onSuccess = { items = it; errorMessage = null },
            onFailure = { errorMessage = it.message ?: "Liste alınamadı." },
        )
        loading = false
    }

    LaunchedEffect(Unit) { reload() }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Onay Bekleyenler") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        Box(modifier = Modifier.padding(padding).fillMaxSize()) {
            when {
                loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
                errorMessage != null -> EmptyOrError(
                    text = errorMessage!!,
                    isError = true,
                    onRetry = { scope.launch { reload() } },
                )
                items.isEmpty() -> EmptyOrError(
                    text = "Onayınızı bekleyen belge yok.",
                    isError = false,
                    onRetry = { scope.launch { reload() } },
                )
                else -> LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    items(items, key = { it.instanceId }) { item ->
                        ApprovalCard(
                            item = item,
                            onApprove = {
                                decisionFor = item; decisionIsApprove = true; decisionNote = ""
                            },
                            onReject = {
                                decisionFor = item; decisionIsApprove = false; decisionNote = ""
                            },
                        )
                    }
                }
            }
        }
    }

    decisionFor?.let { target ->
        val isApprove = decisionIsApprove
        // Reddetmede gerekce ZORUNLU — buton, not girilene kadar pasif kalir (sunucu da reddeder,
        // bu yalniz erken/anlasilir geri bildirim).
        val canSubmit = !submitting && (isApprove || decisionNote.isNotBlank())
        AlertDialog(
            onDismissRequest = { if (!submitting) decisionFor = null },
            title = { Text(if (isApprove) "Onayla" else "Reddet") },
            text = {
                Column {
                    Text(
                        text = "${target.documentTypeName ?: "Belge"} · ${target.documentNumber}",
                        style = MaterialTheme.typography.bodyMedium,
                        fontWeight = FontWeight.Medium,
                    )
                    target.contactName?.takeIf { it.isNotBlank() }?.let {
                        Text(it, style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Spacer(Modifier.height(12.dp))
                    OutlinedTextField(
                        value = decisionNote,
                        onValueChange = { decisionNote = it },
                        label = { Text(if (isApprove) "Not (isteğe bağlı)" else "Gerekçe (zorunlu)") },
                        isError = !isApprove && decisionNote.isBlank(),
                        enabled = !submitting,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            },
            confirmButton = {
                TextButton(
                    enabled = canSubmit,
                    onClick = {
                        submitting = true
                        scope.launch {
                            val result = if (isApprove) repo.approve(target.instanceId, decisionNote)
                            else repo.reject(target.instanceId, decisionNote)
                            submitting = false
                            result.fold(
                                onSuccess = {
                                    decisionFor = null
                                    snackbarHostState.showSnackbar(
                                        if (isApprove) "Onaylandı." else "Reddedildi.",
                                    )
                                    reload()
                                },
                                onFailure = {
                                    snackbarHostState.showSnackbar(it.message ?: "İşlem başarısız.")
                                },
                            )
                        }
                    },
                ) { Text(if (isApprove) "Onayla" else "Reddet") }
            },
            dismissButton = {
                TextButton(enabled = !submitting, onClick = { decisionFor = null }) { Text("Vazgeç") }
            },
        )
    }
}

@Composable
private fun ApprovalCard(
    item: PendingApprovalDto,
    onApprove: () -> Unit,
    onReject: () -> Unit,
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = item.documentTypeName?.takeIf { it.isNotBlank() } ?: "Belge",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.primary,
                    )
                    Text(
                        text = item.documentNumber,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                    )
                }
                if (item.totalSteps > 0) {
                    Surface(
                        shape = MaterialTheme.shapes.small,
                        color = MaterialTheme.colorScheme.secondaryContainer,
                        contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
                    ) {
                        Text(
                            text = "Adım ${item.stepPosition}/${item.totalSteps}",
                            style = MaterialTheme.typography.labelSmall,
                            modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp),
                        )
                    }
                }
            }

            item.contactName?.takeIf { it.isNotBlank() }?.let {
                Spacer(Modifier.height(4.dp))
                Text(it, style = MaterialTheme.typography.bodyMedium)
            }

            Spacer(Modifier.height(6.dp))
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = item.stepName.takeIf { it.isNotBlank() } ?: item.flowName,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = formatQty(item.grandTotal) +
                        (item.currencyCode?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                )
            }

            Spacer(Modifier.height(12.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                OutlinedButton(
                    onClick = onReject,
                    modifier = Modifier.weight(1f),
                    colors = ButtonDefaults.outlinedButtonColors(
                        contentColor = MaterialTheme.colorScheme.error,
                    ),
                ) {
                    Icon(Icons.Default.Close, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.size(6.dp))
                    Text("Reddet")
                }
                Button(onClick = onApprove, modifier = Modifier.weight(1f)) {
                    Icon(Icons.Default.Check, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.size(6.dp))
                    Text("Onayla")
                }
            }
        }
    }
}

@Composable
private fun EmptyOrError(text: String, isError: Boolean, onRetry: () -> Unit) {
    val tint = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
    Column(
        modifier = Modifier.fillMaxSize().padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(Icons.Default.TaskAlt, contentDescription = null, tint = tint, modifier = Modifier.size(44.dp))
        Spacer(Modifier.height(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
        Spacer(Modifier.height(16.dp))
        OutlinedButton(onClick = onRetry) { Text("Yenile") }
    }
}

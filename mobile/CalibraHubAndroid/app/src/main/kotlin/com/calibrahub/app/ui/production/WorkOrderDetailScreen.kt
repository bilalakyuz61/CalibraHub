package com.calibrahub.app.ui.production

import androidx.compose.foundation.background
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Badge
import androidx.compose.material.icons.filled.Build
import androidx.compose.material.icons.filled.Done
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
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
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.WorkOrderDetailDto
import com.calibrahub.app.data.WorkOrderOperationDto
import kotlinx.coroutines.launch

/**
 * Üretim → İş Emri Detayı. Başlık bloğu (numara/malzeme/miktar/durum) + operasyon listesi;
 * her operasyon canStart/canComplete bayraklarına göre "Başlat"/"Tamamla" butonu gösterir.
 *
 * Operatör kimliği (Sicil No + PIN, 2026-07-16 sözleşme güncellemesi): ekran-ömrü boyunca
 * rememberSaveable ile hatırlanır — Başlat/Tamamla ilk kez tıklandığında kimlik doğrulama
 * dialoğu açılır, auth-operator başarılı olunca operatorId/name saklanır ve sıradaki
 * aksiyonlarda TEKRAR sorulmaz (koordinatör kararı). Ekrandan çıkılınca (geri tuşu, iş emri
 * listesine dönüş) bu composable komple compose-out olur, state kaybolur — bir sonraki
 * girişte kimlik yeniden istenir (kasıtlı, kalıcı oturum YOK).
 *
 * Başlat onayı basit bir AlertDialog (form yok; hata → dialog kapanır + snackbar). Tamamla
 * ise miktar/fire/not formu taşıdığından hata durumunda AÇIK kalır ve hatayı dialog içinde
 * satır olarak gösterir (kullanıcı girdiği değerleri kaybetmeden düzeltip tekrar dener).
 *
 * DELTA uyarısı (koordinatör netleştirmesi): Tamamla dialoğundaki sağlam/fire alanları bu
 * OTURUMDA üretilen EK miktardır — operasyon kartındaki "Toplam Sağlam/Fire" ile karıştırma.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WorkOrderDetailScreen(workOrderId: Int, onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.productionRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var detail by remember { mutableStateOf<WorkOrderDetailDto?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var reloadTick by remember { mutableStateOf(0) }
    var actionBusy by remember { mutableStateOf(false) }

    // Ekran-ömrü boyunca hatırlanan operatör — bkz. dosya üstü KDoc.
    var operatorId by rememberSaveable { mutableStateOf<Int?>(null) }
    var operatorName by rememberSaveable { mutableStateOf<String?>(null) }

    var pendingAction by remember { mutableStateOf<PendingAction?>(null) }

    var showPinDialog by remember { mutableStateOf(false) }
    var personnelCodeValue by remember { mutableStateOf("") }
    var pinValue by remember { mutableStateOf("") }
    var pinAuthenticating by remember { mutableStateOf(false) }
    var pinError by remember { mutableStateOf<String?>(null) }

    var showStartConfirm by remember { mutableStateOf(false) }

    var showCompleteDialog by remember { mutableStateOf(false) }
    var completeGoodText by remember { mutableStateOf("0") }
    var completeScrapText by remember { mutableStateOf("0") }
    var completeNote by remember { mutableStateOf("") }
    var completeError by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(workOrderId, reloadTick) {
        errorMessage = null
        repo.workOrderDetail(workOrderId).fold(
            onSuccess = { detail = it },
            onFailure = { errorMessage = it.message ?: "İş emri yüklenemedi" }
        )
    }

    fun resetPendingFlow() {
        pendingAction = null
        showPinDialog = false
        personnelCodeValue = ""
        pinValue = ""
        pinError = null
        showStartConfirm = false
        showCompleteDialog = false
        completeError = null
    }

    fun beginAction(action: PendingAction) {
        pendingAction = action
        pinError = null
        personnelCodeValue = ""
        pinValue = ""
        if (operatorId == null) {
            showPinDialog = true
        } else when (action) {
            is PendingAction.Start -> showStartConfirm = true
            is PendingAction.Complete -> {
                completeGoodText = "0"
                completeScrapText = "0"
                completeNote = ""
                completeError = null
                showCompleteDialog = true
            }
        }
    }

    fun submitPin() {
        val code = personnelCodeValue.trim()
        val pin = pinValue.trim()
        if (code.isEmpty() || pin.isEmpty() || pinAuthenticating) return
        scope.launch {
            pinAuthenticating = true
            pinError = null
            repo.authOperator(code, pin).fold(
                onSuccess = { auth ->
                    operatorId = auth.operatorId
                    operatorName = auth.name
                    showPinDialog = false
                    personnelCodeValue = ""
                    pinValue = ""
                    when (pendingAction) {
                        is PendingAction.Start -> showStartConfirm = true
                        is PendingAction.Complete -> {
                            completeGoodText = "0"
                            completeScrapText = "0"
                            completeNote = ""
                            completeError = null
                            showCompleteDialog = true
                        }
                        null -> {}
                    }
                },
                onFailure = { pinError = it.message ?: "Doğrulama başarısız" }
            )
            pinAuthenticating = false
        }
    }

    fun confirmStart() {
        val action = pendingAction as? PendingAction.Start ?: return
        val opId = operatorId ?: return
        if (actionBusy) return
        scope.launch {
            actionBusy = true
            repo.startOperation(action.operationId, opId).fold(
                onSuccess = {
                    showStartConfirm = false
                    pendingAction = null
                    reloadTick++
                    scope.launch { snackbarHostState.showSnackbar("${action.operationName} başlatıldı") }
                },
                onFailure = { failure ->
                    showStartConfirm = false
                    pendingAction = null
                    scope.launch { snackbarHostState.showSnackbar(failure.message ?: "Başlatma başarısız") }
                }
            )
            actionBusy = false
        }
    }

    fun submitComplete() {
        val action = pendingAction as? PendingAction.Complete ?: return
        val opId = operatorId ?: return
        val good = completeGoodText.trim().replace(',', '.').toDoubleOrNull() ?: return
        val scrap = completeScrapText.trim().replace(',', '.').toDoubleOrNull() ?: return
        if (good < 0.0 || scrap < 0.0 || actionBusy) return
        scope.launch {
            actionBusy = true
            completeError = null
            repo.completeOperation(
                operationId = action.operationId,
                operatorId = opId,
                goodQuantity = good,
                scrapQuantity = scrap,
                note = completeNote
            ).fold(
                onSuccess = {
                    showCompleteDialog = false
                    pendingAction = null
                    reloadTick++
                    scope.launch { snackbarHostState.showSnackbar("${action.operationName} tamamlandı") }
                },
                onFailure = { failure -> completeError = failure.message ?: "Tamamlama başarısız" }
            )
            actionBusy = false
        }
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = { Text("İş Emri Detayı") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Geri")
                    }
                }
            )
        }
    ) { padding ->
        when {
            errorMessage != null -> Column(
                modifier = Modifier
                    .padding(padding)
                    .fillMaxSize()
                    .padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center
            ) {
                Icon(
                    Icons.Default.ErrorOutline,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.error,
                    modifier = Modifier.size(40.dp)
                )
                Spacer(Modifier.height(12.dp))
                Text(
                    text = errorMessage!!,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium,
                    textAlign = TextAlign.Center
                )
                Spacer(Modifier.height(12.dp))
                OutlinedButton(onClick = { reloadTick++ }) { Text("Tekrar Dene") }
            }
            detail == null -> Box(
                modifier = Modifier.padding(padding).fillMaxSize(),
                contentAlignment = Alignment.Center
            ) { CircularProgressIndicator() }
            else -> {
                val d = detail!!
                Column(
                    modifier = Modifier
                        .padding(padding)
                        .fillMaxSize()
                        .verticalScroll(rememberScrollState())
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(14.dp)
                ) {
                    WorkOrderHeaderCard(d)

                    if (operatorName != null) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(
                                Icons.Default.Badge,
                                contentDescription = null,
                                modifier = Modifier.size(16.dp),
                                tint = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            Spacer(Modifier.width(6.dp))
                            Text(
                                text = "Operatör: $operatorName",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }

                    Text(
                        text = "Operasyonlar (${d.operations.size})",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold
                    )

                    if (d.operations.isEmpty()) {
                        Text(
                            text = "Bu iş emri için tanımlı operasyon yok.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    } else {
                        d.operations.sortedBy { it.seq }.forEach { op ->
                            OperationRow(
                                op = op,
                                busy = actionBusy,
                                onStart = { beginAction(PendingAction.Start(op.id, op.name)) },
                                onComplete = { beginAction(PendingAction.Complete(op.id, op.name)) }
                            )
                        }
                    }
                    Spacer(Modifier.height(8.dp))
                }
            }
        }
    }

    if (showPinDialog) {
        OperatorPinDialog(
            personnelCode = personnelCodeValue,
            onPersonnelCodeChange = { personnelCodeValue = it },
            pin = pinValue,
            onPinChange = { pinValue = it },
            authenticating = pinAuthenticating,
            error = pinError,
            onConfirm = { submitPin() },
            onDismiss = { resetPendingFlow() }
        )
    }

    if (showStartConfirm) {
        val action = pendingAction as? PendingAction.Start
        if (action != null) {
            StartConfirmDialog(
                operationName = action.operationName,
                operatorName = operatorName ?: "",
                busy = actionBusy,
                onConfirm = { confirmStart() },
                onDismiss = { resetPendingFlow() }
            )
        }
    }

    if (showCompleteDialog) {
        val action = pendingAction as? PendingAction.Complete
        if (action != null) {
            CompleteOperationDialog(
                operationName = action.operationName,
                goodText = completeGoodText,
                onGoodChange = { completeGoodText = it },
                scrapText = completeScrapText,
                onScrapChange = { completeScrapText = it },
                note = completeNote,
                onNoteChange = { completeNote = it },
                busy = actionBusy,
                error = completeError,
                onConfirm = { submitComplete() },
                onDismiss = { resetPendingFlow() }
            )
        }
    }
}

private sealed class PendingAction {
    data class Start(val operationId: Int, val operationName: String) : PendingAction()
    data class Complete(val operationId: Int, val operationName: String) : PendingAction()
}

@Composable
private fun WorkOrderHeaderCard(detail: WorkOrderDetailDto) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = detail.number,
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.weight(1f)
                )
                StatusChip(label = detail.statusLabel, statusCode = detail.statusCode)
            }
            Spacer(Modifier.height(8.dp))
            Text(detail.itemName, style = MaterialTheme.typography.bodyLarge)
            Text(
                text = detail.itemCode,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(6.dp))
            Text(
                text = "Miktar: " + formatQty(detail.quantity) + (detail.unit.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Medium
            )
        }
    }
}

@Composable
private fun OperationRow(
    op: WorkOrderOperationDto,
    busy: Boolean,
    onStart: () -> Unit,
    onComplete: () -> Unit
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = "${op.seq}. ${op.name}",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f)
                )
                StatusChip(label = op.statusLabel, statusCode = op.statusCode)
            }
            if (op.machineName.isNotBlank()) {
                Spacer(Modifier.height(4.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        Icons.Default.Build,
                        contentDescription = null,
                        modifier = Modifier.size(14.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(Modifier.width(4.dp))
                    Text(op.machineName, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            Spacer(Modifier.height(6.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                Text("Toplam Sağlam: " + formatQty(op.goodQuantity), style = MaterialTheme.typography.bodySmall)
                Text(
                    text = "Toplam Fire: " + formatQty(op.scrapQuantity),
                    style = MaterialTheme.typography.bodySmall,
                    color = if (op.scrapQuantity > 0.0) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            if (op.canStart || op.canComplete) {
                Spacer(Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (op.canStart) {
                        Button(onClick = onStart, enabled = !busy) {
                            Icon(Icons.Default.PlayArrow, contentDescription = null, modifier = Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text("Başlat")
                        }
                    }
                    if (op.canComplete) {
                        Button(onClick = onComplete, enabled = !busy) {
                            Icon(Icons.Default.Done, contentDescription = null, modifier = Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text("Tamamla")
                        }
                    }
                }
            }
        }
    }
}

/**
 * Operatör kimlik doğrulama dialoğu — Sicil No (normal metin/rakam klavye) + PIN
 * (NumberPassword, maskeli) birlikte zorunlu (2026-07-16 sözleşme güncellemesi). Sicil No
 * alanında Enter/Next → PIN alanına odak taşınır (FocusRequester); PIN alanında Enter/Done
 * → onConfirm(). {error} backend mesajı (yanlış kimlik veya kilitli personel) olduğu gibi
 * gösterilir.
 */
@Composable
private fun OperatorPinDialog(
    personnelCode: String,
    onPersonnelCodeChange: (String) -> Unit,
    pin: String,
    onPinChange: (String) -> Unit,
    authenticating: Boolean,
    error: String?,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit
) {
    val pinFocusRequester = remember { FocusRequester() }
    AlertDialog(
        onDismissRequest = { if (!authenticating) onDismiss() },
        icon = { Icon(Icons.Default.Lock, contentDescription = null) },
        title = { Text("Operatör Doğrulama") },
        text = {
            Column {
                Text(
                    text = "Devam etmek için sicil no ve PIN kodunu girin.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Spacer(Modifier.height(12.dp))
                OutlinedTextField(
                    value = personnelCode,
                    onValueChange = onPersonnelCodeChange,
                    label = { Text("Sicil No") },
                    singleLine = true,
                    enabled = !authenticating,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Text, imeAction = ImeAction.Next),
                    keyboardActions = KeyboardActions(onNext = { pinFocusRequester.requestFocus() }),
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(10.dp))
                OutlinedTextField(
                    value = pin,
                    onValueChange = { new -> if (new.length <= 12) onPinChange(new.filter { it.isDigit() }) },
                    label = { Text("PIN") },
                    singleLine = true,
                    enabled = !authenticating,
                    isError = error != null,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { onConfirm() }),
                    modifier = Modifier
                        .fillMaxWidth()
                        .focusRequester(pinFocusRequester)
                )
                if (error != null) {
                    Spacer(Modifier.height(8.dp))
                    Text(error, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = onConfirm,
                enabled = personnelCode.isNotBlank() && pin.isNotBlank() && !authenticating
            ) {
                if (authenticating) CircularProgressIndicator(modifier = Modifier.size(16.dp))
                else Text("Onayla")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !authenticating) { Text("Vazgeç") }
        }
    )
}

@Composable
private fun StartConfirmDialog(
    operationName: String,
    operatorName: String,
    busy: Boolean,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit
) {
    AlertDialog(
        onDismissRequest = { if (!busy) onDismiss() },
        icon = { Icon(Icons.Default.PlayArrow, contentDescription = null, tint = MaterialTheme.colorScheme.primary) },
        title = { Text("Operasyonu Başlat") },
        text = { Text("\"$operationName\" operasyonu $operatorName tarafından başlatılsın mı?") },
        confirmButton = {
            TextButton(onClick = onConfirm, enabled = !busy) {
                if (busy) CircularProgressIndicator(modifier = Modifier.size(16.dp)) else Text("Başlat")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !busy) { Text("Vazgeç") }
        }
    )
}

/**
 * Tamamla formu — sağlam/fire alanları bu OTURUMDA üretilen EK (delta) miktardır, operasyon
 * kartındaki kümülatif toplam DEĞİLDİR (koordinatör netleştirmesi, 2026-07-16); bu yüzden
 * her ikisi de "0" ile başlar ve dialogda açık bir uyarı metni gösterilir. Negatif değer
 * client tarafında da engellenir (backend zaten 400 döner); hata durumunda dialog AÇIK
 * kalır ki kullanıcı girdiği değerleri kaybetmeden düzeltip tekrar denesin.
 */
@Composable
private fun CompleteOperationDialog(
    operationName: String,
    goodText: String,
    onGoodChange: (String) -> Unit,
    scrapText: String,
    onScrapChange: (String) -> Unit,
    note: String,
    onNoteChange: (String) -> Unit,
    busy: Boolean,
    error: String?,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit
) {
    val goodValid = goodText.trim().replace(',', '.').toDoubleOrNull()?.let { it >= 0.0 } == true
    val scrapValid = scrapText.trim().replace(',', '.').toDoubleOrNull()?.let { it >= 0.0 } == true

    AlertDialog(
        onDismissRequest = { if (!busy) onDismiss() },
        icon = { Icon(Icons.Default.Done, contentDescription = null, tint = MaterialTheme.colorScheme.primary) },
        title = { Text("Operasyonu Tamamla") },
        text = {
            Column(modifier = Modifier.verticalScroll(rememberScrollState())) {
                Text(operationName, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(4.dp))
                Text(
                    text = "Bu değerler toplam değil — bu oturumda üretilen EK miktardır.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Spacer(Modifier.height(12.dp))
                OutlinedTextField(
                    value = goodText,
                    onValueChange = onGoodChange,
                    label = { Text("Sağlam Adet (Bu Oturum)") },
                    singleLine = true,
                    enabled = !busy,
                    isError = goodText.isNotBlank() && !goodValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal, imeAction = ImeAction.Next),
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(10.dp))
                OutlinedTextField(
                    value = scrapText,
                    onValueChange = onScrapChange,
                    label = { Text("Fire (Bu Oturum)") },
                    singleLine = true,
                    enabled = !busy,
                    isError = scrapText.isNotBlank() && !scrapValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal, imeAction = ImeAction.Next),
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(10.dp))
                OutlinedTextField(
                    value = note,
                    onValueChange = onNoteChange,
                    label = { Text("Not (opsiyonel)") },
                    enabled = !busy,
                    minLines = 2,
                    maxLines = 4,
                    modifier = Modifier.fillMaxWidth()
                )
                if (error != null) {
                    Spacer(Modifier.height(8.dp))
                    Text(error, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onConfirm, enabled = goodValid && scrapValid && !busy) {
                if (busy) CircularProgressIndicator(modifier = Modifier.size(16.dp)) else Text("Tamamla")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !busy) { Text("Vazgeç") }
        }
    )
}

/**
 * Durum rozeti — WorkOrderListScreen'deki StatusChip ile AYNI mantık (dosya-özel duplicate,
 * WarehouseModule'daki formatQuantity/formatQty presedanına uyularak paylaşılan util dosyası
 * açılmadı). statusCode kümesi KİLİTLİ (koordinatör, 2026-07-16) — bkz. WorkOrderListScreen.
 */
@Composable
private fun StatusChip(label: String, statusCode: String) {
    val scheme = MaterialTheme.colorScheme
    val (container, content) = when (statusCode.trim().lowercase()) {
        "in_progress" -> scheme.secondary.copy(alpha = 0.18f) to scheme.secondary
        "completed" -> scheme.primary.copy(alpha = 0.18f) to scheme.primary
        "released" -> Color(0xFF3B82F6).copy(alpha = 0.18f) to Color(0xFF3B82F6)
        "cancelled" -> scheme.error.copy(alpha = 0.18f) to scheme.error
        "closed", "skipped" -> scheme.surfaceVariant to scheme.onSurfaceVariant.copy(alpha = 0.6f)
        else -> scheme.surfaceVariant to scheme.onSurfaceVariant   // planned/pending/bilinmeyen
    }
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(container)
            .padding(horizontal = 10.dp, vertical = 4.dp)
    ) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = content, fontWeight = FontWeight.Medium)
    }
}

/** Tam sayıları ".00" olmadan, ondalıklıları 2 haneye yuvarlayarak gösterir (WarehouseModule ile aynı V1 format). */
private fun formatQty(q: Double): String =
    if (q == q.toLong().toDouble()) q.toLong().toString() else "%.2f".format(q)

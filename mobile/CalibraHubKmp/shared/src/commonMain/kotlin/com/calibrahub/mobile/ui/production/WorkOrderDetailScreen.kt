package com.calibrahub.mobile.ui.production

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
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Badge
import androidx.compose.material.icons.filled.Build
import androidx.compose.material.icons.filled.Done
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.SwapHoriz
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
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.ActiveActivityDto
import com.calibrahub.mobile.data.ActivityTypeDto
import com.calibrahub.mobile.data.WorkOrderDetailDto
import com.calibrahub.mobile.data.WorkOrderOperationDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.warehouse.formatQty
import kotlinx.coroutines.launch

/**
 * Üretim → İş Emri Detayı — CalibraHubAndroid `ui/production/WorkOrderDetailScreen.kt` ile
 * BİREBİR sadık port. Başlık bloğu (numara/malzeme/miktar/durum) + operasyon listesi; her
 * operasyon canStart/canComplete bayraklarına göre "Başlat"/"Tamamla" butonu gösterir. [session]
 * Android'deki `context.app.productionRepository` yerine PARAMETRE olarak alınır (KMP'de
 * Android Context/Application-scoped singleton yok).
 *
 * Operatör kimliği (Sicil No + PIN): ekran-ömrü boyunca rememberSaveable ile hatırlanır —
 * Başlat/Tamamla ilk kez tıklandığında kimlik doğrulama dialoğu açılır, auth-operator başarılı
 * olunca operatorId/name saklanır ve sıradaki aksiyonlarda TEKRAR sorulmaz. Ekrandan çıkılınca
 * (geri tuşu, iş emri listesine dönüş) bu composable komple compose-out olur, state kaybolur —
 * bir sonraki girişte kimlik yeniden istenir (kasıtlı, kalıcı oturum YOK).
 *
 * Başlat onayı basit bir AlertDialog (form yok; hata → dialog kapanır + snackbar). Tamamla ise
 * miktar/fire/not formu taşıdığından hata durumunda AÇIK kalır ve hatayı dialog içinde satır
 * olarak gösterir (kullanıcı girdiği değerleri kaybetmeden düzeltip tekrar dener).
 *
 * DELTA uyarısı: Tamamla dialoğundaki sağlam/fire alanları bu OTURUMDA üretilen EK miktardır —
 * operasyon kartındaki "Toplam Sağlam/Fire" ile karıştırma (bkz. [WorkOrderOperationDto] KDoc).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WorkOrderDetailScreen(session: SessionManager, workOrderId: Int, onBack: () -> Unit) {
    val repo = session.productionRepository
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

    // ── Durus / aktivite ────────────────────────────────────────────────────
    // Aktivite tipleri bir kez cekilir (tip + sebepleri birlikte). Aktif aktivite operasyon
    // basina tutulur: iki operasyon ayni anda acik olabilir, tek bir "aktif" yetmez.
    var activityTypes by remember { mutableStateOf<List<ActivityTypeDto>>(emptyList()) }
    var activeByOperation by remember { mutableStateOf<Map<Int, ActiveActivityDto>>(emptyMap()) }
    var activityDialogFor by remember { mutableStateOf<Int?>(null) }

    var showStartConfirm by remember { mutableStateOf(false) }

    var showCompleteDialog by remember { mutableStateOf(false) }
    var completeGoodText by remember { mutableStateOf("0") }
    var completeScrapText by remember { mutableStateOf("0") }
    var completeNote by remember { mutableStateOf("") }
    var completeError by remember { mutableStateOf<String?>(null) }

    // Kisisel telefon senaryosu: personel kartinda "Mobilde PIN Sor" KAPALI ve kullanici
    // o personele bagliysa, operator kimligi acilista cozulur ve PIN ekrani HIC gosterilmez.
    // Hata/bagsizlik durumunda pinRequired=true doner -> mevcut PIN akisi aynen isler.
    LaunchedEffect(Unit) {
        val me = repo.myOperator()
        if (me.linked && !me.pinRequired && me.operatorId != null) {
            operatorId = me.operatorId
            operatorName = me.name
        }
    }

    LaunchedEffect(Unit) {
        repo.activityTypes().onSuccess { activityTypes = it }
    }

    LaunchedEffect(workOrderId, reloadTick) {
        errorMessage = null
        repo.workOrderDetail(workOrderId).fold(
            onSuccess = { d ->
                detail = d
                // Her operasyonun AN AKTIF aktivitesini cek — kartta canli durum rozeti icin.
                // Hata durumunda sessizce bos birakilir: aktivite bilgisi eksik gorunur ama
                // is emri ekrani calismaya devam eder.
                val map = mutableMapOf<Int, ActiveActivityDto>()
                d.operations.forEach { op ->
                    repo.activeActivity(op.id).getOrNull()?.let { map[op.id] = it }
                }
                activeByOperation = map
            },
            onFailure = { errorMessage = it.message ?: "İş emri yüklenemedi" },
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
                onFailure = { pinError = it.message ?: "Doğrulama başarısız" },
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
                },
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
                note = completeNote,
            ).fold(
                onSuccess = {
                    showCompleteDialog = false
                    pendingAction = null
                    reloadTick++
                    scope.launch { snackbarHostState.showSnackbar("${action.operationName} tamamlandı") }
                },
                onFailure = { failure -> completeError = failure.message ?: "Tamamlama başarısız" },
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
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
    ) { padding ->
        when {
            errorMessage != null -> Column(
                modifier = Modifier
                    .padding(padding)
                    .fillMaxSize()
                    .padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Icon(
                    Icons.Default.ErrorOutline,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.error,
                    modifier = Modifier.size(40.dp),
                )
                Spacer(Modifier.height(12.dp))
                Text(
                    text = errorMessage!!,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium,
                    textAlign = TextAlign.Center,
                )
                Spacer(Modifier.height(12.dp))
                OutlinedButton(onClick = { reloadTick++ }) { Text("Tekrar Dene") }
            }
            detail == null -> Box(
                modifier = Modifier.padding(padding).fillMaxSize(),
                contentAlignment = Alignment.Center,
            ) { CircularProgressIndicator() }
            else -> {
                val d = detail!!
                Column(
                    modifier = Modifier
                        .padding(padding)
                        .fillMaxSize()
                        .verticalScroll(rememberScrollState())
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(14.dp),
                ) {
                    WorkOrderHeaderCard(d)

                    if (operatorName != null) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(
                                Icons.Default.Badge,
                                contentDescription = null,
                                modifier = Modifier.size(16.dp),
                                tint = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                            Spacer(Modifier.width(6.dp))
                            Text(
                                text = "Operatör: $operatorName",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }

                    Text(
                        text = "Operasyonlar (${d.operations.size})",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                    )

                    if (d.operations.isEmpty()) {
                        Text(
                            text = "Bu iş emri için tanımlı operasyon yok.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    } else {
                        d.operations.sortedBy { it.seq }.forEach { op ->
                            OperationRow(
                                op = op,
                                busy = actionBusy,
                                activeActivity = activeByOperation[op.id],
                                onStart = { beginAction(PendingAction.Start(op.id, op.name)) },
                                onComplete = { beginAction(PendingAction.Complete(op.id, op.name)) },
                                onChangeStatus = { activityDialogFor = op.id },
                            )
                        }
                    }
                    Spacer(Modifier.height(8.dp))
                }
            }
        }
    }

    // Durum degistirme diyalogu — tip sec, (varsa) sebep sec, baslat. Aktif aktivite varsa
    // ayrica "Durumu Bitir" sunulur (servis yeni aktivite baslatirken zaten oncekini kapatir,
    // ama operator "hicbir sey yapmiyorum" demek isteyebilir).
    activityDialogFor?.let { opId ->
        val active = activeByOperation[opId]
        ActivityDialog(
            types = activityTypes,
            active = active,
            enabled = !actionBusy,
            onDismiss = { activityDialogFor = null },
            onEnd = {
                val opr = operatorId
                activityDialogFor = null
                if (opr != null) {
                    scope.launch {
                        actionBusy = true
                        repo.endActivity(opId, opr).fold(
                            onSuccess = { reloadTick++ },
                            onFailure = { snackbarHostState.showSnackbar(it.message ?: "Durum kapatılamadı") },
                        )
                        actionBusy = false
                    }
                }
            },
            onSelect = { type, reasonId ->
                val opr = operatorId
                activityDialogFor = null
                if (opr == null) {
                    // Operator henuz dogrulanmadi — PIN akisi zaten baslat/tamamla ile ayni kapi.
                    showPinDialog = true
                } else {
                    scope.launch {
                        actionBusy = true
                        repo.startActivity(opId, opr, type, reasonId, null).fold(
                            onSuccess = { reloadTick++ },
                            onFailure = { snackbarHostState.showSnackbar(it.message ?: "Durum değiştirilemedi") },
                        )
                        actionBusy = false
                    }
                }
            },
        )
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
            onDismiss = { resetPendingFlow() },
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
                onDismiss = { resetPendingFlow() },
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
                onDismiss = { resetPendingFlow() },
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
                    modifier = Modifier.weight(1f),
                )
                StatusChip(label = detail.statusLabel, statusCode = detail.statusCode)
            }
            Spacer(Modifier.height(8.dp))
            Text(detail.itemName, style = MaterialTheme.typography.bodyLarge)
            Text(
                text = detail.itemCode,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(6.dp))
            Text(
                text = "Miktar: " + formatQty(detail.quantity) + (detail.unit.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Medium,
            )
        }
    }
}

@Composable
private fun OperationRow(
    op: WorkOrderOperationDto,
    busy: Boolean,
    activeActivity: ActiveActivityDto?,
    onStart: () -> Unit,
    onComplete: () -> Unit,
    onChangeStatus: () -> Unit,
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = "${op.seq}. ${op.name}",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
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
                        tint = MaterialTheme.colorScheme.onSurfaceVariant,
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
                    color = if (op.scrapQuantity > 0.0) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            // Aktif durum rozeti — operator ne yapiyor (Uretim / Ariza / Malzeme Bekleme…).
            activeActivity?.let { act ->
                Spacer(Modifier.height(8.dp))
                Surface(
                    shape = MaterialTheme.shapes.small,
                    color = MaterialTheme.colorScheme.secondaryContainer,
                    contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
                ) {
                    Text(
                        text = act.activityTypeLabel +
                            (act.activityReasonName?.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""),
                        style = MaterialTheme.typography.labelMedium,
                        modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
                    )
                }
            }

            if (op.canStart || op.canComplete) {
                Spacer(Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    // Durum degistirme baslamis operasyonda anlamli: duruş/kurulum/mola kaydi.
                    if (!op.canStart) {
                        OutlinedButton(onClick = onChangeStatus, enabled = !busy) {
                            Icon(Icons.Default.SwapHoriz, contentDescription = null, modifier = Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text("Durum")
                        }
                    }
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
 * Operatör kimlik doğrulama dialoğu — Sicil No (normal metin klavye) + PIN (NumberPassword,
 * maskeli) birlikte zorunlu. Sicil No alanında Enter/Next → PIN alanına odak taşınır
 * (FocusRequester); PIN alanında Enter/Done → onConfirm(). {error} backend mesajı (yanlış kimlik
 * veya kilitli personel) olduğu gibi gösterilir.
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
    onDismiss: () -> Unit,
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
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
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
                    modifier = Modifier.fillMaxWidth(),
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
                        .focusRequester(pinFocusRequester),
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
                enabled = personnelCode.isNotBlank() && pin.isNotBlank() && !authenticating,
            ) {
                if (authenticating) CircularProgressIndicator(modifier = Modifier.size(16.dp))
                else Text("Onayla")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !authenticating) { Text("Vazgeç") }
        },
    )
}

@Composable
private fun StartConfirmDialog(
    operationName: String,
    operatorName: String,
    busy: Boolean,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
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
        },
    )
}

/**
 * Tamamla formu — sağlam/fire alanları bu OTURUMDA üretilen EK (delta) miktardır, operasyon
 * kartındaki kümülatif toplam DEĞİLDİR; bu yüzden her ikisi de "0" ile başlar ve dialogda açık
 * bir uyarı metni gösterilir. Negatif değer client tarafında da engellenir (backend zaten 400
 * döner); hata durumunda dialog AÇIK kalır ki kullanıcı girdiği değerleri kaybetmeden düzeltip
 * tekrar denesin.
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
    onDismiss: () -> Unit,
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
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
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
                    modifier = Modifier.fillMaxWidth(),
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
                    modifier = Modifier.fillMaxWidth(),
                )
                Spacer(Modifier.height(10.dp))
                OutlinedTextField(
                    value = note,
                    onValueChange = onNoteChange,
                    label = { Text("Not (opsiyonel)") },
                    enabled = !busy,
                    minLines = 2,
                    maxLines = 4,
                    modifier = Modifier.fillMaxWidth(),
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
        },
    )
}

/**
 * Durum (aktivite) secim diyalogu. Once TIP secilir, tipin sebepleri varsa altinda listelenir.
 * Sebep ZORUNLU DEGILDIR — sunucu yalniz "Diger" tipinde not zorunlu kilar.
 *
 * Arastirma notu: duruş sebebi olay TAZEYKEN sorulmali (vardiya sonunda hafizadan doldurma
 * yaygin bir antipattern) — bu yuzden akis tek diyalogda biter, sonraya birakilmaz.
 */
@Composable
private fun ActivityDialog(
    types: List<ActivityTypeDto>,
    active: ActiveActivityDto?,
    enabled: Boolean,
    onDismiss: () -> Unit,
    onEnd: () -> Unit,
    onSelect: (type: Int, reasonId: Int?) -> Unit,
) {
    var selectedType by remember { mutableStateOf<Int?>(null) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Durum Değiştir") },
        text = {
            Column(modifier = Modifier.verticalScroll(rememberScrollState())) {
                active?.let {
                    Text(
                        "Şu an: ${it.activityTypeLabel}" +
                            (it.activityReasonName?.takeIf { r -> r.isNotBlank() }?.let { r -> " · $r" } ?: ""),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Spacer(Modifier.height(10.dp))
                }

                if (types.isEmpty()) {
                    Text(
                        "Durum tipleri yüklenemedi.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.error,
                    )
                }

                types.forEach { t ->
                    val isSelected = selectedType == t.value
                    Surface(
                        onClick = { selectedType = if (isSelected) null else t.value },
                        shape = MaterialTheme.shapes.small,
                        color = if (isSelected) MaterialTheme.colorScheme.secondaryContainer
                                else MaterialTheme.colorScheme.surface,
                        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
                    ) {
                        Text(t.label, modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp))
                    }

                    // Sebepler yalniz secili tipin altinda acilir — liste kisa kalir.
                    if (isSelected) {
                        if (t.reasons.isEmpty()) {
                            // Sebep tanimli degil: tip tek basina gecerli bir durumdur.
                            Surface(
                                onClick = { onSelect(t.value, null) },
                                shape = MaterialTheme.shapes.small,
                                modifier = Modifier.fillMaxWidth().padding(start = 16.dp, top = 2.dp, bottom = 6.dp),
                            ) {
                                Text(
                                    "Sebepsiz devam et",
                                    style = MaterialTheme.typography.bodySmall,
                                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                                )
                            }
                        } else {
                            t.reasons.forEach { r ->
                                Surface(
                                    onClick = { onSelect(t.value, r.id) },
                                    shape = MaterialTheme.shapes.small,
                                    modifier = Modifier.fillMaxWidth().padding(start = 16.dp, top = 2.dp, bottom = 2.dp),
                                ) {
                                    Text(
                                        r.name,
                                        style = MaterialTheme.typography.bodyMedium,
                                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                                    )
                                }
                            }
                            Spacer(Modifier.height(6.dp))
                        }
                    }
                }
            }
        },
        confirmButton = {},
        dismissButton = {
            Row {
                if (active != null) {
                    TextButton(onClick = onEnd, enabled = enabled) { Text("Durumu Bitir") }
                }
                TextButton(onClick = onDismiss) { Text("Vazgeç") }
            }
        },
    )
}

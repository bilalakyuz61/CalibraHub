package com.calibrahub.mobile.ui.warehouse

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.StockDocLineRequest
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.data.WarehouseLocationDto
import com.calibrahub.mobile.data.WidgetFieldDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.widgets.DynamicFieldsSection
import com.calibrahub.mobile.ui.widgets.dynamicFieldsPayload
import com.calibrahub.mobile.ui.widgets.validateDynamicFields
import kotlinx.coroutines.launch

/** Lokasyon seçim diyaloğunun hangi alanı hedeflediğini ayırt eder (aynı dialog, iki hedef). */
private enum class TransferLocationTarget { FROM, TO }

/** Dinamik ek saha şeması için GET /api/mobile/widgets/schema?formCode= sorgusunda kullanılan
 * SABİT kod — TransferScreen'in tek modu var. */
private const val TRANSFER_WIDGET_FORM_CODE = "TRANSFER"

private data class TransferLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val quantity: Double,
    /** Lot-takipli kalemde tasinacak lot; takipsizde null. */
    val lotNo: String? = null,
    /** Seri-takipli kalemde tasinacak seriler (kaynak lokasyondaki mevcut serilerden secilir). */
    val serials: List<String> = emptyList(),
)

/**
 * Depo Transfer ekranı — CalibraHubAndroid `ui/warehouse/TransferScreen.kt` ile BİREBİR sadık
 * port (bir lokasyondan diğerine kalem taşıma belgesi).
 *
 * Akış: Kaynak + Hedef lokasyon seç (aynı seçim diyaloğu iki hedefe hizmet eder) →
 * [MaterialPickerField] ile malzeme ara/seç → miktar gir → satıra ekle → (opsiyonel not) →
 * Kaydet = POST transfer. Kaynak==Hedef istemci tarafında engellenir; sunucu asıl karar mercii
 * olarak kalır. Başarı burada AlertDialog değil, "documentNumber'lı snackbar + form temizleme"
 * ile bildirilir (lokasyonlar korunur, yalnız kalemler/not/malzeme arama alanı sıfırlanır).
 *
 * Lokasyon seçici kartı [StockDocScreen]'in `LocationSelectorCard`'ıyla (`internal`, aynı görünüm)
 * PAYLAŞILIR — Android'in ayrı dosyada aynı içerikli `TransferLocationCard` kopyasını tekrar
 * yazmak yerine (CLAUDE.md DRY kuralı).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TransferScreen(session: SessionManager, onBack: () -> Unit) {
    val repo = session.warehouseRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var locations by remember { mutableStateOf<List<WarehouseLocationDto>?>(null) }
    var locationsError by remember { mutableStateOf<String?>(null) }
    var locationsAttempt by remember { mutableStateOf(0) }

    var fromLocationId by rememberSaveable { mutableStateOf<Int?>(null) }
    var toLocationId by rememberSaveable { mutableStateOf<Int?>(null) }
    var pickerTarget by remember { mutableStateOf<TransferLocationTarget?>(null) }

    val fromLocation = locations?.firstOrNull { it.id == fromLocationId }
    val toLocation = locations?.firstOrNull { it.id == toLocationId }
    val sameLocation = fromLocationId != null && fromLocationId == toLocationId

    LaunchedEffect(locationsAttempt) {
        locations = null
        locationsError = null
        repo.locations().fold(
            onSuccess = { locations = it },
            onFailure = { locationsError = it.message ?: "Lokasyonlar yüklenemedi" },
        )
    }

    var code by rememberSaveable { mutableStateOf("") }
    var resolved by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by rememberSaveable { mutableStateOf("") }

    // Lot/seri girisi — satir eklenince TransferLineUi'ye tasinir ve sifirlanir.
    var lotNo by remember { mutableStateOf("") }
    var serials by remember { mutableStateOf(listOf<String>()) }
    var showSerialPicker by remember { mutableStateOf(false) }

    var lines by remember { mutableStateOf(listOf<TransferLineUi>()) }
    var note by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }

    var widgetSchema by remember { mutableStateOf<List<WidgetFieldDto>>(emptyList()) }
    var widgetValues by remember { mutableStateOf<Map<String, String>>(emptyMap()) }
    var widgetValidationFailed by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        repo.getWidgetSchema(TRANSFER_WIDGET_FORM_CODE).fold(
            onSuccess = { widgetSchema = it },
            onFailure = { widgetSchema = emptyList() },
        )
    }

    val qtyValue = qtyText.trim().replace(',', '.').toDoubleOrNull()
    val qtyValid = qtyValue != null && qtyValue > 0.0

    fun addLine() {
        val item = resolved ?: return
        val qty = qtyValue
        if (qty == null || qty <= 0.0 || saving) return

        // Erken geri bildirim — asil kural sunucuda (SqlStockDocRepository). Transfer bir
        // CIKIS+GIRIS ciftidir: takipli malzemede hangi lot/serinin tasindigi bilinmeli.
        val tracking = trackingTypeFromString(item.trackingType)
        when {
            tracking == ItemTrackingType.LOT && lotNo.isBlank() -> {
                resolveError = "Bu malzeme lot takipli — taşınacak lot seçilmeli."
                return
            }
            tracking == ItemTrackingType.SERIAL && serials.size != qty.toInt() -> {
                resolveError = "Bu malzeme seri takipli — miktar kadar (${qty.toInt()}) seri seçilmeli, " +
                    "şu an ${serials.size} adet."
                return
            }
        }

        lines = lines + TransferLineUi(
            itemId = item.itemId,
            itemCode = item.itemCode,
            itemName = item.itemName,
            unit = item.unit,
            quantity = qty,
            lotNo = lotNo.trim().takeIf { it.isNotBlank() },
            serials = serials,
        )
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        lotNo = ""
        serials = emptyList()
    }

    fun resetForm() {
        lines = emptyList()
        note = ""
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        widgetValues = emptyMap()
        widgetValidationFailed = false
    }

    fun save() {
        val from = fromLocation ?: return
        val to = toLocation ?: return
        if (from.id == to.id || lines.isEmpty() || saving) return
        if (!validateDynamicFields(widgetSchema, widgetValues)) {
            widgetValidationFailed = true
            scope.launch { snackbarHostState.showSnackbar("Ek sahalarda zorunlu alanlar var. Lütfen doldurun.") }
            return
        }
        scope.launch {
            saving = true
            val reqLines = lines.map {
                StockDocLineRequest(
                    itemId = it.itemId,
                    quantity = it.quantity,
                    lotNo = it.lotNo,
                    serials = it.serials.takeIf { s -> s.isNotEmpty() },
                )
            }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            val extraFields = dynamicFieldsPayload(widgetValues)
            val result = repo.transfer(from.id, to.id, reqLines, noteOrNull, extraFields)
            result.fold(
                onSuccess = { res ->
                    resetForm()
                    scope.launch {
                        snackbarHostState.showSnackbar("Transfer belgesi oluşturuldu (${res.documentNumber})")
                        if (res.extraFieldsError != null) {
                            snackbarHostState.showSnackbar("Ek sahalar kaydedilemedi: ${res.extraFieldsError}")
                        }
                    }
                },
                onFailure = { failure ->
                    scope.launch { snackbarHostState.showSnackbar(failure.message ?: "Kaydetme başarısız") }
                },
            )
            saving = false
        }
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = { Text("Transfer") },
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
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            when {
                locationsError != null -> LocationsErrorCard(
                    message = locationsError!!,
                    onRetry = { locationsAttempt++ },
                )
                locations == null -> Box(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
                    contentAlignment = Alignment.Center,
                ) { CircularProgressIndicator(modifier = Modifier.size(28.dp)) }
                locations!!.isEmpty() -> Text(
                    text = "Seçilebilir aktif lokasyon bulunamadı.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.error,
                )
                else -> {
                    LocationSelectorCard(
                        label = "Kaynak Lokasyon",
                        selected = fromLocation,
                        enabled = !saving,
                        onClick = { pickerTarget = TransferLocationTarget.FROM },
                    )
                    LocationSelectorCard(
                        label = "Hedef Lokasyon",
                        selected = toLocation,
                        enabled = !saving,
                        onClick = { pickerTarget = TransferLocationTarget.TO },
                    )
                    if (sameLocation) {
                        Text(
                            text = "Kaynak ve hedef lokasyon aynı olamaz.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                }
            }

            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
                    Text(
                        text = "Kalem Ekle",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Spacer(Modifier.height(10.dp))

                    MaterialPickerField(
                        query = code,
                        onQueryChange = {
                            code = it
                            resolved = null
                            resolveError = null
                        },
                        onResolved = { dto ->
                            resolved = dto
                            resolveError = null
                        },
                        onResolveError = { msg ->
                            resolved = null
                            resolveError = msg
                        },
                        repo = repo,
                        enabled = !saving,
                        modifier = Modifier.fillMaxWidth(),
                    )

                    if (resolveError != null) {
                        Spacer(Modifier.height(8.dp))
                        Text(
                            text = resolveError!!,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }

                    val item = resolved
                    if (item != null) {
                        Spacer(Modifier.height(12.dp))
                        Text(
                            text = item.itemName,
                            style = MaterialTheme.typography.bodyLarge,
                            fontWeight = FontWeight.SemiBold,
                        )
                        Text(
                            text = item.itemCode +
                                (item.unit?.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )

                        val from = fromLocation
                        if (from != null) {
                            val bal = item.balances.firstOrNull { it.locationId == from.id }?.quantity ?: 0.0
                            val warn = bal <= 0.0
                            Spacer(Modifier.height(4.dp))
                            Text(
                                text = "${from.name} bakiyesi: " + formatQty(bal) +
                                    (item.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                                style = MaterialTheme.typography.bodySmall,
                                color = if (warn) MaterialTheme.colorScheme.error
                                        else MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }

                        Spacer(Modifier.height(10.dp))
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            OutlinedTextField(
                                value = qtyText,
                                onValueChange = { qtyText = it },
                                label = {
                                    Text("Miktar" +
                                        (item.unit?.takeIf { it.isNotBlank() }?.let { " ($it)" } ?: ""))
                                },
                                singleLine = true,
                                enabled = !saving,
                                isError = qtyText.isNotBlank() && !qtyValid,
                                keyboardOptions = KeyboardOptions(
                                    keyboardType = KeyboardType.Decimal,
                                    imeAction = ImeAction.Done,
                                ),
                                keyboardActions = KeyboardActions(onDone = { addLine() }),
                                modifier = Modifier.weight(1f),
                            )
                            Spacer(Modifier.width(8.dp))
                            FilledIconButton(
                                onClick = { addLine() },
                                enabled = qtyValid && !saving,
                            ) {
                                Icon(Icons.Default.Add, contentDescription = "Satıra ekle")
                            }
                        }

                        // ── Lot / Seri — transfer KAYNAK lokasyondan cikis gibidir:
                        // mevcut lotlar onerilir, seriler stoktakilerden secilir.
                        val trackingUi = trackingTypeFromString(item.trackingType)
                        if (trackingUi == ItemTrackingType.LOT) {
                            LotInputRow(
                                itemId = item.itemId,
                                value = lotNo,
                                enabled = !saving,
                                isSales = true,          // mevcut lotlari oner (kaynakta var olan)
                                repo = repo,
                                onValueChange = { lotNo = it },
                            )
                        } else if (trackingUi == ItemTrackingType.SERIAL) {
                            SalesSerialTrackingRow(
                                selectedSerials = serials,
                                targetQuantity = qtyValue?.toInt() ?: 0,
                                enabled = !saving,
                                onOpenPicker = { showSerialPicker = true },
                            )
                        }
                    }
                }
            }

            Text(
                text = "Kalemler (${lines.size})",
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold,
            )
            if (lines.isEmpty()) {
                Text(
                    text = "Henüz kalem eklenmedi. Yukarıdan malzeme arayıp ekleyin.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                lines.forEachIndexed { index, line ->
                    TransferLineRow(
                        line = line,
                        enabled = !saving,
                        onDelete = { lines = lines.filterIndexed { i, _ -> i != index } },
                    )
                }
            }

            OutlinedTextField(
                value = note,
                onValueChange = { note = it },
                label = { Text("Not (opsiyonel)") },
                enabled = !saving,
                minLines = 2,
                maxLines = 4,
                modifier = Modifier.fillMaxWidth(),
            )

            if (widgetSchema.isNotEmpty()) {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
                        Text(
                            text = "Ek Sahalar",
                            style = MaterialTheme.typography.titleSmall,
                            fontWeight = FontWeight.SemiBold,
                        )
                        Spacer(Modifier.height(10.dp))
                        DynamicFieldsSection(
                            schema = widgetSchema,
                            values = widgetValues,
                            onChange = { key, value -> widgetValues = widgetValues + (key to value) },
                            enabled = !saving,
                            showRequiredErrors = widgetValidationFailed,
                        )
                    }
                }
            }

            Button(
                onClick = { save() },
                enabled = fromLocation != null && toLocation != null && !sameLocation &&
                    lines.isNotEmpty() && !saving,
                modifier = Modifier.fillMaxWidth(),
            ) {
                if (saving) CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    color = MaterialTheme.colorScheme.onPrimary,
                )
                else Text("Kaydet")
            }

            Spacer(Modifier.height(8.dp))
        }
    }

    // Kaynak lokasyondaki mevcut serilerden secim — giris/cikis ve irsaliyedeki AYNI diyalog.
    if (showSerialPicker) {
        val pickItem = resolved
        if (pickItem == null) {
            showSerialPicker = false
        } else {
            SerialSelectionDialog(
                itemId = pickItem.itemId,
                itemName = pickItem.itemName,
                itemCode = pickItem.itemCode,
                targetQuantity = qtyValue?.toInt() ?: 0,
                initiallySelected = serials,
                repo = repo,
                onDismiss = { showSerialPicker = false },
                onConfirm = { picked ->
                    serials = picked
                    showSerialPicker = false
                },
            )
        }
    }

    if (pickerTarget != null) {
        val target = pickerTarget!!
        val list = locations.orEmpty()
        val currentSelectedId = if (target == TransferLocationTarget.FROM) fromLocationId else toLocationId
        AlertDialog(
            onDismissRequest = { pickerTarget = null },
            title = {
                Text(if (target == TransferLocationTarget.FROM) "Kaynak Lokasyon Seçin" else "Hedef Lokasyon Seçin")
            },
            text = {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 380.dp)
                        .verticalScroll(rememberScrollState()),
                ) {
                    list.forEach { loc ->
                        val isSelected = loc.id == currentSelectedId
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    if (target == TransferLocationTarget.FROM) fromLocationId = loc.id
                                    else toLocationId = loc.id
                                    pickerTarget = null
                                }
                                .padding(vertical = 12.dp, horizontal = 4.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Icon(
                                Icons.Default.LocationOn,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(20.dp),
                            )
                            Spacer(Modifier.width(10.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(loc.name, style = MaterialTheme.typography.bodyLarge)
                                if (loc.code.isNotBlank() && loc.code != loc.name) {
                                    Text(
                                        text = loc.code,
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    )
                                }
                            }
                            if (isSelected) {
                                Icon(
                                    Icons.Default.Check,
                                    contentDescription = "Seçili",
                                    tint = MaterialTheme.colorScheme.primary,
                                )
                            }
                        }
                    }
                }
            },
            confirmButton = {},
            dismissButton = {
                TextButton(onClick = { pickerTarget = null }) { Text("Vazgeç") }
            },
        )
    }
}

@Composable
private fun TransferLineRow(line: TransferLineUi, enabled: Boolean, onDelete: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 12.dp, end = 4.dp, top = 8.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(line.itemName, style = MaterialTheme.typography.bodyLarge)
                Text(
                    text = line.itemCode,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Text(
                text = formatQty(line.quantity) +
                    (line.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )
            IconButton(onClick = onDelete, enabled = enabled) {
                Icon(
                    Icons.Default.Delete,
                    contentDescription = "Satırı sil",
                    tint = MaterialTheme.colorScheme.error,
                )
            }
        }
    }
}

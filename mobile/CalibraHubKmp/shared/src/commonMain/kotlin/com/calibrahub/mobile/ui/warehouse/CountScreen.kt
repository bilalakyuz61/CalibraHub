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
import androidx.compose.material.icons.filled.Checklist
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.CountLotItem
import com.calibrahub.mobile.data.CountSerialItem
import com.calibrahub.mobile.data.InventoryCountLineRequest
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.data.WarehouseLocationDto
import com.calibrahub.mobile.data.WidgetFieldDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.widgets.DynamicFieldsSection
import com.calibrahub.mobile.ui.widgets.dynamicFieldsPayload
import com.calibrahub.mobile.ui.widgets.validateDynamicFields
import kotlinx.coroutines.launch

/** Dinamik ek saha şeması için GET /api/mobile/widgets/schema?formCode= sorgusunda kullanılan
 * SABİT kod — CountScreen'in tek modu var. */
private const val COUNT_WIDGET_FORM_CODE = "INVENTORY_COUNT"

private data class CountLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val systemQuantity: Double,
    val countedQuantity: Double,
    /** "None" | "Lot" | "Serial" — gonderimde hangi kirilim alanina yazilacagini belirler. */
    val trackingType: String = "None",
    /** Lot/seri kirilimi (takipsizde bos). Toplami [countedQuantity]'ye esit olmali. */
    val breakdown: List<CountBreakdownRow> = emptyList(),
)

/** Taslak (applied=false) kaydedilmiş sayım belgesinin Sayım Yansıt dialoğu için tuttuğu kimlik. */
private data class DraftCountResult(val id: Int, val documentNumber: String, val extraFieldsError: String? = null)

/**
 * Depo Sayım ekranı — CalibraHubAndroid `ui/warehouse/CountScreen.kt` ile BİREBİR sadık port
 * (tek lokasyon için fiziksel sayım belgesi + Sayım Yansıt akışı).
 *
 * Akış: Lokasyon seç → [MaterialPickerField] ile malzeme ara/seç → SAYILAN miktarı gir (0
 * GEÇERLİ — "raf boş" sayımı) → satıra ekle (sistem bakiyesi + sayılan + fark client-side
 * gösterilir) → opsiyonel not → Kaydet = POST inventory-count. Yanıttaki `applied` alanına göre:
 * true → doğrudan uygulandı (snackbar); false → taslak kaldı, "Sayım Yansıt" dialoğu açılır.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CountScreen(session: SessionManager, onBack: () -> Unit) {
    val repo = session.warehouseRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var locations by remember { mutableStateOf<List<WarehouseLocationDto>?>(null) }
    var locationsError by remember { mutableStateOf<String?>(null) }
    var locationsAttempt by remember { mutableStateOf(0) }

    var locationId by rememberSaveable { mutableStateOf<Int?>(null) }
    var showLocationPicker by remember { mutableStateOf(false) }

    val selectedLocation = locations?.firstOrNull { it.id == locationId }

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

    // Secili malzemenin lot/seri kirilimi — satir eklenince CountLineUi'ye tasinir.
    var breakdown by remember { mutableStateOf(listOf<CountBreakdownRow>()) }

    var lines by remember { mutableStateOf(listOf<CountLineUi>()) }
    var note by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }

    var draftResult by remember { mutableStateOf<DraftCountResult?>(null) }
    var applying by remember { mutableStateOf(false) }
    var applyError by remember { mutableStateOf<String?>(null) }

    var widgetSchema by remember { mutableStateOf<List<WidgetFieldDto>>(emptyList()) }
    var widgetValues by remember { mutableStateOf<Map<String, String>>(emptyMap()) }
    var widgetValidationFailed by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        repo.getWidgetSchema(COUNT_WIDGET_FORM_CODE).fold(
            onSuccess = { widgetSchema = it },
            onFailure = { widgetSchema = emptyList() },
        )
    }

    val qtyValue = qtyText.trim().replace(',', '.').toDoubleOrNull()
    val qtyValid = qtyValue != null && qtyValue >= 0.0

    fun systemQtyFor(dto: StockQueryDto): Double {
        val loc = selectedLocation ?: return 0.0
        return dto.balances.firstOrNull { it.locationId == loc.id }?.quantity ?: 0.0
    }

    fun addLine() {
        val item = resolved ?: return
        val qty = qtyValue
        if (qty == null || qty < 0.0 || saving) return

        // Erken geri bildirim — asil kural sunucuda. Sayimda kirilim TOPLAMI sayilan miktara
        // ESIT olmali; "0 sayildi" (raf bos) durumunda kirilim beklenmez.
        val tracking = trackingTypeFromString(item.trackingType)
        if (tracking != ItemTrackingType.NONE && qty > 0.0) {
            val total = breakdown.sumOf { it.qty }
            if (breakdown.isEmpty()) {
                resolveError = if (tracking == ItemTrackingType.LOT)
                    "Bu malzeme lot takipli — lot kırılımı girilmeli."
                else "Bu malzeme seri takipli — seri kırılımı girilmeli."
                return
            }
            if (kotlin.math.abs(total - qty) > 0.0001) {
                resolveError = "Kırılım toplamı (${formatQty(total)}) sayılan miktara " +
                    "(${formatQty(qty)}) eşit olmalı."
                return
            }
        }

        lines = lines + CountLineUi(
            itemId = item.itemId,
            itemCode = item.itemCode,
            itemName = item.itemName,
            unit = item.unit,
            systemQuantity = systemQtyFor(item),
            countedQuantity = qty,
            trackingType = item.trackingType,
            breakdown = if (qty > 0.0) breakdown else emptyList(),
        )
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        breakdown = emptyList()
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
        val loc = selectedLocation ?: return
        if (lines.isEmpty() || saving) return
        if (!validateDynamicFields(widgetSchema, widgetValues)) {
            widgetValidationFailed = true
            scope.launch { snackbarHostState.showSnackbar("Ek sahalarda zorunlu alanlar var. Lütfen doldurun.") }
            return
        }
        scope.launch {
            saving = true
            val reqLines = lines.map {
                InventoryCountLineRequest(
                    itemId = it.itemId,
                    countedQuantity = it.countedQuantity,
                    // Kirilim, takip tipine gore DOGRU alana yazilir — sunucu ikisini ayri
                    // kurallarla isler (lot: Lot.ExpiryDate'e; seri: ItemSerial'a yansir).
                    lotBreakdown = if (it.trackingType == "Lot" && it.breakdown.isNotEmpty())
                        it.breakdown.map { b -> CountLotItem(lotNo = b.key, qty = b.qty) } else null,
                    serialBreakdown = if (it.trackingType == "Serial" && it.breakdown.isNotEmpty())
                        it.breakdown.map { b -> CountSerialItem(serialNo = b.key, qty = b.qty) } else null,
                )
            }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            val extraFields = dynamicFieldsPayload(widgetValues)
            val result = repo.inventoryCount(loc.id, reqLines, noteOrNull, extraFields)
            result.fold(
                onSuccess = { res ->
                    resetForm()
                    if (res.applied) {
                        scope.launch {
                            snackbarHostState.showSnackbar("Sayım kaydedildi ve uygulandı (${res.documentNumber})")
                            if (res.extraFieldsError != null) {
                                snackbarHostState.showSnackbar("Ek sahalar kaydedilemedi: ${res.extraFieldsError}")
                            }
                        }
                    } else {
                        draftResult = DraftCountResult(
                            id = res.id,
                            documentNumber = res.documentNumber,
                            extraFieldsError = res.extraFieldsError,
                        )
                    }
                },
                onFailure = { failure ->
                    scope.launch { snackbarHostState.showSnackbar(failure.message ?: "Kaydetme başarısız") }
                },
            )
            saving = false
        }
    }

    fun dismissDraftDialog() {
        draftResult = null
        applyError = null
    }

    fun applyDraft() {
        val draft = draftResult ?: return
        if (applying) return
        scope.launch {
            applying = true
            applyError = null
            repo.applyInventoryCount(draft.id).fold(
                onSuccess = { res ->
                    draftResult = null
                    scope.launch { snackbarHostState.showSnackbar("Yansıtıldı (${res.writtenCount} satır yazıldı)") }
                },
                onFailure = { failure -> applyError = failure.message ?: "Yansıtma başarısız" },
            )
            applying = false
        }
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = { Text("Sayım") },
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
                else -> LocationSelectorCard(
                    label = "Sayım Lokasyonu",
                    selected = selectedLocation,
                    enabled = !saving,
                    onClick = { showLocationPicker = true },
                )
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

                        if (selectedLocation != null) {
                            Spacer(Modifier.height(4.dp))
                            Text(
                                text = "${selectedLocation.name} sistem bakiyesi: " +
                                    formatQty(systemQtyFor(item)) +
                                    (item.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }

                        Spacer(Modifier.height(10.dp))
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            OutlinedTextField(
                                value = qtyText,
                                onValueChange = { qtyText = it },
                                label = {
                                    Text("Sayılan Miktar" +
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

                        // ── Lot / Seri kirilimi — YALNIZ takipli malzemede ve sayilan miktar
                        // sifirdan buyukse. "0 sayildi" (raf bos) durumunda kirilim anlamsizdir.
                        val trackingUi = trackingTypeFromString(item.trackingType)
                        if (trackingUi != ItemTrackingType.NONE && (qtyValue ?: 0.0) > 0.0) {
                            CountBreakdownSection(
                                isSerial = trackingUi == ItemTrackingType.SERIAL,
                                rows = breakdown,
                                countedQuantity = qtyValue ?: 0.0,
                                enabled = !saving,
                                onRowsChange = { breakdown = it },
                            )
                        }
                        Spacer(Modifier.height(4.dp))
                        Text(
                            text = "0 girilirse \"raf boş\" olarak sayılır.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
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
                    CountLineRow(
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
                enabled = selectedLocation != null && lines.isNotEmpty() && !saving,
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

    if (showLocationPicker) {
        val list = locations.orEmpty()
        AlertDialog(
            onDismissRequest = { showLocationPicker = false },
            title = { Text("Lokasyon Seçin") },
            text = {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 380.dp)
                        .verticalScroll(rememberScrollState()),
                ) {
                    list.forEach { loc ->
                        val isSelected = loc.id == locationId
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    locationId = loc.id
                                    showLocationPicker = false
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
                TextButton(onClick = { showLocationPicker = false }) { Text("Vazgeç") }
            },
        )
    }

    if (draftResult != null) {
        val draft = draftResult!!
        AlertDialog(
            onDismissRequest = { if (!applying) dismissDraftDialog() },
            icon = {
                Icon(
                    Icons.Default.Checklist,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                )
            },
            title = { Text("Sayım Taslak Kaydedildi") },
            text = {
                Column {
                    Text("Sayım taslak kaydedildi (${draft.documentNumber}). Stoğa yansıtılsın mı?")
                    if (draft.extraFieldsError != null) {
                        Spacer(Modifier.height(8.dp))
                        Text(
                            text = "Ek sahalar kaydedilemedi: ${draft.extraFieldsError}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                    if (applyError != null) {
                        Spacer(Modifier.height(8.dp))
                        Text(
                            text = applyError!!,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = { applyDraft() }, enabled = !applying) {
                    if (applying) CircularProgressIndicator(modifier = Modifier.size(16.dp))
                    else Text("Yansıt")
                }
            },
            dismissButton = {
                TextButton(onClick = { dismissDraftDialog() }, enabled = !applying) { Text("Sonra") }
            },
        )
    }
}

/** Eklenmiş sayım kalemi satırı — ad/kod + sil + alt satırda Sistem/Sayılan/Fark üçlüsü. */
@Composable
private fun CountLineRow(line: CountLineUi, enabled: Boolean, onDelete: () -> Unit) {
    val diff = line.countedQuantity - line.systemQuantity
    val diffColor = when {
        diff > 0.0 -> MaterialTheme.colorScheme.primary
        diff < 0.0 -> MaterialTheme.colorScheme.error
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    val diffText = (if (diff > 0.0) "+" else "") + formatQty(diff)

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 12.dp, end = 4.dp, top = 8.dp, bottom = 10.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(line.itemName, style = MaterialTheme.typography.bodyLarge)
                    Text(
                        text = line.itemCode,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                IconButton(onClick = onDelete, enabled = enabled) {
                    Icon(
                        Icons.Default.Delete,
                        contentDescription = "Satırı sil",
                        tint = MaterialTheme.colorScheme.error,
                    )
                }
            }
            Spacer(Modifier.height(4.dp))
            Row(
                modifier = Modifier.fillMaxWidth().padding(end = 8.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                val neutralColor = MaterialTheme.colorScheme.onSurface
                CountLineStat(label = "Sistem", value = formatQty(line.systemQuantity), unit = line.unit, valueColor = neutralColor)
                CountLineStat(label = "Sayılan", value = formatQty(line.countedQuantity), unit = line.unit, valueColor = neutralColor)
                CountLineStat(label = "Fark", value = diffText, unit = line.unit, valueColor = diffColor)
            }
        }
    }
}

@Composable
private fun CountLineStat(label: String, value: String, unit: String?, valueColor: Color) {
    Column(horizontalAlignment = Alignment.Start) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(
            text = value + (unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.SemiBold,
            color = valueColor,
        )
    }
}

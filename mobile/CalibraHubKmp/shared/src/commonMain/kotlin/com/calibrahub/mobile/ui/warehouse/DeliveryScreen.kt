package com.calibrahub.mobile.ui.warehouse

import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.material.icons.automirrored.filled.ReceiptLong
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Contacts
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
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
import com.calibrahub.mobile.data.ContactSearchDto
import com.calibrahub.mobile.data.DeliveryLineRequest
import com.calibrahub.mobile.data.DeliveryLineResultDto
import com.calibrahub.mobile.data.DeliveryResult
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.data.WidgetFieldDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.widgets.DynamicFieldsSection
import com.calibrahub.mobile.ui.widgets.dynamicFieldsPayload
import com.calibrahub.mobile.ui.widgets.validateDynamicFields
import kotlin.math.roundToInt
import kotlinx.coroutines.launch

/**
 * İrsaliye ekranının belge yönü — Alış (tedarikçiden gelen) / Satış (müşteriye giden).
 * [apiValue] sunucuya AYNEN gönderilen `docType` string'i (enum-benzeri sözleşme, backend ile
 * AYNI: "purchase" | "sales"). [widgetFormCode] dinamik ek saha şeması sorgusunda kullanılan
 * SABİT kod.
 */
enum class DeliveryDocType(
    val apiValue: String,
    val screenTitle: String,
    val successTitle: String,
    val widgetFormCode: String,
) {
    PURCHASE("purchase", "Alış İrsaliyesi", "Alış İrsaliyesi Oluşturuldu", "PURCHASE_DELIVERY_EDIT"),
    SALES("sales", "Satış İrsaliyesi", "Satış İrsaliyesi Oluşturuldu", "SALES_DELIVERY_EDIT"),
}

private data class DeliveryLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val quantity: Double,
    val serials: List<String>? = null,
    val lotCode: String? = null,
    val autoGenerateSerials: Boolean? = null,
)

/** [StockQueryDto.trackingType] gerçek alanı [trackingTypeFromString] üzerinden okunur —
 * [OpenOrderDetailScreen] ile PAYLAŞILAN dönüşüm (bkz. DeliverySerialLotSection.kt). */
private fun resolveTrackingType(item: StockQueryDto): ItemTrackingType = trackingTypeFromString(item.trackingType)

/**
 * Alış İrsaliyesi / Satış İrsaliyesi ekranı — CalibraHubAndroid `ui/warehouse/DeliveryScreen.kt`
 * ile BİREBİR sadık port (TEK composable, [docType] parametresiyle iki yönü de kapsar).
 *
 * Akış: Cari seç ([ContactPickerField], kod/ad ile ara) → [MaterialPickerField] ile malzeme
 * ara/seç → miktar gir → satıra ekle → (opsiyonel not) → Kaydet = POST delivery. Sunucu
 * satırları açık siparişlere FIFO bağlar; başarı yanıtındaki `lines[]` başarı diyaloğunda satır
 * bazında özetlenir (bkz. [buildDeliveryLinkSummary]).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeliveryScreen(session: SessionManager, docType: DeliveryDocType, onBack: () -> Unit) {
    val repo = session.warehouseRepository
    val scope = rememberCoroutineScope()

    var contactQuery by rememberSaveable { mutableStateOf("") }
    var resolvedContact by remember { mutableStateOf<ContactSearchDto?>(null) }
    var showContactPicker by remember { mutableStateOf(false) }

    var code by rememberSaveable { mutableStateOf("") }
    var resolved by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by remember { mutableStateOf("") }

    var pendingSerials by remember { mutableStateOf(listOf<String>()) }
    var pendingAutoSerial by remember { mutableStateOf(true) }
    var pendingLot by remember { mutableStateOf("") }
    var showSerialPicker by remember { mutableStateOf(false) }

    var lines by remember { mutableStateOf(listOf<DeliveryLineUi>()) }
    var note by remember { mutableStateOf("") }
    var externalRefNumber by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<String?>(null) }
    var successResult by remember { mutableStateOf<DeliveryResult?>(null) }
    var lastDocumentNumber by rememberSaveable { mutableStateOf<String?>(null) }

    var widgetSchema by remember { mutableStateOf<List<WidgetFieldDto>>(emptyList()) }
    var widgetValues by remember { mutableStateOf<Map<String, String>>(emptyMap()) }
    var widgetValidationFailed by remember { mutableStateOf(false) }

    LaunchedEffect(docType) {
        repo.getWidgetSchema(docType.widgetFormCode).fold(
            onSuccess = { widgetSchema = it },
            onFailure = { widgetSchema = emptyList() },
        )
    }

    val qtyValue = qtyText.trim().replace(',', '.').toDoubleOrNull()
    val qtyValid = qtyValue != null && qtyValue > 0.0
    val targetQtyInt = (qtyValue ?: 0.0).roundToInt()

    val serialMismatch: Boolean = run {
        val item = resolved ?: return@run false
        if (resolveTrackingType(item) != ItemTrackingType.SERIAL) return@run false
        if (docType == DeliveryDocType.SALES) {
            pendingSerials.isNotEmpty() && targetQtyInt > 0 && pendingSerials.size != targetQtyInt
        } else {
            val effectiveAuto = pendingAutoSerial && item.autoSerial
            !effectiveAuto && pendingSerials.isNotEmpty() && targetQtyInt > 0 && pendingSerials.size != targetQtyInt
        }
    }

    fun addLine() {
        val item = resolved ?: return
        val qty = qtyValue
        if (qty == null || qty <= 0.0 || saving || serialMismatch) return
        val tracking = resolveTrackingType(item)
        lines = lines + DeliveryLineUi(
            itemId = item.itemId,
            itemCode = item.itemCode,
            itemName = item.itemName,
            unit = item.unit,
            quantity = qty,
            serials = if (tracking == ItemTrackingType.SERIAL) pendingSerials else null,
            lotCode = if (tracking == ItemTrackingType.LOT) pendingLot.trim().takeIf { it.isNotBlank() } else null,
            autoGenerateSerials = if (tracking == ItemTrackingType.SERIAL && docType == DeliveryDocType.PURCHASE)
                pendingAutoSerial else null,
        )
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        saveError = null
        pendingSerials = emptyList()
        pendingAutoSerial = true
        pendingLot = ""
    }

    fun resetForNewDoc() {
        lines = emptyList()
        note = ""
        externalRefNumber = ""
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        saveError = null
        pendingSerials = emptyList()
        pendingAutoSerial = true
        pendingLot = ""
        widgetValues = emptyMap()
        widgetValidationFailed = false
    }

    fun save() {
        val contact = resolvedContact ?: return
        if (lines.isEmpty() || saving) return
        if (!validateDynamicFields(widgetSchema, widgetValues)) {
            widgetValidationFailed = true
            saveError = "Ek sahalarda zorunlu alanlar var. Lütfen doldurun."
            return
        }
        scope.launch {
            saving = true
            saveError = null
            val reqLines = lines.map {
                DeliveryLineRequest(
                    itemId = it.itemId,
                    quantity = it.quantity,
                    serials = it.serials,
                    lotCode = it.lotCode,
                    autoGenerateSerials = it.autoGenerateSerials,
                )
            }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            val refOrNull = if (docType == DeliveryDocType.PURCHASE)
                externalRefNumber.trim().takeIf { it.isNotBlank() } else null
            val extraFields = dynamicFieldsPayload(widgetValues)
            repo.delivery(docType.apiValue, contact.id, reqLines, noteOrNull, refOrNull, extraFields = extraFields).fold(
                onSuccess = {
                    successResult = it
                    lastDocumentNumber = it.documentNumber
                },
                onFailure = { saveError = it.message ?: "Kaydetme başarısız" },
            )
            saving = false
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(docType.screenTitle) },
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
            if (lastDocumentNumber != null) {
                InfoBadge(text = "Son Belge: $lastDocumentNumber", icon = Icons.AutoMirrored.Filled.ReceiptLong)
            }

            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
                    Text(
                        text = "Cari",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Spacer(Modifier.height(10.dp))

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        ContactPickerField(
                            query = contactQuery,
                            onQueryChange = {
                                contactQuery = it
                                resolvedContact = null
                            },
                            onSelected = { dto -> resolvedContact = dto },
                            repo = repo,
                            enabled = !saving,
                            modifier = Modifier.weight(1f),
                        )
                        Spacer(Modifier.width(8.dp))
                        FilledTonalIconButton(
                            onClick = { showContactPicker = true },
                            enabled = !saving,
                        ) {
                            Icon(Icons.Default.Contacts, contentDescription = "Cari rehberinden seç")
                        }
                    }

                    val contact = resolvedContact
                    if (contact != null) {
                        Spacer(Modifier.height(10.dp))
                        Text(contact.name, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                        Text(
                            text = contact.code,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }

                    if (docType == DeliveryDocType.PURCHASE) {
                        Spacer(Modifier.height(10.dp))
                        OutlinedTextField(
                            value = externalRefNumber,
                            onValueChange = { externalRefNumber = it },
                            label = { Text("Tedarikçi İrsaliye No") },
                            placeholder = { Text("Opsiyonel") },
                            singleLine = true,
                            enabled = !saving,
                            modifier = Modifier.fillMaxWidth(),
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
                            pendingSerials = emptyList()
                            pendingAutoSerial = true
                            pendingLot = ""
                        },
                        onResolved = { dto ->
                            resolved = dto
                            resolveError = null
                            pendingAutoSerial = dto.autoSerial
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
                                enabled = qtyValid && !saving && !serialMismatch,
                            ) {
                                Icon(Icons.Default.Add, contentDescription = "Satıra ekle")
                            }
                        }

                        when (resolveTrackingType(item)) {
                            ItemTrackingType.SERIAL -> if (docType == DeliveryDocType.SALES) {
                                SalesSerialTrackingRow(
                                    selectedSerials = pendingSerials,
                                    targetQuantity = targetQtyInt,
                                    enabled = !saving,
                                    onOpenPicker = { showSerialPicker = true },
                                )
                            } else {
                                PurchaseSerialEntryRow(
                                    serials = pendingSerials,
                                    autoGenerate = pendingAutoSerial,
                                    autoGenerateAvailable = item.autoSerial,
                                    targetQuantity = targetQtyInt,
                                    enabled = !saving,
                                    onAddSerial = { pendingSerials = pendingSerials + it },
                                    onRemoveSerial = { s -> pendingSerials = pendingSerials.filterNot { it == s } },
                                    onAutoGenerateChange = { pendingAutoSerial = it },
                                )
                            }
                            ItemTrackingType.LOT -> LotInputRow(
                                itemId = item.itemId,
                                value = pendingLot,
                                enabled = !saving,
                                isSales = docType == DeliveryDocType.SALES,
                                repo = repo,
                                onValueChange = { pendingLot = it },
                            )
                            ItemTrackingType.NONE -> {}
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
                    DeliveryLineRow(
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

            if (saveError != null) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer,
                    ),
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Icon(
                            Icons.Default.ErrorOutline,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.onErrorContainer,
                            modifier = Modifier.size(20.dp),
                        )
                        Spacer(Modifier.width(8.dp))
                        Text(
                            text = saveError!!,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onErrorContainer,
                        )
                    }
                }
            }

            Button(
                onClick = { save() },
                enabled = resolvedContact != null && lines.isNotEmpty() && !saving,
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

    if (showContactPicker) {
        ContactPickerDialog(
            repo = repo,
            onDismiss = { showContactPicker = false },
            onSelected = { dto ->
                resolvedContact = dto
                contactQuery = dto.code
            },
        )
    }

    val pickerItem = resolved
    if (showSerialPicker && pickerItem != null) {
        SerialSelectionDialog(
            itemId = pickerItem.itemId,
            itemName = pickerItem.itemName,
            itemCode = pickerItem.itemCode,
            targetQuantity = targetQtyInt,
            initiallySelected = pendingSerials,
            repo = repo,
            onDismiss = { showSerialPicker = false },
            onConfirm = { picked -> pendingSerials = picked },
        )
    }

    if (successResult != null) {
        val res = successResult!!
        AlertDialog(
            onDismissRequest = {
                successResult = null
                onBack()
            },
            icon = {
                Icon(
                    Icons.Default.CheckCircle,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                )
            },
            title = { Text(docType.successTitle) },
            text = {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 420.dp)
                        .verticalScroll(rememberScrollState()),
                ) {
                    Text(
                        text = "Belge No: ${res.documentNumber}",
                        style = MaterialTheme.typography.bodyLarge,
                        fontWeight = FontWeight.SemiBold,
                    )
                    if (res.extraFieldsError != null) {
                        Spacer(Modifier.height(8.dp))
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(
                                Icons.Default.ErrorOutline,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.error,
                                modifier = Modifier.size(18.dp),
                            )
                            Spacer(Modifier.width(6.dp))
                            Text(
                                text = "Ek sahalar kaydedilemedi: ${res.extraFieldsError}",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.error,
                            )
                        }
                    }
                    Spacer(Modifier.height(10.dp))
                    if (res.lines.isEmpty()) {
                        Text(
                            text = "Kalem bağlantı bilgisi alınamadı.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    } else {
                        res.lines.forEach { lineResult ->
                            val lineUi = lines.firstOrNull { it.itemId == lineResult.itemId }
                            DeliveryLinkResultRow(
                                lineResult = lineResult,
                                itemName = lineUi?.itemName,
                                itemCode = lineUi?.itemCode,
                                unit = lineUi?.unit,
                            )
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    successResult = null
                    resetForNewDoc()
                }) { Text("Yeni Belge") }
            },
            dismissButton = {
                TextButton(onClick = {
                    successResult = null
                    onBack()
                }) { Text("Kapat") }
            },
        )
    }
}

@Composable
private fun DeliveryLineRow(line: DeliveryLineUi, enabled: Boolean, onDelete: () -> Unit) {
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
                val tag = listOfNotNull(
                    line.serials?.takeIf { it.isNotEmpty() }?.let { "${it.size} seri seçili" },
                    line.lotCode?.let { "Lot: $it" },
                    line.autoGenerateSerials?.takeIf { it }?.let { "Seri: Otomatik üret" },
                ).joinToString(" · ")
                if (tag.isNotBlank()) {
                    Text(tag, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.primary)
                }
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

/**
 * Başarı diyaloğundaki tek kalem satırı — ad/kod başlığı + [buildDeliveryLinkSummary] metni +
 * (varsa) sunucunun ATADIĞI seri/lot dökümü ([buildSerialLotSummary]). `internal` —
 * [OpenOrderDetailScreen]'in "aynı bağlama-sonucu dialog'u" gereksinimi için PAYLAŞILIR.
 */
@Composable
internal fun DeliveryLinkResultRow(
    lineResult: DeliveryLineResultDto,
    itemName: String?,
    itemCode: String?,
    unit: String?,
) {
    Column(modifier = Modifier.fillMaxWidth().padding(bottom = 10.dp)) {
        Text(
            text = itemName ?: "Kalem #${lineResult.itemId}",
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.SemiBold,
        )
        if (itemCode != null) {
            Text(
                text = itemCode,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Spacer(Modifier.height(2.dp))
        Text(
            text = buildDeliveryLinkSummary(lineResult, unit),
            style = MaterialTheme.typography.bodySmall,
            color = if (lineResult.unlinkedQuantity > 0.0) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.onSurfaceVariant,
        )
        val serialLotText = buildSerialLotSummary(lineResult)
        if (serialLotText != null) {
            Spacer(Modifier.height(2.dp))
            Text(
                text = serialLotText,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/** Bağlama özetini TEK satıra indirger. `internal` — [DeliveryLinkResultRow] ile birlikte
 * [OpenOrderDetailScreen]'e PAYLAŞILIR. */
internal fun buildDeliveryLinkSummary(result: DeliveryLineResultDto, unit: String?): String {
    val unitSuffix = unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""
    val linkedPart = if (result.linked.isNotEmpty()) {
        "→ " + result.linked.joinToString(", ") { "${it.orderNumber}'e ${formatQty(it.quantity)}$unitSuffix" } + " bağlandı"
    } else null
    val unlinkedPart = if (result.unlinkedQuantity > 0.0) {
        "${formatQty(result.unlinkedQuantity)}$unitSuffix bağlantısız"
    } else null
    return listOfNotNull(linkedPart, unlinkedPart).joinToString("; ").ifBlank { "Bağlantı bilgisi yok" }
}

/** Sunucunun (otomatik veya manuel) ATADIĞI seri/lot dökümü. İkisi de boşsa null. */
internal fun buildSerialLotSummary(result: DeliveryLineResultDto): String? {
    val parts = mutableListOf<String>()
    if (result.serials.isNotEmpty()) {
        val shown = result.serials.take(3).joinToString(", ")
        parts += if (result.serials.size > 3) "Seri: $shown +${result.serials.size - 3} tane daha" else "Seri: $shown"
    }
    result.lotCode?.takeIf { it.isNotBlank() }?.let { parts += "Lot: $it" }
    return parts.joinToString(" · ").ifBlank { null }
}

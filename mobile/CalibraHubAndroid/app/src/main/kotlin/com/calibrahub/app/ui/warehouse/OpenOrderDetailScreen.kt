package com.calibrahub.app.ui.warehouse

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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.DeliveryLineRequest
import com.calibrahub.app.data.DeliveryResult
import com.calibrahub.app.data.OpenOrderDetailDto
import com.calibrahub.app.data.WarehouseRepository
import kotlinx.coroutines.launch
import kotlin.math.roundToInt

/**
 * FAZ C(a) — Açık Sipariş detayı → Teslim Et (2026-07-17, koordinatör FINAL kontrat).
 *
 * [docType] navigasyondan taşınır (OpenOrderDetailDto sözleşmesinde YOK — OpenOrderListScreen
 * zaten docType'a göre filtrelenmiş bir listeden geldiği için buraya path argümanı olarak
 * aktarılır, bkz. MainActivity "warehouse_open_order_detail/{docType}/{id}").
 *
 * DeliveryScreen'den kasıtlı FARK: kalemler [MaterialPickerField] ile aranıp eklenmez — sipariş
 * satırları zaten bellidir, her biri KENDİ düzenlenebilir "Teslim Miktarı" alanına sahiptir
 * (varsayılan = openQuantity, 0 = bu satırı teslim etme, openQuantity üzeri client-side reddedilir).
 * Seri/Lot takipli satırlarda DeliveryScreen'deki İLE BİREBİR AYNI bileşenler (SalesSerialTrackingRow/
 * SerialSelectionDialog/PurchaseSerialEntryRow/LotInputRow) satır bazında tekrar kullanılır.
 *
 * "Teslim Et" mevcut delivery POST'unu [preferredOrderId] ile çağırır — aynı [DeliveryResult]/
 * [DeliveryLineResultDto] response'u, aynı bağlama-sonucu diyaloğu (DeliveryLinkResultRow,
 * DeliveryScreen'den `internal` olarak PAYLAŞILDI).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OpenOrderDetailScreen(orderId: Int, docType: DeliveryDocType, onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository
    val scope = rememberCoroutineScope()

    var detail by remember { mutableStateOf<OpenOrderDetailDto?>(null) }
    var loading by remember { mutableStateOf(true) }
    var loadError by remember { mutableStateOf<String?>(null) }
    var lineStates by remember { mutableStateOf(listOf<OpenOrderLineEditUi>()) }
    var activeSerialPickerLineId by remember { mutableStateOf<Int?>(null) }

    var note by remember { mutableStateOf("") }
    var externalRefNumber by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<String?>(null) }
    var successResult by remember { mutableStateOf<DeliveryResult?>(null) }
    var loadTick by remember { mutableStateOf(0) }

    LaunchedEffect(orderId, loadTick) {
        loading = true
        loadError = null
        repo.openOrderDetail(orderId).fold(
            onSuccess = { d ->
                detail = d
                lineStates = d.lines.map { line ->
                    OpenOrderLineEditUi(
                        orderLineId = line.orderLineId,
                        itemId = line.itemId,
                        itemCode = line.itemCode,
                        itemName = line.itemName,
                        unit = line.unit,
                        orderedQuantity = line.orderedQuantity,
                        deliveredQuantity = line.deliveredQuantity,
                        openQuantity = line.openQuantity,
                        trackingType = trackingTypeFromString(line.trackingType),
                        autoSerial = line.autoSerial,
                        quantityText = formatDeliveryQty(line.openQuantity),
                        pendingAutoSerial = line.autoSerial
                    )
                }
            },
            onFailure = { loadError = it.message ?: "Sipariş yüklenemedi" }
        )
        loading = false
    }

    fun updateLine(orderLineId: Int, transform: (OpenOrderLineEditUi) -> OpenOrderLineEditUi) {
        lineStates = lineStates.map { if (it.orderLineId == orderLineId) transform(it) else it }
    }

    // Satır bazlı seri-adet ön-kontrolü — DeliveryScreen.serialMismatch ile AYNI formül.
    fun lineMismatch(line: OpenOrderLineEditUi): Boolean {
        if (line.trackingType != ItemTrackingType.SERIAL) return false
        val qty = line.quantityText.trim().replace(',', '.').toDoubleOrNull()?.roundToInt() ?: 0
        return if (docType == DeliveryDocType.SALES) {
            line.serials.isNotEmpty() && qty > 0 && line.serials.size != qty
        } else {
            val effectiveAuto = line.pendingAutoSerial && line.autoSerial
            !effectiveAuto && line.serials.isNotEmpty() && qty > 0 && line.serials.size != qty
        }
    }

    fun lineQuantityInvalid(line: OpenOrderLineEditUi): Boolean {
        if (line.quantityText.isBlank()) return false
        val qty = line.quantityText.trim().replace(',', '.').toDoubleOrNull()
        return qty == null || qty < 0.0 || qty > line.openQuantity + 0.0001
    }

    val anyMismatch = lineStates.any { lineMismatch(it) }
    val anyQuantityInvalid = lineStates.any { lineQuantityInvalid(it) }
    val selectedTotal = lineStates.sumOf { it.quantityText.trim().replace(',', '.').toDoubleOrNull() ?: 0.0 }
    val canDeliver = detail != null && !saving && !anyMismatch && !anyQuantityInvalid && selectedTotal > 0.0

    fun deliver() {
        val d = detail ?: return
        if (!canDeliver) return
        scope.launch {
            saving = true
            saveError = null
            val reqLines = lineStates.mapNotNull { line ->
                val qty = line.quantityText.trim().replace(',', '.').toDoubleOrNull() ?: 0.0
                if (qty <= 0.0) return@mapNotNull null
                DeliveryLineRequest(
                    itemId = line.itemId,
                    quantity = qty,
                    serials = if (line.trackingType == ItemTrackingType.SERIAL) line.serials else null,
                    lotCode = if (line.trackingType == ItemTrackingType.LOT)
                        line.lotCode.trim().takeIf { it.isNotBlank() } else null,
                    autoGenerateSerials = if (line.trackingType == ItemTrackingType.SERIAL && docType == DeliveryDocType.PURCHASE)
                        line.pendingAutoSerial else null
                )
            }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            val refOrNull = if (docType == DeliveryDocType.PURCHASE) externalRefNumber.trim().takeIf { it.isNotBlank() } else null
            repo.delivery(
                docType = docType.apiValue,
                contactId = d.contactId,
                lines = reqLines,
                note = noteOrNull,
                externalRefNumber = refOrNull,
                preferredOrderId = orderId
            ).fold(
                onSuccess = { successResult = it },
                onFailure = { saveError = it.message ?: "Teslimat başarısız" }
            )
            saving = false
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(detail?.number ?: "Sipariş Detayı") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                }
            )
        }
    ) { padding ->
        when {
            loading -> Box(
                modifier = Modifier.padding(padding).fillMaxSize(),
                contentAlignment = Alignment.Center
            ) { CircularProgressIndicator() }

            loadError != null -> Box(modifier = Modifier.padding(padding).fillMaxSize().padding(24.dp)) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.fillMaxWidth()) {
                    Icon(Icons.Default.ErrorOutline, contentDescription = null, tint = MaterialTheme.colorScheme.error, modifier = Modifier.size(40.dp))
                    Spacer(Modifier.height(12.dp))
                    Text(loadError!!, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error)
                    Spacer(Modifier.height(12.dp))
                    OutlinedButton(onClick = { loadTick++ }) { Text("Tekrar Dene") }
                }
            }

            detail != null -> {
                val d = detail!!
                Column(
                    modifier = Modifier
                        .padding(padding)
                        .fillMaxSize()
                        .verticalScroll(rememberScrollState())
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(14.dp)
                ) {
                    Card(modifier = Modifier.fillMaxWidth()) {
                        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
                            Text(d.contactName, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                            Text(
                                text = formatIsoDate(d.date),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            if (docType == DeliveryDocType.PURCHASE) {
                                Spacer(Modifier.height(10.dp))
                                OutlinedTextField(
                                    value = externalRefNumber,
                                    onValueChange = { externalRefNumber = it },
                                    label = { Text("Tedarikçi İrsaliye No") },
                                    placeholder = { Text("Opsiyonel") },
                                    singleLine = true,
                                    enabled = !saving,
                                    modifier = Modifier.fillMaxWidth()
                                )
                            }
                        }
                    }

                    Text(
                        text = "Kalemler (${lineStates.size})",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold
                    )
                    lineStates.forEach { line ->
                        OpenOrderLineCard(
                            line = line,
                            docType = docType,
                            enabled = !saving,
                            repo = repo,
                            onQuantityChange = { text -> updateLine(line.orderLineId) { it.copy(quantityText = text) } },
                            onSerialsChange = { s -> updateLine(line.orderLineId) { it.copy(serials = s) } },
                            onAutoSerialChange = { a -> updateLine(line.orderLineId) { it.copy(pendingAutoSerial = a) } },
                            onLotChange = { l -> updateLine(line.orderLineId) { it.copy(lotCode = l) } },
                            onOpenSerialPicker = { activeSerialPickerLineId = line.orderLineId }
                        )
                    }

                    OutlinedTextField(
                        value = note,
                        onValueChange = { note = it },
                        label = { Text("Not (opsiyonel)") },
                        enabled = !saving,
                        minLines = 2,
                        maxLines = 4,
                        modifier = Modifier.fillMaxWidth()
                    )

                    if (saveError != null) {
                        Card(
                            modifier = Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth().padding(12.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Icon(
                                    Icons.Default.ErrorOutline,
                                    contentDescription = null,
                                    tint = MaterialTheme.colorScheme.onErrorContainer,
                                    modifier = Modifier.size(20.dp)
                                )
                                Spacer(Modifier.width(8.dp))
                                Text(
                                    text = saveError!!,
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = MaterialTheme.colorScheme.onErrorContainer
                                )
                            }
                        }
                    }

                    Button(onClick = { deliver() }, enabled = canDeliver, modifier = Modifier.fillMaxWidth()) {
                        if (saving) CircularProgressIndicator(
                            modifier = Modifier.size(20.dp),
                            color = MaterialTheme.colorScheme.onPrimary
                        )
                        else Text("Teslim Et")
                    }

                    Spacer(Modifier.height(8.dp))
                }
            }
        }
    }

    // ── Seri seçim diyaloğu — hangi satırın açtığı activeSerialPickerLineId ile izlenir ──────
    val activeLine = lineStates.firstOrNull { it.orderLineId == activeSerialPickerLineId }
    if (activeLine != null) {
        val targetQty = activeLine.quantityText.trim().replace(',', '.').toDoubleOrNull()?.roundToInt() ?: 0
        SerialSelectionDialog(
            itemId = activeLine.itemId,
            itemName = activeLine.itemName,
            itemCode = activeLine.itemCode,
            targetQuantity = targetQty,
            initiallySelected = activeLine.serials,
            repo = repo,
            onDismiss = { activeSerialPickerLineId = null },
            onConfirm = { picked -> updateLine(activeLine.orderLineId) { it.copy(serials = picked) } }
        )
    }

    // ── Başarı diyaloğu — DeliveryScreen ile AYNI bağlama-sonucu görünümü (internal paylaşım) ──
    if (successResult != null) {
        val res = successResult!!
        AlertDialog(
            onDismissRequest = {
                successResult = null
                onBack()
            },
            icon = {
                Icon(Icons.Default.CheckCircle, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            },
            title = { Text(if (docType == DeliveryDocType.SALES) "Teslimat Oluşturuldu" else "Mal Kabul Oluşturuldu") },
            text = {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 420.dp)
                        .verticalScroll(rememberScrollState())
                ) {
                    Text(
                        text = "Belge No: ${res.documentNumber}",
                        style = MaterialTheme.typography.bodyLarge,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(Modifier.height(10.dp))
                    if (res.lines.isEmpty()) {
                        Text(
                            text = "Kalem bağlantı bilgisi alınamadı.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    } else {
                        res.lines.forEach { lineResult ->
                            val src = lineStates.firstOrNull { it.itemId == lineResult.itemId }
                            DeliveryLinkResultRow(
                                lineResult = lineResult,
                                itemName = src?.itemName,
                                itemCode = src?.itemCode,
                                unit = src?.unit
                            )
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    successResult = null
                    onBack()
                }) { Text("Tamam") }
            }
        )
    }
}

/**
 * Açık sipariş kalemi düzenleme durumu — sunucudan gelen sabit bilgiler (orderLineId..autoSerial)
 * + kullanıcının doldurduğu mutable alanlar (quantityText..lotCode). [copy] ile immutable güncelleme
 * (updateLine() içinde) — DeliveryScreen'in tek-satır pending* state'inin ÇOKLU-satır karşılığı.
 */
private data class OpenOrderLineEditUi(
    val orderLineId: Int,
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val orderedQuantity: Double,
    val deliveredQuantity: Double,
    val openQuantity: Double,
    val trackingType: ItemTrackingType,
    val autoSerial: Boolean,
    val quantityText: String,
    val serials: List<String> = emptyList(),
    val pendingAutoSerial: Boolean,
    val lotCode: String = ""
)

/** Tek açık sipariş kalemi kartı — sipariş/teslim/açık miktar bilgisi + düzenlenebilir "Teslim
 * Miktarı" + (varsa) DeliveryScreen ile AYNI Seri/Lot bölümü. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun OpenOrderLineCard(
    line: OpenOrderLineEditUi,
    docType: DeliveryDocType,
    enabled: Boolean,
    repo: WarehouseRepository,
    onQuantityChange: (String) -> Unit,
    onSerialsChange: (List<String>) -> Unit,
    onAutoSerialChange: (Boolean) -> Unit,
    onLotChange: (String) -> Unit,
    onOpenSerialPicker: () -> Unit
) {
    val qtyValue = line.quantityText.trim().replace(',', '.').toDoubleOrNull()
    val qtyInvalid = line.quantityText.isNotBlank() && (qtyValue == null || qtyValue < 0.0 || qtyValue > line.openQuantity + 0.0001)
    val targetQtyInt = (qtyValue ?: 0.0).roundToInt()
    val unitSuffix = line.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Text(line.itemName, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
            Text(
                text = line.itemCode,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(4.dp))
            Text(
                text = "Sipariş: ${formatDeliveryQty(line.orderedQuantity)}$unitSuffix · " +
                    "Teslim: ${formatDeliveryQty(line.deliveredQuantity)}$unitSuffix · " +
                    "Açık: ${formatDeliveryQty(line.openQuantity)}$unitSuffix",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = line.quantityText,
                onValueChange = onQuantityChange,
                label = { Text("Teslim Miktarı$unitSuffix") },
                singleLine = true,
                enabled = enabled,
                isError = qtyInvalid,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal, imeAction = ImeAction.Done),
                modifier = Modifier.fillMaxWidth()
            )
            if (qtyInvalid) {
                Spacer(Modifier.height(4.dp))
                Text(
                    text = "Miktar 0 ile ${formatDeliveryQty(line.openQuantity)}$unitSuffix arasında olmalı.",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.error
                )
            }

            when (line.trackingType) {
                ItemTrackingType.SERIAL -> if (docType == DeliveryDocType.SALES) {
                    SalesSerialTrackingRow(
                        selectedSerials = line.serials,
                        targetQuantity = targetQtyInt,
                        enabled = enabled,
                        onOpenPicker = onOpenSerialPicker
                    )
                } else {
                    PurchaseSerialEntryRow(
                        serials = line.serials,
                        autoGenerate = line.pendingAutoSerial,
                        autoGenerateAvailable = line.autoSerial,
                        targetQuantity = targetQtyInt,
                        enabled = enabled,
                        onAddSerial = { onSerialsChange(line.serials + it) },
                        onRemoveSerial = { s -> onSerialsChange(line.serials.filterNot { it == s }) },
                        onAutoGenerateChange = onAutoSerialChange
                    )
                }
                ItemTrackingType.LOT -> LotInputRow(
                    itemId = line.itemId,
                    value = line.lotCode,
                    enabled = enabled,
                    isSales = docType == DeliveryDocType.SALES,
                    repo = repo,
                    onValueChange = onLotChange
                )
                ItemTrackingType.NONE -> {}
            }
        }
    }
}

package com.calibrahub.app.ui.warehouse

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
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
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
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.StockDocLineRequest
import com.calibrahub.app.data.StockQueryDto
import com.calibrahub.app.data.WarehouseLocationDto
import kotlinx.coroutines.launch

/** Ekranın çalıştığı belge yönü — Giriş (STOCK_IN) / Çıkış (STOCK_OUT). */
enum class StockDocMode { IN, OUT }

/**
 * Satır listesinin UI modeli. Sunucuya yalnız itemId + quantity gider
 * (StockDocLineRequest); kod/ad/birim satır kartında gösterim içindir.
 */
private data class DocLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val quantity: Double
)

/**
 * Depo Giriş / Çıkış belge oluşturma ekranı (Increment 2a) — iki mod tek composable.
 *
 * Akış: lokasyon seç → malzeme kodu gir → GET /warehouse/stock ile çöz (ad/birim/bakiye)
 * → miktar gir → satıra ekle → (opsiyonel not) → Kaydet = POST stock-in|stock-out.
 * Başarıda docNumber onay diyaloğunda gösterilir; "Yeni Belge" formu temizleyip lokasyonu
 * korur, "Kapat" geri döner. ok:false hataları (yetersiz stok, lot/seri/varyant reddi) ve
 * 403 yetki mesajı repository'de tek Result kanalına normalize edilir, aynen gösterilir.
 *
 * Malzeme çözüm deseni StockQueryScreen ile aynı (kod → stock endpoint'i); kod alanı her
 * değiştiğinde çözülmüş malzeme sıfırlanır — bayat malzemeyle yanlış satır eklenemez.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StockDocScreen(mode: StockDocMode, onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository
    val scope = rememberCoroutineScope()

    val isStockIn = mode == StockDocMode.IN
    val screenTitle = if (isStockIn) "Depo Giriş" else "Depo Çıkış"
    val locationLabel = if (isStockIn) "Hedef Lokasyon" else "Kaynak Lokasyon"

    // ── Lokasyonlar (açılışta bir kez; hata halinde retry ile yeniden) ──────
    var locations by remember { mutableStateOf<List<WarehouseLocationDto>?>(null) }
    var locationsError by remember { mutableStateOf<String?>(null) }
    var locationsAttempt by remember { mutableStateOf(0) }
    var selectedLocation by remember { mutableStateOf<WarehouseLocationDto?>(null) }
    var showLocationPicker by remember { mutableStateOf(false) }

    LaunchedEffect(locationsAttempt) {
        locations = null
        locationsError = null
        repo.locations().fold(
            onSuccess = { locations = it },
            onFailure = { locationsError = it.message ?: "Lokasyonlar yüklenemedi" }
        )
    }

    // ── Kalem ekleme formu (kod → çözüm → miktar) ───────────────────────────
    var code by remember { mutableStateOf("") }
    var resolving by remember { mutableStateOf(false) }
    var resolved by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by remember { mutableStateOf("") }

    // ── Belge durumu ────────────────────────────────────────────────────────
    var lines by remember { mutableStateOf(listOf<DocLineUi>()) }
    var note by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<String?>(null) }
    var successDocNumber by remember { mutableStateOf<String?>(null) }

    fun resolveItem() {
        val trimmed = code.trim()
        if (trimmed.isBlank() || resolving) return
        scope.launch {
            resolving = true
            resolveError = null
            resolved = null
            repo.stock(trimmed).fold(
                onSuccess = { resolved = it },
                onFailure = { resolveError = it.message ?: "Malzeme çözülemedi" }
            )
            resolving = false
        }
    }

    // Miktar TR klavyede virgülle de girilebilir — nokta ile normalize edilip parse edilir.
    val qtyValue = qtyText.trim().replace(',', '.').toDoubleOrNull()
    val qtyValid = qtyValue != null && qtyValue > 0.0

    fun addLine() {
        val item = resolved ?: return
        val qty = qtyValue
        if (qty == null || qty <= 0.0 || saving) return
        lines = lines + DocLineUi(
            itemId = item.itemId,
            itemCode = item.itemCode,
            itemName = item.itemName,
            unit = item.unit,
            quantity = qty
        )
        // Form sıradaki kalem için sıfırlanır; önceki kaydetme hatası da bayatladı.
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        saveError = null
    }

    fun save() {
        val loc = selectedLocation ?: return
        if (lines.isEmpty() || saving) return
        scope.launch {
            saving = true
            saveError = null
            val reqLines = lines.map { StockDocLineRequest(itemId = it.itemId, quantity = it.quantity) }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            val result = if (isStockIn) repo.stockIn(loc.id, reqLines, noteOrNull)
                         else repo.stockOut(loc.id, reqLines, noteOrNull)
            result.fold(
                onSuccess = { successDocNumber = it.docNumber },
                onFailure = { saveError = it.message ?: "Kaydetme başarısız" }
            )
            saving = false
        }
    }

    fun resetForNewDoc() {
        lines = emptyList()
        note = ""
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        saveError = null
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(screenTitle) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Geri")
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // ── 1) Lokasyon ────────────────────────────────────────────────
            when {
                locationsError != null -> LocationsErrorCard(
                    message = locationsError!!,
                    onRetry = { locationsAttempt++ }
                )
                locations == null -> Box(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
                    contentAlignment = Alignment.Center
                ) { CircularProgressIndicator(modifier = Modifier.size(28.dp)) }
                locations!!.isEmpty() -> Text(
                    text = "Seçilebilir aktif lokasyon bulunamadı.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.error
                )
                else -> LocationSelectorCard(
                    label = locationLabel,
                    selected = selectedLocation,
                    enabled = !saving,
                    onClick = { showLocationPicker = true }
                )
            }

            // ── 2) Kalem ekleme ────────────────────────────────────────────
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
                    Text(
                        text = "Kalem Ekle",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(Modifier.height(10.dp))

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        OutlinedTextField(
                            value = code,
                            onValueChange = {
                                code = it
                                // Kod değişti → önceki çözüm bayat; Ekle butonu kapanır.
                                resolved = null
                                resolveError = null
                            },
                            label = { Text("Malzeme kodu") },
                            singleLine = true,
                            enabled = !resolving && !saving,
                            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                            keyboardActions = KeyboardActions(onSearch = { resolveItem() }),
                            modifier = Modifier.weight(1f)
                        )
                        Spacer(Modifier.width(8.dp))
                        FilledIconButton(
                            onClick = { resolveItem() },
                            enabled = code.isNotBlank() && !resolving && !saving
                        ) {
                            if (resolving) CircularProgressIndicator(
                                modifier = Modifier.size(18.dp),
                                color = MaterialTheme.colorScheme.onPrimary
                            )
                            else Icon(Icons.Default.Search, contentDescription = "Malzemeyi bul")
                        }
                    }

                    if (resolveError != null) {
                        Spacer(Modifier.height(8.dp))
                        Text(
                            text = resolveError!!,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error
                        )
                    }

                    val item = resolved
                    if (item != null) {
                        Spacer(Modifier.height(12.dp))
                        Text(
                            text = item.itemName,
                            style = MaterialTheme.typography.bodyLarge,
                            fontWeight = FontWeight.SemiBold
                        )
                        Text(
                            text = item.itemCode +
                                (item.unit?.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )

                        // Seçili lokasyondaki mevcut bakiye — çıkışta 0 ise uyarı tonunda
                        // (sunucudaki eksi bakiye guard'ına takılmadan önce erken sinyal).
                        val loc = selectedLocation
                        if (loc != null) {
                            val bal = item.balances.firstOrNull { it.locationId == loc.id }?.quantity ?: 0.0
                            val warn = !isStockIn && bal <= 0.0
                            Spacer(Modifier.height(4.dp))
                            Text(
                                text = "${loc.name} bakiyesi: " + formatQty(bal) +
                                    (item.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                                style = MaterialTheme.typography.bodySmall,
                                color = if (warn) MaterialTheme.colorScheme.error
                                        else MaterialTheme.colorScheme.onSurfaceVariant
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
                                    imeAction = ImeAction.Done
                                ),
                                keyboardActions = KeyboardActions(onDone = { addLine() }),
                                modifier = Modifier.weight(1f)
                            )
                            Spacer(Modifier.width(8.dp))
                            FilledIconButton(
                                onClick = { addLine() },
                                enabled = qtyValid && !saving
                            ) {
                                Icon(Icons.Default.Add, contentDescription = "Satıra ekle")
                            }
                        }
                    }
                }
            }

            // ── 3) Satır listesi ───────────────────────────────────────────
            Text(
                text = "Kalemler (${lines.size})",
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold
            )
            if (lines.isEmpty()) {
                Text(
                    text = "Henüz kalem eklenmedi. Yukarıdan malzeme kodu ile ekleyin.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            } else {
                // Belge kalem sayısı mobilde küçük kalır; ekran zaten scroll'lu Column
                // olduğundan LazyColumn yerine düz forEach kullanıldı (StockQueryScreen notu).
                lines.forEachIndexed { index, line ->
                    DocLineRow(
                        line = line,
                        enabled = !saving,
                        onDelete = { lines = lines.filterIndexed { i, _ -> i != index } }
                    )
                }
            }

            // ── 4) Not (opsiyonel) ─────────────────────────────────────────
            OutlinedTextField(
                value = note,
                onValueChange = { note = it },
                label = { Text("Not (opsiyonel)") },
                enabled = !saving,
                minLines = 2,
                maxLines = 4,
                modifier = Modifier.fillMaxWidth()
            )

            // ── 5) Hata + kaydet ───────────────────────────────────────────
            if (saveError != null) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer
                    )
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

            Button(
                onClick = { save() },
                enabled = selectedLocation != null && lines.isNotEmpty() && !saving,
                modifier = Modifier.fillMaxWidth()
            ) {
                if (saving) CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    color = MaterialTheme.colorScheme.onPrimary
                )
                else Text("Kaydet")
            }

            Spacer(Modifier.height(8.dp))
        }
    }

    // ── Lokasyon seçim diyaloğu ─────────────────────────────────────────────
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
                        .verticalScroll(rememberScrollState())
                ) {
                    list.forEach { loc ->
                        val isSelected = loc.id == selectedLocation?.id
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    selectedLocation = loc
                                    showLocationPicker = false
                                }
                                .padding(vertical = 12.dp, horizontal = 4.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Icon(
                                Icons.Default.LocationOn,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(20.dp)
                            )
                            Spacer(Modifier.width(10.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(loc.name, style = MaterialTheme.typography.bodyLarge)
                                if (loc.code.isNotBlank() && loc.code != loc.name) {
                                    Text(
                                        text = loc.code,
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                }
                            }
                            if (isSelected) {
                                Icon(
                                    Icons.Default.Check,
                                    contentDescription = "Seçili",
                                    tint = MaterialTheme.colorScheme.primary
                                )
                            }
                        }
                    }
                }
            },
            confirmButton = {},
            dismissButton = {
                TextButton(onClick = { showLocationPicker = false }) { Text("Vazgeç") }
            }
        )
    }

    // ── Başarı diyaloğu — docNumber onayı ──────────────────────────────────
    if (successDocNumber != null) {
        AlertDialog(
            onDismissRequest = {
                successDocNumber = null
                onBack()
            },
            icon = {
                Icon(
                    Icons.Default.CheckCircle,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary
                )
            },
            title = { Text(if (isStockIn) "Giriş Belgesi Oluşturuldu" else "Çıkış Belgesi Oluşturuldu") },
            text = { Text("Belge No: ${successDocNumber!!}") },
            confirmButton = {
                TextButton(onClick = {
                    successDocNumber = null
                    resetForNewDoc()   // lokasyon korunur — art arda belge girişi için
                }) { Text("Yeni Belge") }
            },
            dismissButton = {
                TextButton(onClick = {
                    successDocNumber = null
                    onBack()
                }) { Text("Kapat") }
            }
        )
    }
}

/** Lokasyon seçici kartı — tıklanınca seçim diyaloğu açılır (stateless). */
@Composable
private fun LocationSelectorCard(
    label: String,
    selected: WarehouseLocationDto?,
    enabled: Boolean,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceVariant,
            disabledContainerColor = MaterialTheme.colorScheme.surfaceVariant
        )
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(
                Icons.Default.LocationOn,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary
            )
            Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = label,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Text(
                    text = selected?.name ?: "Lokasyon seçin",
                    style = MaterialTheme.typography.bodyLarge,
                    fontWeight = if (selected != null) FontWeight.SemiBold else FontWeight.Normal,
                    color = if (selected != null) MaterialTheme.colorScheme.onSurface
                            else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Icon(
                Icons.Default.ArrowDropDown,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

/** Lokasyon listesi yüklenemedi kartı — hata mesajı + tekrar dene (stateless). */
@Composable
private fun LocationsErrorCard(message: String, onRetry: () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)
    ) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    Icons.Default.ErrorOutline,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onErrorContainer,
                    modifier = Modifier.size(20.dp)
                )
                Spacer(Modifier.width(8.dp))
                Text(
                    text = message,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onErrorContainer
                )
            }
            Spacer(Modifier.height(8.dp))
            OutlinedButton(onClick = onRetry) { Text("Tekrar Dene") }
        }
    }
}

/** Eklenmiş belge kalemi satırı — ad/kod + miktar + sil (stateless). */
@Composable
private fun DocLineRow(line: DocLineUi, enabled: Boolean, onDelete: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 12.dp, end = 4.dp, top = 8.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(line.itemName, style = MaterialTheme.typography.bodyLarge)
                Text(
                    text = line.itemCode,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Text(
                text = formatQty(line.quantity) +
                    (line.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            IconButton(onClick = onDelete, enabled = enabled) {
                Icon(
                    Icons.Default.Delete,
                    contentDescription = "Satırı sil",
                    tint = MaterialTheme.colorScheme.error
                )
            }
        }
    }
}

/** Tam sayıları ".00" olmadan, ondalıklıları 2 haneye yuvarlayarak gösterir (StockQueryScreen ile aynı V1 format). */
private fun formatQty(q: Double): String =
    if (q == q.toLong().toDouble()) q.toLong().toString() else "%.2f".format(q)

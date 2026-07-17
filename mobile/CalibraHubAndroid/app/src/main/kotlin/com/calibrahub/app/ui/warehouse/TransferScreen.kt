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
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.LocationOn
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

/** Lokasyon seçim diyaloğunun hangi alanı hedeflediğini ayırt eder (aynı dialog, iki hedef). */
private enum class TransferLocationTarget { FROM, TO }

/**
 * Satır listesinin UI modeli — StockDocScreen'in DocLineUi'ıyla aynı alan seti. Sunucuya
 * yalnız itemId + quantity gider ([StockDocLineRequest] — transfer/stock-in/out arasında
 * PAYLAŞILAN aynı kalem şekli, koordinatör sözleşmesi); kod/ad/birim yalnız kart gösterimi içindir.
 */
private data class TransferLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val quantity: Double
)

/**
 * Depo Transfer ekranı (Increment 2b) — bir lokasyondan diğerine kalem taşıma belgesi.
 *
 * Akış: Kaynak + Hedef lokasyon seç (aynı seçim diyaloğu iki hedefe hizmet eder, bkz.
 * [TransferLocationTarget]) → [MaterialPickerField] ile malzeme ara/seç (kod/ad/barkod,
 * StockDocScreen ile paylaşılan bileşen) → miktar gir → satıra ekle → (opsiyonel not) →
 * Kaydet = POST transfer. Kaynak==Hedef istemci tarafında engellenir (Kaydet devre dışı kalır +
 * uyarı metni gösterilir); sunucu asıl karar mercii olarak kalır — NegativeBalanceGuard/lot-seri
 * reddi gibi hatalar {error} gövdesiyle gelir, snackbar'da aynen gösterilir.
 *
 * StockDocScreen'den kasıtlı fark: başarı burada AlertDialog değil, "documentNumber'lı snackbar +
 * form temizleme" ile bildirilir (koordinatör sözleşmesi) — lokasyonlar korunur (art arda
 * transfer girişi için), yalnız kalemler/not/malzeme arama alanı sıfırlanır.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TransferScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    // ── Lokasyonlar (açılışta bir kez; hata halinde retry ile yeniden) ──────
    var locations by remember { mutableStateOf<List<WarehouseLocationDto>?>(null) }
    var locationsError by remember { mutableStateOf<String?>(null) }
    var locationsAttempt by remember { mutableStateOf(0) }

    // Seçili lokasyon ID'leri rememberSaveable — process-death'e karşı ek sağlamlık (StockDocScreen'in
    // "code" alanı için uyguladığı gerekçeyle aynı: kamera barkod taramasından dönüşte MainActivity
    // configChanges ile korunuyor olsa da). Nesnenin kendisi değil ID saklanır; DTO her recomposition'da
    // locations listesinden id ile bulunur (aşağıdaki fromLocation/toLocation).
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
            onFailure = { locationsError = it.message ?: "Lokasyonlar yüklenemedi" }
        )
    }

    // ── Kalem ekleme formu (rehber ile ara/seç → miktar) ────────────────────
    var code by rememberSaveable { mutableStateOf("") }
    var resolved by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by rememberSaveable { mutableStateOf("") }

    // ── Belge durumu ────────────────────────────────────────────────────────
    var lines by remember { mutableStateOf(listOf<TransferLineUi>()) }
    var note by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }

    // Miktar TR klavyede virgülle de girilebilir — nokta ile normalize edilip parse edilir.
    val qtyValue = qtyText.trim().replace(',', '.').toDoubleOrNull()
    val qtyValid = qtyValue != null && qtyValue > 0.0

    fun addLine() {
        val item = resolved ?: return
        val qty = qtyValue
        if (qty == null || qty <= 0.0 || saving) return
        lines = lines + TransferLineUi(
            itemId = item.itemId,
            itemCode = item.itemCode,
            itemName = item.itemName,
            unit = item.unit,
            quantity = qty
        )
        // Form sıradaki kalem için sıfırlanır; önceki çözüm bayatladı.
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
    }

    fun resetForm() {
        lines = emptyList()
        note = ""
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
    }

    fun save() {
        val from = fromLocation ?: return
        val to = toLocation ?: return
        if (from.id == to.id || lines.isEmpty() || saving) return
        scope.launch {
            saving = true
            val reqLines = lines.map { StockDocLineRequest(itemId = it.itemId, quantity = it.quantity) }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            val result = repo.transfer(from.id, to.id, reqLines, noteOrNull)
            // showSnackbar bir dismiss'e kadar suspend olur — doğrudan burada çağrılırsa
            // "saving = false" snackbar kaybolana dek gecikir (buton yükleniyor görünür kalır).
            // Ayrı scope.launch (fire-and-forget) ile WorkOrderDetailScreen'deki aynı desen
            // kullanılır: snackbar arka planda gösterilir, buton hemen serbest kalır.
            result.fold(
                onSuccess = { res ->
                    resetForm()
                    scope.launch { snackbarHostState.showSnackbar("Transfer belgesi oluşturuldu (${res.documentNumber})") }
                },
                onFailure = { failure ->
                    scope.launch { snackbarHostState.showSnackbar(failure.message ?: "Kaydetme başarısız") }
                }
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
            // ── 1) Lokasyonlar ─────────────────────────────────────────────
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
                else -> {
                    TransferLocationCard(
                        label = "Kaynak Lokasyon",
                        selected = fromLocation,
                        enabled = !saving,
                        onClick = { pickerTarget = TransferLocationTarget.FROM }
                    )
                    TransferLocationCard(
                        label = "Hedef Lokasyon",
                        selected = toLocation,
                        enabled = !saving,
                        onClick = { pickerTarget = TransferLocationTarget.TO }
                    )
                    if (sameLocation) {
                        Text(
                            text = "Kaynak ve hedef lokasyon aynı olamaz.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error
                        )
                    }
                }
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

                    MaterialPickerField(
                        query = code,
                        onQueryChange = {
                            code = it
                            // Kod/ad değişti → önceki çözüm bayat; Ekle butonu kapanır.
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
                        modifier = Modifier.fillMaxWidth()
                    )

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

                        // Seçili KAYNAK lokasyondaki mevcut bakiye — transfer kaynaktan düşer,
                        // StockDocScreen'in çıkış modundaki uyarı mantığıyla aynı (bal<=0 → error tonu).
                        // Salt gösterim; sunucudaki NegativeBalanceGuard son sözü söyler.
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
                    text = "Henüz kalem eklenmedi. Yukarıdan malzeme arayıp ekleyin.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            } else {
                // Belge kalem sayısı mobilde küçük kalır; ekran zaten scroll'lu Column
                // olduğundan LazyColumn yerine düz forEach kullanıldı (StockDocScreen ile aynı gerekçe).
                lines.forEachIndexed { index, line ->
                    TransferLineRow(
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

            // ── 5) Kaydet ───────────────────────────────────────────────────
            Button(
                onClick = { save() },
                enabled = fromLocation != null && toLocation != null && !sameLocation &&
                    lines.isNotEmpty() && !saving,
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

    // ── Lokasyon seçim diyaloğu — kaynak/hedef PAYLAŞILAN tek dialog ────────
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
                        .verticalScroll(rememberScrollState())
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
                TextButton(onClick = { pickerTarget = null }) { Text("Vazgeç") }
            }
        )
    }
}

/** Lokasyon seçici kartı — tıklanınca seçim diyaloğu açılır (stateless). StockDocScreen'in
 * LocationSelectorCard'ıyla aynı görünüm; iki ayrı dosyada private top-level fonksiyon
 * olduğundan isim çakışması yoktur (Kotlin'de dosya-özel görünürlük). */
@Composable
private fun TransferLocationCard(
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

/** Eklenmiş transfer kalemi satırı — ad/kod + miktar + sil (stateless). */
@Composable
private fun TransferLineRow(line: TransferLineUi, enabled: Boolean, onDelete: () -> Unit) {
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

/** Tam sayıları ".00" olmadan, ondalıklıları 2 haneye yuvarlayarak gösterir (StockDocScreen ile aynı V1 format). */
private fun formatQty(q: Double): String =
    if (q == q.toLong().toDouble()) q.toLong().toString() else "%.2f".format(q)

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
import androidx.compose.material.icons.filled.Checklist
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.InventoryCountLineRequest
import com.calibrahub.app.data.StockQueryDto
import com.calibrahub.app.data.WarehouseLocationDto
import kotlinx.coroutines.launch

/**
 * Satır listesinin UI modeli — sayılan miktar + SİSTEM bakiyesi (item.balances'tan resolve
 * anında donmuş anlık değer, salt gösterim) + fark (countedQuantity - systemQuantity, client-side
 * hesap). Sunucuya yalnız itemId + countedQuantity gider ([InventoryCountLineRequest]); sistem
 * bakiyesi/fark yalnız ekranda gösterim içindir — sunucu kendi karşılaştırmasını yapar (`applied`
 * yanıt alanı belgenin doğrudan uygulanıp uygulanmadığını taşır, bkz. dosya üstü KDoc).
 */
private data class CountLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val systemQuantity: Double,
    val countedQuantity: Double
)

/** Taslak (applied=false) kaydedilmiş sayım belgesinin Sayım Yansıt dialoğu için tuttuğu kimlik. */
private data class DraftCountResult(val id: Int, val documentNumber: String)

/**
 * Depo Sayım ekranı (Increment 2b + Sayım Yansıt) — tek lokasyon için fiziksel sayım belgesi.
 *
 * Akış: Lokasyon seç → [MaterialPickerField] ile malzeme ara/seç → SAYILAN miktarı gir (0
 * GEÇERLİ — "raf boş" sayımı; StockDocScreen'in "qty > 0" kısıtından FARKLI, burada >= 0
 * kabul edilir) → satıra ekle (satırda sistem bakiyesi + sayılan + fark client-side gösterilir,
 * sunucu son sözü söyler) → opsiyonel not → Kaydet = POST inventory-count. Yanıttaki `applied`
 * alanına göre iki farklı davranış (koordinatör sözleşmesi): true → belge doğrudan uygulandı,
 * mevcut "kaydedildi ve uygulandı" snackbar'ı gösterilir; false → taslak kaldı, "Sayım Yansıt"
 * dialoğu açılır ([DraftCountResult], 2026-07-16 sözleşme genişletmesi — yanıtta artık `id` var).
 *
 * Sayım Yansıt: dialogda [Sonra] taslağı olduğu gibi bırakır (mevcut davranış — web'den
 * yansıtılabilir), [Yansıt] POST inventory-count/{id}/apply çağırır; başarıda "Yansıtıldı (N
 * satır yazıldı)" snackbar'ı ile dialog kapanır. Hata (ör. idempotent reddi — belge zaten
 * yansıtılmış) dialog İÇİNDE satır olarak gösterilir ve dialog AÇIK kalır (WorkOrderDetailScreen'in
 * CompleteOperationDialog hata deseniyle aynı gerekçe: kullanıcı mesajı okuyup tekrar deneyebilsin
 * veya "Sonra" ile vazgeçebilsin — id kaybolmasın diye snackbar'a düşürülüp dialog kapatılmadı).
 *
 * Başarıda form HER İKİ dalda da (applied true/false) hemen temizlenir, lokasyon korunur (art
 * arda sayım girişi için — StockDocScreen'in "Yeni Belge" idiomuyla aynı gerekçe); Sayım Yansıt
 * dialoğu bu temizlemeden BAĞIMSIZ ayrı bir state'tir.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CountScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    // ── Lokasyonlar (açılışta bir kez; hata halinde retry ile yeniden) ──────
    var locations by remember { mutableStateOf<List<WarehouseLocationDto>?>(null) }
    var locationsError by remember { mutableStateOf<String?>(null) }
    var locationsAttempt by remember { mutableStateOf(0) }

    // Seçili lokasyon ID rememberSaveable — process-death'e karşı ek sağlamlık (StockDocScreen'in
    // "code" alanı için uyguladığı gerekçeyle aynı). Nesnenin kendisi değil ID saklanır; DTO her
    // recomposition'da locations listesinden id ile bulunur.
    var locationId by rememberSaveable { mutableStateOf<Int?>(null) }
    var showLocationPicker by remember { mutableStateOf(false) }

    val selectedLocation = locations?.firstOrNull { it.id == locationId }

    LaunchedEffect(locationsAttempt) {
        locations = null
        locationsError = null
        repo.locations().fold(
            onSuccess = { locations = it },
            onFailure = { locationsError = it.message ?: "Lokasyonlar yüklenemedi" }
        )
    }

    // ── Kalem ekleme formu (rehber ile ara/seç → sayılan miktar) ────────────
    var code by rememberSaveable { mutableStateOf("") }
    var resolved by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by rememberSaveable { mutableStateOf("") }

    // ── Belge durumu ────────────────────────────────────────────────────────
    var lines by remember { mutableStateOf(listOf<CountLineUi>()) }
    var note by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }

    // ── Sayım Yansıt (2026-07-16) — applied=false dalında dialog state'i ────
    var draftResult by remember { mutableStateOf<DraftCountResult?>(null) }
    var applying by remember { mutableStateOf(false) }
    var applyError by remember { mutableStateOf<String?>(null) }

    // Sayımda 0 GEÇERLİ ("raf boş") — StockDocScreen'in "qty > 0" kısıtından farklı, >= 0 kabul.
    // Miktar TR klavyede virgülle de girilebilir — nokta ile normalize edilip parse edilir.
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
        lines = lines + CountLineUi(
            itemId = item.itemId,
            itemCode = item.itemCode,
            itemName = item.itemName,
            unit = item.unit,
            systemQuantity = systemQtyFor(item),
            countedQuantity = qty
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
        val loc = selectedLocation ?: return
        if (lines.isEmpty() || saving) return
        scope.launch {
            saving = true
            val reqLines = lines.map {
                InventoryCountLineRequest(itemId = it.itemId, countedQuantity = it.countedQuantity)
            }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            val result = repo.inventoryCount(loc.id, reqLines, noteOrNull)
            // showSnackbar bir dismiss'e kadar suspend olur — doğrudan burada çağrılırsa
            // "saving = false" snackbar kaybolana dek gecikir. Ayrı scope.launch (fire-and-forget)
            // ile WorkOrderDetailScreen'deki aynı desen kullanılır.
            result.fold(
                onSuccess = { res ->
                    resetForm()
                    if (res.applied) {
                        // Sunucu doğrudan uyguladı — "yansıtılsın mı" sormanın anlamı yok,
                        // mevcut davranış (snackbar) korunur.
                        scope.launch { snackbarHostState.showSnackbar("Sayım kaydedildi ve uygulandı (${res.documentNumber})") }
                    } else {
                        // Taslak kaldı — Sayım Yansıt dialoğu açılır (bkz. dosya üstü KDoc).
                        draftResult = DraftCountResult(id = res.id, documentNumber = res.documentNumber)
                    }
                },
                onFailure = { failure ->
                    scope.launch { snackbarHostState.showSnackbar(failure.message ?: "Kaydetme başarısız") }
                }
            )
            saving = false
        }
    }

    fun dismissDraftDialog() {
        draftResult = null
        applyError = null
    }

    /**
     * Sayım Yansıt onayı — POST inventory-count/{id}/apply. Sunucu tarafında idempotent DEĞİL
     * (ikinci kez çağrılırsa 400 {error} ile reddedilir); hata dialog İÇİNDE gösterilir ve
     * dialog AÇIK kalır (bkz. dosya üstü KDoc — snackbar'a düşürülmez, id kaybolmasın diye).
     */
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
                onFailure = { failure -> applyError = failure.message ?: "Yansıtma başarısız" }
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
                    label = "Sayım Lokasyonu",
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

                        if (selectedLocation != null) {
                            Spacer(Modifier.height(4.dp))
                            Text(
                                text = "${selectedLocation.name} sistem bakiyesi: " +
                                    formatQty(systemQtyFor(item)) +
                                    (item.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
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
                        Spacer(Modifier.height(4.dp))
                        Text(
                            text = "0 girilirse \"raf boş\" olarak sayılır.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
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
                    CountLineRow(
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
                        val isSelected = loc.id == locationId
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    locationId = loc.id
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

    // ── Sayım Yansıt diyaloğu — applied=false dalında save() sonrası açılır ─────────────────
    if (draftResult != null) {
        val draft = draftResult!!
        AlertDialog(
            onDismissRequest = { if (!applying) dismissDraftDialog() },
            icon = {
                Icon(
                    Icons.Default.Checklist,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary
                )
            },
            title = { Text("Sayım Taslak Kaydedildi") },
            text = {
                Column {
                    Text("Sayım taslak kaydedildi (${draft.documentNumber}). Stoğa yansıtılsın mı?")
                    if (applyError != null) {
                        Spacer(Modifier.height(8.dp))
                        Text(
                            text = applyError!!,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error
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
            }
        )
    }
}

/** Lokasyon seçici kartı — tıklanınca seçim diyaloğu açılır (stateless). StockDocScreen'in
 * LocationSelectorCard'ıyla aynı görünüm; ayrı dosyada private top-level fonksiyon olduğundan
 * isim çakışması yoktur (Kotlin'de dosya-özel görünürlük). */
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

/**
 * Eklenmiş sayım kalemi satırı — ad/kod + sil + alt satırda Sistem/Sayılan/Fark üçlüsü
 * (client-side, salt gösterim). Fark rengi: fazla → primary, eksik → error, eşit → nötr.
 */
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
                .padding(start = 12.dp, end = 4.dp, top = 8.dp, bottom = 10.dp)
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(line.itemName, style = MaterialTheme.typography.bodyLarge)
                    Text(
                        text = line.itemCode,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                IconButton(onClick = onDelete, enabled = enabled) {
                    Icon(
                        Icons.Default.Delete,
                        contentDescription = "Satırı sil",
                        tint = MaterialTheme.colorScheme.error
                    )
                }
            }
            Spacer(Modifier.height(4.dp))
            Row(
                modifier = Modifier.fillMaxWidth().padding(end = 8.dp),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                val neutralColor = MaterialTheme.colorScheme.onSurface
                CountLineStat(label = "Sistem", value = formatQty(line.systemQuantity), unit = line.unit, valueColor = neutralColor)
                CountLineStat(label = "Sayılan", value = formatQty(line.countedQuantity), unit = line.unit, valueColor = neutralColor)
                CountLineStat(label = "Fark", value = diffText, unit = line.unit, valueColor = diffColor)
            }
        }
    }
}

/** Sayım satırındaki tek bir istatistik hücresi (etiket üstte küçük, değer altta vurgulu). */
@Composable
private fun CountLineStat(label: String, value: String, unit: String?, valueColor: Color) {
    Column(horizontalAlignment = Alignment.Start) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Text(
            text = value + (unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.SemiBold,
            color = valueColor
        )
    }
}

/** Tam sayıları ".00" olmadan, ondalıklıları 2 haneye yuvarlayarak gösterir (StockDocScreen ile aynı V1 format). */
private fun formatQty(q: Double): String =
    if (q == q.toLong().toDouble()) q.toLong().toString() else "%.2f".format(q)

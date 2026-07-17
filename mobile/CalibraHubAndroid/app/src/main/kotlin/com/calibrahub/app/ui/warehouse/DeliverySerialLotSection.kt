package com.calibrahub.app.ui.warehouse

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.PlaylistAddCheck
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Numbers
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.InputChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedIconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.calibrahub.app.FlexibleCaptureActivity
import com.calibrahub.app.data.ItemLotDto
import com.calibrahub.app.data.ItemSerialDto
import com.calibrahub.app.data.WarehouseRepository
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.delay

/**
 * Alış/Satış İrsaliyesi — Seri/Lot bölümü (2026-07-17, koordinatör FINAL kontrat — UYANDIRILDI).
 *
 * DeliveryScreen.resolveTrackingType() artık StockQueryDto/ItemSearchDto'nun trackingType/
 * autoSerial alanlarını GERÇEKTEN okur — önceki iskelet sürümünde bu fonksiyon her zaman NONE
 * dönüp aşağıdaki bölüm dormant kalıyordu; kontrat gelince (bu commit) aktifleşti. Aynı ekran +
 * aynı state/navigasyon iskeleti korunuyor, yalnızca "bağlama" (gerçek alan okuma + gerçek
 * Retrofit çağrıları) tamamlandı.
 *
 * Kamera taraması MaterialPickerField'daki AYNI ZXing ScanContract + FlexibleCaptureActivity
 * deseninin (ayrı dosyada private top-level olduğu için) küçük bir kod-tekrarıdır — kapsam
 * kısıtı gereği MaterialPickerField.kt'ye DOKUNULMADI.
 */

/** Mobil yerel temsil — backend Items.TrackingType ("None"|"Lot"|"Serial", bkz.
 * MobileWarehouseApiController.ValidateWriteItemsAsync) ile AYNI 3 değer üzerine kurulu. */
internal enum class ItemTrackingType { NONE, LOT, SERIAL }

/**
 * Backend TrackingType string'ini ([ItemSearchDto.trackingType] / [StockQueryDto.trackingType] /
 * [com.calibrahub.app.data.OpenOrderLineDto.trackingType] — üçü de AYNI sözleşme) yerel enum'a
 * çevirir. Bilinmeyen/boş değer (savunma amaçlı) NONE'a düşer. DeliveryScreen.resolveTrackingType()
 * ve OpenOrderDetailScreen PAYLAŞIR.
 */
internal fun trackingTypeFromString(raw: String): ItemTrackingType = when (raw) {
    "Serial" -> ItemTrackingType.SERIAL
    "Lot" -> ItemTrackingType.LOT
    else -> ItemTrackingType.NONE
}

/** ISO tarih/datetime string'inin ilk 10 karakterini gün.ay.yıl'a çevirir; parse edilemezse ham
 * metni döner (WorkOrderListScreen.formatPlannedDate ile AYNI desen — farklı paket, dosya-özel
 * küçük tekrar; bu dosyadan `internal` olarak diğer warehouse ekranlarına da açık). */
internal fun formatIsoDate(raw: String): String = try {
    val datePart = if (raw.length >= 10) raw.substring(0, 10) else raw
    val date = java.time.LocalDate.parse(datePart)
    "%02d.%02d.%04d".format(date.dayOfMonth, date.monthValue, date.year)
} catch (e: Exception) {
    raw
}

// ───────────────────────────────────────────────────────────────────────
// Ortak küçük parça — bilgi rozeti (chip). DeliveryScreen'in "Son Belge" çipi İLE
// bu dosyanın "Seri: Otomatik" rozeti tarafından PAYLAŞILIR.
// ───────────────────────────────────────────────────────────────────────

/** Küçük pill/rozet — ikon + metin; renk şeması parametrik (varsayılan nötr ikincil, uyuşmazlık
 * durumunda çağıran taraf error renklerini geçebilir — bkz. SalesSerialTrackingRow). */
@Composable
fun InfoBadge(
    text: String,
    icon: ImageVector,
    modifier: Modifier = Modifier,
    containerColor: Color = MaterialTheme.colorScheme.secondaryContainer,
    contentColor: Color = MaterialTheme.colorScheme.onSecondaryContainer
) {
    Row(
        modifier = modifier
            .clip(RoundedCornerShape(50))
            .background(containerColor)
            .padding(horizontal = 10.dp, vertical = 5.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(icon, contentDescription = null, tint = contentColor, modifier = Modifier.size(14.dp))
        Spacer(Modifier.width(5.dp))
        Text(text = text, style = MaterialTheme.typography.labelMedium, color = contentColor, fontWeight = FontWeight.Medium)
    }
}

// ───────────────────────────────────────────────────────────────────────
// SATIŞ — varsayılan "Otomatik" rozet + manuel seçime geçiş
// ───────────────────────────────────────────────────────────────────────

/**
 * Satış: seri takipli malzeme satırı için varsayılan davranış rozeti + manuel seçim tetikleyicisi.
 * Varsayılan "Otomatik" — seri seçimi SATIŞ'ta OPSİYONEL; boş bırakılırsa sunucu kayıt anında
 * sipariş-rezerve/FIFO ile seri atar (atanan seriler başarı diyaloğunda gösterilir, bkz.
 * DeliveryLinkResultRow). [targetQuantity] > 0 iken seçilen seri sayısı ondan FARKLIYSA rozet +
 * satır altı uyarı error rengine döner (sunucu 400 mesajının AYNISI, client-side ön-kontrol).
 */
@Composable
fun SalesSerialTrackingRow(
    selectedSerials: List<String>,
    targetQuantity: Int,
    enabled: Boolean,
    onOpenPicker: () -> Unit,
    modifier: Modifier = Modifier
) {
    val mismatch = selectedSerials.isNotEmpty() && targetQuantity > 0 && selectedSerials.size != targetQuantity
    Column(modifier = modifier.fillMaxWidth().padding(top = 10.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            InfoBadge(
                text = if (selectedSerials.isEmpty()) "Seri: Otomatik (sipariş/FIFO)"
                       else "Seri: ${selectedSerials.size} seçili",
                icon = Icons.Default.Numbers,
                containerColor = if (mismatch) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
                contentColor = if (mismatch) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer
            )
            Spacer(Modifier.weight(1f))
            TextButton(onClick = onOpenPicker, enabled = enabled) {
                Icon(Icons.AutoMirrored.Filled.PlaylistAddCheck, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(6.dp))
                Text("Serileri Seç")
            }
        }
        if (mismatch) {
            Spacer(Modifier.height(4.dp))
            Text(
                text = "Seçilen seri sayısı (${selectedSerials.size}) miktarla ($targetQuantity) eşleşmiyor.",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.error
            )
        }
    }
}

/**
 * Satış: müsait (available) seri listesinden manuel seçim — tam ekran diyalog. Üstte arama kutusu
 * (GET items/{itemId}/serials?q=, 300ms debounce) + kamera okutma (MEVCUT ZXing ScanContract
 * deseni), altta checkbox'lı liste (FIFO sıralı, lot/giriş tarihi ikincil bilgi) + seçilen/hedef
 * sayaç. "Onayla" yalnız BOŞ (otomatik) ya da TAM eşleşen seçimde etkindir; ara durumda sunucuya
 * hiç gitmeden engellenir (sunucunun 400'ü zaten aynı kuralı uygular — çift savunma).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SerialSelectionDialog(
    itemId: Int,
    itemName: String,
    itemCode: String,
    targetQuantity: Int,
    initiallySelected: List<String>,
    repo: WarehouseRepository,
    onDismiss: () -> Unit,
    onConfirm: (List<String>) -> Unit
) {
    var query by remember { mutableStateOf("") }
    var available by remember { mutableStateOf<List<ItemSerialDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var loadError by remember { mutableStateOf<String?>(null) }
    var selected by remember { mutableStateOf(initiallySelected.toSet()) }

    LaunchedEffect(itemId, query) {
        delay(if (query.isBlank()) 0 else 300)
        loading = true
        loadError = null
        repo.availableSerialsForDeliveryLine(itemId, q = query.trim().takeIf { it.isNotBlank() }).fold(
            onSuccess = { available = it },
            onFailure = {
                available = emptyList()
                loadError = it.message ?: "Yüklenemedi"
            }
        )
        loading = false
    }

    // Kamera ile seri okutma — MaterialPickerField'daki AYNI ScanContract deseni (MEVCUT reuse).
    // Taranan değer müsait listede serialNo'ya KESİN (case-insensitive) eşleşiyorsa işaretlenir.
    val scanLauncher = rememberLauncherForActivityResult(ScanContract()) { result ->
        val scanned = result.contents?.trim()
        if (!scanned.isNullOrEmpty()) {
            val match = available.firstOrNull { it.serialNo.equals(scanned, ignoreCase = true) }
            if (match != null) selected = selected + match.serialNo
        }
    }

    val mismatch = selected.isNotEmpty() && targetQuantity > 0 && selected.size != targetQuantity
    val counterText = if (targetQuantity > 0) "${selected.size} / $targetQuantity seçildi" else "${selected.size} seçildi"

    Dialog(onDismissRequest = onDismiss, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            Scaffold(
                topBar = {
                    TopAppBar(
                        title = { Text("Seri Seç — $itemName") },
                        navigationIcon = {
                            IconButton(onClick = onDismiss) {
                                Icon(Icons.Default.Close, contentDescription = "Kapat")
                            }
                        }
                    )
                },
                bottomBar = {
                    Surface(tonalElevation = 3.dp) {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp, vertical = 12.dp)
                        ) {
                            if (mismatch) {
                                Text(
                                    text = "Seçim miktarla eşleşmiyor — boş bırakıp otomatik atamaya bırakabilir ya da tam $targetQuantity adet seçebilirsiniz.",
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.error
                                )
                                Spacer(Modifier.height(6.dp))
                            }
                            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                                Text(
                                    text = counterText,
                                    style = MaterialTheme.typography.bodyMedium,
                                    fontWeight = FontWeight.SemiBold,
                                    color = if (mismatch) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface,
                                    modifier = Modifier.weight(1f)
                                )
                                Button(
                                    onClick = { onConfirm(selected.toList()); onDismiss() },
                                    enabled = !mismatch
                                ) { Text("Onayla") }
                            }
                        }
                    }
                }
            ) { padding ->
                Column(
                    modifier = Modifier
                        .padding(padding)
                        .fillMaxSize()
                        .padding(16.dp)
                ) {
                    Text(itemCode, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Spacer(Modifier.height(10.dp))
                    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                        OutlinedTextField(
                            value = query,
                            onValueChange = { query = it },
                            label = { Text("Seri no ara") },
                            singleLine = true,
                            trailingIcon = {
                                if (loading) CircularProgressIndicator(modifier = Modifier.size(18.dp))
                                else Icon(Icons.Default.Search, contentDescription = null)
                            },
                            modifier = Modifier.weight(1f)
                        )
                        Spacer(Modifier.width(8.dp))
                        OutlinedIconButton(onClick = { scanLauncher.launch(serialScanOptions()) }) {
                            Icon(Icons.Default.QrCodeScanner, contentDescription = "Kamera ile okut")
                        }
                    }
                    Spacer(Modifier.height(14.dp))

                    when {
                        loading -> Box(
                            modifier = Modifier.fillMaxWidth().weight(1f),
                            contentAlignment = Alignment.Center
                        ) { CircularProgressIndicator() }

                        loadError != null -> SerialPickerHint(
                            text = loadError!!,
                            isError = true,
                            modifier = Modifier.fillMaxWidth().weight(1f)
                        )

                        available.isEmpty() -> SerialPickerHint(
                            text = if (query.isBlank()) "Müsait seri bulunamadı." else "\"$query\" ile eşleşen seri bulunamadı.",
                            modifier = Modifier.fillMaxWidth().weight(1f)
                        )

                        else -> LazyColumn(modifier = Modifier.fillMaxWidth().weight(1f)) {
                            items(available) { dto ->
                                SerialCheckRow(
                                    dto = dto,
                                    checked = dto.serialNo in selected,
                                    onToggle = {
                                        selected = if (dto.serialNo in selected) selected - dto.serialNo else selected + dto.serialNo
                                    }
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun SerialPickerHint(text: String, isError: Boolean = false, modifier: Modifier = Modifier) {
    Box(modifier = modifier.padding(top = 16.dp), contentAlignment = Alignment.TopCenter) {
        Text(
            text = text,
            style = MaterialTheme.typography.bodyMedium,
            color = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

/** Müsait seri satırı — serial no + (varsa) lot/giriş tarihi ikincil bilgi (FIFO bağlamı). */
@Composable
private fun SerialCheckRow(dto: ItemSerialDto, checked: Boolean, onToggle: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onToggle)
            .padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Checkbox(checked = checked, onCheckedChange = { onToggle() })
        Spacer(Modifier.width(8.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(dto.serialNo, style = MaterialTheme.typography.bodyLarge)
            val meta = listOfNotNull(
                dto.lotCode?.takeIf { it.isNotBlank() }?.let { "Lot: $it" },
                dto.entryDate?.takeIf { it.isNotBlank() }?.let { "Giriş: ${formatIsoDate(it)}" }
            ).joinToString(" · ")
            if (meta.isNotBlank()) {
                Text(meta, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

// ───────────────────────────────────────────────────────────────────────
// ALIŞ — "Seri Gir/Okut" çipli liste + AutoSerial toggle
// ───────────────────────────────────────────────────────────────────────

/**
 * Alış: satırda seri numaralarını elle yazma/kamera ile okutma — girilen değerler çipli
 * listede birikir (her çipte kaldır/x). [autoGenerateAvailable] (= malzemenin AutoSerial
 * bayrağı, ItemSearchDto/StockQueryDto.autoSerial) false ise "Otomatik üret" toggle'ı HİÇ
 * gösterilmez — manuel giriş zorunlu hale gelir. Toggle açıkken sunucu belge kaydında seri
 * üretir; [serials] o durumda boş gönderilebilir. Kapalıyken (veya toggle hiç yoksa) girilen
 * seri sayısı [targetQuantity] ile eşleşmezse satır altı uyarı gösterilir (sunucu 400'ünün
 * client-side ön-kontrolü).
 */
@Composable
fun PurchaseSerialEntryRow(
    serials: List<String>,
    autoGenerate: Boolean,
    autoGenerateAvailable: Boolean,
    targetQuantity: Int,
    enabled: Boolean,
    onAddSerial: (String) -> Unit,
    onRemoveSerial: (String) -> Unit,
    onAutoGenerateChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier
) {
    var manualEntry by remember { mutableStateOf("") }

    // Kamera ile seri okutma — MaterialPickerField'daki AYNI ScanContract deseni (MEVCUT reuse).
    val scanLauncher = rememberLauncherForActivityResult(ScanContract()) { result ->
        val scanned = result.contents?.trim()
        if (!scanned.isNullOrEmpty()) onAddSerial(scanned)
    }

    fun submitManual() {
        val v = manualEntry.trim()
        if (v.isNotEmpty()) {
            onAddSerial(v)
            manualEntry = ""
        }
    }

    val effectiveAuto = autoGenerate && autoGenerateAvailable
    val mismatch = !effectiveAuto && serials.isNotEmpty() && targetQuantity > 0 && serials.size != targetQuantity

    Column(modifier = modifier.fillMaxWidth().padding(top = 10.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = "Seri Gir/Okut",
                style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.weight(1f)
            )
            if (autoGenerateAvailable) {
                Text(
                    text = "Otomatik üret",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Spacer(Modifier.width(6.dp))
                Switch(checked = autoGenerate, onCheckedChange = onAutoGenerateChange, enabled = enabled)
            }
        }

        if (effectiveAuto) {
            Spacer(Modifier.height(4.dp))
            Text(
                text = "Bu malzemede seri numaraları belge kaydedilirken otomatik üretilir.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        } else {
            Spacer(Modifier.height(8.dp))
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = manualEntry,
                    onValueChange = { manualEntry = it },
                    label = { Text("Seri no") },
                    singleLine = true,
                    enabled = enabled,
                    trailingIcon = {
                        IconButton(onClick = { scanLauncher.launch(serialScanOptions()) }, enabled = enabled) {
                            Icon(Icons.Default.QrCodeScanner, contentDescription = "Kamera ile okut")
                        }
                    },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { submitManual() }),
                    modifier = Modifier.weight(1f)
                )
                Spacer(Modifier.width(8.dp))
                FilledIconButton(onClick = { submitManual() }, enabled = enabled && manualEntry.isNotBlank()) {
                    Icon(Icons.Default.Add, contentDescription = "Seri ekle")
                }
            }

            if (serials.isNotEmpty()) {
                Spacer(Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    serials.forEach { serial ->
                        InputChip(
                            selected = false,
                            onClick = {},
                            enabled = enabled,
                            label = { Text(serial) },
                            trailingIcon = {
                                IconButton(
                                    onClick = { onRemoveSerial(serial) },
                                    enabled = enabled,
                                    modifier = Modifier.size(18.dp)
                                ) {
                                    Icon(Icons.Default.Close, contentDescription = "Kaldır", modifier = Modifier.size(14.dp))
                                }
                            }
                        )
                    }
                }
                Spacer(Modifier.height(4.dp))
                Text(
                    text = "${serials.size} seri girildi",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            if (mismatch) {
                Spacer(Modifier.height(4.dp))
                Text(
                    text = "Girilen seri sayısı (${serials.size}) miktarla ($targetQuantity) eşleşmiyor.",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.error
                )
            }
        }
    }
}

/**
 * Kamera tarama seçenekleri — MaterialPickerField.barcodeScanOptions() ile AYNI desen (ayrı
 * dosyada private top-level olduğu için bilinçli küçük kod tekrarı; MaterialPickerField'a
 * DOKUNULMADI — kapsam kısıtı, davranışını bozma riski alınmadı).
 */
private fun serialScanOptions(): ScanOptions =
    ScanOptions()
        .setCaptureActivity(FlexibleCaptureActivity::class.java)
        .setDesiredBarcodeFormats(ScanOptions.ALL_CODE_TYPES)
        .setPrompt("Seri numarasını kare içine hizalayın")
        .setBeepEnabled(true)
        .setOrientationLocked(false)

// ───────────────────────────────────────────────────────────────────────
// LOT — opsiyonel Lot alanı + (satışta) FEFO öneri listesi
// ───────────────────────────────────────────────────────────────────────

/**
 * Lot: satırda opsiyonel Lot numarası girişi. SATIŞ'ta ([isSales]=true) [repo.
 * availableLotsForItem] ile FEFO (son-kullanma-tarihi-önce) sıralı müsait lot listesi getirilip
 * metin alanının altında dokun-doldur önerileri olarak gösterilir. ALIŞ'ta öneri YOK (yeni lot
 * kodu tanımlanıyor, seçilecek mevcut bir lot yok) — yalnız düz metin alanı.
 */
@Composable
fun LotInputRow(
    itemId: Int,
    value: String,
    enabled: Boolean,
    isSales: Boolean,
    repo: WarehouseRepository,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier
) {
    var suggestions by remember { mutableStateOf<List<ItemLotDto>>(emptyList()) }
    var loading by remember { mutableStateOf(false) }
    var loadError by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(itemId, isSales) {
        if (!isSales) return@LaunchedEffect
        loading = true
        loadError = null
        repo.availableLotsForItem(itemId).fold(
            onSuccess = { suggestions = it },
            onFailure = {
                suggestions = emptyList()
                loadError = it.message ?: "Yüklenemedi"
            }
        )
        loading = false
    }

    Column(modifier = modifier.fillMaxWidth().padding(top = 10.dp)) {
        OutlinedTextField(
            value = value,
            onValueChange = onValueChange,
            label = { Text("Lot") },
            placeholder = { Text("Opsiyonel") },
            singleLine = true,
            enabled = enabled,
            trailingIcon = if (isSales && loading) {
                { CircularProgressIndicator(modifier = Modifier.size(18.dp)) }
            } else null,
            modifier = Modifier.fillMaxWidth()
        )
        if (isSales) {
            Spacer(Modifier.height(6.dp))
            when {
                loadError != null -> Text(
                    text = "FEFO önerileri yüklenemedi: $loadError",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.error
                )
                !loading && suggestions.isEmpty() -> Text(
                    text = "Bu malzeme için müsait lot bulunamadı.",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                suggestions.isNotEmpty() -> Column {
                    Text(
                        text = "FEFO önerileri (son kullanma tarihine göre):",
                        style = MaterialTheme.typography.labelSmall,
                        fontWeight = FontWeight.Medium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(Modifier.height(4.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        suggestions.forEach { lot ->
                            LotSuggestionRow(
                                lot = lot,
                                selected = lot.lotCode == value,
                                enabled = enabled,
                                onClick = { onValueChange(lot.lotCode) }
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun LotSuggestionRow(lot: ItemLotDto, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(8.dp))
            .then(if (selected) Modifier.background(MaterialTheme.colorScheme.primaryContainer) else Modifier)
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(lot.lotCode, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
            // formatDeliveryQty — DeliveryScreen.kt'de `internal` (aynı paket, bu dosyaya da açık).
            // NOT "formatQty" adı BİLİNÇLİ kullanılmadı: StockDocScreen/TransferScreen/CountScreen'in
            // KENDİ private formatQty'leri ile aynı paket içinde JVM imza çakışması (Kotlin
            // "Conflicting overloads") üretiyordu — bkz. formatDeliveryQty üstü KDoc.
            val meta = listOfNotNull(
                "Bakiye: ${formatDeliveryQty(lot.quantity)}",
                lot.expiry?.takeIf { it.isNotBlank() }?.let { "SKT: ${formatIsoDate(it)}" }
            ).joinToString(" · ")
            Text(meta, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (selected) {
            Icon(
                Icons.Default.Check,
                contentDescription = "Seçili",
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(18.dp)
            )
        }
    }
}

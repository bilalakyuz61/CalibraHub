package com.calibrahub.app.ui.warehouse

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import com.calibrahub.app.FlexibleCaptureActivity
import com.calibrahub.app.data.ItemSearchDto
import com.calibrahub.app.data.StockQueryDto
import com.calibrahub.app.data.WarehouseRepository
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Malzeme rehberi (arama/seçim) — Depo modülünün paylaşılan "malzeme çözüm" bileşeni.
 * StockQueryScreen ve StockDocScreen tarafından ortak kullanılır.
 *
 * Akış: kullanıcı kod VEYA ad yazar → ~300ms debounce → GET items/search (kısmi LIKE,
 * backend) → açılır liste (ad + kod + birim) → satıra dokununca GET stock(code) ile tam
 * çözüm (itemId/bakiye/birim) yapılıp [onResolved] ile parent'a bildirilir. Bu, eskiden
 * "tam kod yaz + arama butonuna bas + eşleşmezse 404" olan akışın YERİNE geçer (mobil
 * rehber teşhis raporu, 2026-07-16). Backend sözleşmesi koordinatör tarafından kilitli:
 * GET /api/mobile/warehouse/items/search?q=&take= → 200 [{id,code,name,unit,barcode}].
 *
 * Kamera ile barkod tarama (2026-07-16): arama alanının trailing bölümünde barkod ikonu —
 * ZXing (com.journeyapps:zxing-android-embedded) CaptureActivity'yi ActivityResultContract
 * ile açar. Dönen ham değer normal debounce'lu searchItems() akışına query olarak verilir;
 * sonuç listesinde barcode alanı taranan değere case-insensitive eşit olan TEK kayıt varsa
 * otomatik seçilip [pick] ile çözülür — sıfır veya birden fazla eşleşmede normal açılır
 * listeye düşülür. CAMERA runtime izni CaptureActivity'nin kendisi tarafından istenir; bu
 * bileşen ayrıca izin istemez. Tarama iptal edilirse (contents null) hiçbir şey yapılmaz.
 *
 * State hoisting: [query]/[onQueryChange] parent'ta tutulur — parent aynı alanı
 * sıfırlayıp "önceki çözüm bayat" mantığını yönetebilsin diye (StockDocScreen'in
 * addLine() sonrası kod alanını temizlemesi gibi). Arama sonuçları/açık-kapalı durumu
 * bileşen içinde kalır; bu saf UI mekaniği parent'ı ilgilendirmez.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MaterialPickerField(
    query: String,
    onQueryChange: (String) -> Unit,
    onResolved: (StockQueryDto) -> Unit,
    onResolveError: (String) -> Unit,
    repo: WarehouseRepository,
    enabled: Boolean,
    modifier: Modifier = Modifier,
    label: String = "Malzeme kodu veya adı"
) {
    val scope = rememberCoroutineScope()

    var expanded by remember { mutableStateOf(false) }
    var searching by remember { mutableStateOf(false) }
    var resolving by remember { mutableStateOf(false) }
    var results by remember { mutableStateOf(listOf<ItemSearchDto>()) }
    var searchError by remember { mutableStateOf<String?>(null) }
    // Bir öneri seçildiğinde onQueryChange(dto.code) parent'ın query'sini değiştirir; bu da
    // aşağıdaki LaunchedEffect(query)'yi TEKRAR tetikler. Kendi seçtiğimiz kodu aramamak
    // için o turu atlayan bayrak.
    var suppressNextSearch by remember { mutableStateOf(false) }
    // Barkod tarayıcıdan dönen ham değer — bir sonraki searchItems() turunda KESİN eşleşme
    // (barcode alanı, case-insensitive) kontrolüne tabi tutulmak üzere burada bekletilir;
    // o turu tüketir tüketmez null'lanır ki sonraki elle yazılan aramalara sızmasın.
    var pendingScanValue by remember { mutableStateOf<String?>(null) }

    fun pick(dto: ItemSearchDto) {
        suppressNextSearch = true
        expanded = false
        results = emptyList()
        searchError = null
        onQueryChange(dto.code)
        scope.launch {
            resolving = true
            repo.stock(dto.code).fold(
                onSuccess = { onResolved(it) },
                onFailure = { onResolveError(it.message ?: "Malzeme çözülemedi") }
            )
            resolving = false
        }
    }

    // Kamera ile barkod tarama (ZXing embedded). CaptureActivity'yi ActivityResultContract
    // ile açar; CAMERA runtime izni CaptureActivity'nin kendisi tarafından istenir (burada
    // ayrıca istenmez). result.contents == null → kullanıcı taramayı iptal etti, hiçbir şey
    // yapılmaz. Aksi halde taranan ham değer normal debounce'lu arama akışına query olarak
    // verilir — asıl eşleştirme aşağıdaki LaunchedEffect(query) içinde yapılır.
    val scanLauncher = rememberLauncherForActivityResult(ScanContract()) { result ->
        val scanned = result.contents?.trim()
        if (!scanned.isNullOrEmpty()) {
            pendingScanValue = scanned
            onQueryChange(scanned)
        }
    }

    LaunchedEffect(query) {
        if (suppressNextSearch) {
            suppressNextSearch = false
            return@LaunchedEffect
        }
        val trimmed = query.trim()
        // Bu arama turu, onu tetikleyen scan değeriyle hâlâ eşleşiyorsa (arada elle
        // düzenleme olmadıysa) tüket; her koşulda bayrağı temizle ki başka bir aramaya
        // yanlışlıkla sızmasın.
        val scanTarget = pendingScanValue?.takeIf { it.equals(trimmed, ignoreCase = true) }
        pendingScanValue = null
        if (trimmed.length < 2) {
            results = emptyList()
            searching = false
            searchError = null
            expanded = false
            return@LaunchedEffect
        }
        delay(300)
        searching = true
        searchError = null
        repo.searchItems(trimmed).fold(
            onSuccess = { list ->
                results = list
                // Taramadan geldiyse barcode alanında KESİN (case-insensitive) eşleşen TEK
                // kayıt varsa otomatik seç/çöz; birden fazla veya hiç yoksa normal açılır
                // listeye düş (kullanıcı dokunarak seçer).
                val autoPick = scanTarget?.let { target ->
                    list.singleOrNull { dto -> dto.barcode.equals(target, ignoreCase = true) }
                }
                if (autoPick != null) {
                    pick(autoPick)
                } else {
                    expanded = list.isNotEmpty()
                }
            },
            onFailure = {
                results = emptyList()
                expanded = false
                searchError = it.message ?: "Arama başarısız"
            }
        )
        searching = false
    }

    Column(modifier = modifier) {
        ExposedDropdownMenuBox(
            expanded = expanded && results.isNotEmpty(),
            onExpandedChange = { if (results.isNotEmpty()) expanded = it }
        ) {
            OutlinedTextField(
                value = query,
                onValueChange = { onQueryChange(it) },
                label = { Text(label) },
                singleLine = true,
                enabled = enabled && !resolving,
                trailingIcon = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        IconButton(
                            onClick = { scanLauncher.launch(barcodeScanOptions()) },
                            enabled = enabled && !resolving
                        ) {
                            Icon(Icons.Default.QrCodeScanner, contentDescription = "Barkod tara")
                        }
                        if (searching || resolving) {
                            CircularProgressIndicator(modifier = Modifier.size(18.dp))
                        } else {
                            Icon(Icons.Default.Search, contentDescription = null)
                        }
                    }
                },
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                // Tam kodu bilen kullanıcı (tek eşleşme kaldıysa) Enter/Ara ile hızlı seçebilir;
                // aksi halde dokunarak listeden seçim asıl akıştır.
                keyboardActions = KeyboardActions(onSearch = { results.singleOrNull()?.let { pick(it) } }),
                modifier = Modifier
                    .menuAnchor()
                    .fillMaxWidth()
            )
            ExposedDropdownMenu(
                expanded = expanded && results.isNotEmpty(),
                onDismissRequest = { expanded = false }
            ) {
                results.forEach { dto ->
                    DropdownMenuItem(
                        text = {
                            Column {
                                Text(dto.name, fontWeight = FontWeight.SemiBold)
                                Text(
                                    text = dto.code + (dto.unit.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        },
                        onClick = { pick(dto) }
                    )
                }
            }
        }
        if (searchError != null) {
            Text(
                text = searchError!!,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(top = 4.dp)
            )
        }
    }
}

/**
 * Barkod tarama ekranı seçenekleri — malzeme etiketlerinde hangi semboloji kullanıldığı
 * bilinmediğinden tüm yaygın 1D/2D formatlar açık bırakılır (ScanOptions.ALL_CODE_TYPES).
 *
 * setCaptureActivity(FlexibleCaptureActivity) + setOrientationLocked(false): ZXing'in
 * varsayılan CaptureActivity'si kütüphane manifest'inde yatay (sensorLandscape) kilitlidir;
 * FlexibleCaptureActivity (app manifest'inde fullSensor ile deklare edilir) bu kilidi
 * kaldırıp tarayıcının cihaz yönüne (dikey/yatay) serbestçe uymasını sağlar (2026-07-16,
 * kullanıcı telefon testinde yatay zorlaması bildirdi).
 */
private fun barcodeScanOptions(): ScanOptions =
    ScanOptions()
        .setCaptureActivity(FlexibleCaptureActivity::class.java)
        .setDesiredBarcodeFormats(ScanOptions.ALL_CODE_TYPES)
        .setPrompt("Barkodu kare içine hizalayın")
        .setBeepEnabled(true)
        .setOrientationLocked(false)

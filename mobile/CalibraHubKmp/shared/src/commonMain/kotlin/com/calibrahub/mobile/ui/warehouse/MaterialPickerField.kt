package com.calibrahub.mobile.ui.warehouse

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
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.foundation.clickable
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Surface
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
import com.calibrahub.mobile.barcode.rememberBarcodeScanner
import com.calibrahub.mobile.data.ItemSearchDto
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.data.WarehouseRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Malzeme rehberi (arama/seçim) — Depo modülünün paylaşılan "malzeme çözüm" bileşeni.
 * CalibraHubAndroid `ui/warehouse/MaterialPickerField.kt` ile BİREBİR sadık port; tek platform
 * farkı barkod tarama seam'i (bkz. aşağıda).
 *
 * Akış: kullanıcı kod VEYA ad yazar → ~300ms debounce → GET items/search (kısmi LIKE, backend) →
 * açılır liste (ad + kod + birim) → satıra dokununca GET stock(code) ile tam çözüm (itemId/
 * bakiye/birim) yapılıp [onResolved] ile parent'a bildirilir.
 *
 * Kamera ile barkod tarama — Android'de doğrudan `rememberLauncherForActivityResult(ScanContract())`
 * (fire-and-forget callback) kullanılırken, burada Faz 2a'da tanımlı ORTAK seam
 * ([com.calibrahub.mobile.barcode.rememberBarcodeScanner]/`BarcodeScanner.scan()`, suspend tabanlı)
 * kullanılır — androidMain'de gerçek ZXing taraması yapar, iosMain'de Faz 3'e kadar STUB olarak
 * her zaman `null` döner (kamera ikonu görünür ama tarama yapmaz; ekran yine de tam çalışır
 * kalır). Taranan değer normal debounce'lu searchItems() akışına query olarak verilir; sonuç
 * listesinde barcode alanı taranan değere case-insensitive eşit olan TEK kayıt varsa otomatik
 * seçilip [pick] ile çözülür.
 *
 * State hoisting: [query]/[onQueryChange] parent'ta tutulur (Android ile AYNI gerekçe).
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
    label: String = "Malzeme kodu veya adı",
) {
    val scope = rememberCoroutineScope()
    val scanner = rememberBarcodeScanner()

    var expanded by remember { mutableStateOf(false) }
    var searching by remember { mutableStateOf(false) }
    var resolving by remember { mutableStateOf(false) }
    var results by remember { mutableStateOf(listOf<ItemSearchDto>()) }
    var searchError by remember { mutableStateOf<String?>(null) }
    // Bir öneri seçildiğinde onQueryChange(dto.code) parent'ın query'sini değiştirir; bu da
    // aşağıdaki LaunchedEffect(query)'yi TEKRAR tetikler. Kendi seçtiğimiz kodu aramamak için o
    // turu atlayan bayrak.
    var suppressNextSearch by remember { mutableStateOf(false) }
    // Barkod tarayıcıdan dönen ham değer — bir sonraki searchItems() turunda KESİN eşleşme
    // (barcode alanı, case-insensitive) kontrolüne tabi tutulmak üzere burada bekletilir; o turu
    // tüketir tüketmez null'lanır ki sonraki elle yazılan aramalara sızmasın.
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
                onFailure = { onResolveError(it.message ?: "Malzeme çözülemedi") },
            )
            resolving = false
        }
    }

    LaunchedEffect(query) {
        if (suppressNextSearch) {
            suppressNextSearch = false
            return@LaunchedEffect
        }
        val trimmed = query.trim()
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
            },
        )
        searching = false
    }

    Column(modifier = modifier) {
        OutlinedTextField(
                value = query,
                onValueChange = { onQueryChange(it) },
                label = { Text(label) },
                singleLine = true,
                enabled = enabled && !resolving,
                trailingIcon = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        IconButton(
                            onClick = {
                                scope.launch {
                                    val scanned = scanner.scan()?.trim()
                                    if (!scanned.isNullOrEmpty()) {
                                        pendingScanValue = scanned
                                        onQueryChange(scanned)
                                    }
                                }
                            },
                            enabled = enabled && !resolving,
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
                keyboardActions = KeyboardActions(onSearch = { results.singleOrNull()?.let { pick(it) } }),
                modifier = Modifier.fillMaxWidth(),
            )
        // Sonuc listesi POPUP DEGIL, alanin ALTINDA satir ici cizilir.
        // Sebep (2026-08-24, iOS cihaz testi): ExposedDropdownMenu bir Popup acar ve iOS'ta
        // (Compose Multiplatform) bu popup metin alaninin ODAGINI CALIYOR — kullanici malzeme
        // adini yazarken liste acilinca klavye/odak kayboluyordu. Material3'un bu surumunde
        // ExposedDropdownMenu `properties = PopupProperties(focusable = false)` KABUL ETMIYOR
        // (yerel derlemeyle dogrulandi: "No parameter with name 'properties'"), bu yuzden popup
        // tamamen birakildi. Satir ici liste her iki platformda ayni davranir ve odagi bozmaz.
        // Kaydirma YOK, en fazla 8 sonuc gosterilir (ic ice dikey kaydirma olcum sorunu yaratir;
        // ekranlarin kendi verticalScroll'u zaten var).
        if (expanded && results.isNotEmpty()) {
            Surface(
                shape = MaterialTheme.shapes.medium,
                tonalElevation = 2.dp,
                modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
            ) {
                Column(modifier = Modifier.fillMaxWidth()) {
                    results.take(8).forEach { dto ->
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable { pick(dto) }
                                .padding(horizontal = 16.dp, vertical = 10.dp),
                        ) {
                            Text(dto.name, fontWeight = FontWeight.SemiBold)
                            Text(
                                text = dto.code + (dto.unit.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        HorizontalDivider()
                    }
                }
            }
        }
        if (searchError != null) {
            Text(
                text = searchError!!,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
    }
}

package com.calibrahub.app.ui.warehouse

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import com.calibrahub.app.data.ContactSearchDto
import com.calibrahub.app.data.WarehouseRepository
import kotlinx.coroutines.delay

/**
 * Cari (contact) rehberi (arama/seçim) — Alış/Satış İrsaliyesi ekranlarının paylaşılan cari
 * çözüm bileşeni. [MaterialPickerField]'ın BİREBİR deseni (300ms debounce, ≥2 karakter,
 * ExposedDropdownMenuBox, kod+ad gösterimi) ama contacts/search'e vurur ve barkod butonu YOK
 * (koordinatör talimatı — cari kartlarında barkod taraması anlamsız).
 *
 * MaterialPickerField'tan kasıtlı FARK: arama sonucu satırının kendisi ([ContactSearchDto])
 * zaten tam kayıttır — malzeme akışındaki "sonuç listesinden seç → ayrı GET stock(code) ile
 * çöz" iki-aşamalı deseni burada YOK, seçim anında [onSelected] doğrudan çağrılır. Bu yüzden
 * [MaterialPickerField]'ın onResolveError/resolving mekanizması burada karşılığı bulunmaz.
 *
 * Bilinçli tasarım kararı: generic bir "arama alanı" bileşenine çıkarılmadı — MaterialPickerField
 * kamera barkod taraması + iki-aşamalı çözüm taşıdığından, ortak soyutlama ekstra karmaşıklık
 * getirirdi ve mevcut davranışını bozma riski taşırdı (kapsam talimatı). İki dosya kasıtlı
 * olarak ayrı tutuldu; ExposedDropdownMenuBox/menuAnchor/ExposedDropdownMenu kullanımı
 * MaterialPickerField ile birebir aynı (Material3 API'si, ayrı import gerekmez — bkz. o dosya).
 *
 * State hoisting: [query]/[onQueryChange] parent'ta tutulur (MaterialPickerField ile aynı gerekçe).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContactPickerField(
    query: String,
    onQueryChange: (String) -> Unit,
    onSelected: (ContactSearchDto) -> Unit,
    repo: WarehouseRepository,
    enabled: Boolean,
    modifier: Modifier = Modifier,
    label: String = "Cari kodu veya adı"
) {
    var expanded by remember { mutableStateOf(false) }
    var searching by remember { mutableStateOf(false) }
    var results by remember { mutableStateOf(listOf<ContactSearchDto>()) }
    var searchError by remember { mutableStateOf<String?>(null) }
    // Bir öneri seçildiğinde onQueryChange(dto.code) parent'ın query'sini değiştirir; bu da
    // aşağıdaki LaunchedEffect(query)'yi TEKRAR tetikler. Kendi seçtiğimiz kodu aramamak
    // için o turu atlayan bayrak (MaterialPickerField ile aynı desen).
    var suppressNextSearch by remember { mutableStateOf(false) }

    fun pick(dto: ContactSearchDto) {
        suppressNextSearch = true
        expanded = false
        results = emptyList()
        searchError = null
        onQueryChange(dto.code)
        onSelected(dto)
    }

    LaunchedEffect(query) {
        if (suppressNextSearch) {
            suppressNextSearch = false
            return@LaunchedEffect
        }
        val trimmed = query.trim()
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
        repo.searchContacts(trimmed).fold(
            onSuccess = { list ->
                results = list
                expanded = list.isNotEmpty()
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
                enabled = enabled,
                trailingIcon = {
                    if (searching) {
                        CircularProgressIndicator(modifier = Modifier.size(18.dp))
                    } else {
                        Icon(Icons.Default.Search, contentDescription = null)
                    }
                },
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
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
                                    text = dto.code,
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

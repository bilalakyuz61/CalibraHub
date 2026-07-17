package com.calibrahub.app.ui.warehouse

import androidx.compose.foundation.clickable
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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Business
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.calibrahub.app.data.ContactSearchDto
import com.calibrahub.app.data.WarehouseRepository
import kotlinx.coroutines.delay

/**
 * Cari rehberi — tam ekran arama/gözat diyaloğu. [ContactPickerField]'ın type-ahead
 * dropdown'ının YANINDA duran "rehber" butonundan açılır (DeliveryScreen). Aynı arama
 * sözleşmesini (contacts/search) kullanır ama sonucu küçük bir dropdown yerine tam ekran
 * kaydırılabilir listede gösterir — büyük cari listelerinde type-ahead'in kısa dropdown'ının
 * karşılamadığı "gözat" ihtiyacını çözer. Mevcut [ContactPickerField] davranışı bu diyalogdan
 * ETKİLENMEZ — o bileşene hiç dokunulmadı, yalnız aynı arama fonksiyonu ([WarehouseRepository.
 * searchContacts]) burada da çağrılır.
 *
 * Boş/kısa sorgu davranışı BİLİNÇLİ: backend contacts/search q boşsa `[]` döner (ilk-N-listele
 * YOK, bkz. MobileWarehouseApiController.SearchContacts) — bu yüzden burada `q=a` gibi bir hack
 * YAPILMADI; 2 karakterden kısa sorguda boş-durum metni gösterilir, kullanıcı yazdıkça liste
 * dolar ([ContactPickerField] ile BİREBİR aynı eşik/debounce: 300ms, ≥2 karakter).
 *
 * StockDocScreen'in lokasyon seçim diyaloğu (`showLocationPicker`, AlertDialog + sabit/kısa
 * liste) deseninin BÜYÜTÜLMÜŞ hali: cari sayısı büyük olabileceğinden burada kendi arama
 * kutusu + debounce'lu sorgu eklendi, AlertDialog yerine tam ekran [Dialog] kullanıldı.
 *
 * State hoisting YOK — bu diyalog kendi arama state'ini kendi yönetir ([ContactPickerField]'daki
 * query'nin aksine parent'a taşınmaz); yalnız nihai seçim [onSelected] ile dışarı bildirilir.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContactPickerDialog(
    repo: WarehouseRepository,
    onDismiss: () -> Unit,
    onSelected: (ContactSearchDto) -> Unit
) {
    var query by remember { mutableStateOf("") }
    var searching by remember { mutableStateOf(false) }
    var results by remember { mutableStateOf(listOf<ContactSearchDto>()) }
    var searchError by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(query) {
        val trimmed = query.trim()
        if (trimmed.length < 2) {
            results = emptyList()
            searching = false
            searchError = null
            return@LaunchedEffect
        }
        delay(300)
        searching = true
        searchError = null
        repo.searchContacts(trimmed).fold(
            onSuccess = { results = it },
            onFailure = {
                results = emptyList()
                searchError = it.message ?: "Arama başarısız"
            }
        )
        searching = false
    }

    Dialog(onDismissRequest = onDismiss, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            Scaffold(
                topBar = {
                    TopAppBar(
                        title = { Text("Cari Rehberi") },
                        navigationIcon = {
                            IconButton(onClick = onDismiss) {
                                Icon(Icons.Default.Close, contentDescription = "Kapat")
                            }
                        }
                    )
                }
            ) { padding ->
                Column(
                    modifier = Modifier
                        .padding(padding)
                        .fillMaxSize()
                        .padding(horizontal = 16.dp, vertical = 12.dp)
                ) {
                    OutlinedTextField(
                        value = query,
                        onValueChange = { query = it },
                        label = { Text("Cari kodu veya adı") },
                        singleLine = true,
                        trailingIcon = {
                            if (searching) CircularProgressIndicator(modifier = Modifier.size(18.dp))
                            else Icon(Icons.Default.Search, contentDescription = null)
                        },
                        modifier = Modifier.fillMaxWidth()
                    )
                    Spacer(Modifier.height(14.dp))

                    when {
                        searchError != null -> ContactPickerHint(
                            text = searchError!!,
                            isError = true,
                            modifier = Modifier.fillMaxWidth().weight(1f)
                        )
                        query.trim().length < 2 -> ContactPickerHint(
                            text = "Aramak için en az 2 karakter yazın.",
                            modifier = Modifier.fillMaxWidth().weight(1f)
                        )
                        !searching && results.isEmpty() -> ContactPickerHint(
                            text = "Sonuç bulunamadı.",
                            modifier = Modifier.fillMaxWidth().weight(1f)
                        )
                        else -> LazyColumn(modifier = Modifier.fillMaxWidth().weight(1f)) {
                            items(results) { contact ->
                                ContactPickerRow(
                                    contact = contact,
                                    onClick = {
                                        onSelected(contact)
                                        onDismiss()
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
private fun ContactPickerHint(text: String, isError: Boolean = false, modifier: Modifier = Modifier) {
    Box(modifier = modifier.padding(top = 24.dp), contentAlignment = Alignment.TopCenter) {
        Text(
            text = text,
            style = MaterialTheme.typography.bodyMedium,
            color = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

@Composable
private fun ContactPickerRow(contact: ContactSearchDto, onClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(
            Icons.Default.Business,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.primary,
            modifier = Modifier.size(22.dp)
        )
        Spacer(Modifier.width(12.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(contact.name, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
            Text(
                text = contact.code,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

package com.calibrahub.mobile.ui.contact

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Business
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
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
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.ContactCardDto
import com.calibrahub.mobile.data.ContactCardRowDto
import com.calibrahub.mobile.session.SessionManager
import kotlinx.coroutines.delay

/**
 * Cari Karti — arama + SALT OKUNUR kart detayi.
 *
 * BAKIYE YOK: sistemde tahsilat/odeme modulu olmadigi icin cari bakiye hesaplanmiyor
 * (bkz. MobileContactApiController KDoc). Fatura toplamindan bakiye turetmek odenen tutari
 * goremeyecegi icin YANLIS rakam uretirdi — yanlis bakiye gostermektense hic gostermemek dogru.
 *
 * Duzenleme yok: cari olusturma/degistirme web'de kalir.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContactCardScreen(session: SessionManager, onBack: () -> Unit) {
    val repo = session.contactCardRepository

    var query by rememberSaveable { mutableStateOf("") }
    var results by remember { mutableStateOf<List<ContactCardRowDto>>(emptyList()) }
    var selected by remember { mutableStateOf<ContactCardDto?>(null) }
    var searching by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    // Secim -> detay cekimi arasindaki tasiyici. Compose state OLMALI: bir Composable'in
    // gorunum lambdasindan suspend cagri yapilamaz, secim burada isaretlenip asagidaki
    // LaunchedEffect ile cekilir.
    var pendingDetailId by remember { mutableStateOf<Int?>(null) }

    // Debounce — MaterialPickerField ile ayni 300 ms; her tusa basista istek atilmaz.
    LaunchedEffect(query) {
        val q = query.trim()
        if (q.length < 2) {
            results = emptyList()
            return@LaunchedEffect
        }
        delay(300)
        searching = true
        repo.search(q).fold(
            onSuccess = { results = it; errorMessage = null },
            onFailure = { results = emptyList(); errorMessage = it.message ?: "Arama başarısız." },
        )
        searching = false
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (selected == null) "Cari Kartı" else "Cari Detayı") },
                navigationIcon = {
                    IconButton(onClick = { if (selected != null) selected = null else onBack() }) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
    ) { padding ->
        Box(modifier = Modifier.padding(padding).fillMaxSize()) {
            val detail = selected
            if (detail != null) {
                ContactDetailView(detail)
            } else {
                Column(modifier = Modifier.fillMaxSize().padding(16.dp)) {
                    OutlinedTextField(
                        value = query,
                        onValueChange = { query = it },
                        label = { Text("Cari ara") },
                        supportingText = { Text("Kod, ünvan veya vergi no — en az 2 karakter") },
                        singleLine = true,
                        leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                        modifier = Modifier.fillMaxWidth(),
                    )

                    Spacer(Modifier.height(12.dp))

                    when {
                        searching -> Box(Modifier.fillMaxWidth().padding(24.dp), contentAlignment = Alignment.Center) {
                            CircularProgressIndicator()
                        }
                        errorMessage != null -> InfoBlock(errorMessage!!, isError = true)
                        query.trim().length < 2 -> InfoBlock("Cari bulmak için kod veya ünvan yazın.", isError = false)
                        results.isEmpty() -> InfoBlock("Eşleşen cari bulunamadı.", isError = false)
                        else -> LazyColumn(
                            contentPadding = PaddingValues(bottom = 16.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            items(results, key = { it.id }) { row ->
                                // Detay cekilene kadar LISTEDE kalinir; hata olursa kullanici
                                // yine listededir (bos bir detay ekrani acilmaz).
                                ContactRow(row) { pendingDetailId = row.id }
                            }
                        }
                    }
                }
            }
        }
    }

    // Secilen carinin detayini cek — [pendingDetailId] degisince calisir.
    LaunchedEffect(pendingDetailId) {
        val id = pendingDetailId ?: return@LaunchedEffect
        searching = true
        repo.detail(id).fold(
            onSuccess = { selected = it; errorMessage = null },
            onFailure = { errorMessage = it.message ?: "Cari kartı açılamadı." },
        )
        searching = false
        pendingDetailId = null
    }
}

@Composable
private fun ContactRow(row: ContactCardRowDto, onClick: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth().clickable { onClick() }) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(
                Icons.Default.Business,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(20.dp),
            )
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(row.title, fontWeight = FontWeight.SemiBold)
                Text(
                    text = listOfNotNull(
                        row.code.takeIf { it.isNotBlank() },
                        row.city?.takeIf { it.isNotBlank() },
                    ).joinToString(" · "),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun ContactDetailView(c: ContactCardDto) {
    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
    ) {
        Text(c.title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
        Text(
            text = c.code,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (!c.isActive) {
            Spacer(Modifier.height(8.dp))
            Surface(
                shape = MaterialTheme.shapes.small,
                color = MaterialTheme.colorScheme.errorContainer,
                contentColor = MaterialTheme.colorScheme.onErrorContainer,
            ) {
                Text(
                    "Pasif",
                    style = MaterialTheme.typography.labelMedium,
                    modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
                )
            }
        }

        Spacer(Modifier.height(16.dp))
        DetailSection("İletişim") {
            DetailRow("Telefon", c.phone)
            DetailRow("Cep", c.mobile)
            DetailRow("E-posta", c.email)
            DetailRow("Yetkili", c.contactPerson)
        }

        DetailSection("Adres") {
            DetailRow("Adres", c.address)
            DetailRow("Mahalle", c.neighborhood)
            DetailRow("İlçe", c.district)
            DetailRow("İl", c.city)
            DetailRow("Posta Kodu", c.postalCode)
        }

        DetailSection("Vergi") {
            DetailRow("Vergi No", c.taxNumber)
            DetailRow("TCKN", c.identityNumber)
            DetailRow("Vergi Dairesi", c.taxOffice)
        }
    }
}

/**
 * Bolum basligi + kart. Icindeki [DetailRow]'lar bos degerde kendini cizmez; bolumun tamami
 * bossa yalniz baslik + ince bos kart kalir (kabul edilebilir, dinamik gizleme icin satir
 * sayimini yukari tasimak gerekirdi — bu ekranda kazanci yok).
 */
@Composable
private fun DetailSection(title: String, content: @Composable () -> Unit) {
    Column(modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp)) {
        Text(
            text = title,
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.primary,
            fontWeight = FontWeight.SemiBold,
        )
        Spacer(Modifier.height(6.dp))
        Card(modifier = Modifier.fillMaxWidth()) {
            Column { content() }
        }
    }
}

/** Degeri bos olan alan HIC cizilmez — telefonda bos "—" satirlari yer kaplar. */
@Composable
private fun DetailRow(label: String, value: String?) {
    val v = value?.trim().orEmpty()
    if (v.isEmpty()) return
    Column {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Text(
                text = label,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.width(96.dp),
            )
            Text(text = v, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
        }
        HorizontalDivider()
    }
}

@Composable
private fun InfoBlock(text: String, isError: Boolean) {
    val tint = if (isError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
    Column(
        modifier = Modifier.fillMaxWidth().padding(top = 32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(Icons.Default.Business, contentDescription = null, tint = tint, modifier = Modifier.size(36.dp))
        Spacer(Modifier.height(10.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
    }
}

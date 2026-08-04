package com.calibrahub.mobile.ui.production

import androidx.compose.foundation.background
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
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Assignment
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.WorkOrderListItemDto
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.warehouse.formatIsoDate
import com.calibrahub.mobile.ui.warehouse.formatQty
import kotlinx.coroutines.delay

/**
 * Üretim → İş Emirleri listesi — CalibraHubAndroid `ui/production/WorkOrderListScreen.kt` ile
 * BİREBİR sadık port ([OpenOrderListScreen]/[DeliveryScreen]'de kurulan aynı tek-composable
 * arama+debounce deseni). [session] Android'deki `context.app.productionRepository` yerine
 * PARAMETRE olarak alınır (KMP'de Android Context/Application-scoped singleton yok — bkz.
 * AppNavHost'un SessionManager'i geçirme deseni).
 *
 * Arama kutusu her karakter değişiminde GET work-orders?q=&take=50 sunucu taraması yapar (backend
 * LIKE araması); reloadTick hem "Tekrar Dene" hem query değişimiyle aynı LaunchedEffect'i tetikler
 * — ilk yükleme ve boşa dönüş (query temizlenince) debounce beklemeden hemen ateşlenir, dolu
 * query'de her karakterde 300ms beklenir. Önceki liste, yeni sonuç gelene kadar ekranda kalır.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WorkOrderListScreen(session: SessionManager, onOpenDetail: (Int) -> Unit, onBack: () -> Unit) {
    val repo = session.productionRepository

    var query by rememberSaveable { mutableStateOf("") }
    var workOrders by remember { mutableStateOf<List<WorkOrderListItemDto>?>(null) }
    var loading by remember { mutableStateOf(true) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var reloadTick by remember { mutableStateOf(0) }

    LaunchedEffect(query, reloadTick) {
        if (query.isNotBlank()) delay(300)
        loading = true
        errorMessage = null
        repo.workOrders(query, 50).fold(
            onSuccess = { workOrders = it },
            onFailure = { errorMessage = it.message ?: "İş emirleri yüklenemedi" },
        )
        loading = false
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("İş Emirleri") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
    ) { padding ->
        Column(modifier = Modifier.padding(padding).fillMaxSize()) {
            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                label = { Text("Numara, malzeme kodu veya adı ara") },
                singleLine = true,
                leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                trailingIcon = {
                    if (loading) {
                        CircularProgressIndicator(modifier = Modifier.size(18.dp))
                    } else if (query.isNotEmpty()) {
                        IconButton(onClick = { query = "" }) {
                            Icon(Icons.Default.Close, contentDescription = "Temizle")
                        }
                    }
                },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
            )

            when {
                errorMessage != null -> WorkOrderMessage(
                    icon = Icons.Default.ErrorOutline,
                    text = errorMessage!!,
                    tint = MaterialTheme.colorScheme.error,
                    onRetry = { reloadTick++ },
                )
                loading && workOrders == null -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) { CircularProgressIndicator() }
                workOrders.isNullOrEmpty() -> WorkOrderMessage(
                    icon = Icons.AutoMirrored.Filled.Assignment,
                    text = if (query.isBlank()) "İş emri yok" else "Aramaya uyan iş emri bulunamadı",
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                else -> LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    items(workOrders!!, key = { it.id }) { wo ->
                        WorkOrderCard(wo, onClick = { onOpenDetail(wo.id) })
                    }
                }
            }
        }
    }
}

@Composable
private fun WorkOrderCard(wo: WorkOrderListItemDto, onClick: () -> Unit) {
    Card(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = wo.number,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                )
                StatusChip(label = wo.statusLabel, statusCode = wo.statusCode)
            }
            Spacer(Modifier.height(6.dp))
            Text(wo.itemName, style = MaterialTheme.typography.bodyLarge)
            Text(
                text = wo.itemCode,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(6.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = formatQty(wo.quantity) + (wo.unit.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                    style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.Medium,
                )
                if (!wo.plannedDate.isNullOrBlank()) {
                    Text(
                        text = formatIsoDate(wo.plannedDate),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
    }
}

@Composable
private fun WorkOrderMessage(icon: ImageVector, text: String, tint: Color, onRetry: (() -> Unit)? = null) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 48.dp, start = 24.dp, end = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(40.dp))
        Spacer(Modifier.height(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
        if (onRetry != null) {
            Spacer(Modifier.height(12.dp))
            OutlinedButton(onClick = onRetry) { Text("Tekrar Dene") }
        }
    }
}

/**
 * Durum rozeti — statusCode kümesi KİLİTLİ (koordinatör, 2026-07-16, CalibraHubAndroid ile AYNI):
 *  İş emri:   planned / released / in_progress / completed / closed / cancelled
 *  Operasyon: pending / in_progress / completed / skipped
 * Renk: in_progress=indigo (tema secondary), completed=yeşil (tema primary), released=mavi
 * (sabit — temada karşılığı yok), cancelled=kırmızımsı (tema error), planned/pending=nötr,
 * closed/skipped=soluk nötr, bilinmeyen=nötr fallback. Etiket HER ZAMAN statusLabel'dan
 * gösterilir — statusCode yalnızca renk seçimi için okunur.
 *
 * BİLİNÇLİ SAPMA: CalibraHubAndroid'de bu composable WorkOrderListScreen VE
 * WorkOrderDetailScreen'de birebir aynı içerikle iki kez tanımlanır (dosya-özel duplicate).
 * KMP portunda CLAUDE.md "Tekrarlanan mantığı tek kaynağa çek" kuralına uyularak `internal`
 * yapılıp WorkOrderDetailScreen.kt'den TEK kopyadan reuse edilir — davranış/çıktı birebir aynı.
 */
@Composable
internal fun StatusChip(label: String, statusCode: String) {
    val scheme = MaterialTheme.colorScheme
    val (container, content) = when (statusCode.trim().lowercase()) {
        "in_progress" -> scheme.secondary.copy(alpha = 0.18f) to scheme.secondary
        "completed" -> scheme.primary.copy(alpha = 0.18f) to scheme.primary
        "released" -> Color(0xFF3B82F6).copy(alpha = 0.18f) to Color(0xFF3B82F6)
        "cancelled" -> scheme.error.copy(alpha = 0.18f) to scheme.error
        "closed", "skipped" -> scheme.surfaceVariant to scheme.onSurfaceVariant.copy(alpha = 0.6f)
        else -> scheme.surfaceVariant to scheme.onSurfaceVariant // planned/pending/bilinmeyen
    }
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(container)
            .padding(horizontal = 10.dp, vertical = 4.dp),
    ) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = content, fontWeight = FontWeight.Medium)
    }
}

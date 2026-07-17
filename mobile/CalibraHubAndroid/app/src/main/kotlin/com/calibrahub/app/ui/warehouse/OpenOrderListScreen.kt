package com.calibrahub.app.ui.warehouse

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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ListAlt
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.OpenOrderSummaryDto
import kotlinx.coroutines.delay

/**
 * FAZ C(a) — Açık Siparişler listesi (2026-07-17, koordinatör FINAL kontrat). WarehouseHome'daki
 * "Açık Satış Siparişleri" / "Açık Alış Siparişleri" kartlarından [docType] parametresiyle açılır
 * — DeliveryScreen'in aynı tek-composable-iki-yön deseni. Arama/liste yapısı WorkOrderListScreen
 * ile BİREBİR aynı (debounce'lu server-side arama, reloadTick ile "Tekrar Dene", searching
 * sırasında önceki liste ekranda kalır).
 *
 * Satıra dokununca [onOpenDetail] ile sipariş Id'si + zaten bilinen [docType] birlikte taşınır
 * (OpenOrderDetailDto sözleşmesinde docType YOK — MainActivity route'u bu değeri path'te taşır,
 * bkz. "warehouse_open_order_detail/{docType}/{id}").
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OpenOrderListScreen(docType: DeliveryDocType, onOpenDetail: (Int) -> Unit, onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository

    var query by rememberSaveable { mutableStateOf("") }
    var orders by remember { mutableStateOf<List<OpenOrderSummaryDto>?>(null) }
    var loading by remember { mutableStateOf(true) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var reloadTick by remember { mutableStateOf(0) }

    LaunchedEffect(query, reloadTick) {
        if (query.isNotBlank()) delay(300)
        loading = true
        errorMessage = null
        repo.openOrders(docType.apiValue, query.trim().takeIf { it.isNotBlank() }, 50).fold(
            onSuccess = { orders = it },
            onFailure = { errorMessage = it.message ?: "Açık siparişler yüklenemedi" }
        )
        loading = false
    }

    val screenTitle = if (docType == DeliveryDocType.SALES) "Açık Satış Siparişleri" else "Açık Alış Siparişleri"

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(screenTitle) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                }
            )
        }
    ) { padding ->
        Column(modifier = Modifier.padding(padding).fillMaxSize()) {
            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                label = { Text("Sipariş no veya cari ara") },
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
                    .padding(16.dp)
            )

            when {
                errorMessage != null -> OpenOrderMessage(
                    icon = Icons.Default.ErrorOutline,
                    text = errorMessage!!,
                    tint = MaterialTheme.colorScheme.error,
                    onRetry = { reloadTick++ }
                )
                loading && orders == null -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center
                ) { CircularProgressIndicator() }
                orders.isNullOrEmpty() -> OpenOrderMessage(
                    icon = Icons.AutoMirrored.Filled.ListAlt,
                    text = if (query.isBlank()) "Açık sipariş yok" else "Aramaya uyan sipariş bulunamadı",
                    tint = MaterialTheme.colorScheme.onSurfaceVariant
                )
                else -> LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    items(orders!!, key = { it.id }) { order ->
                        OpenOrderCard(order, onClick = { onOpenDetail(order.id) })
                    }
                }
            }
        }
    }
}

@Composable
private fun OpenOrderCard(order: OpenOrderSummaryDto, onClick: () -> Unit) {
    Card(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = order.number,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f)
                )
                Text(
                    text = formatIsoDate(order.date),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Spacer(Modifier.height(6.dp))
            Text(order.contactName, style = MaterialTheme.typography.bodyLarge)
            Spacer(Modifier.height(8.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                InfoBadge(text = "${order.openLineCount} açık kalem", icon = Icons.AutoMirrored.Filled.ListAlt)
                Text(
                    text = "Açık miktar: ${formatDeliveryQty(order.totalOpenQuantity)}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}

@Composable
private fun OpenOrderMessage(icon: ImageVector, text: String, tint: Color, onRetry: (() -> Unit)? = null) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 48.dp, start = 24.dp, end = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
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

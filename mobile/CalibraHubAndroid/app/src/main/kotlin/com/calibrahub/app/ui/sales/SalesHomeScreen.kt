package com.calibrahub.app.ui.sales

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.PendingActions
import androidx.compose.material.icons.filled.ReceiptLong
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * Satış modülü ana ekranı (2026-07-17 drawer migration, FAZ D) — [com.calibrahub.app.ui.warehouse.WarehouseHomeScreen]'den
 * TAŞINAN iki kart: Satış İrsaliyesi + Açık Satış Siparişleri. Rota hedefleri AYNEN korunur
 * ("warehouse_delivery/sales", "warehouse_open_orders/sales") — DeliveryScreen/
 * OpenOrderListScreen'in kendisi hiç değişmedi, yalnız giriş noktası buraya taşındı.
 *
 * "Açık Satış Siparişleri" kartı [com.calibrahub.app.ui.shipping.ShippingHomeScreen]'deki kartla
 * AYNI route'a gider (koordinatör kararı: Satış/Sevkiyat kısmi örtüşme kabul, kullanıcı ayarlar).
 *
 * Kök modül ekranı olduğu için TopAppBar'da geri ok yerine hamburger (sol drawer'ı açar).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SalesHomeScreen(
    onOpenDeliverySales: () -> Unit,
    onOpenOpenOrdersSales: () -> Unit,
    onOpenDrawer: () -> Unit
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Satış") },
                navigationIcon = {
                    IconButton(onClick = onOpenDrawer) {
                        Icon(Icons.Default.Menu, contentDescription = "Menü")
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            SalesOperationCard(
                title = "Satış İrsaliyesi",
                subtitle = "Müşteriye giden malzeme irsaliyesi",
                icon = Icons.Default.ReceiptLong,
                onClick = onOpenDeliverySales
            )
            SalesOperationCard(
                title = "Açık Satış Siparişleri",
                subtitle = "Teslim edilmemiş sipariş kalemlerini teslim et",
                icon = Icons.Default.PendingActions,
                onClick = onOpenOpenOrdersSales
            )
        }
    }
}

@Composable
private fun SalesOperationCard(
    title: String,
    subtitle: String,
    icon: ImageVector,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .background(MaterialTheme.colorScheme.primaryContainer),
                contentAlignment = Alignment.Center
            ) {
                Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.onPrimaryContainer)
            }
            Spacer(Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                Text(
                    subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}

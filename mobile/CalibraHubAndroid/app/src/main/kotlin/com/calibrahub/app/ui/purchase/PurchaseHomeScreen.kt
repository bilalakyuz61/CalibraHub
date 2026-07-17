package com.calibrahub.app.ui.purchase

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
import androidx.compose.material.icons.automirrored.filled.Assignment
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.Menu
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
 * Satın Alma modülü ana ekranı (2026-07-17 drawer migration, FAZ D) — [com.calibrahub.app.ui.warehouse.WarehouseHomeScreen]'den
 * TAŞINAN iki kart: Alış İrsaliyesi + Açık Alış Siparişleri (Depo artık saf depo operasyonlarına
 * indirgendi). Rota hedefleri AYNEN korunur ("warehouse_delivery/purchase",
 * "warehouse_open_orders/purchase") — DeliveryScreen/OpenOrderListScreen'in kendisi hiç
 * değişmedi, yalnız bu kartlara giriş noktası buraya taşındı.
 *
 * Kök modül ekranı olduğu için TopAppBar'da geri ok yerine hamburger (sol drawer'ı açar) —
 * bkz. [com.calibrahub.app.ui.nav.AppDrawerContent].
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PurchaseHomeScreen(
    onOpenDeliveryPurchase: () -> Unit,
    onOpenOpenOrdersPurchase: () -> Unit,
    onOpenDrawer: () -> Unit
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Satın Alma") },
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
            PurchaseOperationCard(
                title = "Alış İrsaliyesi",
                subtitle = "Tedarikçiden gelen malzeme irsaliyesi",
                icon = Icons.Default.LocalShipping,
                onClick = onOpenDeliveryPurchase
            )
            PurchaseOperationCard(
                title = "Açık Alış Siparişleri",
                subtitle = "Mal kabul bekleyen sipariş kalemlerini teslim al",
                icon = Icons.AutoMirrored.Filled.Assignment,
                onClick = onOpenOpenOrdersPurchase
            )
        }
    }
}

@Composable
private fun PurchaseOperationCard(
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

package com.calibrahub.app.ui.shipping

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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * Sevkiyat modülü ana ekranı (2026-07-17 drawer migration, FAZ D — YENİ modül, koordinatör
 * spesifikasyonu). Tek odak: "Açık Satış Siparişleri → Teslim Et" — [com.calibrahub.app.ui.sales.SalesHomeScreen]'deki
 * "Açık Satış Siparişleri" kartıyla AYNI route'a ("warehouse_open_orders/sales") gider, yani AYNI
 * [com.calibrahub.app.ui.warehouse.OpenOrderListScreen] ekranını açar. Bilinçli kısmi örtüşme —
 * sevkiyat personeli Satış modülüne girmeden doğrudan teslimat akışına ulaşsın diye ayrı bir
 * drawer girişi olarak eklendi (koordinatör: "basit tut", ayrı bir ekran YAZILMADI).
 *
 * Kök modül ekranı olduğu için TopAppBar'da geri ok yerine hamburger (sol drawer'ı açar).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ShippingHomeScreen(
    onOpenOpenOrdersSales: () -> Unit,
    onOpenDrawer: () -> Unit
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Sevkiyat") },
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
            ShippingOperationCard(
                title = "Açık Satış Siparişleri",
                subtitle = "Teslim edilmemiş sipariş kalemlerini seç ve teslim et",
                onClick = onOpenOpenOrdersSales
            )
        }
    }
}

@Composable
private fun ShippingOperationCard(
    title: String,
    subtitle: String,
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
                Icon(Icons.Default.LocalShipping, contentDescription = null, tint = MaterialTheme.colorScheme.onPrimaryContainer)
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

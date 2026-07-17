package com.calibrahub.app.ui.warehouse

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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Assignment
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.FactCheck
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.MoveToInbox
import androidx.compose.material.icons.filled.Outbox
import androidx.compose.material.icons.filled.PendingActions
import androidx.compose.material.icons.filled.ReceiptLong
import androidx.compose.material.icons.filled.SwapHoriz
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
 * Depo modülü ana ekranı — operasyon listesi.
 * 2026-07-17 FAZ C: Açık Siparişler (Satış+Alış, 2 kart — DeliveryDocType parametreli TEK
 * OpenOrderListScreen'e navigate eder, Alış/Satış İrsaliyesi'nin AYNI iki-kart-tek-ekran deseni)
 * + Taslak Sayımlar eklendi (10/10 depo kartı). Kart sayısı arttığı için Column artık
 * kaydırılabilir ([verticalScroll]) — önceki 7 kartlık sürümde taşma riski yoktu, şimdi var.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WarehouseHomeScreen(
    onOpenStockQuery: () -> Unit,
    onOpenStockIn: () -> Unit,
    onOpenStockOut: () -> Unit,
    onOpenDeliveryPurchase: () -> Unit,
    onOpenDeliverySales: () -> Unit,
    onOpenTransfer: () -> Unit,
    onOpenCount: () -> Unit,
    onOpenOpenOrdersSales: () -> Unit,
    onOpenOpenOrdersPurchase: () -> Unit,
    onOpenDraftCounts: () -> Unit,
    onBack: () -> Unit
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Depo") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            WarehouseOperationCard(
                title = "Stok Sorgu",
                subtitle = "Malzeme koduna göre lokasyon bazlı bakiye",
                icon = Icons.Default.Inventory2,
                enabled = true,
                onClick = onOpenStockQuery
            )
            WarehouseOperationCard(
                title = "Giriş",
                subtitle = "Depoya mal giriş belgesi oluştur",
                icon = Icons.Default.MoveToInbox,
                enabled = true,
                onClick = onOpenStockIn
            )
            WarehouseOperationCard(
                title = "Çıkış",
                subtitle = "Depodan mal çıkış belgesi oluştur",
                icon = Icons.Default.Outbox,
                enabled = true,
                onClick = onOpenStockOut
            )
            WarehouseOperationCard(
                title = "Alış İrsaliyesi",
                subtitle = "Tedarikçiden gelen malzeme irsaliyesi",
                icon = Icons.Default.LocalShipping,
                enabled = true,
                onClick = onOpenDeliveryPurchase
            )
            WarehouseOperationCard(
                title = "Satış İrsaliyesi",
                subtitle = "Müşteriye giden malzeme irsaliyesi",
                icon = Icons.Default.ReceiptLong,
                enabled = true,
                onClick = onOpenDeliverySales
            )
            WarehouseOperationCard(
                title = "Açık Satış Siparişleri",
                subtitle = "Teslim edilmemiş sipariş kalemlerini teslim et",
                icon = Icons.Default.PendingActions,
                enabled = true,
                onClick = onOpenOpenOrdersSales
            )
            WarehouseOperationCard(
                title = "Açık Alış Siparişleri",
                subtitle = "Mal kabul bekleyen sipariş kalemlerini teslim al",
                icon = Icons.AutoMirrored.Filled.Assignment,
                enabled = true,
                onClick = onOpenOpenOrdersPurchase
            )
            WarehouseOperationCard(
                title = "Transfer",
                subtitle = "Lokasyonlar arası malzeme transferi",
                icon = Icons.Default.SwapHoriz,
                enabled = true,
                onClick = onOpenTransfer
            )
            WarehouseOperationCard(
                title = "Sayım",
                subtitle = "Fiziksel sayım ile bakiye karşılaştırma",
                icon = Icons.Default.Checklist,
                enabled = true,
                onClick = onOpenCount
            )
            WarehouseOperationCard(
                title = "Taslak Sayımlar",
                subtitle = "Yansıtılmamış sayım belgelerini stoğa işle",
                icon = Icons.AutoMirrored.Filled.FactCheck,
                enabled = true,
                onClick = onOpenDraftCounts
            )
        }
    }
}

@Composable
private fun WarehouseOperationCard(
    title: String,
    subtitle: String,
    icon: ImageVector,
    enabled: Boolean,
    onClick: () -> Unit
) {
    // Card'ın kendi disabled state'i yalnız container rengini/tıklanabilirliğini yönetir;
    // içerik renkleri (icon tint, text color) burada özel/marka renkleri kullandığı için
    // LocalContentColor'a bırakılmıyor — enabled=false'da hepsi elle soluklaştırılır.
    val contentAlpha = if (enabled) 1f else 0.5f
    Card(
        onClick = onClick,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceVariant,
            disabledContainerColor = MaterialTheme.colorScheme.surfaceVariant
        )
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
                    .background(MaterialTheme.colorScheme.primaryContainer.copy(alpha = contentAlpha)),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    icon,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = contentAlpha)
                )
            }
            Spacer(Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    title,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = contentAlpha)
                )
                Text(
                    subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = if (enabled) 1f else 0.8f)
                )
            }
        }
    }
}

package com.calibrahub.mobile.ui.warehouse

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.StockBalanceDto
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.session.SessionManager

/**
 * Depo → Stok Sorgu: malzeme rehberinden (kod veya ad ile arama) seçilen malzemenin lokasyon
 * bazlı bakiyesi. CalibraHubAndroid `ui/warehouse/StockQueryScreen.kt` ile BİREBİR sadık port —
 * Faz 2a'da POC `ui/StockScreen.kt`'nin GEÇİCİ olarak doldurduğu `WAREHOUSE_STOCK_QUERY` rotasının
 * yerini alır (bkz. AppNavHost). Tek platform farkı: `context.app.warehouseRepository` yerine
 * [session] parametresi üzerinden [SessionManager.warehouseRepository] (KMP'de Context/Application
 * yok, bkz. diğer commonMain ekranlarındaki AYNI desen).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StockQueryScreen(session: SessionManager, onBack: () -> Unit) {
    val repo = session.warehouseRepository

    var code by rememberSaveable { mutableStateOf("") }
    var result by remember { mutableStateOf<StockQueryDto?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Stok Sorgu") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
        ) {
            MaterialPickerField(
                query = code,
                onQueryChange = {
                    code = it
                    result = null
                    errorMessage = null
                },
                onResolved = { dto ->
                    result = dto
                    errorMessage = null
                },
                onResolveError = { msg ->
                    result = null
                    errorMessage = msg
                },
                repo = repo,
                enabled = true,
                modifier = Modifier.fillMaxWidth(),
            )

            Spacer(Modifier.height(24.dp))

            when {
                errorMessage != null -> {
                    StockQueryMessage(
                        icon = Icons.Default.ErrorOutline,
                        text = errorMessage!!,
                        tint = MaterialTheme.colorScheme.error,
                    )
                }
                result != null -> {
                    StockQueryResultView(result!!)
                }
                else -> {
                    StockQueryMessage(
                        icon = Icons.Default.Inventory2,
                        text = "Bakiyeyi görmek için malzeme kodu veya adı yazıp listeden seçin.",
                        tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
    }
}

@Composable
private fun StockQueryResultView(dto: StockQueryDto) {
    Column {
        Text(dto.itemName, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
        Text(
            text = dto.itemCode + (dto.unit?.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""),
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        // Barkod yalnizca GERCEKTEN girilmisse gosterilir — sunucu bos barkodu null'a
        // normalize eder (items/search'teki `Barcode ?? Code` fallback'i burada YOK).
        dto.barcode?.takeIf { it.isNotBlank() }?.let { barcode ->
            Spacer(Modifier.height(2.dp))
            Text(
                text = "Barkod: $barcode",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        trackingLabel(dto.trackingType, dto.autoSerial)?.let { label ->
            Spacer(Modifier.height(8.dp))
            TrackingBadge(label)
        }

        Spacer(Modifier.height(16.dp))

        if (dto.balances.isEmpty()) {
            StockQueryMessage(
                icon = Icons.Default.LocationOn,
                text = "Bu malzeme için hiçbir lokasyonda bakiye kaydı yok.",
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            // Toplam ONCE gelir: kullanicinin ilk aradigi sayi "elimde ne kadar var" —
            // lokasyon kirilimini kafadan toplamak zorunda kalmasin.
            TotalBalanceRow(dto.balances.sumOf { it.quantity }, dto.unit, dto.balances.size)
            Spacer(Modifier.height(12.dp))
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                dto.balances.forEach { balance -> StockBalanceRow(balance, dto.unit) }
            }
        }
    }
}

/** Takip tipi rozeti metni — takipsiz malzemede rozet HIC gosterilmez (gurultu olurdu). */
private fun trackingLabel(trackingType: String, autoSerial: Boolean): String? = when (trackingType) {
    "Lot" -> "Lot Takipli"
    "Serial" -> if (autoSerial) "Seri Takipli · Otomatik Seri" else "Seri Takipli"
    else -> null
}

@Composable
private fun TrackingBadge(label: String) {
    Surface(
        shape = MaterialTheme.shapes.small,
        color = MaterialTheme.colorScheme.secondaryContainer,
        contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelMedium,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
        )
    }
}

@Composable
private fun TotalBalanceRow(total: Double, unit: String?, locationCount: Int) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.primaryContainer,
            contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
        ),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(14.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text("Toplam Bakiye", style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
                Text(
                    text = if (locationCount == 1) "1 lokasyon" else "$locationCount lokasyon",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            Text(
                text = formatQty(total) + (unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
            )
        }
    }
}

@Composable
private fun StockBalanceRow(balance: StockBalanceDto, unit: String?) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(14.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                Icon(
                    Icons.Default.LocationOn,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(20.dp),
                )
                Spacer(Modifier.width(8.dp))
                Text(balance.locationName, style = MaterialTheme.typography.bodyLarge)
            }
            Text(
                text = formatQty(balance.quantity) + (unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )
        }
    }
}

@Composable
private fun StockQueryMessage(icon: ImageVector, text: String, tint: Color) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 32.dp, start = 16.dp, end = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(40.dp))
        Spacer(Modifier.height(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
    }
}

package com.calibrahub.app.ui.warehouse

import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.StockBalanceDto
import com.calibrahub.app.data.StockQueryDto
import kotlinx.coroutines.launch

/**
 * Depo → Stok Sorgu: malzeme kodu ile lokasyon bazlı bakiye sorgulama.
 * Increment 1: tek-yönlü canlı sorgu ekranı — offline cache yok, her sorgu /warehouse/stock
 * çağırır. Lokasyon sayısı tipik olarak küçük olduğundan LazyColumn yerine düz
 * Column + verticalScroll kullanıldı (sınırsız yükseklik kısıtı riskini de ortadan kaldırır).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StockQueryScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository
    val scope = rememberCoroutineScope()

    var code by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(false) }
    var result by remember { mutableStateOf<StockQueryDto?>(null) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    fun runQuery() {
        val trimmed = code.trim()
        if (trimmed.isBlank() || loading) return
        scope.launch {
            loading = true
            errorMessage = null
            repo.stock(trimmed).fold(
                onSuccess = { dto -> result = dto },
                onFailure = { e ->
                    result = null
                    errorMessage = e.message ?: "Sorgu başarısız"
                }
            )
            loading = false
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Stok Sorgu") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Geri")
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
                .padding(16.dp)
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = code,
                    onValueChange = { code = it },
                    label = { Text("Malzeme kodu") },
                    singleLine = true,
                    enabled = !loading,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { runQuery() }),
                    modifier = Modifier.weight(1f)
                )
                Spacer(Modifier.width(8.dp))
                FilledIconButton(
                    onClick = { runQuery() },
                    enabled = code.isNotBlank() && !loading
                ) {
                    Icon(Icons.Default.Search, contentDescription = "Sorgula")
                }
            }

            Spacer(Modifier.height(24.dp))

            when {
                loading -> {
                    Box(modifier = Modifier.fillMaxWidth().padding(top = 40.dp), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator()
                    }
                }
                errorMessage != null -> {
                    StockQueryMessage(
                        icon = Icons.Default.ErrorOutline,
                        text = errorMessage!!,
                        tint = MaterialTheme.colorScheme.error
                    )
                }
                result != null -> {
                    StockQueryResultView(result!!)
                }
                else -> {
                    StockQueryMessage(
                        icon = Icons.Default.Inventory2,
                        text = "Bakiyeyi görmek için malzeme kodu girip sorgulayın.",
                        tint = MaterialTheme.colorScheme.onSurfaceVariant
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
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Spacer(Modifier.height(16.dp))

        if (dto.balances.isEmpty()) {
            StockQueryMessage(
                icon = Icons.Default.LocationOn,
                text = "Bu malzeme için hiçbir lokasyonda bakiye kaydı yok.",
                tint = MaterialTheme.colorScheme.onSurfaceVariant
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                dto.balances.forEach { balance -> StockBalanceRow(balance, dto.unit) }
            }
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
            verticalAlignment = Alignment.CenterVertically
        ) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                Icon(
                    Icons.Default.LocationOn,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(20.dp)
                )
                Spacer(Modifier.width(8.dp))
                Text(balance.locationName, style = MaterialTheme.typography.bodyLarge)
            }
            Text(
                text = formatQuantity(balance.quantity) + (unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
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
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(40.dp))
        Spacer(Modifier.height(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = tint, textAlign = TextAlign.Center)
    }
}

/** Tam sayıları ".00" olmadan, ondalıklıları 2 haneye yuvarlayarak gösterir (V1 basit format). */
private fun formatQuantity(q: Double): String =
    if (q == q.toLong().toDouble()) q.toLong().toString() else "%.2f".format(q)

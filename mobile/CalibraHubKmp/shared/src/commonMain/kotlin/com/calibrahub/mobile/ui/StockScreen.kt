package com.calibrahub.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.ListItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.session.SessionManager
import kotlinx.coroutines.launch

@Composable
fun StockScreen(
    sessionManager: SessionManager,
    displayName: String?,
    onLogout: () -> Unit,
) {
    var code by remember { mutableStateOf("") }
    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var result by remember { mutableStateOf<StockQueryDto?>(null) }

    val scope = rememberCoroutineScope()

    fun onSorgulaClick() {
        isLoading = true
        errorMessage = null
        result = null
        scope.launch {
            sessionManager.warehouseRepository.stock(code).fold(
                onSuccess = {
                    isLoading = false
                    result = it
                },
                onFailure = {
                    isLoading = false
                    errorMessage = it.message ?: "Bağlantı hatası"
                },
            )
        }
    }

    Column(modifier = Modifier.fillMaxSize().padding(24.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text("Stok Sorgu${displayName?.let { " — $it" } ?: ""}")
            OutlinedButton(onClick = onLogout) {
                Text("Geri")
            }
        }
        Spacer(modifier = Modifier.height(16.dp))
        OutlinedTextField(
            value = code,
            onValueChange = { code = it },
            label = { Text("Malzeme kodu") },
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))
        Button(
            onClick = { onSorgulaClick() },
            enabled = !isLoading && code.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("Sorgula")
        }
        Spacer(modifier = Modifier.height(16.dp))

        if (isLoading) {
            Row(horizontalArrangement = Arrangement.Center, modifier = Modifier.fillMaxWidth()) {
                CircularProgressIndicator()
            }
        }

        errorMessage?.let {
            Text(text = it)
        }

        result?.let { stock ->
            Text("${stock.itemCode} — ${stock.itemName}")
            stock.unit?.let { Text("Birim: $it") }
            Spacer(modifier = Modifier.height(8.dp))
            HorizontalDivider()
            LazyColumn(modifier = Modifier.fillMaxWidth()) {
                items(stock.balances) { balance ->
                    ListItem(
                        headlineContent = { Text(balance.locationName) },
                        trailingContent = { Text(balance.quantity.toString()) },
                    )
                }
            }
        }
    }
}

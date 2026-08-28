package com.calibrahub.mobile.ui.warehouse

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.barcode.rememberBarcodeScanner
import kotlinx.coroutines.launch
import kotlin.math.abs

/**
 * Sayim lot/seri KIRILIM satiri — mobil yerel model.
 *
 * Sayim, giris/cikistan farkli bir modeldir: bir malzemenin rafta birden fazla lotu/partisi
 * olabilir, bu yuzden tek deger degil KIRILIM girilir ve kirilim toplami SAYILAN miktara
 * esit olmalidir (kural sunucuda — SqlStockDocRepository).
 *
 * [key] lot no ya da seri no; [qty] o lot/parti icin sayilan miktar.
 */
internal data class CountBreakdownRow(val key: String, val qty: Double)

/**
 * Sayimda lot/seri kirilim editoru.
 *
 * Neden ayri bir bilesen: giris/cikisin "tek lot / adet kadar seri" modeli sayimda ISE YARAMAZ.
 * Burada kullanici satir satir "lot + miktar" girer; bilesen toplami canli hesaplar ve sayilan
 * miktarla uyusmadiginda UYARIR — sunucuya gidip hata almadan once.
 *
 * Dogrulama TEKRARLANMAZ, yalnizca ONCEDEN gosterilir: nihai karar sunucunundur.
 */
@Composable
internal fun CountBreakdownSection(
    isSerial: Boolean,
    rows: List<CountBreakdownRow>,
    countedQuantity: Double,
    enabled: Boolean,
    onRowsChange: (List<CountBreakdownRow>) -> Unit,
    modifier: Modifier = Modifier,
) {
    val scope = rememberCoroutineScope()
    val scanner = rememberBarcodeScanner()

    var keyInput by remember { mutableStateOf("") }
    var qtyInput by remember { mutableStateOf("") }

    val label = if (isSerial) "Seri" else "Lot"
    val total = rows.sumOf { it.qty }
    // 0.0001 tolerans — sunucudaki karsilastirmayla AYNI esik (ondalik gosterim farki hataya donmesin).
    val matches = abs(total - countedQuantity) <= 0.0001
    val qtyValue = qtyInput.trim().replace(',', '.').toDoubleOrNull()
    val canAdd = enabled && keyInput.isNotBlank() && qtyValue != null && qtyValue > 0.0

    fun addRow() {
        val k = keyInput.trim()
        val q = qtyValue ?: return
        if (k.isEmpty() || q <= 0.0) return
        // Ayni lot/seri iki kez girilemez (sunucu da reddeder) — mevcut satira EKLEMEK yerine
        // acikca engellenir ki kullanici yanlislikla miktari ikiye katlamasin.
        if (rows.any { it.key.equals(k, ignoreCase = true) }) return
        onRowsChange(rows + CountBreakdownRow(k, q))
        keyInput = ""
        qtyInput = ""
    }

    Column(modifier = modifier.fillMaxWidth().padding(top = 10.dp)) {
        Text(
            text = "$label Kırılımı",
            style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.SemiBold,
        )
        Spacer(Modifier.height(6.dp))

        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = keyInput,
                onValueChange = { keyInput = it },
                label = { Text("$label No") },
                singleLine = true,
                enabled = enabled,
                trailingIcon = if (isSerial) {
                    {
                        IconButton(
                            onClick = {
                                scope.launch {
                                    scanner.scan()?.trim()?.takeIf { it.isNotEmpty() }?.let { keyInput = it }
                                }
                            },
                            enabled = enabled,
                        ) { Icon(Icons.Default.QrCodeScanner, contentDescription = "Seri okut") }
                    }
                } else null,
                modifier = Modifier.weight(2f),
            )
            Spacer(Modifier.width(8.dp))
            OutlinedTextField(
                value = qtyInput,
                onValueChange = { qtyInput = it },
                label = { Text("Miktar") },
                singleLine = true,
                enabled = enabled,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                modifier = Modifier.weight(1f),
            )
            Spacer(Modifier.width(8.dp))
            FilledIconButton(onClick = { addRow() }, enabled = canAdd) {
                Icon(Icons.Default.Add, contentDescription = "$label ekle")
            }
        }

        if (rows.isNotEmpty()) {
            Spacer(Modifier.height(8.dp))
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                rows.forEach { row ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(row.key, style = MaterialTheme.typography.bodyMedium,
                            modifier = Modifier.weight(1f))
                        Text(formatQty(row.qty), style = MaterialTheme.typography.bodyMedium,
                            fontWeight = FontWeight.Medium)
                        IconButton(
                            onClick = { onRowsChange(rows.filterNot { it.key == row.key }) },
                            enabled = enabled,
                        ) {
                            Icon(
                                Icons.Default.Delete,
                                contentDescription = "Sil",
                                tint = MaterialTheme.colorScheme.error,
                                modifier = Modifier.size(18.dp),
                            )
                        }
                    }
                }
            }
        }

        // Toplam gostergesi — sayilan miktarla uyusuyor mu? Sunucunun kuralini ONCEDEN gosterir.
        Spacer(Modifier.height(6.dp))
        Surface(
            shape = MaterialTheme.shapes.small,
            color = if (matches) MaterialTheme.colorScheme.secondaryContainer
                    else MaterialTheme.colorScheme.errorContainer,
            contentColor = if (matches) MaterialTheme.colorScheme.onSecondaryContainer
                           else MaterialTheme.colorScheme.onErrorContainer,
        ) {
            Text(
                text = if (matches) {
                    "Toplam ${formatQty(total)} — sayılan miktarla uyuşuyor"
                } else {
                    "Toplam ${formatQty(total)} / sayılan ${formatQty(countedQuantity)} — eşit olmalı"
                },
                style = MaterialTheme.typography.labelMedium,
                modifier = Modifier.padding(horizontal = 10.dp, vertical = 5.dp),
            )
        }
    }
}

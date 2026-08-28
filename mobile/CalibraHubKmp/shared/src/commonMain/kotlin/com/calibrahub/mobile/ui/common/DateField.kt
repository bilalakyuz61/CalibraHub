package com.calibrahub.mobile.ui.common

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

// ── Tarih yardimcilari (ORTAK) ──────────────────────────────────────────────
// Bu fonksiyonlar once ui/widgets/DynamicFields.kt icinde private idi. Belge ekranlarina da
// tarih alani eklenince ikinci kopya gerekecekti — DRY geregi buraya tasindi, DynamicFields
// artik buradan kullanir. `java.time` commonMain'de YOK; Howard Hinnant'in kutuphanesiz
// "days_from_civil"/"civil_from_days" tamsayi algoritmalari kullanilir (Android + iOS ayni sonuc).

internal const val MILLIS_PER_DAY = 86_400_000L

/** yyyy-MM-dd ISO string'i GG.AA.YYYY gosterime cevirir; bos/parse edilemezse null. */
internal fun isoDateToDisplay(iso: String): String? {
    if (iso.isBlank()) return null
    val (y, m, d) = parseIsoDateOrNull(iso) ?: return null
    return "${d.toString().padStart(2, '0')}.${m.toString().padStart(2, '0')}.${y.toString().padStart(4, '0')}"
}

/** ISO tarihi Material3 DatePickerState'in bekledigi UTC-epoch-millis'e cevirir (gun basi, UTC). */
internal fun isoDateToUtcMillis(iso: String): Long? {
    if (iso.isBlank()) return null
    val (y, m, d) = parseIsoDateOrNull(iso) ?: return null
    return daysFromCivilEpoch(y, m, d) * MILLIS_PER_DAY
}

/** DatePicker'in dondurdugu UTC-epoch-millis'i yyyy-MM-dd ISO string'e cevirir.
 * `DatePickerState.selectedDateMillis` DAIMA UTC yorumlanir (Material3 kontrati). */
internal fun utcMillisToIsoDate(millis: Long): String {
    val days = millis.floorDiv(MILLIS_PER_DAY)
    val (y, m, d) = civilFromDaysEpoch(days)
    return "${y.toString().padStart(4, '0')}-${m.toString().padStart(2, '0')}-${d.toString().padStart(2, '0')}"
}

/** "yyyy-MM-dd" -> (yil, ay, gun). Tam takvim dogrulamasi (30 Subat) BILINCLI atlanir —
 * alan yalniz DatePicker ile doldugundan pratikte hep gecerlidir. */
internal fun parseIsoDateOrNull(iso: String): Triple<Int, Int, Int>? {
    val parts = iso.split("-")
    if (parts.size != 3) return null
    val y = parts[0].toIntOrNull() ?: return null
    val m = parts[1].toIntOrNull() ?: return null
    val d = parts[2].toIntOrNull() ?: return null
    if (m !in 1..12 || d !in 1..31) return null
    return Triple(y, m, d)
}

internal fun daysFromCivilEpoch(y: Int, m: Int, d: Int): Long {
    val yy = (if (m <= 2) y - 1 else y).toLong()
    val era = (if (yy >= 0) yy else yy - 399) / 400
    val yoe = yy - era * 400
    val doy = (153L * (if (m > 2) m - 3 else m + 9) + 2) / 5 + d - 1
    val doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
    return era * 146097L + doe - 719468L
}

internal fun civilFromDaysEpoch(z: Long): Triple<Int, Int, Int> {
    val zz = z + 719468
    val era = (if (zz >= 0) zz else zz - 146096) / 146097
    val doe = zz - era * 146097
    val yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365
    val y = yoe + era * 400
    val doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
    val mp = (5 * doy + 2) / 153
    val d = (doy - (153 * mp + 2) / 5 + 1).toInt()
    val m = (if (mp < 10) mp + 3 else mp - 9).toInt()
    return Triple((if (m <= 2) y + 1 else y).toInt(), m, d)
}

/**
 * Belge tarihi alani — depo giris/cikis/transfer/sayim ekranlarinin ORTAK bileseni.
 *
 * NEDEN VAR: mobil bugune kadar belge tarihini SORMUYOR, sunucuya "simdi" yaziliyordu. Oysa
 * web ekrani kullanicidan tarih alir — sahada dun kesilen bir fisi bugun girmek normaldir.
 *
 * [value] "yyyy-MM-dd" ISO string; bos birakilirsa sunucu BUGUNu kullanir (geriye uyumlu).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DocumentDateField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String = "Belge Tarihi",
    enabled: Boolean = true,
    modifier: Modifier = Modifier,
) {
    var showPicker by remember { mutableStateOf(false) }
    val displayText = isoDateToDisplay(value)

    Card(
        onClick = { showPicker = true },
        enabled = enabled,
        modifier = modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceVariant,
            disabledContainerColor = MaterialTheme.colorScheme.surfaceVariant,
        ),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(
                Icons.Default.CalendarMonth,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
            )
            Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = label,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Text(
                    // Bos = bugun. Kullaniciya "secilmedi" degil, ne olacagi soylenir.
                    text = displayText ?: "Bugün",
                    style = MaterialTheme.typography.bodyLarge,
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }
    }

    if (showPicker) {
        val state = rememberDatePickerState(initialSelectedDateMillis = isoDateToUtcMillis(value))
        DatePickerDialog(
            onDismissRequest = { showPicker = false },
            confirmButton = {
                TextButton(onClick = {
                    state.selectedDateMillis?.let { onValueChange(utcMillisToIsoDate(it)) }
                    showPicker = false
                }) { Text("Tamam") }
            },
            dismissButton = {
                TextButton(onClick = { showPicker = false }) { Text("Vazgeç") }
            },
        ) {
            DatePicker(state = state)
        }
    }
}

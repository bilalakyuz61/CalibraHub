package com.calibrahub.mobile.ui.widgets

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
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
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.WidgetFieldDto
import com.calibrahub.mobile.ui.common.isoDateToDisplay
import com.calibrahub.mobile.ui.common.isoDateToUtcMillis
import com.calibrahub.mobile.ui.common.utcMillisToIsoDate
import com.calibrahub.mobile.data.WidgetOptionDto

/**
 * Mobil "ek saha" (dinamik WidgetMas alan) RENDERER'ı — CalibraHubAndroid
 * `ui/widgets/DynamicFields.kt` ile BİREBİR sadık port. Backend kontratı KİLİTLİ:
 * GET /api/mobile/widgets/schema?formCode= → [WidgetFieldDto] listesi.
 * V1 tüketicileri: StockDocScreen (STOCK_IN/STOCK_OUT), TransferScreen (TRANSFER),
 * CountScreen (INVENTORY_COUNT), DeliveryScreen (SALES_DELIVERY_EDIT/PURCHASE_DELIVERY_EDIT).
 *
 * **KMP sapması (java.time → manuel takvim aritmetiği):** Android sürümü `date` tipi alanı için
 * `java.time.LocalDate`/`Instant`/`ZoneOffset` kullanır — bunlar JVM'e özgüdür, commonMain'de
 * DERLENMEZ (iOS hedefinde çözülemez). Bu port yerine Howard Hinnant'in "days_from_civil" /
 * "civil_from_days" (herhangi bir kütüphane olmadan çalışan, standart/kanıtlanmış) tamsayı
 * algoritmalarını kullanır — bkz. dosya altındaki [daysFromCivilEpoch]/[civilFromDaysEpoch].
 * Çıktı/davranış Android ile BİREBİR AYNI (ISO yyyy-MM-dd saklama, UTC gün-başlangıcı millis).
 */

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DynamicFieldsSection(
    schema: List<WidgetFieldDto>,
    values: Map<String, String>,
    onChange: (key: String, value: String) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    showRequiredErrors: Boolean = false,
) {
    if (schema.isEmpty()) return
    val ordered = remember(schema) { schema.sortedBy { it.order } }
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ordered.forEach { field ->
            val rawValue = values[field.key] ?: ""
            val showError = showRequiredErrors && isFieldMissing(field, values)
            val label = if (field.required) "${field.label} *" else field.label
            when (field.type) {
                "textarea" -> DynamicTextField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError,
                    multiline = true,
                )
                "number" -> DynamicTextField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError,
                    keyboardType = KeyboardType.Decimal,
                )
                "date" -> DynamicDateField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError,
                )
                "select" -> DynamicSelectField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError,
                    options = field.options.orEmpty(),
                )
                "bool" -> DynamicBoolField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                )
                else -> DynamicTextField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError,
                )
            }
        }
    }
}

/** [field] zorunlu ama değeri eksik mi? `bool` tipi HER ZAMAN dolu sayılır (Android ile AYNI
 * gerekçe — bkz. Android KDoc). */
private fun isFieldMissing(field: WidgetFieldDto, values: Map<String, String>): Boolean =
    field.required && field.type != "bool" && values[field.key].isNullOrBlank()

/** Tüm zorunlu alanlar dolu mu? Host ekran Kaydet'e basıldığında çağırır. */
fun validateDynamicFields(schema: List<WidgetFieldDto>, values: Map<String, String>): Boolean =
    schema.none { isFieldMissing(it, values) }

/** [values] boşsa `null` döner (ADDITIVE backend sözleşmesi — Android ile AYNI). */
fun dynamicFieldsPayload(values: Map<String, String>): Map<String, String>? =
    if (values.isEmpty()) null else values

@Composable
private fun DynamicTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    showError: Boolean,
    multiline: Boolean = false,
    keyboardType: KeyboardType = KeyboardType.Text,
) {
    val supportingText: (@Composable () -> Unit)? =
        if (showError) { { Text("Bu alan zorunludur") } } else null
    if (multiline) {
        OutlinedTextField(
            value = value,
            onValueChange = onValueChange,
            label = { Text(label) },
            enabled = enabled,
            isError = showError,
            supportingText = supportingText,
            minLines = 3,
            maxLines = 6,
            modifier = Modifier.fillMaxWidth(),
        )
    } else {
        OutlinedTextField(
            value = value,
            onValueChange = onValueChange,
            label = { Text(label) },
            enabled = enabled,
            isError = showError,
            supportingText = supportingText,
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = keyboardType),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

/**
 * date tipi — "tıkla → diyalog aç" Card deseni (Android ile AYNI görünüm). Değer DAİMA ISO
 * yyyy-MM-dd string olarak saklanır (sözleşme).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DynamicDateField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    showError: Boolean,
) {
    var showPicker by remember { mutableStateOf(false) }
    val displayText = isoDateToDisplay(value)

    Column {
        Card(
            onClick = { showPicker = true },
            enabled = enabled,
            modifier = Modifier.fillMaxWidth(),
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
                    tint = if (showError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
                )
                Spacer(Modifier.width(10.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = label,
                        style = MaterialTheme.typography.bodySmall,
                        color = if (showError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Text(
                        text = displayText ?: "Tarih seçin",
                        style = MaterialTheme.typography.bodyLarge,
                        fontWeight = if (displayText != null) FontWeight.SemiBold else FontWeight.Normal,
                        color = if (displayText != null) MaterialTheme.colorScheme.onSurface
                                else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
        if (showError) {
            Text(
                text = "Bu alan zorunludur",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(start = 16.dp, top = 4.dp),
            )
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

/**
 * select tipi — [WidgetOptionDto.value] sunucuya giden/saklanan değer (ID-benzeri),
 * [WidgetOptionDto.label] kullanıcıya gösterilen metin. `ExposedDropdownMenu`/`.menuAnchor()`
 * BİLEREK import EDİLMEZ — bunlar `ExposedDropdownMenuBoxScope`'un üye fonksiyonu/uzantısıdır,
 * yalnızca `ExposedDropdownMenuBox { ... }` gövdesi içinde çözülür (Android ile AYNI gerekçe).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DynamicSelectField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    showError: Boolean,
    options: List<WidgetOptionDto>,
) {
    var expanded by remember { mutableStateOf(false) }
    val selectedLabel = options.firstOrNull { it.value == value }?.label ?: value

    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { if (enabled) expanded = it },
    ) {
        OutlinedTextField(
            value = selectedLabel,
            onValueChange = {},
            readOnly = true,
            label = { Text(label) },
            enabled = enabled,
            isError = showError,
            supportingText = if (showError) { { Text("Bu alan zorunludur") } } else null,
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
            modifier = Modifier.menuAnchor().fillMaxWidth(),
        )
        ExposedDropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false },
        ) {
            options.forEach { opt ->
                DropdownMenuItem(
                    text = { Text(opt.label) },
                    onClick = {
                        onValueChange(opt.value)
                        expanded = false
                    },
                )
            }
        }
    }
}

/** bool tipi — checkbox DEĞİL Switch kullanılır (CLAUDE.md: boolean alanlar için switchkey
 * zorunlu). Değer sözleşme gereği "true"/"false" küçük harf string. */
@Composable
private fun DynamicBoolField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
) {
    val checked = value.equals("true", ignoreCase = true)
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodyLarge,
            modifier = Modifier.weight(1f),
        )
        Switch(
            checked = checked,
            onCheckedChange = { onValueChange(it.toString()) },
            enabled = enabled,
        )
    }
}

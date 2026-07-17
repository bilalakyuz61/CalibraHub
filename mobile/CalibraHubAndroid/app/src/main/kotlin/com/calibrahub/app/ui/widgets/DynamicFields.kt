package com.calibrahub.app.ui.widgets

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
import com.calibrahub.app.data.WidgetFieldDto
import com.calibrahub.app.data.WidgetOptionDto
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

/**
 * Mobil "ek saha" (dinamik WidgetMas alan) RENDERER'ı — backend kontratı KİLİTLİ:
 * GET /api/mobile/widgets/schema?formCode= → [WidgetFieldDto] listesi (bkz. WarehouseApi.kt).
 * V1 tüketicileri: StockDocScreen (STOCK_IN/STOCK_OUT), TransferScreen (TRANSFER),
 * CountScreen (INVENTORY_COUNT), DeliveryScreen (SALES_DELIVERY_EDIT/PURCHASE_DELIVERY_EDIT)
 * — hepsi belge ÜST-BİLGİsi için bu bölümü mount eder.
 *
 * Kullanım deseni (host ekran):
 * 1) `LaunchedEffect` içinde `repo.getWidgetSchema(formCode)` çağrılır, sonuç `widgetSchema`
 *    state'ine yazılır (hata/permission/404 durumları [com.calibrahub.app.data.WarehouseRepository.getWidgetSchema]
 *    içinde SESSİZCE boş listeye düşürülür — bu bölüm opsiyonel/best-effort'tur, ana belge
 *    akışını bloklamaz).
 * 2) Kullanıcı girdileri `widgetValues: Map<String, String>` state'inde (key = [WidgetFieldDto.key])
 *    hoisted tutulur; [DynamicFieldsSection] bu map'i SADECE okur/değiştirme isteği bildirir
 *    ([onChange]), kendi state'i yoktur (stateless composable + state hoisting).
 * 3) Kaydet butonuna basılınca host [validateDynamicFields] ile zorunlu alanları kontrol eder;
 *    false dönerse kaydı ENGELLER + kendi mevcut hata gösterim deseniyle (inline kart/snackbar)
 *    kullanıcıyı bilgilendirir, `showRequiredErrors = true` yaparak alan bazlı kırmızı
 *    işaretlemeyi de tetikler.
 * 4) Geçerliyse [dynamicFieldsPayload] ile `Map<String,String>?` payload üretilir (boşsa null)
 *    ve ilgili repository fonksiyonunun (stockIn/stockOut/transfer/inventoryCount/delivery)
 *    `extraFields` parametresine geçirilir. Yazım NON-ATOMIC'tir: sunucu yanıtındaki
 *    `extraFieldsError` doluysa belge YİNE DE başarılı sayılır, host ekran bunu NON-BLOCKING
 *    bir uyarı olarak (başarı diyaloğu/snackbar) gösterir — belge kaydı asla geri alınmaz.
 */

/**
 * [schema]'daki her alanı [WidgetFieldDto.order]'a göre sıralı render eder. Alan yoksa
 * (boş liste) HİÇBİR ŞEY çizmez — host ekran bu durumda sarmalayan Card'ı da hiç göstermemelidir.
 *
 * @param values host'ta tutulan güncel değerler (key = [WidgetFieldDto.key]).
 * @param onChange kullanıcı bir alanı değiştirdiğinde tetiklenir; host [values] map'ini günceller.
 * @param showRequiredErrors true ise, boş bırakılmış zorunlu alanlar kırmızı/`isError` olarak
 * işaretlenir (host bir kaydetme denemesi başarısız olunca bunu true yapar — bkz. dosya üstü KDoc).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DynamicFieldsSection(
    schema: List<WidgetFieldDto>,
    values: Map<String, String>,
    onChange: (key: String, value: String) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    showRequiredErrors: Boolean = false
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
                    multiline = true
                )
                // number: KeyboardType.Decimal BİLİNÇLİ seçildi (talimattaki "Number" yerine) —
                // Android'in salt "Number" klavyesi ondalık ayıracı GÖSTERMEZ; widget alanı
                // fiyat/ağırlık gibi ondalıklı bir değer taşıyorsa kullanıcı onu hiç giremezdi.
                // Uygulamanın kendi miktar alanları da (StockDocScreen/TransferScreen/CountScreen
                // "Miktar") aynı gerekçeyle Decimal kullanıyor; tutarlılık için aynı seçim yapıldı.
                "number" -> DynamicTextField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError,
                    keyboardType = KeyboardType.Decimal
                )
                "date" -> DynamicDateField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError
                )
                "select" -> DynamicSelectField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError,
                    options = field.options.orEmpty()
                )
                "bool" -> DynamicBoolField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled
                )
                // "text" ve (kontrat dışı) bilinmeyen bir tip için güvenli metin alanı fallback'i —
                // WidgetFieldDto.type BİLİNÇLİ olarak String tutulur (bkz. o DTO'nun KDoc'u),
                // burada asla exception fırlatılmaz.
                else -> DynamicTextField(
                    value = rawValue,
                    onValueChange = { onChange(field.key, it) },
                    label = label,
                    enabled = enabled,
                    showError = showError
                )
            }
        }
    }
}

/** [field] zorunlu ama değeri eksik mi? `bool` tipi HER ZAMAN dolu sayılır — bir Switch'in
 * doğal "false" durumu da geçerli bir cevaptır, EAV alanında "hiç dokunulmadı" ile "false
 * seçildi" mobilde ayırt edilemez/edilmesi gerekmez (kasıtlı basitleştirme). */
private fun isFieldMissing(field: WidgetFieldDto, values: Map<String, String>): Boolean =
    field.required && field.type != "bool" && values[field.key].isNullOrBlank()

/**
 * Tüm zorunlu alanlar dolu mu? Host ekran Kaydet'e basıldığında bu fonksiyonu çağırır; false
 * dönerse belge kaydını ENGELLER ve kendi mevcut hata gösterim deseniyle (inline hata kartı
 * veya snackbar — ekrana göre değişir) kullanıcıyı bilgilendirir.
 */
fun validateDynamicFields(schema: List<WidgetFieldDto>, values: Map<String, String>): Boolean =
    schema.none { isFieldMissing(it, values) }

/**
 * [values] boşsa `null` döner — backend sözleşmesi "extraFields null/boş bırakılırsa hiçbir
 * şey yazılmaz, eski davranış birebir korunur" (ADDITIVE). Host ekranlar save() çağrısında bu
 * fonksiyonun sonucunu ilgili repository fonksiyonunun (stockIn/stockOut/transfer/
 * inventoryCount/delivery) `extraFields` parametresine geçirir.
 */
fun dynamicFieldsPayload(values: Map<String, String>): Map<String, String>? =
    if (values.isEmpty()) null else values

/** text/textarea/number/fallback ortak metin alanı. `singleLine` ile `minLines`/`maxLines`
 * AYNI ANDA verilmez (Material3 OutlinedTextField bu ikisini karıştırınca tutarsız olur) —
 * bu yüzden iki dal TAM AYRI çağrılara bölündü (StockDocScreen'in "Miktar" tekil-satır alanı
 * ile "Not" çok-satır alanının kendi ayrı çağrıları izlenerek). */
@Composable
private fun DynamicTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    showError: Boolean,
    multiline: Boolean = false,
    keyboardType: KeyboardType = KeyboardType.Text
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
            modifier = Modifier.fillMaxWidth()
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
            modifier = Modifier.fillMaxWidth()
        )
    }
}

/**
 * date tipi — uygulamada henüz bir tarih GİRİŞ (input) alanı bulunmuyor (mevcut ekranlar
 * tarihi yalnız salt-okunur gösteriyor, bkz. DeliverySerialLotSection.formatPlannedDate);
 * bu yüzden StockDocScreen/TransferScreen/CountScreen'de ZATEN kullanılan "tıkla → diyalog
 * aç" Card deseni (LocationSelectorCard) buraya taşındı — hem kanıtlanmış/derlenen bir
 * desen hem de OutlinedTextField(readOnly=true) + clickable modifier kombinasyonunun (dokunma
 * olayı text-field tarafından yutulabilir) belirsizliğinden kaçınır. Değer DAİMA ISO
 * yyyy-MM-dd string olarak saklanır (sözleşme).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DynamicDateField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    showError: Boolean
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
                disabledContainerColor = MaterialTheme.colorScheme.surfaceVariant
            )
        ) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(14.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(
                    Icons.Default.CalendarMonth,
                    contentDescription = null,
                    tint = if (showError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary
                )
                Spacer(Modifier.width(10.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = label,
                        style = MaterialTheme.typography.bodySmall,
                        color = if (showError) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text(
                        text = displayText ?: "Tarih seçin",
                        style = MaterialTheme.typography.bodyLarge,
                        fontWeight = if (displayText != null) FontWeight.SemiBold else FontWeight.Normal,
                        color = if (displayText != null) MaterialTheme.colorScheme.onSurface
                                else MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }
        if (showError) {
            Text(
                text = "Bu alan zorunludur",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(start = 16.dp, top = 4.dp)
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
            }
        ) {
            DatePicker(state = state)
        }
    }
}

/** yyyy-MM-dd ISO string'i GG.AA.YYYY gösterime çevirir; boş/parse edilemezse null döner
 * (DeliverySerialLotSection.formatPlannedDate ile aynı yönde bağımsız kopya — StockDocScreen/
 * TransferScreen/CountScreen'deki formatQty ikilemesiyle aynı gerekçe: dosyalar arası private
 * yardımcı paylaşılmaz). */
private fun isoDateToDisplay(iso: String): String? {
    if (iso.isBlank()) return null
    return try {
        val date = LocalDate.parse(iso)
        "%02d.%02d.%04d".format(date.dayOfMonth, date.monthValue, date.year)
    } catch (e: Exception) {
        null
    }
}

/** ISO tarihi Material3 DatePickerState'in beklediği UTC-epoch-millis'e çevirir (gün başlangıcı, UTC). */
private fun isoDateToUtcMillis(iso: String): Long? {
    if (iso.isBlank()) return null
    return try {
        LocalDate.parse(iso).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
    } catch (e: Exception) {
        null
    }
}

/** DatePicker'ın döndürdüğü UTC-epoch-millis'i (gün başlangıcı) yyyy-MM-dd ISO string'e çevirir.
 * `DatePickerState.selectedDateMillis` DAİMA UTC olarak yorumlanmalıdır (Material3 kontratı) —
 * yerel saat dilimiyle yorumlanırsa gün kayması (off-by-one) oluşur. */
private fun utcMillisToIsoDate(millis: Long): String =
    Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate().toString()

/**
 * select tipi — [WidgetOptionDto.value] sunucuya giden/saklanan değer (ID-benzeri),
 * [WidgetOptionDto.label] kullanıcıya gösterilen metin (CLAUDE.md "ID tabanlı eşleştirme"
 * kuralıyla tutarlı: karar/karşılaştırma value üzerinden, gösterim label üzerinden).
 * MaterialPickerField.kt'deki ExposedDropdownMenuBox iskeleti izlenir; `ExposedDropdownMenu`
 * ve `.menuAnchor()` BİLEREK import EDİLMEZ — bunlar `ExposedDropdownMenuBoxScope`'un üye
 * fonksiyonu/uzantısıdır, yalnızca `ExposedDropdownMenuBox { ... }` gövdesi içinde çözülür.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DynamicSelectField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean,
    showError: Boolean,
    options: List<WidgetOptionDto>
) {
    var expanded by remember { mutableStateOf(false) }
    val selectedLabel = options.firstOrNull { it.value == value }?.label ?: value

    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { if (enabled) expanded = it }
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
            modifier = Modifier.menuAnchor().fillMaxWidth()
        )
        ExposedDropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false }
        ) {
            options.forEach { opt ->
                DropdownMenuItem(
                    text = { Text(opt.label) },
                    onClick = {
                        onValueChange(opt.value)
                        expanded = false
                    }
                )
            }
        }
    }
}

/** bool tipi — checkbox DEĞİL Switch kullanılır (CLAUDE.md: boolean alanlar için switchkey
 * zorunlu). Değer sözleşme gereği "true"/"false" küçük harf string; `Boolean.toString()`
 * Kotlin'de zaten küçük harf üretir. */
@Composable
private fun DynamicBoolField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    enabled: Boolean
) {
    val checked = value.equals("true", ignoreCase = true)
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodyLarge,
            modifier = Modifier.weight(1f)
        )
        Switch(
            checked = checked,
            onCheckedChange = { onValueChange(it.toString()) },
            enabled = enabled
        )
    }
}

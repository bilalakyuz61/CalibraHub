package com.calibrahub.app.ui.warehouse

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ReceiptLong
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Contacts
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.ContactSearchDto
import com.calibrahub.app.data.DeliveryLineRequest
import com.calibrahub.app.data.DeliveryLineResultDto
import com.calibrahub.app.data.DeliveryResult
import com.calibrahub.app.data.StockQueryDto
import kotlinx.coroutines.launch
import kotlin.math.roundToInt

/**
 * İrsaliye ekranının belge yönü — Alış (tedarikçiden gelen) / Satış (müşteriye giden).
 * [apiValue] sunucuya AYNEN gönderilen `docType` string'i (koordinatör sözleşmesi KİLİTLİ,
 * enum-benzeri: "purchase" | "sales" — web tarafındaki aynı sözleşmeyle senkron tutulmalı).
 * [screenTitle]/[successTitle] yalnız ekran metnidir, sunucuya gitmez.
 */
enum class DeliveryDocType(val apiValue: String, val screenTitle: String, val successTitle: String) {
    PURCHASE("purchase", "Alış İrsaliyesi", "Alış İrsaliyesi Oluşturuldu"),
    SALES("sales", "Satış İrsaliyesi", "Satış İrsaliyesi Oluşturuldu")
}

/**
 * Satır listesinin UI modeli — StockDocScreen'in DocLineUi'ıyla aynı temel alan seti + irsaliyeye
 * özel Seri/Lot alanları. Sunucuya [DeliveryLineRequest] ile gider (itemId/quantity/serials/
 * lotCode/autoGenerateSerials); kod/ad/birim hem kart gösterimi hem başarı diyaloğundaki satır
 * eşlemesi içindir.
 *
 * [serials]/[lotCode]/[autoGenerateSerials] (2026-07-17 FINAL kontrat) — `addLine()` bunları
 * [resolveTrackingType] sonucuna göre doldurur: takipsiz malzemede hepsi null; SERIAL takipte
 * `serials` (satışta opsiyonel seçim, boş bırakılabilir), ALIŞ+SERIAL'de ayrıca
 * `autoGenerateSerials`; LOT takipte `lotCode`.
 */
private data class DeliveryLineUi(
    val itemId: Int,
    val itemCode: String,
    val itemName: String,
    val unit: String?,
    val quantity: Double,
    val serials: List<String>? = null,
    val lotCode: String? = null,
    val autoGenerateSerials: Boolean? = null
)

/**
 * Seri/Lot mobil yerel temsil — backend Items.TrackingType ("None"|"Lot"|"Serial", bkz.
 * MobileWarehouseApiController.ValidateWriteItemsAsync) ile AYNI 3 değer üzerine kurulu.
 *
 * 2026-07-17 FINAL kontrat: [StockQueryDto.trackingType] artık gerçek alanı taşıyor — bu
 * fonksiyon [trackingTypeFromString] üzerinden onu okur (OpenOrderDetailScreen ile PAYLAŞILAN
 * dönüşüm — bkz. DeliverySerialLotSection.kt).
 */
private fun resolveTrackingType(item: StockQueryDto): ItemTrackingType = trackingTypeFromString(item.trackingType)

/**
 * Alış İrsaliyesi / Satış İrsaliyesi ekranı — TEK composable, [docType] parametresiyle iki yönü
 * de kapsar (StockDocScreen'in StockDocMode desenindeki AYNI yaklaşım: ayrı ekran yerine tek
 * ekran + parametre).
 *
 * Akış: Cari seç ([ContactPickerField], kod/ad ile ara) → [MaterialPickerField] ile malzeme
 * ara/seç (kod/ad/barkod, StockDocScreen/TransferScreen ile PAYLAŞILAN bileşen) → miktar gir →
 * satıra ekle → (opsiyonel not) → Kaydet = POST delivery. Sunucu satırları açık siparişlere
 * FIFO bağlar; başarı yanıtındaki `lines[]` (itemId + linked[] + unlinkedQuantity) başarı
 * diyaloğunda satır bazında "→ SIP-...'e 5, SIP-...'e 3 bağlandı; 2 bağlantısız" biçiminde
 * özetlenir (bkz. [buildDeliveryLinkSummary]). "Bağlantısız yasak" parametresi sunucuda açıksa
 * eşleşmeyen kayıt TÜMDEN 400/404 {error} ile reddedilir — bu durum diğer iş kuralı redleriyle
 * AYNI kanaldan (inline hata kartı) gösterilir, ayrık bir UI dalı YOK.
 *
 * StockDocScreen'den kasıtlı fark: bu ekranda lokasyon seçimi YOK — sözleşmede `locationId`
 * alanı yok, sunucu hedef/kaynak lokasyonu kendi tarafında çözer. Başarı sonrası "Yeni Belge"
 * cari seçimini KORUR (StockDocScreen'in lokasyon korumasıyla aynı gerekçe: art arda aynı
 * cariye belge girişi), yalnız kalem listesi/not sıfırlanır.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeliveryScreen(docType: DeliveryDocType, onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = context.app.warehouseRepository
    val scope = rememberCoroutineScope()

    // ── Cari seçimi ──────────────────────────────────────────────────────
    var contactQuery by rememberSaveable { mutableStateOf("") }
    var resolvedContact by remember { mutableStateOf<ContactSearchDto?>(null) }
    var showContactPicker by remember { mutableStateOf(false) }

    // ── Kalem ekleme formu (rehber ile ara/seç → miktar) ────────────────────
    var code by rememberSaveable { mutableStateOf("") }
    var resolved by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by remember { mutableStateOf("") }

    // ── Seri/Lot iskeleti (kontrat bekliyor) — bkz. resolveTrackingType() / DeliverySerialLotSection.kt.
    // resolveTrackingType() daima NONE döndüğü sürece bu state pratikte hiç dolmaz/kullanılmaz.
    var pendingSerials by remember { mutableStateOf(listOf<String>()) }
    var pendingAutoSerial by remember { mutableStateOf(true) }
    var pendingLot by remember { mutableStateOf("") }
    var showSerialPicker by remember { mutableStateOf(false) }

    // ── Belge durumu ────────────────────────────────────────────────────────
    var lines by remember { mutableStateOf(listOf<DeliveryLineUi>()) }
    var note by remember { mutableStateOf("") }
    // ALIŞ modunda tedarikçinin KENDİ irsaliye numarası (opsiyonel) — bkz. DeliveryRequest
    // üstü KDoc (externalRefNumber, backend sözleşmesi henüz YOK).
    var externalRefNumber by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<String?>(null) }
    var successResult by remember { mutableStateOf<DeliveryResult?>(null) }
    // Son başarılı kayıt belge no'su — "Yeni Belge" lines/note'u sıfırlasa da BİLİNÇLİ KORUNUR;
    // ekran başlığının altındaki kalıcı çipte gösterilir (bkz. Column içindeki InfoBadge çağrısı).
    var lastDocumentNumber by rememberSaveable { mutableStateOf<String?>(null) }

    // Miktar TR klavyede virgülle de girilebilir — nokta ile normalize edilip parse edilir.
    val qtyValue = qtyText.trim().replace(',', '.').toDoubleOrNull()
    val qtyValid = qtyValue != null && qtyValue > 0.0
    val targetQtyInt = (qtyValue ?: 0.0).roundToInt()

    // Seri-adet ön-kontrolü (client-side, sunucu 400'ünün AYNI kuralının erken uygulanması) —
    // SalesSerialTrackingRow/PurchaseSerialEntryRow'un KENDİ iç mismatch hesabıyla BİREBİR aynı
    // formül; "Ekle" butonunu kilitler. Boş seçim (otomatik) veya AutoSerial ile üretim HER ZAMAN
    // geçerlidir — yalnız "bir kısmını seçtim ama adet tutmuyor" durumu engellenir.
    val serialMismatch: Boolean = run {
        val item = resolved ?: return@run false
        if (resolveTrackingType(item) != ItemTrackingType.SERIAL) return@run false
        if (docType == DeliveryDocType.SALES) {
            pendingSerials.isNotEmpty() && targetQtyInt > 0 && pendingSerials.size != targetQtyInt
        } else {
            val effectiveAuto = pendingAutoSerial && item.autoSerial
            !effectiveAuto && pendingSerials.isNotEmpty() && targetQtyInt > 0 && pendingSerials.size != targetQtyInt
        }
    }

    fun addLine() {
        val item = resolved ?: return
        val qty = qtyValue
        if (qty == null || qty <= 0.0 || saving || serialMismatch) return
        val tracking = resolveTrackingType(item)
        lines = lines + DeliveryLineUi(
            itemId = item.itemId,
            itemCode = item.itemCode,
            itemName = item.itemName,
            unit = item.unit,
            quantity = qty,
            // 2026-07-17 FINAL kontrat — tracking dışı satırlarda hepsi null (backend'e hiç
            // gönderilmez); SERIAL/LOT'ta yalnız ilgili alan(lar) doldurulur.
            serials = if (tracking == ItemTrackingType.SERIAL) pendingSerials else null,
            lotCode = if (tracking == ItemTrackingType.LOT) pendingLot.trim().takeIf { it.isNotBlank() } else null,
            autoGenerateSerials = if (tracking == ItemTrackingType.SERIAL && docType == DeliveryDocType.PURCHASE)
                pendingAutoSerial else null
        )
        // Form sıradaki kalem için sıfırlanır; önceki kaydetme hatası da bayatladı.
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        saveError = null
        pendingSerials = emptyList()
        pendingAutoSerial = true
        pendingLot = ""
    }

    fun resetForNewDoc() {
        lines = emptyList()
        note = ""
        externalRefNumber = ""
        code = ""
        qtyText = ""
        resolved = null
        resolveError = null
        saveError = null
        pendingSerials = emptyList()
        pendingAutoSerial = true
        pendingLot = ""
        // resolvedContact/contactQuery/lastDocumentNumber BİLİNÇLİ KORUNUR — bkz. dosya üstü KDoc.
    }

    fun save() {
        val contact = resolvedContact ?: return
        if (lines.isEmpty() || saving) return
        scope.launch {
            saving = true
            saveError = null
            // 2026-07-17 FINAL kontrat — DeliveryLineRequest satır başına serials/lotCode/
            // autoGenerateSerials taşır (addLine() zaten tracking'e göre doldurdu).
            val reqLines = lines.map {
                DeliveryLineRequest(
                    itemId = it.itemId,
                    quantity = it.quantity,
                    serials = it.serials,
                    lotCode = it.lotCode,
                    autoGenerateSerials = it.autoGenerateSerials
                )
            }
            val noteOrNull = note.trim().takeIf { it.isNotBlank() }
            // externalRefNumber yalnız ALIŞ modunda anlamlı (bkz. alan üstü KDoc + Cari kartındaki
            // "Tedarikçi İrsaliye No" alanı); SATIŞ'ta her zaman null gönderilir.
            val refOrNull = if (docType == DeliveryDocType.PURCHASE)
                externalRefNumber.trim().takeIf { it.isNotBlank() } else null
            repo.delivery(docType.apiValue, contact.id, reqLines, noteOrNull, refOrNull).fold(
                onSuccess = {
                    successResult = it
                    lastDocumentNumber = it.documentNumber
                },
                onFailure = { saveError = it.message ?: "Kaydetme başarısız" }
            )
            saving = false
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(docType.screenTitle) },
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
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // ── Son Belge çipi ──────────────────────────────────────────────
            // Kayıt başarılı olunca dolar, "Yeni Belge" ile lines/note sıfırlansa da KORUNUR
            // (bkz. lastDocumentNumber tanımı üstü KDoc) — başlığın hemen altında kalıcı iz.
            if (lastDocumentNumber != null) {
                InfoBadge(text = "Son Belge: $lastDocumentNumber", icon = Icons.AutoMirrored.Filled.ReceiptLong)
            }

            // ── 1) Cari ──────────────────────────────────────────────────
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
                    Text(
                        text = "Cari",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(Modifier.height(10.dp))

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        ContactPickerField(
                            query = contactQuery,
                            onQueryChange = {
                                contactQuery = it
                                // Kod/ad değişti → önceki seçim bayat.
                                resolvedContact = null
                            },
                            onSelected = { dto -> resolvedContact = dto },
                            repo = repo,
                            enabled = !saving,
                            modifier = Modifier.weight(1f)
                        )
                        Spacer(Modifier.width(8.dp))
                        // Rehber-browse (tam ekran diyalog) — type-ahead'in YANINDA, StockDocScreen'in
                        // lokasyon-seçim butonuyla AYNI ruhta (küçük/kısa listede değil, büyük
                        // cari listesinde "gözat" ihtiyacı için). Mevcut type-ahead davranışı
                        // BİRE BİR korunur — bu buton yalnız ALTERNATİF bir seçim yolu ekler.
                        FilledTonalIconButton(
                            onClick = { showContactPicker = true },
                            enabled = !saving
                        ) {
                            Icon(Icons.Default.Contacts, contentDescription = "Cari rehberinden seç")
                        }
                    }

                    val contact = resolvedContact
                    if (contact != null) {
                        Spacer(Modifier.height(10.dp))
                        Text(contact.name, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                        Text(
                            text = contact.code,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }

                    // ALIŞ modunda tedarikçinin KENDİ irsaliye numarası (opsiyonel) — bkz.
                    // externalRefNumber tanımı + DeliveryRequest üstü KDoc (backend sözleşmesi
                    // henüz YOK, koordinatör teyidi bekleniyor).
                    if (docType == DeliveryDocType.PURCHASE) {
                        Spacer(Modifier.height(10.dp))
                        OutlinedTextField(
                            value = externalRefNumber,
                            onValueChange = { externalRefNumber = it },
                            label = { Text("Tedarikçi İrsaliye No") },
                            placeholder = { Text("Opsiyonel") },
                            singleLine = true,
                            enabled = !saving,
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                }
            }

            // ── 2) Kalem ekleme ────────────────────────────────────────────
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
                    Text(
                        text = "Kalem Ekle",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(Modifier.height(10.dp))

                    MaterialPickerField(
                        query = code,
                        onQueryChange = {
                            code = it
                            // Kod/ad değişti → önceki çözüm bayat; Ekle butonu kapanır.
                            resolved = null
                            resolveError = null
                            // Önceki malzemeye ait Seri/Lot iskelet seçimleri de bayatladı.
                            pendingSerials = emptyList()
                            pendingAutoSerial = true
                            pendingLot = ""
                        },
                        onResolved = { dto ->
                            resolved = dto
                            resolveError = null
                            // "Otomatik üret" varsayılanı malzemenin AutoSerial yeteneğini izler
                            // (bkz. PurchaseSerialEntryRow.autoGenerateAvailable).
                            pendingAutoSerial = dto.autoSerial
                        },
                        onResolveError = { msg ->
                            resolved = null
                            resolveError = msg
                        },
                        repo = repo,
                        enabled = !saving,
                        modifier = Modifier.fillMaxWidth()
                    )

                    if (resolveError != null) {
                        Spacer(Modifier.height(8.dp))
                        Text(
                            text = resolveError!!,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error
                        )
                    }

                    val item = resolved
                    if (item != null) {
                        Spacer(Modifier.height(12.dp))
                        Text(
                            text = item.itemName,
                            style = MaterialTheme.typography.bodyLarge,
                            fontWeight = FontWeight.SemiBold
                        )
                        Text(
                            text = item.itemCode +
                                (item.unit?.takeIf { it.isNotBlank() }?.let { " · $it" } ?: ""),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )

                        Spacer(Modifier.height(10.dp))
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            OutlinedTextField(
                                value = qtyText,
                                onValueChange = { qtyText = it },
                                label = {
                                    Text("Miktar" +
                                        (item.unit?.takeIf { it.isNotBlank() }?.let { " ($it)" } ?: ""))
                                },
                                singleLine = true,
                                enabled = !saving,
                                isError = qtyText.isNotBlank() && !qtyValid,
                                keyboardOptions = KeyboardOptions(
                                    keyboardType = KeyboardType.Decimal,
                                    imeAction = ImeAction.Done
                                ),
                                keyboardActions = KeyboardActions(onDone = { addLine() }),
                                modifier = Modifier.weight(1f)
                            )
                            Spacer(Modifier.width(8.dp))
                            FilledIconButton(
                                onClick = { addLine() },
                                enabled = qtyValid && !saving && !serialMismatch
                            ) {
                                Icon(Icons.Default.Add, contentDescription = "Satıra ekle")
                            }
                        }

                        // ── Seri/Lot bölümü (2026-07-17 FINAL kontrat — UYANDIRILDI) ───────
                        when (resolveTrackingType(item)) {
                            ItemTrackingType.SERIAL -> if (docType == DeliveryDocType.SALES) {
                                SalesSerialTrackingRow(
                                    selectedSerials = pendingSerials,
                                    targetQuantity = targetQtyInt,
                                    enabled = !saving,
                                    onOpenPicker = { showSerialPicker = true }
                                )
                            } else {
                                PurchaseSerialEntryRow(
                                    serials = pendingSerials,
                                    autoGenerate = pendingAutoSerial,
                                    autoGenerateAvailable = item.autoSerial,
                                    targetQuantity = targetQtyInt,
                                    enabled = !saving,
                                    onAddSerial = { pendingSerials = pendingSerials + it },
                                    onRemoveSerial = { s -> pendingSerials = pendingSerials.filterNot { it == s } },
                                    onAutoGenerateChange = { pendingAutoSerial = it }
                                )
                            }
                            ItemTrackingType.LOT -> LotInputRow(
                                itemId = item.itemId,
                                value = pendingLot,
                                enabled = !saving,
                                isSales = docType == DeliveryDocType.SALES,
                                repo = repo,
                                onValueChange = { pendingLot = it }
                            )
                            ItemTrackingType.NONE -> {}
                        }
                    }
                }
            }

            // ── 3) Satır listesi ───────────────────────────────────────────
            Text(
                text = "Kalemler (${lines.size})",
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold
            )
            if (lines.isEmpty()) {
                Text(
                    text = "Henüz kalem eklenmedi. Yukarıdan malzeme arayıp ekleyin.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            } else {
                // Belge kalem sayısı mobilde küçük kalır; ekran zaten scroll'lu Column
                // olduğundan LazyColumn yerine düz forEach kullanıldı (StockDocScreen ile aynı gerekçe).
                lines.forEachIndexed { index, line ->
                    DeliveryLineRow(
                        line = line,
                        enabled = !saving,
                        onDelete = { lines = lines.filterIndexed { i, _ -> i != index } }
                    )
                }
            }

            // ── 4) Not (opsiyonel) ─────────────────────────────────────────
            OutlinedTextField(
                value = note,
                onValueChange = { note = it },
                label = { Text("Not (opsiyonel)") },
                enabled = !saving,
                minLines = 2,
                maxLines = 4,
                modifier = Modifier.fillMaxWidth()
            )

            // ── 5) Hata + kaydet ───────────────────────────────────────────
            // {error} sunucudan geldiği gibi gösterilir — "bağlantısız yasak" reddi dahil
            // diğer iş kuralı redleriyle AYNI kanal (bkz. dosya üstü KDoc).
            if (saveError != null) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer
                    )
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(12.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Icon(
                            Icons.Default.ErrorOutline,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.onErrorContainer,
                            modifier = Modifier.size(20.dp)
                        )
                        Spacer(Modifier.width(8.dp))
                        Text(
                            text = saveError!!,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onErrorContainer
                        )
                    }
                }
            }

            Button(
                onClick = { save() },
                enabled = resolvedContact != null && lines.isNotEmpty() && !saving,
                modifier = Modifier.fillMaxWidth()
            ) {
                if (saving) CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    color = MaterialTheme.colorScheme.onPrimary
                )
                else Text("Kaydet")
            }

            Spacer(Modifier.height(8.dp))
        }
    }

    // ── Cari rehberi diyaloğu — type-ahead'in YANINDAKİ butondan açılır ────
    if (showContactPicker) {
        ContactPickerDialog(
            repo = repo,
            onDismiss = { showContactPicker = false },
            onSelected = { dto ->
                resolvedContact = dto
                contactQuery = dto.code   // ContactPickerField.pick() ile AYNI davranış.
            }
        )
    }

    // ── Seri seçim diyaloğu — yalnız SATIŞ + SERIAL dalındaki "Serileri Seç" butonundan açılır.
    val pickerItem = resolved
    if (showSerialPicker && pickerItem != null) {
        SerialSelectionDialog(
            itemId = pickerItem.itemId,
            itemName = pickerItem.itemName,
            itemCode = pickerItem.itemCode,
            targetQuantity = targetQtyInt,
            initiallySelected = pendingSerials,
            repo = repo,
            onDismiss = { showSerialPicker = false },
            onConfirm = { picked -> pendingSerials = picked }
        )
    }

    // ── Başarı diyaloğu — belge no + satır bazlı bağlama özeti ─────────────
    if (successResult != null) {
        val res = successResult!!
        AlertDialog(
            onDismissRequest = {
                successResult = null
                onBack()
            },
            icon = {
                Icon(
                    Icons.Default.CheckCircle,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary
                )
            },
            title = { Text(docType.successTitle) },
            text = {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 420.dp)
                        .verticalScroll(rememberScrollState())
                ) {
                    Text(
                        text = "Belge No: ${res.documentNumber}",
                        style = MaterialTheme.typography.bodyLarge,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(Modifier.height(10.dp))
                    if (res.lines.isEmpty()) {
                        Text(
                            text = "Kalem bağlantı bilgisi alınamadı.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    } else {
                        // lines (dış state) BİLİNÇLİ olarak henüz sıfırlanmadı — StockDocScreen'in
                        // successDocNumber dialoğu açıkken lines'ı korumasıyla aynı desen; itemId
                        // eşlemesi burada ad/kod/birim göstermek için kullanılır.
                        res.lines.forEach { lineResult ->
                            val lineUi = lines.firstOrNull { it.itemId == lineResult.itemId }
                            DeliveryLinkResultRow(
                                lineResult = lineResult,
                                itemName = lineUi?.itemName,
                                itemCode = lineUi?.itemCode,
                                unit = lineUi?.unit
                            )
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    successResult = null
                    resetForNewDoc()   // cari korunur — art arda belge girişi için
                }) { Text("Yeni Belge") }
            },
            dismissButton = {
                TextButton(onClick = {
                    successResult = null
                    onBack()
                }) { Text("Kapat") }
            }
        )
    }
}

/** Eklenmiş irsaliye kalemi satırı — ad/kod + miktar + sil (stateless) + (varsa) seri/lot etiketi.
 * StockDocScreen'in DocLineRow'uyla aynı temel görünüm; ayrı dosyada private top-level fonksiyon
 * olduğundan isim çakışması yoktur (Kotlin'de dosya-özel görünürlük). */
@Composable
private fun DeliveryLineRow(line: DeliveryLineUi, enabled: Boolean, onDelete: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 12.dp, end = 4.dp, top = 8.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(line.itemName, style = MaterialTheme.typography.bodyLarge)
                Text(
                    text = line.itemCode,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                // Seri/Lot etiketi — kullanıcının bu satır için seçtiği/girdiği takip bilgisi.
                val tag = listOfNotNull(
                    line.serials?.takeIf { it.isNotEmpty() }?.let { "${it.size} seri seçili" },
                    line.lotCode?.let { "Lot: $it" },
                    line.autoGenerateSerials?.takeIf { it }?.let { "Seri: Otomatik üret" }
                ).joinToString(" · ")
                if (tag.isNotBlank()) {
                    Text(tag, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.primary)
                }
            }
            Text(
                text = formatDeliveryQty(line.quantity) +
                    (line.unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            IconButton(onClick = onDelete, enabled = enabled) {
                Icon(
                    Icons.Default.Delete,
                    contentDescription = "Satırı sil",
                    tint = MaterialTheme.colorScheme.error
                )
            }
        }
    }
}

/**
 * Başarı diyaloğundaki tek kalem satırı — ad/kod başlığı + [buildDeliveryLinkSummary] metni +
 * (varsa) sunucunun ATADIĞI seri/lot dökümü ([buildSerialLotSummary]). `internal` — FAZ C
 * OpenOrderDetailScreen'in "aynı bağlama-sonucu dialog'u" gereksinimi için PAYLAŞILIR (bkz.
 * DeliveryResponse/DeliveryLineResultDto — Açık Sipariş teslimatı da AYNI response şeklini kullanır).
 *
 * Kasıtlı olarak `DeliveryLineUi` (bu dosyada `private`) yerine ÇIPLAK ad/kod/birim alır — böylece
 * OpenOrderDetailScreen kendi (farklı) satır UI modelinden doğrudan çağırabilir, dosyalar arası
 * private tip sızıntısı olmaz. Hepsi null olması teorik olarak beklenmez (sunucu yalnız
 * gönderdiğimiz itemId'leri döner) ama savunma amaçlı ele alınır: null ise "Kalem #<itemId>" gösterilir.
 */
@Composable
internal fun DeliveryLinkResultRow(
    lineResult: DeliveryLineResultDto,
    itemName: String?,
    itemCode: String?,
    unit: String?
) {
    Column(modifier = Modifier.fillMaxWidth().padding(bottom = 10.dp)) {
        Text(
            text = itemName ?: "Kalem #${lineResult.itemId}",
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.SemiBold
        )
        if (itemCode != null) {
            Text(
                text = itemCode,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        Spacer(Modifier.height(2.dp))
        Text(
            text = buildDeliveryLinkSummary(lineResult, unit),
            style = MaterialTheme.typography.bodySmall,
            color = if (lineResult.unlinkedQuantity > 0.0) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.onSurfaceVariant
        )
        val serialLotText = buildSerialLotSummary(lineResult)
        if (serialLotText != null) {
            Spacer(Modifier.height(2.dp))
            Text(
                text = serialLotText,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

/**
 * Bağlama özetini TEK satıra indirger: "→ SIP-2026-00012'ye 5, SIP-2026-00015'e 3 bağlandı;
 * 2 bağlantısız" biçiminde (koordinatör örneği, task tanımından). linked[] boşsa yalnız
 * "bağlantısız" kısmı; ikisi de boşsa "Bağlantı bilgisi yok" düşer (savunma — sözleşmede bu
 * durum tanımsız, sunucu her satırda ya linked ya unlinkedQuantity>0 döner). `internal` —
 * DeliveryLinkResultRow ile birlikte OpenOrderDetailScreen'e PAYLAŞILIR.
 */
internal fun buildDeliveryLinkSummary(result: DeliveryLineResultDto, unit: String?): String {
    val unitSuffix = unit?.takeIf { it.isNotBlank() }?.let { " $it" } ?: ""
    val linkedPart = if (result.linked.isNotEmpty()) {
        "→ " + result.linked.joinToString(", ") { "${it.orderNumber}'e ${formatDeliveryQty(it.quantity)}$unitSuffix" } + " bağlandı"
    } else null
    val unlinkedPart = if (result.unlinkedQuantity > 0.0) {
        "${formatDeliveryQty(result.unlinkedQuantity)}$unitSuffix bağlantısız"
    } else null
    return listOfNotNull(linkedPart, unlinkedPart).joinToString("; ").ifBlank { "Bağlantı bilgisi yok" }
}

/**
 * Sunucunun (otomatik veya manuel) ATADIĞI seri/lot dökümü — "Seri: SN-001, SN-002, SN-003
 * +2 tane daha" biçiminde kısaltılmış (2026-07-17 FINAL kontrat, DeliveryLineResultDto.serials/
 * lotCode). İkisi de boşsa null (satır bilgi eklenmez).
 */
internal fun buildSerialLotSummary(result: DeliveryLineResultDto): String? {
    val parts = mutableListOf<String>()
    if (result.serials.isNotEmpty()) {
        val shown = result.serials.take(3).joinToString(", ")
        parts += if (result.serials.size > 3) "Seri: $shown +${result.serials.size - 3} tane daha" else "Seri: $shown"
    }
    result.lotCode?.takeIf { it.isNotBlank() }?.let { parts += "Lot: $it" }
    return parts.joinToString(" · ").ifBlank { null }
}

/**
 * Tam sayıları ".00" olmadan, ondalıklıları 2 haneye yuvarlayarak gösterir (StockDocScreen ile
 * aynı V1 format). `internal` — DeliverySerialLotSection.kt/OpenOrderListScreen.kt/
 * OpenOrderDetailScreen.kt PAYLAŞIR. Adı BİLİNÇLİ olarak `formatQty` DEĞİL: StockDocScreen/
 * TransferScreen/CountScreen'in KENDİ (aynı paketteki) `private fun formatQty` fonksiyonlarıyla
 * AYNI adı taşısaydı Kotlin "Conflicting overloads" derleme hatası verirdi — bir top-level
 * fonksiyon `internal`/public olunca aynı paketteki AYNI imzalı `private` fonksiyonlarla bile
 * JVM-düzeyinde çakışıyor (private-private arası çakışma yok, ama internal-private arası var).
 */
internal fun formatDeliveryQty(q: Double): String =
    if (q == q.toLong().toDouble()) q.toLong().toString() else "%.2f".format(q)

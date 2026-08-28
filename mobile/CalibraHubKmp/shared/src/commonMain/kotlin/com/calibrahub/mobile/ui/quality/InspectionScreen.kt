package com.calibrahub.mobile.ui.quality

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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
import com.calibrahub.mobile.data.DefectCodeDto
import com.calibrahub.mobile.data.InspectionDispositions
import com.calibrahub.mobile.data.InspectionVerdicts
import com.calibrahub.mobile.data.LineResults
import com.calibrahub.mobile.data.SaveInspectionLine
import com.calibrahub.mobile.data.SaveInspectionRequest
import com.calibrahub.mobile.data.StockQueryDto
import com.calibrahub.mobile.photo.CapturedPhoto
import com.calibrahub.mobile.photo.rememberPhotoCapture
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.ui.warehouse.MaterialPickerField
import kotlinx.coroutines.launch

/**
 * Ekrandaki bir olcum satiri.
 *
 * [isNumeric] true ise kullanici DEGER girer, sonucu SUNUCU hesaplar (nominal + tolerans).
 * false ise (gorsel kontrol) kullanici dogrudan Uygun/Uygunsuz secer.
 */
private data class LineUi(
    val planLineId: Int?,
    val name: String,
    val nominal: Double?,
    val lowerTol: Double?,
    val upperTol: Double?,
    val isNumeric: Boolean,
    val orderNo: Int,
    val measuredText: String = "",
    val visualResult: Int = LineResults.NOT_EVALUATED,
    val defectCodeId: Int? = null,
)

/**
 * Kalite Muayene — saha girisi.
 *
 * TASARIM:
 * - **Sonuc secilmez, olculur.** Sayisal karakteristikte kullanici yalniz olcum degerini girer;
 *   Uygun/Uygunsuz kararini sunucu tolerans karsilastirmasiyla verir. Bu, telefonda yanlis karar
 *   verilmesini imkansiz kilar.
 * - **Plan zorunlu degil.** Malzemenin plani varsa karakteristikler hazir gelir; yoksa tek bir
 *   "Genel Degerlendirme" satiri (gorsel tip) ile sade kayit acilir.
 * - **Uygunsuz satirda hata kodu zorunlu** — sunucu kurali; burada erken uyari verilir.
 * - Kaydettikten sonra sunucunun hesapladigi sonuc okunur; UYGUNSUZ ise **karar** (Kabul/Ret/
 *   Yeniden Islem/Sapma Izni) sorulur — sunucu karar olmadan tamamlamaya izin vermez.
 *
 * [sourceKind]/[sourceId]: muayenenin hangi belgeden dogdugu (alis_irsaliyesi | depo_giris |
 * is_emri). Yalniz izlenebilirlik; hicbir stok hareketi tetiklemez.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun InspectionScreen(
    session: SessionManager,
    inspectionType: Int,
    sourceKind: String?,
    sourceId: Int?,
    onBack: () -> Unit,
) {
    val repo = session.qualityRepository
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }
    val photoCapture = rememberPhotoCapture()

    var code by remember { mutableStateOf("") }
    var item by remember { mutableStateOf<StockQueryDto?>(null) }
    var resolveError by remember { mutableStateOf<String?>(null) }
    var qtyText by remember { mutableStateOf("") }
    var notes by remember { mutableStateOf("") }

    var planName by remember { mutableStateOf<String?>(null) }
    var planId by remember { mutableStateOf<Int?>(null) }
    var lines by remember { mutableStateOf<List<LineUi>>(emptyList()) }
    var defectCodes by remember { mutableStateOf<List<DefectCodeDto>>(emptyList()) }
    var photos by remember { mutableStateOf<List<CapturedPhoto>>(emptyList()) }

    var saving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<String?>(null) }

    // Kaydedildikten sonra sunucudan gelen sonuc; uygunsuzsa karar diyalogu acilir.
    var pendingDocumentId by remember { mutableStateOf<Int?>(null) }
    var showDispositionDialog by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        repo.defectCodes().onSuccess { defectCodes = it }
    }

    // Malzeme secilince plani cek. Plan yoksa TEK satirlik sade kayda duser — hata degil.
    LaunchedEffect(item?.itemId) {
        val id = item?.itemId ?: run { lines = emptyList(); planId = null; planName = null; return@LaunchedEffect }
        repo.plan(id, inspectionType).fold(
            onSuccess = { plan ->
                planId = plan?.planId
                planName = plan?.planName
                lines = plan?.lines
                    ?.sortedBy { it.orderNo }
                    ?.map {
                        LineUi(
                            planLineId = it.id,
                            name = it.characteristicName,
                            nominal = it.nominal,
                            lowerTol = it.lowerTol,
                            upperTol = it.upperTol,
                            isNumeric = it.isNumeric,
                            orderNo = it.orderNo,
                        )
                    }
                    ?: listOf(
                        LineUi(
                            planLineId = null,
                            name = "Genel Değerlendirme",
                            nominal = null, lowerTol = null, upperTol = null,
                            isNumeric = false, orderNo = 0,
                        ),
                    )
            },
            onFailure = { saveError = it.message ?: "Muayene planı alınamadı." },
        )
    }

    val qty = qtyText.trim().replace(',', '.').toDoubleOrNull()

    fun buildRequest() = SaveInspectionRequest(
        id = 0,
        planId = planId,
        itemId = item?.itemId,
        inspectionType = inspectionType,
        sourceKind = sourceKind,
        sourceId = sourceId,
        quantity = qty,
        notes = notes.trim().takeIf { it.isNotBlank() },
        lines = lines.map {
            SaveInspectionLine(
                planLineId = it.planLineId,
                characteristicName = it.name,
                nominal = it.nominal,
                lowerTol = it.lowerTol,
                upperTol = it.upperTol,
                measured = if (it.isNumeric) it.measuredText.trim().replace(',', '.').toDoubleOrNull() else null,
                isNumeric = it.isNumeric,
                result = if (it.isNumeric) LineResults.NOT_EVALUATED else it.visualResult,
                defectCodeId = it.defectCodeId,
                orderNo = it.orderNo,
            )
        },
    )

    /** Sunucuya gitmeden onceki erken uyari — asil kural sunucuda, burada tekrarlanmaz. */
    fun localValidationError(): String? {
        if (item == null) return "Malzeme seçilmeli."
        if (lines.isEmpty()) return "Ölçüm satırı yok."
        lines.forEach { l ->
            if (l.isNumeric && l.measuredText.isBlank()) return "'${l.name}' için ölçüm değeri girilmeli."
            val looksNonConforming = !l.isNumeric && l.visualResult == LineResults.NON_CONFORMING
            if (looksNonConforming && l.defectCodeId == null) return "'${l.name}' uygunsuz — hata kodu seçilmeli."
        }
        return null
    }

    fun save() {
        val err = localValidationError()
        if (err != null) { saveError = err; return }
        saving = true
        saveError = null
        scope.launch {
            repo.save(buildRequest()).fold(
                onSuccess = { res ->
                    pendingDocumentId = res.documentId

                    // Fotograflari kaydin ARDINDAN yukle. Basarisiz olursa muayene YINE gecerlidir —
                    // kullaniciya bildirilir ama akis bozulmaz (audit felsefesiyle ayni: yardimci
                    // yazim ana isi asla dusurmez).
                    var photoError = false
                    photos.forEach { p ->
                        repo.uploadPhoto(res.documentId, p).onFailure { photoError = true }
                    }

                    saving = false
                    if (res.verdict == InspectionVerdicts.NON_CONFORMING) {
                        // Sunucu, uygunsuz muayeneyi KARAR olmadan tamamlamaz.
                        showDispositionDialog = true
                        if (photoError) snackbarHostState.showSnackbar("Muayene kaydedildi, bazı fotoğraflar yüklenemedi.")
                    } else {
                        repo.complete(res.documentId, null).fold(
                            onSuccess = {
                                snackbarHostState.showSnackbar(
                                    "Muayene tamamlandı: ${res.documentNumber ?: res.documentId}" +
                                        if (photoError) " (fotoğraf yüklenemedi)" else "",
                                )
                                onBack()
                            },
                            onFailure = { saveError = it.message ?: "Muayene tamamlanamadı." },
                        )
                    }
                },
                onFailure = {
                    saving = false
                    saveError = it.message ?: "Muayene kaydedilemedi."
                },
            )
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Kalite Muayene") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
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
                onQueryChange = { code = it; item = null; resolveError = null },
                onResolved = { item = it; resolveError = null },
                onResolveError = { item = null; resolveError = it },
                repo = session.warehouseRepository,
                enabled = !saving,
                modifier = Modifier.fillMaxWidth(),
            )
            resolveError?.let {
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            }

            if (item != null) {
                Spacer(Modifier.height(10.dp))
                OutlinedTextField(
                    value = qtyText,
                    onValueChange = { qtyText = it },
                    label = { Text("Muayene Miktarı" + (item?.unit?.let { " ($it)" } ?: "")) },
                    singleLine = true,
                    enabled = !saving,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    modifier = Modifier.fillMaxWidth(),
                )

                Spacer(Modifier.height(14.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = planName ?: "Plan tanımlı değil — sade kayıt",
                        style = MaterialTheme.typography.labelLarge,
                        fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.weight(1f),
                    )
                }
                Spacer(Modifier.height(6.dp))

                lines.forEachIndexed { index, line ->
                    LineCard(
                        line = line,
                        defectCodes = defectCodes,
                        enabled = !saving,
                        onMeasuredChange = { v ->
                            lines = lines.toMutableList().also { it[index] = line.copy(measuredText = v) }
                        },
                        onVisualResultChange = { r ->
                            lines = lines.toMutableList().also {
                                // Uygun'a donerse secili hata kodu anlamsizlasir — temizlenir.
                                it[index] = line.copy(
                                    visualResult = r,
                                    defectCodeId = if (r == LineResults.NON_CONFORMING) line.defectCodeId else null,
                                )
                            }
                        },
                        onDefectCodeChange = { id ->
                            lines = lines.toMutableList().also { it[index] = line.copy(defectCodeId = id) }
                        },
                    )
                    Spacer(Modifier.height(8.dp))
                }

                Spacer(Modifier.height(6.dp))
                OutlinedTextField(
                    value = notes,
                    onValueChange = { notes = it },
                    label = { Text("Not (isteğe bağlı)") },
                    enabled = !saving,
                    modifier = Modifier.fillMaxWidth(),
                )

                Spacer(Modifier.height(12.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedButton(
                        onClick = {
                            scope.launch { photoCapture.capture()?.let { photos = photos + it } }
                        },
                        enabled = !saving,
                    ) {
                        Icon(Icons.Default.PhotoCamera, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Fotoğraf Ekle")
                    }
                    Spacer(Modifier.width(10.dp))
                    if (photos.isNotEmpty()) {
                        AssistChip(onClick = { photos = emptyList() }, label = { Text("${photos.size} foto · temizle") })
                    }
                }

                saveError?.let {
                    Spacer(Modifier.height(10.dp))
                    Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
                }

                Spacer(Modifier.height(16.dp))
                Button(
                    onClick = { save() },
                    enabled = !saving,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    if (saving) CircularProgressIndicator(modifier = Modifier.size(18.dp))
                    else Text("Kaydet ve Tamamla")
                }
                Spacer(Modifier.height(24.dp))
            }
        }
    }

    if (showDispositionDialog) {
        val docId = pendingDocumentId
        DispositionDialog(
            onDismiss = { showDispositionDialog = false },
            onSelect = { disposition ->
                showDispositionDialog = false
                if (docId != null) {
                    scope.launch {
                        repo.complete(docId, disposition).fold(
                            onSuccess = {
                                snackbarHostState.showSnackbar("Uygunsuzluk kaydedildi, muayene tamamlandı.")
                                onBack()
                            },
                            onFailure = { saveError = it.message ?: "Muayene tamamlanamadı." },
                        )
                    }
                }
            },
        )
    }
}

@Composable
private fun LineCard(
    line: LineUi,
    defectCodes: List<DefectCodeDto>,
    enabled: Boolean,
    onMeasuredChange: (String) -> Unit,
    onVisualResultChange: (Int) -> Unit,
    onDefectCodeChange: (Int?) -> Unit,
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth().padding(14.dp)) {
            Text(line.name, fontWeight = FontWeight.Medium)

            if (line.isNumeric) {
                // Tolerans BILGI amacli gosterilir; karari sunucu verir.
                val tol = buildString {
                    line.nominal?.let { append("Nominal $it") }
                    if (line.lowerTol != null || line.upperTol != null) {
                        if (isNotEmpty()) append(" · ")
                        append("Sınır ${line.lowerTol ?: "—"} … ${line.upperTol ?: "—"}")
                    }
                }
                if (tol.isNotEmpty()) {
                    Text(tol, style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = line.measuredText,
                    onValueChange = onMeasuredChange,
                    label = { Text("Ölçülen değer") },
                    singleLine = true,
                    enabled = enabled,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    modifier = Modifier.fillMaxWidth(),
                )
            } else {
                Spacer(Modifier.height(8.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(
                        selected = line.visualResult == LineResults.CONFORMING,
                        onClick = { onVisualResultChange(LineResults.CONFORMING) },
                        enabled = enabled,
                        label = { Text("Uygun") },
                        leadingIcon = { Icon(Icons.Default.Check, contentDescription = null, modifier = Modifier.size(16.dp)) },
                    )
                    FilterChip(
                        selected = line.visualResult == LineResults.NON_CONFORMING,
                        onClick = { onVisualResultChange(LineResults.NON_CONFORMING) },
                        enabled = enabled,
                        label = { Text("Uygunsuz") },
                        leadingIcon = { Icon(Icons.Default.Close, contentDescription = null, modifier = Modifier.size(16.dp)) },
                    )
                }
            }

            // Hata kodu YALNIZ uygunsuz gorsel satirda sorulur. Sayisal satirda sonuc sunucuda
            // hesaplandigi icin uygunsuzluk kaydetme aninda belli olur — bu durumda sunucu
            // hata kodu ister ve istek reddedilir; kullanici satiri gorsel isaretleyip kod secer.
            if (!line.isNumeric && line.visualResult == LineResults.NON_CONFORMING) {
                Spacer(Modifier.height(10.dp))
                Text("Hata Kodu", style = MaterialTheme.typography.labelMedium)
                Spacer(Modifier.height(4.dp))
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    defectCodes.forEach { dc ->
                        FilterChip(
                            selected = line.defectCodeId == dc.id,
                            onClick = { onDefectCodeChange(if (line.defectCodeId == dc.id) null else dc.id) },
                            enabled = enabled,
                            label = { Text(dc.name) },
                        )
                    }
                    if (defectCodes.isEmpty()) {
                        Text(
                            "Hata kodu tanımlı değil — yöneticinizden Kalite → Hata Kodları altında tanımlamasını isteyin.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                }
            }
        }
    }
}

/**
 * Uygunsuzluk karari — sunucu, uygunsuz muayeneyi karar OLMADAN tamamlamaz.
 * Kapatilamaz degil ama kapatilirsa muayene TASLAK kalir; kullaniciya bu soylenir.
 */
@Composable
private fun DispositionDialog(onDismiss: () -> Unit, onSelect: (Int) -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Uygunsuzluk Kararı") },
        text = {
            Column {
                Text(
                    "Muayene uygunsuz çıktı. Tamamlamak için bir karar verin.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                Spacer(Modifier.height(12.dp))
                listOf(
                    InspectionDispositions.ACCEPT to "Kabul",
                    InspectionDispositions.REJECT to "Ret",
                    InspectionDispositions.REWORK to "Yeniden İşlem",
                    InspectionDispositions.DEVIATION to "Sapma İzni",
                ).forEach { (value, label) ->
                    Surface(
                        onClick = { onSelect(value) },
                        shape = MaterialTheme.shapes.small,
                        modifier = Modifier.fillMaxWidth().padding(vertical = 3.dp),
                    ) {
                        Text(label, modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp))
                    }
                }
            }
        },
        confirmButton = {},
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Sonra (taslak kalsın)") }
        },
    )
}

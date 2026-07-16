package com.calibrahub.app.ui.login

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Business
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.calibrahub.app.R
import com.calibrahub.app.app
import com.calibrahub.app.data.CompanyDto
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(onLoggedIn: () -> Unit) {
    val context = LocalContext.current
    val repo    = context.app.repository
    val session = context.app.session
    val scope   = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var email    by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var baseUrl  by remember { mutableStateOf("") }
    var showPwd  by remember { mutableStateOf(false) }
    var loading  by remember { mutableStateOf(false) }
    var showServerSettings by remember { mutableStateOf(false) }

    // Parola doğrulandıktan sonra dönen erişilebilir şirket listesi.
    // Boş = kimlik bilgisi adımı gösterilir; dolu = şirket seçim adımı gösterilir.
    var companyChoices by remember { mutableStateOf<List<CompanyDto>>(emptyList()) }

    // Kilit-kadranının bulmaca durumu — YALNIZ görsel katmanı sürer, login akış mantığına
    // karışmaz. KRİTİK: Solved (çözüldü) durumu client'ta parola kontrolüyle DEĞİL, yalnız
    // sunucu login onayıyla (loginCompanies >= 1 şirket veya doLogin başarısı) tetiklenir.
    var dialState by remember { mutableStateOf(LockDialState.Idle) }

    // Mevcut base URL (sunucu ayarları paneli için)
    LaunchedEffect(Unit) {
        baseUrl = session.currentBaseUrl()
    }

    // Seçilen şirketle asıl login çağrısı — hem "tek şirket → otomatik gir" hem de
    // "şirket seçici → tıkla → gir" akışlarından paylaşılır. Result.fold inline olduğu için
    // (kotlin.Result.fold inline'dır) suspend çağrılar burada askıya alma zincirini bozmaz.
    suspend fun doLogin(companyId: Int) {
        loading = true
        // Çok-şirket akışında kadran loginCompanies onayıyla zaten çözülmüş olabilir;
        // o durumda Loading'e geri düşürmeyiz (çözülmüş kilit tekrar karışmasın).
        if (dialState != LockDialState.Solved) dialState = LockDialState.Loading
        repo.login(email.trim(), password, companyId).fold(
            onSuccess = {
                // Bulmaca sunucu onayıyla çözüldü: katmanlar kilitlenir + yeşil kutlama.
                // Kısa kutlamadan sonra mevcut navigasyon aynen devam eder (onLoggedIn).
                if (dialState != LockDialState.Solved) {
                    dialState = LockDialState.Solved
                    delay(1600)
                } else {
                    delay(250)
                }
                onLoggedIn()
            },
            onFailure = { e ->
                dialState = LockDialState.Failed
                snackbarHostState.showSnackbar("Giriş başarısız: ${e.message ?: "bilinmeyen hata"}")
            }
        )
        loading = false
    }

    Scaffold(snackbarHost = { SnackbarHost(snackbarHostState) }) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(24.dp),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            CalibraLoginBadge(passwordLength = password.length, dialState = dialState)
            Spacer(Modifier.height(40.dp))

            if (companyChoices.isEmpty()) {
                // ── Adım 1: kimlik bilgileri ─────────────────────────────
                OutlinedTextField(
                    value = email,
                    onValueChange = { email = it },
                    label = { Text("E-posta") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                    singleLine = true,
                    enabled = !loading,
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(12.dp))

                OutlinedTextField(
                    value = password,
                    onValueChange = {
                        password = it
                        // Her düzenlemede kadran "karışıyor" moduna döner (Failed'dan da çıkar);
                        // tamamen silinince başlangıç karışık konumuna geri sarar.
                        dialState = if (it.isEmpty()) LockDialState.Idle else LockDialState.Typing
                    },
                    label = { Text("Parola") },
                    singleLine = true,
                    enabled = !loading,
                    visualTransformation = if (showPwd) VisualTransformation.None else PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                    trailingIcon = {
                        IconButton(onClick = { showPwd = !showPwd }) {
                            if (showPwd) Icon(Icons.Default.VisibilityOff, contentDescription = "Parolayı gizle")
                            else         Icon(Icons.Default.Visibility, contentDescription = "Parolayı göster")
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(24.dp))

                Button(
                    onClick = {
                        scope.launch {
                            loading = true
                            dialState = LockDialState.Loading
                            repo.loginCompanies(email.trim(), password).fold(
                                onSuccess = { list ->
                                    when {
                                        list.isEmpty() -> {
                                            loading = false
                                            dialState = LockDialState.Failed
                                            snackbarHostState.showSnackbar("Kimlik geçersiz veya erişilebilir şirket yok")
                                        }
                                        list.size == 1 -> doLogin(list.first().id)
                                        else -> {
                                            loading = false
                                            // Parola sunucuda doğrulandı → bulmaca çözüldü;
                                            // şirket seçimi çözülmüş (yeşil) kadranla yapılır.
                                            dialState = LockDialState.Solved
                                            companyChoices = list
                                        }
                                    }
                                },
                                onFailure = {
                                    loading = false
                                    dialState = LockDialState.Failed
                                    snackbarHostState.showSnackbar("Kimlik geçersiz veya erişilebilir şirket yok")
                                }
                            )
                        }
                    },
                    enabled = !loading && email.isNotBlank() && password.isNotBlank(),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    if (loading) CircularProgressIndicator(modifier = Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary)
                    else         Text("Giriş yap")
                }
            } else {
                // ── Adım 2: birden çok şirket erişimi varsa seçim ─────────
                Text(
                    text = "Şirket seçin",
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(12.dp))

                companyChoices.forEach { company ->
                    OutlinedButton(
                        onClick = { scope.launch { doLogin(company.id) } },
                        enabled = !loading,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Icon(Icons.Default.Business, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(8.dp))
                        Text(company.name)
                    }
                    Spacer(Modifier.height(8.dp))
                }

                if (loading) {
                    Spacer(Modifier.height(4.dp))
                    CircularProgressIndicator(modifier = Modifier.size(24.dp))
                    Spacer(Modifier.height(4.dp))
                }

                TextButton(
                    onClick = {
                        companyChoices = emptyList()
                        // Kimlik adımına dönüldü: çözülmüş görünüm yanıltmasın, kadran
                        // parola durumuna uygun karışık konuma geri döner (görsel reset).
                        dialState = if (password.isEmpty()) LockDialState.Idle else LockDialState.Typing
                    },
                    enabled = !loading
                ) { Text("Geri") }
            }

            Spacer(Modifier.height(32.dp))
            TextButton(onClick = { showServerSettings = !showServerSettings }) {
                Text(if (showServerSettings) "Sunucu ayarlarını gizle" else "Sunucu ayarları")
            }

            if (showServerSettings) {
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = baseUrl,
                    onValueChange = { baseUrl = it },
                    label = { Text("Backend URL") },
                    supportingText = {
                        Text("Emulator için: http://10.0.2.2:61001/")
                    },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(8.dp))
                OutlinedButton(
                    onClick = {
                        scope.launch {
                            session.setBaseUrl(baseUrl.trim())
                            snackbarHostState.showSnackbar("Sunucu adresi kaydedildi.")
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                ) { Text("Kaydet") }
            }
        }
    }
}

/**
 * Kilit-kadranının görsel durum makinesi. YALNIZ görsel katmanı sürer; login akış mantığını
 * etkilemez. Solved, client'ta parola kontrolüyle değil sadece sunucu onayıyla set edilir.
 */
private enum class LockDialState { Idle, Typing, Loading, Solved, Failed }

/**
 * Bir kilit katmanının (halkasının) sabit tanımı.
 *
 * Harf dizilimi sözleşmesi: CALIBRAHUB'ın bu katmana düşen segmenti dizinin İLK
 * [segmentLength] karakteridir; kalanı şaşırtmaca (decoy) harflerdir. Katman ofseti slot
 * cinsinden tam-tur katına (0, ±slotSayısı, …) geldiğinde segment, kadranın üst okuma
 * yayında ortalanmış durur.
 *
 * [baseScramble] BİLEREK yarım-slot kesirlidir (x.5): yazma adımları hep tamsayı slot
 * olduğundan, katman parola yazımı sırasında matematiksel olarak HİÇBİR uzunlukta tam
 * hizalanamaz — "doğru kelime yazarak çözme" ihtimali yapısal olarak kapalıdır; çözülme
 * yalnız sunucu onayında ofset tam-tur katına animasyonla oturtularak gerçekleşir.
 */
private data class LockRingSpec(
    val letters: String,      // slot başına bir harf; segment + decoy'lar
    val segmentLength: Int,   // CALIBRAHUB segmentinin uzunluğu (dizinin başı)
    val baseScramble: Float,  // boşta duruş ofseti (slot; kasıtlı .5 kesirli)
    val direction: Int,       // yazarken dönüş yönü: +1 saat yönü, -1 tersi
    val radiusDp: Float,      // harf merkezlerinin yarıçapı (dp)
    val fontSp: Float,        // harf punto boyutu (sp)
)

/**
 * Dıştan içe 3 katman: CALI (dış, 16 slot) + BRA (orta, 14 slot) + HUB (iç, 12 slot).
 * Hizalanınca üst yaydan dışarıdan içeriye "CALI / BRA / HUB" = CALIBRAHUB okunur.
 * Decoy harfler segment dizilimlerini tekrar etmeyecek şekilde seçildi. Slot sayıları
 * kasıtlı farklı (16/14/12): karışırken katmanlar farklı hızda "dağılıyor" hissi verir.
 */
private val LockDialRings = listOf(
    LockRingSpec("CALIWTSKEDPNXOVZ", 4, +7.5f, +1, 84f, 11f),
    LockRingSpec("BRAKMYTUZEWDOS",   3, -5.5f, -1, 71f, 10f),
    LockRingSpec("HUBSTKAEMYOZ",     3, +4.5f, +1, 58f, 10f),
)

/**
 * n. tuş vuruşu katmanları döngüsel gezer (1→dış, 2→orta, 3→iç, 4→dış…): [ring] katmanının
 * parola [passwordLength] uzunluğundayken almış olduğu toplam adım sayısı. Uzunluktan
 * türetildiği için silme işlemi otomatik olarak aynı adımları geri sarar.
 */
private fun ringSteps(passwordLength: Int, ring: Int): Int =
    if (passwordLength <= ring) 0 else (passwordLength - ring + 2) / 3

/** Yazma/karışma hedef ofseti (slot) — taban karışıklık + yönlü adımlar. */
private fun ringTypingTarget(passwordLength: Int, ring: Int): Float {
    val spec = LockDialRings[ring]
    return spec.baseScramble + spec.direction * ringSteps(passwordLength, ring)
}

/**
 * Çözülmüş hedef ofset: mevcut karışık konuma EN YAKIN tam-tur katı — kilit en kısa yoldan
 * "klik" diye oturur; segment üst okuma yayına gelir.
 */
private fun ringSolvedTarget(passwordLength: Int, ring: Int): Float {
    val slots = LockDialRings[ring].letters.length
    return ((ringTypingTarget(passwordLength, ring) / slots).roundToInt() * slots).toFloat()
}

/**
 * Login formunun üstünde ortalı marka bloğu: harf-bulmacalı kilit kadranı + logo + alt başlık.
 * Stateless — kadranın tepki verdiği durum (parola uzunluğu, bulmaca durumu) parametreyle
 * yukarıdan gelir (state hoisting), kendi state'i yoktur.
 *
 * @param passwordLength Parola alanındaki karakter sayısı; her karakter bir katmanı bir
 *                       adım döndürür (katman seçimi tuş sırasına göre döngüseldir).
 * @param dialState      Bulmaca durumu — Solved yalnız sunucu login onayında gelir.
 */
@Composable
private fun CalibraLoginBadge(
    passwordLength: Int,
    dialState: LockDialState
) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            modifier = Modifier.size(200.dp),
            contentAlignment = Alignment.Center
        ) {
            CalibrationLockDial(
                passwordLength = passwordLength,
                state = dialState,
                modifier = Modifier.fillMaxSize()
            )

            Image(
                painter = painterResource(id = R.drawable.calibrahub_logo),
                contentDescription = "CalibraHub logosu",
                contentScale = ContentScale.Crop,
                modifier = Modifier
                    .size(92.dp)
                    .clip(CircleShape)
                    .border(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.4f), CircleShape)
            )
        }
        Spacer(Modifier.height(20.dp))
        Text(
            text = "Mobil Companion",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

/**
 * 3 katmanlı harf-bulmacalı kilit kadranı. Logo çevresinde iç içe üç harf halkası döner;
 * katman ofsetleri tam-tur katına oturduğunda üst okuma yayında dıştan içe CALI / BRA / HUB
 * (= CALIBRAHUB) okunur, karışıkken decoy harfler okumayı bozar. Renkler Material3 tema
 * token'larından gelir; "çözüldü" yeşili tek yerel sabittir (M3 şemasında success tokeni yok).
 *
 * Durum davranışları:
 * - Idle: kadran tamamen sabittir (sürekli animasyon yok); katmanlar taban karışık konumda.
 * - Typing: her karakter bir katmanı bir slot döndürür (tuş sırası katmanları döngüsel
 *   gezer), silme geri sarar; spring geçişi mekanik "dişli" hissi verir. Taban ofsetler
 *   yarım-slot kesirli olduğundan yazarken hizalanma İMKANSIZDIR — CALIBRAHUB oluşmaz.
 * - Loading: katmanlar düşük genlikli "ayar arıyor" salınımı yapar (kalibrasyon hissi).
 * - Solved (YALNIZ sunucu login onayı): katmanlar sırayla en yakın hizalı konuma "klik"
 *   diye oturur, segment harfleri yeşile döner, decoy'lar söner, kadran yeşil parıltıyla
 *   yanar — bulmaca çözüldü.
 * - Failed: hizalanma OLMAZ; kadran kısa sarsılma (shake) + kırmızı flaş verir, harfler
 *   görünür kalır ama kelime oluşmaz. Yeni yazımda tekrar Typing'e dönülür.
 */
@Composable
private fun CalibrationLockDial(
    passwordLength: Int,
    state: LockDialState,
    modifier: Modifier = Modifier
) {
    // ── Katman ofsetleri (slot birimi) — her katman kendi Animatable'ı ile döner ────────
    val ringOffsets = remember { LockDialRings.map { Animatable(it.baseScramble) } }

    LaunchedEffect(state, passwordLength) {
        when (state) {
            LockDialState.Solved -> {
                // Kombinasyon kilidi çözülüşü: katmanlar dıştan içe SIRAYLA hizaya oturur.
                LockDialRings.indices.forEach { r ->
                    launch {
                        delay(r * 170L)
                        ringOffsets[r].animateTo(
                            targetValue = ringSolvedTarget(passwordLength, r),
                            animationSpec = spring(dampingRatio = 0.58f, stiffness = 700f)
                        )
                    }
                }
            }
            else -> {
                // Idle/Typing/Loading/Failed: karışık (yazım) hedefine dön. Efekt her tuşta
                // yeniden başlar; süren animasyon iptal olup mevcut konumdan retarget eder.
                LockDialRings.indices.forEach { r ->
                    launch {
                        ringOffsets[r].animateTo(
                            targetValue = ringTypingTarget(passwordLength, r),
                            animationSpec = spring(dampingRatio = 0.72f, stiffness = 300f)
                        )
                    }
                }
            }
        }
    }

    // ── Yükleme salınımı — yalnız Loading'de döner, çıkışta sıfıra iner ─────────────────
    val wobble = remember { Animatable(0f) }
    LaunchedEffect(state) {
        if (state == LockDialState.Loading) {
            wobble.snapTo(0f)
            wobble.animateTo(
                targetValue = 1f,
                animationSpec = infiniteRepeatable(
                    animation = tween(durationMillis = 850, easing = FastOutSlowInEasing),
                    repeatMode = RepeatMode.Reverse
                )
            )
        } else {
            wobble.animateTo(0f, tween(durationMillis = 200))
        }
    }

    // ── Çözülme yeşili / hata kırmızısı / sarsılma geçişleri ────────────────────────────
    val solveGlow = remember { Animatable(0f) }   // 0..1 → yeşil vurgu ağırlığı
    val failFlash = remember { Animatable(0f) }   // 0..1 → kırmızı flaş ağırlığı
    val shakeDeg  = remember { Animatable(0f) }   // derece → tüm katmanlara eklenen sarsılma
    LaunchedEffect(state) {
        when (state) {
            LockDialState.Solved -> {
                failFlash.snapTo(0f)
                // Katman klikleri başladıktan hemen sonra yeşil dalga yükselir.
                solveGlow.animateTo(1f, tween(durationMillis = 700, delayMillis = 300))
            }
            LockDialState.Failed -> {
                solveGlow.animateTo(0f, tween(durationMillis = 150))
                launch {
                    failFlash.animateTo(1f, tween(durationMillis = 90))
                    failFlash.animateTo(0f, tween(durationMillis = 650))
                }
                // Kilit "reddetti" sarsılması: sönümlenen açısal jiggle.
                shakeDeg.animateTo(
                    targetValue = 0f,
                    animationSpec = keyframes {
                        durationMillis = 500
                        0f at 0
                        5.5f at 70
                        -4.5f at 160
                        3.2f at 260
                        -2f at 350
                        1f at 430
                        0f at 500
                    }
                )
            }
            else -> {
                solveGlow.animateTo(0f, tween(durationMillis = 250))
                failFlash.snapTo(0f)
                shakeDeg.snapTo(0f)
            }
        }
    }

    // ── Harf ölçüm altyapısı — layout'lar (katman, harf) anahtarıyla cache'lenir ────────
    val textMeasurer = rememberTextMeasurer(cacheSize = 64)
    val ringStyles = remember {
        LockDialRings.map { TextStyle(fontSize = it.fontSp.sp, fontWeight = FontWeight.SemiBold) }
    }
    val letterLayouts = remember(textMeasurer) { mutableMapOf<Pair<Int, Char>, TextLayoutResult>() }

    val letterColor  = MaterialTheme.colorScheme.onSurfaceVariant
    val trackColor   = MaterialTheme.colorScheme.outline
    val accentColor  = MaterialTheme.colorScheme.primary
    val errorColor   = MaterialTheme.colorScheme.error
    val successGreen = Color(0xFF2FBF71) // "bulmaca çözüldü" yeşili — light/dark'ta okunur

    Canvas(modifier = modifier) {
        val mid   = Offset(size.width / 2f, size.height / 2f)
        val p     = solveGlow.value
        val q     = failFlash.value
        val w     = wobble.value
        val shake = shakeDeg.value

        // 0) Merkez parıltı: nötr accent → çözülünce yeşil, hatada kısa kırmızı ton.
        val glowCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.7f)
        drawCircle(
            brush = Brush.radialGradient(
                colors = listOf(
                    glowCol.copy(alpha = 0.10f + 0.16f * p + 0.05f * w + 0.10f * q),
                    Color.Transparent
                ),
                center = mid,
                radius = size.minDimension / 2f
            ),
            radius = size.minDimension / 2f,
            center = mid
        )

        // 1) Yapı çemberleri — katmanları ayıran ince oluklar (rozet iskeleti).
        val bezelCol = lerp(lerp(trackColor, successGreen, p * 0.8f), errorColor, q * 0.5f)
        for (bezelRadiusDp in listOf(51f, 64.5f, 77.5f, 91f)) {
            drawCircle(
                color = bezelCol.copy(alpha = 0.22f),
                radius = bezelRadiusDp.dp.toPx(),
                center = mid,
                style = Stroke(width = 1.dp.toPx())
            )
        }

        // 2) Çözüldüğünde üst okuma yayının arkasına yumuşak yeşil hale (harflerin altına).
        if (p > 0.01f) {
            val arcMidR = 71f.dp.toPx()
            drawArc(
                color = successGreen.copy(alpha = 0.14f * p),
                startAngle = -145f,
                sweepAngle = 110f,
                useCenter = false,
                topLeft = mid - Offset(arcMidR, arcMidR),
                size = Size(arcMidR * 2f, arcMidR * 2f),
                style = Stroke(width = 40f.dp.toPx(), cap = StrokeCap.Round)
            )
        }

        // 3) Harf katmanları. Her harf kendi slot açısında, teğetsel yönelimle (kadran gibi)
        //    çizilir; sarsılma (shake) tüm katman açılarına eklenir. Segment harfleri
        //    çözülmeden önce decoy'larla AYNI görünür (bulmaca ipucu sızdırmaz); çözülünce
        //    segment yeşile döner, decoy'lar söner → CALIBRAHUB öne çıkar.
        LockDialRings.forEachIndexed { r, spec ->
            val slots       = spec.letters.length
            val stepDeg     = 360f / slots
            val wobbleSlots = w * 0.16f * spec.direction * (1f - r * 0.18f)
            val offset      = ringOffsets[r].value + wobbleSlots
            val radiusPx    = spec.radiusDp.dp.toPx()
            val centerShift = (spec.segmentLength - 1) / 2f

            spec.letters.forEachIndexed { j, ch ->
                val deg = -90f + shake + (j - centerShift + offset) * stepDeg
                val rad = deg * (PI / 180.0)
                val letterCenter = mid + Offset(cos(rad).toFloat(), sin(rad).toFloat()) * radiusPx
                val layout = letterLayouts.getOrPut(r to ch) {
                    textMeasurer.measure(AnnotatedString(ch.toString()), ringStyles[r])
                }
                val isSegment = j < spec.segmentLength
                val baseCol =
                    if (isSegment) lerp(letterColor.copy(alpha = 0.85f), successGreen, p)
                    else letterColor.copy(alpha = 0.85f - 0.60f * p)
                val finalCol = lerp(baseCol, errorColor, q * 0.65f)
                rotate(degrees = deg + 90f, pivot = letterCenter) {
                    drawText(
                        textLayoutResult = layout,
                        color = finalCol,
                        topLeft = letterCenter - Offset(layout.size.width / 2f, layout.size.height / 2f)
                    )
                }
            }
        }

        // 4) Okuma penceresi işareti — üstte sabit indeks çentiği (kombinasyon kilidi imi).
        //    Stator'a aittir: sarsılmadan etkilenmez, kadranın nereden okunduğunu gösterir.
        val markerCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.6f)
        val up = Offset(0f, -1f)
        drawLine(
            color = markerCol.copy(alpha = 0.85f),
            start = mid + up * 92.5f.dp.toPx(),
            end = mid + up * 97.5f.dp.toPx(),
            strokeWidth = 2.5f.dp.toPx(),
            cap = StrokeCap.Round
        )
    }
}

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
 * CALIBRAHUB'ın kilit kadranında dönen TEK kelimesi. Decoy/dolgu harf YOK — üç katman da
 * BİREBİR bu 10 harfi, çemberin tamamı boyunca sırayla taşır. Katmanlar hizalandığında
 * (ofsetleri eşit ya da 10'un aynı katı) üçü üst üste tam çakışır ve tek net "CALIBRAHUB"
 * okunur; hizasızken her katman farklı açıda durduğundan aynı slotta üç farklı harf üst
 * üste biner (hayalet/üçlenmiş görünüm).
 */
private const val LockDialWord = "CALIBRAHUB"

/**
 * Bir kilit katmanının sabit tanımı. Üç katman AYNI yarıçapta + AYNI 10 harfi taşır — İÇ İÇE
 * (konsantrik) DEĞİL, ÜST ÜSTE (superimposed) çizilirler; yalnız kendi taban faz ofseti
 * ([baseScramble]) ve dönüş yönüyle ([direction]) birbirinden ayrışırlar. [alpha] düşük
 * tutulur ki üç katman üst üste bindiğinde alttaki/üstteki görünsün (yarı saydam çakışma).
 *
 * [baseScramble] katmanlar arası BİLEREK tam sayı farkı OLMAYAN (kesirli) değerlerdedir:
 * yazma adımları hep tam sayı slot olduğundan iki katman arasındaki faz farkı yazarken asla
 * tam sayıya (dolayısıyla hizaya) yuvarlanamaz — "doğru parolayı yazarak çözme" ihtimali
 * yapısal olarak kapalıdır. Çözülme YALNIZ sunucu onayında, katmanlar ortak hedefe (10'un
 * katı) animasyonla oturtularak gerçekleşir.
 */
private data class LockLayerSpec(
    val baseScramble: Float,  // taban faz ofseti (slot; katmanlar arası kesirli fark zorunlu)
    val direction: Int,       // yazarken dönüş yönü: +1 saat yönü, -1 tersi
    val alpha: Float,         // katman saydamlığı — üst üste binme bu sayede görünür olur
)

/**
 * 3 katman — hepsi [LockDialWord]'ün aynı 10 harfini, aynı yarıçap bandında taşır. Taban
 * fazları kasıtlı farklı + kesirli (0 / +0.35 / -0.30 slot) → boşta üç kopya hafif kaymış
 * açılarda üst üste biner ("hayalet" görünüm). Alpha değerleri kademeli (derinlik hissi);
 * üçü hizalandığında toplam kaplama ~%93'e çıkar ve tek net kelime gibi görünür.
 */
private val LockDialLayers = listOf(
    LockLayerSpec(baseScramble =  0.00f, direction = +1, alpha = 0.55f),
    LockLayerSpec(baseScramble =  0.35f, direction = -1, alpha = 0.60f),
    LockLayerSpec(baseScramble = -0.30f, direction = +1, alpha = 0.65f),
)

private const val LockDialRadiusDp = 74f   // üç katmanın da PAYLAŞTIĞI ortak yarıçap
private const val LockDialFontSp   = 17f   // ortak harf punto boyutu

/**
 * n. tuş vuruşu katmanları döngüsel gezer (1→katman0, 2→katman1, 3→katman2, 4→katman0…):
 * [layer] katmanının parola [passwordLength] uzunluğundayken almış olduğu toplam adım sayısı.
 * Uzunluktan türetildiği için silme işlemi otomatik olarak aynı adımları geri sarar.
 */
private fun layerSteps(passwordLength: Int, layer: Int): Int =
    if (passwordLength <= layer) 0 else (passwordLength - layer + 2) / 3

/** Yazma/karışma hedef ofseti (slot) — taban faz + yönlü adımlar. */
private fun layerTypingTarget(passwordLength: Int, layer: Int): Float {
    val spec = LockDialLayers[layer]
    return spec.baseScramble + spec.direction * layerSteps(passwordLength, layer)
}

/**
 * Çözülmüş hedef ofset: mevcut karışık konuma EN YAKIN tam-tur (10 slot) katı. Her katman
 * KENDİ en yakın 10-katına oturur; 10 slot = 360° olduğundan hangi katına oturduklarından
 * bağımsız olarak üçünün GÖRSEL rotasyonu birebir çakışır (mod 360 eşit) — yani üç katman
 * "kayıt olur" (register) ve tek kelime gibi görünür.
 */
private fun layerSolvedTarget(passwordLength: Int, layer: Int): Float {
    val slots = LockDialWord.length
    return ((layerTypingTarget(passwordLength, layer) / slots).roundToInt() * slots).toFloat()
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
 * 3 katmanlı harf-bulmacalı kilit kadranı. Katmanlar İÇ İÇE (konsantrik) DEĞİL — logo
 * çevresinde AYNI yarıçap bandında ÜST ÜSTE binmiş 3 yarı saydam kopya olarak döner; her
 * kopya CALIBRAHUB'ın aynı 10 harfini taşır (decoy YOK). Katmanlar hizalandığında üçü tam
 * çakışıp (register) tek net "CALIBRAHUB" okunur; hizasızken aynı slotta üç farklı katmandan
 * üç farklı harf üst üste biner (hayalet/üçlenmiş görünüm) ve kelime okunmaz. Renkler
 * Material3 tema token'larından gelir; "çözüldü" yeşili tek yerel sabittir (M3 şemasında
 * success tokeni yok).
 *
 * Durum davranışları:
 * - Idle: kadran tamamen sabittir (sürekli animasyon yok); katmanlar taban faz farklarıyla
 *   hizasız durur (üst üste binen harfler karışık, kelime oluşmaz).
 * - Typing: her karakter bir katmanı bir slot döndürür (tuş sırası katmanları döngüsel
 *   gezer), silme geri sarar; spring geçişi mekanik "dişli" hissi verir. Taban fazlar
 *   kesirli olduğundan yazarken katmanlar arası fark asla tam sayıya düşmez — CALIBRAHUB
 *   yazarak OLUŞMAZ, örtüşme yalnızca kayar/daha da karışır.
 * - Loading: katmanlar düşük genlikli "ayar arıyor" salınımı yapar (kalibrasyon hissi).
 * - Solved (YALNIZ sunucu login onayı): katmanlar sırayla en yakın ortak hizaya "klik" diye
 *   oturur (register), üç kopya tam üst üste çakışır, harfler yeşile döner, kadran yeşil
 *   parıltıyla yanar — bulmaca çözüldü.
 * - Failed: hizalanma OLMAZ; katmanlar hizasız (üçlenmiş karışık) kalır, kadran kısa
 *   sarsılma (shake) + kırmızı flaş verir. Yeni yazımda tekrar Typing'e dönülür.
 */
@Composable
private fun CalibrationLockDial(
    passwordLength: Int,
    state: LockDialState,
    modifier: Modifier = Modifier
) {
    // ── Katman ofsetleri (slot birimi) — her katman kendi Animatable'ı ile döner ────────
    val layerOffsets = remember { LockDialLayers.map { Animatable(it.baseScramble) } }

    LaunchedEffect(state, passwordLength) {
        when (state) {
            LockDialState.Solved -> {
                // Kombinasyon kilidi çözülüşü: katmanlar SIRAYLA ortak hizaya "kayıt olur"
                // (register) — her biri kendi en yakın 10-katına oturur, mod-360 aynı
                // rotasyonda çakışırlar.
                LockDialLayers.indices.forEach { li ->
                    launch {
                        delay(li * 170L)
                        layerOffsets[li].animateTo(
                            targetValue = layerSolvedTarget(passwordLength, li),
                            animationSpec = spring(dampingRatio = 0.58f, stiffness = 700f)
                        )
                    }
                }
            }
            else -> {
                // Idle/Typing/Loading/Failed: karışık (yazım) hedefine dön. Efekt her tuşta
                // yeniden başlar; süren animasyon iptal olup mevcut konumdan retarget eder.
                LockDialLayers.indices.forEach { li ->
                    launch {
                        layerOffsets[li].animateTo(
                            targetValue = layerTypingTarget(passwordLength, li),
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

    // ── Harf ölçüm altyapısı — CALIBRAHUB'ın harfleri bir kez ölçülür; üç katman da AYNI
    //    layout'u paylaşır (aynı yarıçap + aynı punto olduğundan katman başına ayrı ölçüm
    //    gerekmez) ─────────────────────────────────────────────────────────────────────
    val textMeasurer = rememberTextMeasurer(cacheSize = 16)
    val layerStyle = remember { TextStyle(fontSize = LockDialFontSp.sp, fontWeight = FontWeight.Bold) }
    val letterLayouts = remember(textMeasurer) {
        val map = mutableMapOf<Char, TextLayoutResult>()
        LockDialWord.forEach { ch ->
            map.getOrPut(ch) { textMeasurer.measure(AnnotatedString(ch.toString()), layerStyle) }
        }
        map
    }

    val letterColor  = MaterialTheme.colorScheme.onSurfaceVariant
    val trackColor   = MaterialTheme.colorScheme.outline
    val accentColor  = MaterialTheme.colorScheme.primary
    val errorColor   = MaterialTheme.colorScheme.error
    val successGreen = Color(0xFF2FBF71) // "bulmaca çözüldü" yeşili — light/dark'ta okunur

    Canvas(modifier = modifier) {
        val mid      = Offset(size.width / 2f, size.height / 2f)
        val p        = solveGlow.value
        val q        = failFlash.value
        val w        = wobble.value
        val shake    = shakeDeg.value
        val radiusPx = LockDialRadiusDp.dp.toPx()

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

        // 1) Paylaşılan yarıçap bandı — üç katmanın ORTAK yörüngesini gösteren geniş, soluk
        //    tek track (iç içe bezel halkaları DEĞİL: üç katman bu TEK bandın üzerinde üst
        //    üste döner).
        val bandCol = lerp(lerp(trackColor, successGreen, p * 0.8f), errorColor, q * 0.5f)
        drawCircle(
            color = bandCol.copy(alpha = 0.15f),
            radius = radiusPx,
            center = mid,
            style = Stroke(width = 26.dp.toPx())
        )

        // 2) Çözüldüğünde bandın tamamı yumuşak yeşil hale ile parlar (artık kelime tek bir
        //    okuma yayı değil, çemberin tamamını kaplıyor).
        if (p > 0.01f) {
            drawCircle(
                color = successGreen.copy(alpha = 0.16f * p),
                radius = radiusPx,
                center = mid,
                style = Stroke(width = 34.dp.toPx())
            )
        }

        // 3) 3 harf katmanı ÜST ÜSTE (superimposed) — her katman CALIBRAHUB'ın 10 harfini
        //    TAM ÇEMBER boyunca çizer, hepsi AYNI radiusPx'te. Sarsılma (shake) tüm katman
        //    açılarına eklenir. Hizasızken katmanlar farklı fazda olduğundan aynı slotta üç
        //    farklı harf üst üste biner (hayalet); çözülünce (p→1) hepsi ortak faza oturmuş
        //    olur (layerSolvedTarget) ve alpha 1'e yaklaşıp yeşile döner → tek net kelime.
        LockDialLayers.forEachIndexed { li, spec ->
            val stepDeg     = 360f / LockDialWord.length
            val wobbleSlots = w * 0.14f * spec.direction * (1f - li * 0.15f)
            val offset      = layerOffsets[li].value + wobbleSlots
            val layerAlpha  = lerp(spec.alpha, 1f, p)

            LockDialWord.forEachIndexed { j, ch ->
                val deg = -90f + shake + (j + offset) * stepDeg
                val rad = deg * (PI / 180.0)
                val letterCenter = mid + Offset(cos(rad).toFloat(), sin(rad).toFloat()) * radiusPx
                val layout = letterLayouts.getValue(ch)
                val baseCol = lerp(letterColor, successGreen, p).copy(alpha = layerAlpha)
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

        // 4) Okuma işareti — üstte sabit indeks çentiği; çözülünce "C" bu işaretin altında
        //    durur. Stator'a aittir: sarsılmadan etkilenmez, kadranın nereden okunduğunu
        //    gösterir.
        val markerCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.6f)
        val up = Offset(0f, -1f)
        drawLine(
            color = markerCol.copy(alpha = 0.85f),
            start = mid + up * 90f.dp.toPx(),
            end = mid + up * 95f.dp.toPx(),
            strokeWidth = 2.5f.dp.toPx(),
            cap = StrokeCap.Round
        )
    }
}

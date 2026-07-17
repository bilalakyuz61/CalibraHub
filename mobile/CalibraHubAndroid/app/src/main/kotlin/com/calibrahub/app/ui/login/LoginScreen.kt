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
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.util.lerp
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.calibrahub.app.R
import com.calibrahub.app.app
import com.calibrahub.app.data.CompanyDto
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.roundToInt

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
 * CALIBRAHUB'ın kimlik kalibrasyon kadranındaki 3 iğne (yelkovan) rengi — merkezden (hub)
 * çıkan, farklı uzunluk/kalınlıkta 3 ibre. Web login ekranındaki `cald-*` SVG kadranıyla
 * (bkz. `Views/Account/Login.cshtml`, `window.CalibraLoginDial`) BİREBİR aynı kanonik marka
 * paleti — SABİT hex'tir (tema token'ından DEĞİL), light/dark her ikisinde de aynı canlı vurgu.
 */
private val NeedleColorIndigo = Color(0xFF6366F1)  // n1 — ana (şifre) iğnesi, en uzun+kalın
private val NeedleColorCyan   = Color(0xFF06B6D4)  // n2 — ikincil iğne
private val NeedleColorViolet = Color(0xFF8B5CF6)  // n3 — üçüncül iğne, en kısa+ince

private const val DialSlotCount = 12  // kadran çentik sayısı (web ile aynı: 12 çentik)
private const val DialSlotDeg   = 30f // çentik başına açı = 360/12 (sabit değer, const-ifade riski yok)

/**
 * Bir iğnenin sabit tanımı: dinlenme (idle) çentiği + yazarken ilerleme yönü ([direction] —
 * YALNIZ kozmetik, hangi yöne "ilerlediğini" belirler, sonucu etkilemez) + görsel özellikler
 * (renk/uzunluk/kalınlık — saat ibresi hiyerarşisi: n1 en uzun+kalın, n3 en kısa+ince).
 * 3 iğne birbirinden TAM 120° ayrık çentiklerde (1/5/9 → 30°/150°/270°) dinlenir.
 */
private data class NeedleSpec(
    val restSlot: Int,
    val direction: Int,
    val color: Color,
    val lengthDp: Float,
    val strokeDp: Float,
)

private val DialNeedles = listOf(
    NeedleSpec(restSlot = 1, direction = +1, color = NeedleColorIndigo, lengthDp = 58f, strokeDp = 3.4f),
    NeedleSpec(restSlot = 5, direction = -1, color = NeedleColorCyan,   lengthDp = 46f, strokeDp = 2.6f),
    NeedleSpec(restSlot = 9, direction = +1, color = NeedleColorViolet, lengthDp = 35f, strokeDp = 2.1f),
)

private const val DialTrackRadiusDp = 74f   // kadran çentik halkasının yarıçapı

/**
 * n. tuş vuruşu iğneleri döngüsel gezer (round-robin: 1.tuş→n1, 2.tuş→n2, 3.tuş→n3, 4.tuş→n1…):
 * [r] iğnesinin parola [passwordLength] uzunluğundayken almış olduğu toplam adım sayısı.
 * Uzunluktan türetildiği için silme işlemi otomatik olarak aynı adımları geri sarar.
 *
 * [KRİTİK — 2026-07-16 kullanıcı geri bildirimi]: önceki sürümde "tüm iğneler sürekli hareket
 * ediyor" şikayeti alınmıştı — bu ASLA tekrarlanmayacak. Bu fonksiyon yalnızca [r] iğnesinin
 * SIRASI geldiğinde artar; diğer iki iğnenin hedefi bu tuş vuruşunda DEĞİŞMEZ — dolayısıyla o
 * iki iğne için sonraki animateTo çağrısı aynı hedefe no-op'a yakın kalır ve görsel olarak
 * KIPIRDAMAZ. Web'deki needleSteps(len, r) ile birebir aynı sonucu üretir (bkz. Login.cshtml).
 */
private fun needleSteps(passwordLength: Int, r: Int): Int =
    if (passwordLength <= r) 0 else (passwordLength - r + 2) / 3

/** Yazma/karışma hedef açısı (derece) — dinlenme açısı + yönlü adım sayısı * çentik açısı. */
private fun needleTypingTarget(passwordLength: Int, r: Int): Float {
    val spec = DialNeedles[r]
    return spec.restSlot * DialSlotDeg + spec.direction * DialSlotDeg * needleSteps(passwordLength, r)
}

/**
 * Çözülmüş hedef açı: iğnenin mevcut (unwrap edilmiş, hiç mod alınmamış) açısına EN YAKIN
 * 360°'nin katı. Üç iğne de kendi en yakın katına oturduğunda mod-360 hepsi 0°'de (yukarı/12
 * yönü) çakışır — "kilit açıldı" hizası. Web'deki `Math.round(ndlDeg[r] / 360) * 360` ile aynı.
 */
private fun needleSolvedTarget(passwordLength: Int, r: Int): Float {
    val current = needleTypingTarget(passwordLength, r)
    return (current / 360f).roundToInt() * 360f
}

/**
 * Login formunun üstünde ortalı marka bloğu: 3 iğneli (yelkovan) kalibrasyon kadranı + logo +
 * alt başlık. Stateless — kadranın tepki verdiği durum (parola uzunluğu, kadran durumu)
 * parametreyle yukarıdan gelir (state hoisting), kendi state'i yoktur.
 *
 * @param passwordLength Parola alanındaki karakter sayısı; her karakter bir iğneyi bir çentik
 *                       döndürür (iğne seçimi tuş sırasına göre round-robin döngüseldir).
 * @param dialState      Kadranın görsel durumu — Solved yalnız sunucu login onayında gelir.
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
 * 3 iğneli (yelkovan) analog kalibrasyon kadranı. Merkezden (hub) çıkan 3 ibre — n1 (indigo,
 * en uzun+kalın = ana/şifre iğnesi), n2 (cyan, orta), n3 (violet, en kısa+ince) — 12 çentikli
 * (30°'lik) dairesel kadranda döner. Web login ekranındaki `cald-*` SVG kadranıyla (bkz.
 * `Views/Account/Login.cshtml`, `window.CalibraLoginDial`) BİREBİR aynı kanonik tasarım ve
 * davranış; yalnız çizim teknolojisi farklıdır (SVG rotate() transform ↔ Compose Canvas
 * rotate()/translate() DrawScope — trigonometri gerekmez). İğne renkleri sabit marka
 * hex'lerinden gelir; track/tick/hub gibi nötr öğeler Material3 tema token'larından gelir.
 *
 * Durum davranışları:
 * - Idle: 3 iğne SABİTTİR (animasyon yok), dinlenme çentiklerinde (1/5/9 → 120° ayrık) durur.
 * - Typing: her şifre karakteri SADECE BİR iğneyi bir çentik döndürür (round-robin, tuş
 *   sırasına göre); TÜM iğneler DEĞİL, SÜREKLİ DEĞİL — diğer iki iğne o tuş vuruşunda
 *   KIPIRDAMAZ (bkz. [needleSteps] dokümantasyonu). Silme aynı adımları geri sarar.
 * - Loading: her iğne KISA BİR KEZLİK "ölçüm" sekmesi yapar (küçük kick + aynı konuma yaylı
 *   geri dönüş), sonra DURUR — client parola doğrulamaz, yalnız "işleniyor" hissi verir.
 *   Sürekli dönen iğne YOKTUR; bunun yerine merkez parıltı yumuşak nefes alır (measuringPulse).
 * - Solved (YALNIZ sunucu login onayı): iğneler SIRAYLA kendi en yakın 360° katına "klik"
 *   diye oturur (register) — üçü de mod-360 aynı açıda (yukarı/12 yönü) çakışır, yeşile döner,
 *   kadran yeşil parıltıyla yanar.
 * - Failed: hizalanma OLMAZ; her iğne kısa bir sekme (kick) yapıp kendi hizasız konumuna geri
 *   oturur, kadran kısa bir yatay sarsılma (shake) + kırmızı flaş verir.
 */
@Composable
private fun CalibrationLockDial(
    passwordLength: Int,
    state: LockDialState,
    modifier: Modifier = Modifier
) {
    // ── İğne açıları (derece, unwrap — hiç mod alınmaz) — her iğne kendi Animatable'ı ile döner
    val needleAngles = remember { DialNeedles.map { Animatable(it.restSlot * DialSlotDeg) } }

    LaunchedEffect(state, passwordLength) {
        when (state) {
            LockDialState.Solved -> {
                // Kilit açılışı: iğneler SIRAYLA en yakın ortak faza "klik" diye oturur.
                DialNeedles.indices.forEach { r ->
                    launch {
                        delay(r * 150L)
                        needleAngles[r].animateTo(
                            targetValue = needleSolvedTarget(passwordLength, r),
                            animationSpec = spring(dampingRatio = 0.55f, stiffness = 260f)
                        )
                    }
                }
            }
            LockDialState.Failed -> {
                // Reddetme sekmesi: kısa kick + AYNI (hizasız) konuma yaylı geri dönüş —
                // hizalanma OLMAZ, sadece "reddedildi" tepkisi.
                DialNeedles.indices.forEach { r ->
                    launch {
                        val settled = needleAngles[r].value
                        val kick = (if (r % 2 == 0) -1f else 1f) * (9f + r * 3f)
                        needleAngles[r].animateTo(settled + kick, tween(90, easing = FastOutSlowInEasing))
                        needleAngles[r].animateTo(settled, spring(dampingRatio = 0.45f, stiffness = 220f))
                    }
                }
            }
            LockDialState.Loading -> {
                // Kısa bir kezlik "ölçüm" sekmesi — küçük kick + AYNI konuma yaylı geri dönüş;
                // client parola doğrulamaz, yalnız "işleniyor" hissi verir. Sekme bitince
                // iğneler DURUR (sürekli dönen iğne yok — merkez parıltı nefesi ayrıca sürer).
                DialNeedles.indices.forEach { r ->
                    launch {
                        val settled = needleAngles[r].value
                        val kick = (if (r % 2 == 0) 1f else -1f) * (14f + r * 4f)
                        needleAngles[r].animateTo(settled + kick, tween(200, easing = FastOutSlowInEasing))
                        needleAngles[r].animateTo(settled, spring(dampingRatio = 0.5f, stiffness = 260f))
                    }
                }
            }
            else -> {
                // Idle/Typing: yazım hedefine yumuşak spring. SADECE sırası gelen iğnenin
                // hedefi bu tuş vuruşunda değişir; diğer ikisi aynı hedefe no-op'a yakın kalıp
                // görsel olarak kıpırdamaz (bkz. needleSteps dokümantasyonu).
                DialNeedles.indices.forEach { r ->
                    launch {
                        needleAngles[r].animateTo(
                            targetValue = needleTypingTarget(passwordLength, r),
                            animationSpec = spring(dampingRatio = 0.72f, stiffness = 340f)
                        )
                    }
                }
            }
        }
    }

    // ── Yükleme nefesi — merkez parıltının yumuşak nabzı, YALNIZ Loading'de sürer (iğneleri
    //    OYNATMAZ, yalnız glow alpha'sını modüle eder) ─────────────────────────────────────
    val measuringPulse = remember { Animatable(0f) }
    LaunchedEffect(state) {
        if (state == LockDialState.Loading) {
            measuringPulse.snapTo(0f)
            measuringPulse.animateTo(
                targetValue = 1f,
                animationSpec = infiniteRepeatable(
                    animation = tween(durationMillis = 1100, easing = FastOutSlowInEasing),
                    repeatMode = RepeatMode.Reverse
                )
            )
        } else {
            measuringPulse.animateTo(0f, tween(durationMillis = 200))
        }
    }

    // ── Çözülme yeşili / hata kırmızısı / yatay sarsılma geçişleri ──────────────────────────
    val solveGlow = remember { Animatable(0f) }   // 0..1 → yeşil vurgu ağırlığı
    val failFlash = remember { Animatable(0f) }   // 0..1 → kırmızı flaş ağırlığı
    val shakeX    = remember { Animatable(0f) }   // dp → tüm kadrana uygulanan yatay sarsılma
    LaunchedEffect(state) {
        when (state) {
            LockDialState.Solved -> {
                failFlash.snapTo(0f)
                // İğne klikleri başladıktan hemen sonra yeşil dalga yükselir.
                solveGlow.animateTo(1f, tween(durationMillis = 700, delayMillis = 300))
            }
            LockDialState.Failed -> {
                solveGlow.animateTo(0f, tween(durationMillis = 150))
                launch {
                    failFlash.animateTo(1f, tween(durationMillis = 90))
                    failFlash.animateTo(0f, tween(durationMillis = 650))
                }
                // Kilit "reddetti" sarsılması — web'in caldShake CSS keyframes'iyle aynı
                // zamanlama/genlik (0/-7/6/-4/3/0, 500ms), yatay öteleme (translate) olarak.
                shakeX.animateTo(
                    targetValue = 0f,
                    animationSpec = keyframes {
                        durationMillis = 500
                        0f at 0
                        -7f at 100
                        6f at 200
                        -4f at 300
                        3f at 400
                        0f at 500
                    }
                )
            }
            else -> {
                solveGlow.animateTo(0f, tween(durationMillis = 250))
                failFlash.snapTo(0f)
                shakeX.snapTo(0f)
            }
        }
    }

    val trackColor   = MaterialTheme.colorScheme.outline
    val tickColor    = MaterialTheme.colorScheme.onSurfaceVariant
    val accentColor  = MaterialTheme.colorScheme.primary
    val errorColor   = MaterialTheme.colorScheme.error
    val successGreen = Color(0xFF2FBF71) // "bulmaca çözüldü" yeşili — light/dark'ta okunur

    Canvas(modifier = modifier) {
        val mid     = Offset(size.width / 2f, size.height / 2f)
        val p       = solveGlow.value
        val q       = failFlash.value
        val pulse   = measuringPulse.value
        val shakePx = shakeX.value.dp.toPx()
        val trackR  = DialTrackRadiusDp.dp.toPx()

        translate(left = shakePx) {
            // 0) Merkez parıltı: nötr accent → çözülünce yeşil, hatada kısa kırmızı ton;
            //    Loading'de yumuşak nefes alır (pulse).
            val glowCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.7f)
            drawCircle(
                brush = Brush.radialGradient(
                    colors = listOf(
                        glowCol.copy(alpha = 0.10f + 0.16f * p + 0.06f * pulse + 0.10f * q),
                        Color.Transparent
                    ),
                    center = mid,
                    radius = size.minDimension / 2f
                ),
                radius = size.minDimension / 2f,
                center = mid
            )

            // 1) İnce kadran çemberi — üç iğnenin ORTAK yörüngesini gösteren tek track.
            val bandCol = lerp(lerp(trackColor, successGreen, p * 0.8f), errorColor, q * 0.5f)
            drawCircle(
                color = bandCol.copy(alpha = 0.5f),
                radius = trackR,
                center = mid,
                style = Stroke(width = 1.4.dp.toPx())
            )

            // 2) 12 çentik — her 3.'sü (0°/90°/180°/270°) belirgin (major); rotate() ile
            //    yerleştirilir, trigonometri gerekmez.
            for (i in 0 until DialSlotCount) {
                val isMajor   = i % 3 == 0
                val outerR    = trackR + 2.dp.toPx()
                val innerR    = outerR - (if (isMajor) 11.dp.toPx() else 6.dp.toPx())
                val tickAlpha = if (isMajor) 0.65f else 0.32f
                rotate(degrees = i * DialSlotDeg, pivot = mid) {
                    drawLine(
                        color = tickColor.copy(alpha = tickAlpha),
                        start = mid + Offset(0f, -innerR),
                        end = mid + Offset(0f, -outerR),
                        strokeWidth = if (isMajor) 2.2.dp.toPx() else 1.4.dp.toPx(),
                        cap = StrokeCap.Round
                    )
                }
            }

            // 3) Çözüldüğünde track'in tamamı yumuşak yeşil hale ile parlar.
            if (p > 0.01f) {
                drawCircle(
                    color = successGreen.copy(alpha = 0.16f * p),
                    radius = trackR,
                    center = mid,
                    style = Stroke(width = 10.dp.toPx())
                )
            }

            // 4) 3 iğne — merkezden (hub) çıkar, kendi rengi/uzunluğu/kalınlığıyla döner.
            //    SVG'deki gibi düz-yukarı çizilip rotate() ile döndürülür; trigonometri
            //    gerekmez (harf-yerleşimi versiyonunun aksine).
            DialNeedles.forEachIndexed { r, spec ->
                val angle     = needleAngles[r].value
                val needleCol = lerp(lerp(spec.color, successGreen, p), errorColor, q * 0.75f)
                val lengthPx  = spec.lengthDp.dp.toPx()
                val strokePx  = spec.strokeDp.dp.toPx()
                rotate(degrees = angle, pivot = mid) {
                    drawLine(
                        color = needleCol,
                        start = mid,
                        end = mid + Offset(0f, -lengthPx),
                        strokeWidth = strokePx,
                        cap = StrokeCap.Round
                    )
                    // n1 (ana/şifre iğnesi) küçük bir kuyruk taşır — gerçek saat ibresi hissi.
                    if (r == 0) {
                        drawLine(
                            color = needleCol,
                            start = mid,
                            end = mid + Offset(0f, 7.dp.toPx()),
                            strokeWidth = strokePx,
                            cap = StrokeCap.Round
                        )
                    }
                }
            }

            // 5) Hub — iğnelerin buluştuğu merkez nokta (logo rozetinin altında kalır, yalnız
            //    kenarları sızabilir; yine de tema/renk tutarlılığı için çizilir).
            val hubCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.6f)
            drawCircle(color = hubCol, radius = 4.dp.toPx(), center = mid)

            // 6) Okuma işareti — üstte sabit çentik; çözülünce üç iğne bu işaretin altında
            //    (yukarı/12 yönü) hizalanır.
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
}

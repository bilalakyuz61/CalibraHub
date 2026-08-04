package com.calibrahub.app.ui.login

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Business
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material.icons.filled.WarningAmber
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.util.lerp
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.CompanyDto
import com.calibrahub.app.data.SessionManager
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlin.random.Random

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(onLoggedIn: () -> Unit) {
    val context = LocalContext.current
    val repo    = context.app.authRepository
    val session = context.app.session
    val scope   = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var email    by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var baseUrl  by remember { mutableStateOf("") }
    var showPwd  by remember { mutableStateOf(false) }
    var loading  by remember { mutableStateOf(false) }
    var showServerSettings by remember { mutableStateOf(false) }

    // "Beni hatırla" (2026-07-17) — varsayılan AÇIK (bkz. LaunchedEffect(Unit) altta, DataStore'da
    // kayıtlı son tercih varsa onunla ezilir). AÇIKKEN: login başarılı olursa oturum çerezi +
    // e-posta/displayName/companyId/companyName DataStore'a kalıcı yazılır (bkz. doLogin) ve
    // uygulama bir sonraki açılışta otomatik giriş dener. KAPALIYSA hiçbiri persist edilmez.
    var rememberMe by remember { mutableStateOf(true) }

    // Sunucu doğrulama ("Doğrula") akışının görsel durumu — login kilit-kadranından
    // (dialState/LockDialState, aşağıda) TAMAMEN bağımsız; kadrana hiç dokunmaz.
    var pingState by remember { mutableStateOf<ServerPingState>(ServerPingState.Idle) }
    // OutlinedTextField'ın gerçek bir blur (focus kaybı) mi yoksa ilk composition mu yaşadığını
    // ayırt etmek için — onFocusChanged ilk mount'ta da isFocused=false ile bir kez tetiklenir;
    // bunu "kullanıcı alandan ayrıldı" sanıp gereksiz otomatik ping atmayalım.
    var baseUrlFieldWasFocused by remember { mutableStateOf(false) }

    // Parola doğrulandıktan sonra dönen erişilebilir şirket listesi.
    // Boş = kimlik bilgisi adımı gösterilir; dolu = şirket seçim adımı gösterilir.
    var companyChoices by remember { mutableStateOf<List<CompanyDto>>(emptyList()) }

    // Kilit-kadranının bulmaca durumu — YALNIZ görsel katmanı sürer, login akış mantığına
    // karışmaz. KRİTİK: Solved (çözüldü) durumu client'ta parola kontrolüyle DEĞİL, yalnız
    // sunucu login onayıyla (loginCompanies >= 1 şirket veya doLogin başarısı) tetiklenir.
    var dialState by remember { mutableStateOf(LockDialState.Idle) }

    // Mevcut base URL + hatırlanan e-posta/"beni hatırla" tercihi (sunucu ayarları paneli +
    // 2026-07-17 otomatik giriş özelliği için).
    LaunchedEffect(Unit) {
        baseUrl = session.currentBaseUrl()
        rememberMe = session.isRememberMeEnabled()
        session.rememberedEmail()?.let { email = it }
    }

    // Seçilen şirketle asıl login çağrısı — hem "tek şirket → otomatik gir" hem de
    // "şirket seçici → tıkla → gir" akışlarından paylaşılır. Result.fold inline olduğu için
    // (kotlin.Result.fold inline'dır) suspend çağrılar burada askıya alma zincirini bozmaz.
    // [company] (companyId DEĞİL) alınır — companyName'e "beni hatırla" AÇIKKEN persist edilecek
    // bundle için ihtiyaç var (bkz. session.persistSessionDisplay).
    suspend fun doLogin(company: CompanyDto) {
        loading = true
        // Çok-şirket akışında kadran loginCompanies onayıyla zaten çözülmüş olabilir;
        // o durumda Loading'e geri düşürmeyiz (çözülmüş kilit tekrar karışmasın).
        if (dialState != LockDialState.Solved) dialState = LockDialState.Loading
        // "Beni hatırla" tercihi + cookie jar'ın DataStore yazımı LOGIN İSTEĞİNDEN ÖNCE
        // senkronlanmalı — Set-Cookie yanıtı PersistentCookieJar.saveFromResponse içinde SENKRON
        // işlenir (bkz. SessionManager.setRememberMe KDoc).
        session.setRememberMe(rememberMe)
        repo.login(email.trim(), password, company.id).fold(
            onSuccess = { displayName ->
                if (rememberMe) {
                    session.persistSessionDisplay(
                        email = email.trim(),
                        displayName = displayName,
                        companyId = company.id,
                        companyName = company.name
                    )
                }
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
                Spacer(Modifier.height(4.dp))

                // "Beni hatırla" (checkbox DEĞİL, Switch — CLAUDE.md switchkey kuralı).
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        text = "Beni hatırla",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f)
                    )
                    Switch(
                        checked = rememberMe,
                        onCheckedChange = { rememberMe = it },
                        enabled = !loading
                    )
                }
                Spacer(Modifier.height(20.dp))

                Button(
                    onClick = {
                        scope.launch {
                            loading = true
                            dialState = LockDialState.Loading

                            // Erken teşhis: sunucu bu oturumda henüz doğrulanmadıysa (kullanıcı
                            // "Doğrula"yı hiç kullanmadı ya da son sonuç Unreachable/Idle) login'i
                            // hiç denemeden persisted base URL'e (currentBaseUrl — repo.login*
                            // zaten bunu kullanacak) hızlı bir ping at. Ulaşılamıyorsa generic
                            // "kimlik geçersiz" yerine net sunucu hatası erken gösterilir; kilit-
                            // kadranı akışı aynen kalır (Failed durumuna aynı şekilde düşer).
                            if (pingState !is ServerPingState.Verified) {
                                val effectiveUrl = session.currentBaseUrl()
                                val quickCheck = checkServer(session, effectiveUrl)
                                pingState = quickCheck
                                if (quickCheck is ServerPingState.Unreachable) {
                                    loading = false
                                    dialState = LockDialState.Failed
                                    snackbarHostState.showSnackbar("Sunucuya ulaşılamadı. Sunucu ayarlarını kontrol edin.")
                                    return@launch
                                }
                            }

                            repo.loginCompanies(email.trim(), password).fold(
                                onSuccess = { list ->
                                    when {
                                        list.isEmpty() -> {
                                            loading = false
                                            dialState = LockDialState.Failed
                                            snackbarHostState.showSnackbar("Kimlik geçersiz veya erişilebilir şirket yok")
                                        }
                                        list.size == 1 -> doLogin(list.first())
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
                        onClick = { scope.launch { doLogin(company) } },
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
                    onValueChange = {
                        baseUrl = it
                        // Adres değişti — önceki doğrulama sonucu artık geçersiz.
                        pingState = ServerPingState.Idle
                    },
                    label = { Text("Backend URL") },
                    supportingText = {
                        Text("Emulator için: http://10.0.2.2:61001/")
                    },
                    singleLine = true,
                    modifier = Modifier
                        .fillMaxWidth()
                        .onFocusChanged { focus ->
                            // Yalnız GERÇEK bir blur'da (önce focus'luydu, şimdi değil) otomatik
                            // doğrula — ilk composition'da da isFocused=false gelir, o an atlanır.
                            if (baseUrlFieldWasFocused && !focus.isFocused && baseUrl.isNotBlank()) {
                                pingState = ServerPingState.Checking
                                scope.launch { pingState = checkServer(session, baseUrl) }
                            }
                            baseUrlFieldWasFocused = focus.isFocused
                        }
                )

                ServerPingStatusRow(state = pingState)

                Spacer(Modifier.height(8.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    OutlinedButton(
                        onClick = {
                            pingState = ServerPingState.Checking
                            scope.launch { pingState = checkServer(session, baseUrl) }
                        },
                        enabled = baseUrl.isNotBlank() && pingState != ServerPingState.Checking,
                        modifier = Modifier.weight(1f)
                    ) { Text("Doğrula") }

                    OutlinedButton(
                        onClick = {
                            scope.launch {
                                session.setBaseUrl(baseUrl.trim())
                                snackbarHostState.showSnackbar("Sunucu adresi kaydedildi.")
                            }
                        },
                        modifier = Modifier.weight(1f)
                    ) { Text("Kaydet") }
                }
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Sunucu doğrulama ("Doğrula") — LoginScreen'deki Backend URL alanına ait küçük bir
// alt-sistem. Login kilit-kadranından (LockDialState/CalibrationLockDial, aşağıda)
// TAMAMEN bağımsızdır — kadran koduna dokunmaz. GET /api/mobile/ping (anonim) çağırır.
// ─────────────────────────────────────────────────────────────────────────

/**
 * Sunucu doğrulama durumu. [Verified] YALNIZ `ok == true && product == "CalibraHub"` olduğunda
 * set edilir; sunucu 200 dönüp başka bir ürün/gövde döndürürse [NotCalibraHub] — bu durum login
 * denemesini ENGELLEMEZ (yalnız uyarı gösterir), oysa [Unreachable] "Giriş yap" akışını erken
 * keser (bkz. LoginScreen Button onClick).
 */
private sealed class ServerPingState {
    object Idle : ServerPingState()
    object Checking : ServerPingState()
    data class Verified(val version: String) : ServerPingState()
    object NotCalibraHub : ServerPingState()
    object Unreachable : ServerPingState()
}

/**
 * "Doğrula" akışının ortak çekirdeği — otomatik (URL alanı blur), manuel (buton) ve "Giriş yap"
 * öncesi erken-teşhis tetikleyicilerinin üçü de bu fonksiyonu çağırır. [SessionManager.ping]
 * henüz kaydedilmemiş adresi bağımsız/kısa-timeout'lu (5 sn) bir Retrofit çağrısıyla dener.
 */
private suspend fun checkServer(session: SessionManager, rawUrl: String): ServerPingState {
    if (rawUrl.isBlank()) return ServerPingState.Idle
    return session.ping(rawUrl).fold(
        onSuccess = { resp ->
            if (resp.ok && resp.product == "CalibraHub") ServerPingState.Verified(resp.version ?: "?")
            else ServerPingState.NotCalibraHub
        },
        onFailure = { ServerPingState.Unreachable }
    )
}

// "Doğrulandı" yeşili / "uyarı" ambar tonu — CalibrationLockDial'daki successGreen'e (aşağıda,
// dial koduna dokunulmadığı için oradan import edilemez) benzer marka tutarlı SABİT hex'ler;
// dinamik renk (Android 12+ Material You) burada da BİLEREK kullanılmaz (aynı gerekçe: dial
// KDoc'una bkz. — durum rengi wallpaper'a göre değişmemeli).
private val PingVerifiedGreen = Color(0xFF2FBF71)
private val PingWarningAmber = Color(0xFFF59E0B)

/** Backend URL alanının altındaki doğrulama durum satırı — Idle'da hiçbir şey çizmez. */
@Composable
private fun ServerPingStatusRow(state: ServerPingState) {
    if (state == ServerPingState.Idle) return

    val color = when (state) {
        is ServerPingState.Verified   -> PingVerifiedGreen
        ServerPingState.NotCalibraHub -> PingWarningAmber
        ServerPingState.Unreachable   -> MaterialTheme.colorScheme.error
        else                           -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    val text = when (state) {
        ServerPingState.Checking      -> "Sunucu kontrol ediliyor…"
        is ServerPingState.Verified   -> "CalibraHub sunucusu doğrulandı (v${state.version})"
        ServerPingState.NotCalibraHub -> "Bu adres bir CalibraHub sunucusu değil"
        ServerPingState.Unreachable   -> "Sunucuya ulaşılamadı"
        ServerPingState.Idle          -> ""
    }

    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 6.dp)
    ) {
        if (state == ServerPingState.Checking) {
            CircularProgressIndicator(modifier = Modifier.size(14.dp), color = color)
        } else {
            val icon = when (state) {
                is ServerPingState.Verified   -> Icons.Default.CheckCircle
                ServerPingState.NotCalibraHub -> Icons.Default.WarningAmber
                ServerPingState.Unreachable   -> Icons.Default.CloudOff
                else -> null
            }
            if (icon != null) {
                Icon(icon, contentDescription = null, tint = color, modifier = Modifier.size(16.dp))
            }
        }
        Spacer(Modifier.width(6.dp))
        Text(text = text, style = MaterialTheme.typography.bodySmall, color = color)
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

// ─────────────────────────────────────────────────────────────────────────────────────────
// İç kadranlar (2026-07-17 görsel zenginleştirme eki) — 2 eş-merkezli İÇ KADRAN, büyük
// iğnelerden TAMAMEN bağımsız, salt dekoratif rastgele-dönüş katmanı (web'deki caldInner1/
// caldInner2 + spinInnerRandom ile birebir aynı tasarım, bkz. `Views/Account/Login.cshtml`).
// Kendi Animatable state/motoruna sahiptir (bkz. CalibrationLockDial içindeki innerAngles);
// needleSteps/typingTarget/round-robin mantığına hiç dokunmaz, hiç okumaz.
// ─────────────────────────────────────────────────────────────────────────────────────────

/**
 * İç kadranların sabit tanımı. [radiusDp]/[tickLengthDp]/[markRadiusDp] web'deki r=60/8-çentik
 * (cyan) ve r=34/6-çentik (violet) karşılıklarının [DialTrackRadiusDp] (Android track yarıçapı,
 * web'in r=88'ine karşılık gelir) ORANIYLA türetilir — web ile birebir aynı görsel oranı korur.
 * Renkler n2 (cyan) / n3 (violet) iğne renkleriyle AYNI marka hex'i (fixed — needle renk
 * kararıyla aynı gerekçe: light/dark'ta aynı canlı vurgu), yalnız çentik için düşük/işaret
 * noktası için yüksek alfa (web'deki --cald-inner1/2 ve *-mark alfa değerleriyle birebir aynı:
 * sırasıyla .42/.95 ve .40/.95). [spinDurationMs] web'in INNER_SPIN_MS'iyle (340/260) aynıdır.
 */
private data class InnerDialSpec(
    val radiusDp: Float,
    val tickCount: Int,
    val tickLengthDp: Float,
    val tickStrokeDp: Float,
    val tickColor: Color,
    val markColor: Color,
    val markRadiusDp: Float,
    val spinDurationMs: Int,
)

private val InnerDialSpecs = listOf(
    // İç kadran 1 (dış, web r=60) — 8 çentik, cyan ton (n2 rengiyle aynı hex).
    InnerDialSpec(
        radiusDp = DialTrackRadiusDp * 60f / 88f,
        tickCount = 8,
        tickLengthDp = DialTrackRadiusDp * 7f / 88f,
        tickStrokeDp = 1.15f,
        tickColor = NeedleColorCyan.copy(alpha = 0.42f),
        markColor = NeedleColorCyan.copy(alpha = 0.95f),
        markRadiusDp = DialTrackRadiusDp * 2.2f / 88f,
        spinDurationMs = 340,
    ),
    // İç kadran 2 (iç, web r=34) — 6 çentik, violet ton (n3 rengiyle aynı hex).
    InnerDialSpec(
        radiusDp = DialTrackRadiusDp * 34f / 88f,
        tickCount = 6,
        tickLengthDp = DialTrackRadiusDp * 5f / 88f,
        tickStrokeDp = 1.05f,
        tickColor = NeedleColorViolet.copy(alpha = 0.40f),
        markColor = NeedleColorViolet.copy(alpha = 0.95f),
        markRadiusDp = DialTrackRadiusDp * 1.8f / 88f,
        spinDurationMs = 260,
    ),
)

private const val InnerSpinMinDeg = 14f   // web INNER_SPIN_MIN — rastgele dönüş min açısı (derece)
private const val InnerSpinMaxDeg = 52f   // web INNER_SPIN_MAX — rastgele dönüş maks açısı (derece)

/**
 * Web'in JS `easeOutCubic`/`easeOutBack` fonksiyonlarıyla (bkz. Login.cshtml script'i)
 * BİREBİR aynı matematiksel formülü yeniden üretir. Compose'un hazır [FastOutSlowInEasing] vb.
 * eğrileri FARKLI bezier eğrileri olduğundan burada kullanılmaz — web ile "birebir eşle"
 * gereksinimi için web'in kendi formülü aynen (satır satır) taşınır.
 */
private val EaseOutCubic = Easing { fraction ->
    val inv = 1f - fraction
    1f - inv * inv * inv
}
private val EaseOutBackCustom = Easing { fraction ->
    val c1 = 1.70158f
    val c3 = c1 + 1f
    val t = fraction - 1f
    1f + c3 * t * t * t + c1 * t * t
}

// ── Büyük iğnelerin tuş-başı "dura-dura" (detent/stepped) hareketi ─────────────────────────
// Web'deki animateNeedleStepped (bkz. Login.cshtml script'i) ile birebir aynı algoritma:
// [start,target] aralığı NeedleStepCount (4) eşit mini-adıma bölünür; her mini-adım kendi
// payının NeedleStepMoveFraction'ını (%60) easeOutCubic ile HAREKET, kalanını (%40) DURAKLAMA
// (detent hissi) olarak geçirir — son adımdan sonra duraklama YOK. Toplam süre:
// 4×(80×.6) + 3×(80×.4) = 192+96 = 288ms (web ile birebir aynı). delta≈0 olan (bu tuşta sırası
// gelmeyen) iğneler anında snapTo ile çıkar — gereksiz animasyon çalışmaz.
private const val NeedleStepCount = 4
private const val NeedleStepMs = 80
private const val NeedleStepMoveFraction = 0.6f

private suspend fun Animatable<Float, AnimationVector1D>.animateNeedleStepped(target: Float) {
    val start = value
    val delta = target - start
    if (abs(delta) < 0.001f) {
        snapTo(target)
        return
    }
    val moveMs = (NeedleStepMs * NeedleStepMoveFraction).roundToInt()
    val pauseMs = NeedleStepMs - moveMs
    for (step in 1..NeedleStepCount) {
        val boundary = if (step >= NeedleStepCount) target else start + delta * step / NeedleStepCount
        animateTo(boundary, tween(durationMillis = moveMs, easing = EaseOutCubic))
        if (step < NeedleStepCount) delay(pauseMs.toLong())
    }
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
        // Kadran TEK BASINA — uzerinde logo YOK (2026-07-19 kullanici karari, web ile parite).
        // Onceden merkeze 92dp'lik logo diski bindiriliyordu (Image, dial'dan SONRA cizildigi
        // icin ustte kaliyordu). Logonun yaricapi 46dp; ic kadranlar r=28.6 ve r=50.5, igneler
        // 35/46/58dp uzunlugunda oldugundan logo ic kadranlarin ikisini de ve iki ignenin
        // tamamini ortuyor, ucuncusunun yalnizca ~12dp ucunu birakiyordu → kalibrasyon
        // animasyonu (tik-tik adimlama + ic kadran random donusu) fiilen GORUNMUYORDU.
        // Web'de kadranin uzerinde/icinde logo yoktur; marka gorseli ayri "login-hero"
        // panelindedir (bkz. Views/Account/Login.cshtml) — mobilde o panel olmadigi icin
        // kadran tek basina birakildi, kimlik alttaki "Mobil Companion" yazisiyla tasinir.
        CalibrationLockDial(
            passwordLength = passwordLength,
            state = dialState,
            modifier = Modifier.size(200.dp)
        )
        Spacer(Modifier.height(20.dp))
        Text(
            text = "Mobil Companion",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

/**
 * 3 iğneli (yelkovan) analog kalibrasyon kadranı + 2 eş-merkezli İÇ KADRAN. Merkezden (hub)
 * çıkan 3 ibre — n1 (indigo, en uzun+kalın = ana/şifre iğnesi), n2 (cyan, orta), n3 (violet, en
 * kısa+ince) — 12 çentikli (30°'lik) dairesel kadranda döner. İki iç kadran (İç-1: 8 çentik/
 * cyan, İç-2: 6 çentik/violet — bkz. [InnerDialSpecs]) büyük iğnelerden TAMAMEN bağımsız kendi
 * rastgele-dönüş motoruna sahiptir. Web login ekranındaki `cald-*` SVG kadranıyla (bkz.
 * `Views/Account/Login.cshtml`, `window.CalibraLoginDial`) BİREBİR aynı kanonik tasarım ve
 * davranış; yalnız çizim teknolojisi farklıdır (SVG rotate() transform ↔ Compose Canvas
 * rotate()/translate() DrawScope — trigonometri gerekmez). İğne renkleri sabit marka
 * hex'lerinden gelir; iç kadran çentik/işaret renkleri de AYNI n2/n3 hex'i (sabit) kullanır —
 * track/tick/hub gibi nötr öğeler Material3 tema token'larından gelir.
 *
 * Durum davranışları:
 * - Idle: 3 iğne SABİTTİR (animasyon yok), dinlenme çentiklerinde (1/5/9 → 120° ayrık) durur.
 * - Typing: her şifre karakteri SADECE BİR iğneyi bir çentik döndürür (round-robin, tuş
 *   sırasına göre); TÜM iğneler DEĞİL, SÜREKLİ DEĞİL — diğer iki iğne o tuş vuruşunda
 *   KIPIRDAMAZ (bkz. [needleSteps] dokümantasyonu). Hareketin kendisi artık tek parça glide
 *   değil, "dura-dura" (detent/stepped, bkz. [animateNeedleStepped]) — 4 mini-adım, her biri
 *   hızlı hareket + kısa duraklama, toplam ~288ms. Silme aynı adımları geri sarar.
 * - İç kadranlar: HER parola değişiminde (yazma VEYA silme — [passwordLength] her değiştiğinde,
 *   ilk composition hariç) her iki iç kadran BAĞIMSIZ rastgele yön (CW/CCW) + rastgele küçük
 *   açı (14°-52°) ile easeOutBack dönüşle döner. needleSteps/round-robin mantığına dokunmaz;
 *   Loading/Solved/Failed durumlarından ETKİLENMEZ (ayrı `LaunchedEffect(passwordLength)`,
 *   büyük iğnelerin `LaunchedEffect(state, passwordLength)`'inden bağımsız).
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

    // ── İç kadran açıları — büyük iğnelerden AYRI state/motor (unwrap, hiç mod alınmaz); web'in
    //    innerDeg=[0,0] başlangıcıyla aynı: 0f = "henüz hiç spin olmamış" konumu.
    val innerAngles = remember { InnerDialSpecs.map { Animatable(0f) } }

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
                // Idle/Typing: yazım hedefine "dura-dura" (detent/stepped) hareketle gider —
                // web'in animateNeedleStepped'iyle birebir aynı algoritma (bkz.
                // [animateNeedleStepped] uzantı fonksiyonu, NeedleStepCount/Ms/MoveFraction).
                // SADECE sırası gelen iğnenin hedefi bu tuş vuruşunda değişir; diğer ikisi aynı
                // hedefe delta≈0 olduğundan anında snapTo ile no-op'a düşer, görsel olarak
                // KIPIRDAMAZ (bkz. needleSteps dokümantasyonu).
                DialNeedles.indices.forEach { r ->
                    launch {
                        needleAngles[r].animateNeedleStepped(needleTypingTarget(passwordLength, r))
                    }
                }
            }
        }
    }

    // ── İç kadranlar — büyük iğnelerden TAMAMEN bağımsız, salt dekoratif rastgele-dönüş
    //    motoru (bkz. InnerDialSpecs / innerAngles). Web'in yalnız pwEl 'input' event'inde
    //    spinInnerRandom() çağırmasıyla birebir aynı tetikleme granülaritesi: yalnız GERÇEK
    //    parola değişiminde (yazma VEYA silme) döner. needleSteps/typingTarget/round-robin/
    //    state (Loading/Solved/Failed) mantığına HİÇ dokunmaz, hiç okumaz — anahtar SADECE
    //    passwordLength'tir (web'in `mode==='measuring'||mode==='solved'` erken-çıkışına
    //    eşdeğer bir koruma burada gerekmez: parola alanı zaten `enabled=!loading` ile o
    //    durumlarda devre dışı bırakılır, bkz. LoginScreen).
    var innerSpinArmed by remember { mutableStateOf(false) }
    LaunchedEffect(passwordLength) {
        // İlk composition'da (henüz hiçbir tuşa basılmamışken) spin OLMAZ — web'de bu durum
        // zaten oluşmaz (JS 'input' event'i yalnız gerçek kullanıcı etkileşiminde ateşlenir);
        // Compose'ta LaunchedEffect ilk mount'ta da bir kez çalıştığından bu guard onu simüle
        // eder.
        if (!innerSpinArmed) {
            innerSpinArmed = true
            return@LaunchedEffect
        }
        InnerDialSpecs.forEachIndexed { idx, spec ->
            launch {
                val direction = if (Random.nextBoolean()) 1f else -1f
                val amount = InnerSpinMinDeg + Random.nextFloat() * (InnerSpinMaxDeg - InnerSpinMinDeg)
                innerAngles[idx].animateTo(
                    targetValue = innerAngles[idx].value + direction * amount,
                    animationSpec = tween(durationMillis = spec.spinDurationMs, easing = EaseOutBackCustom)
                )
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

            // 2b) İç kadranlar (2 adet, eş-merkezli) — büyük iğnelerden bağımsız, salt
            //     dekoratif rastgele-dönüş katmanı (bkz. InnerDialSpecs / innerAngles). Her
            //     kadran: ince sabit halka (trackColor, web'in .cald-inner-track'ine karşılık
            //     gelir) + kendi açısınca (innerAngles[idx]) dönen çentik grubu + tepe işaret
            //     noktası (web'deki caldInner1/caldInner2 <g> transform'una karşılık gelir).
            //     Her çentik/işaret TEK SEVİYE rotate() ile (spin açısı + kendi yerel açısı
            //     toplamı) çizilir — iç içe transform'a gerek yok, needle çizimiyle aynı idiom.
            //     Solved/Failed renk lerp'i (p/q) UYGULANMAZ — web'de de bu katman solve/fail
            //     durumundan etkilenmez (yalnız .cald-needle/.cald-tail retint olur).
            InnerDialSpecs.forEachIndexed { idx, spec ->
                val radiusPx = spec.radiusDp.dp.toPx()
                drawCircle(
                    color = trackColor.copy(alpha = 0.33f),
                    radius = radiusPx,
                    center = mid,
                    style = Stroke(width = 0.9.dp.toPx())
                )
                val spin = innerAngles[idx].value
                for (i in 0 until spec.tickCount) {
                    val tickDeg = i * (360f / spec.tickCount)
                    rotate(degrees = spin + tickDeg, pivot = mid) {
                        drawLine(
                            color = spec.tickColor,
                            start = mid + Offset(0f, -radiusPx),
                            end = mid + Offset(0f, -(radiusPx - spec.tickLengthDp.dp.toPx())),
                            strokeWidth = spec.tickStrokeDp.dp.toPx(),
                            cap = StrokeCap.Round
                        )
                    }
                }
                rotate(degrees = spin, pivot = mid) {
                    drawCircle(
                        color = spec.markColor,
                        radius = spec.markRadiusDp.dp.toPx(),
                        center = mid + Offset(0f, -radiusPx)
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

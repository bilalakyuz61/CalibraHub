package com.calibrahub.mobile.ui.login

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.AnimationVector1D
import androidx.compose.animation.core.Easing
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.material.icons.filled.Business
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material.icons.filled.WarningAmber
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
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
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.util.lerp
import com.calibrahub.mobile.data.ApiResult
import com.calibrahub.mobile.data.CompanyDto
import com.calibrahub.mobile.data.PingResponse
import com.calibrahub.mobile.session.SessionManager
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlin.random.Random
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Giris ekrani — CalibraHubAndroid `ui/login/LoginScreen.kt` ile BIREBIR ayni akis + kalibrasyon
 * kadrani animasyonu (sadik port). Faz 2a'da POC'un basit LoginScreen'inin (sirket-once/parola-
 * sonra, Retrofit-benzeri kotlin.Result akisi) YERINE gecer.
 *
 * Farklar (yalniz KMP altyapisina uyum, DAVRANIS AYNI):
 * - `LocalContext.current.app.repository/session` yerine [session] parametre olarak alinir
 *   (state hoisting — bkz. AppDrawer.kt/HomeScreen.kt'deki AYNI karar).
 * - Android'in `repo.login`/`repo.loginCompanies` (kotlin.Result<String?>/Result<List<CompanyDto>>)
 *   yerine [SessionManager.login]/[SessionManager.loginCompanies] (ApiResult<LoginResponse>/
 *   ApiResult<List<CompanyDto>>) kullanilir — sonuc `when` ile ayristirilir (ApiResult'ta `.fold`
 *   yok, bkz. AuthApi.kt KDoc'u); LoginResponse.ok/error alanlarinin islenmesi Android'deki
 *   `body.ok`/`body.error` ile AYNI.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(sessionManager: SessionManager, onLoggedIn: () -> Unit) {
    val session = sessionManager
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    // Sunucu adresi ARTIK PARCALI tutulur (host + port + sema). Kullanici tek bir "Backend URL"
    // yazarken sema/slash/port hatalari sik yapiliyordu; parcali giris her alani tek basina
    // dogrulanabilir kilar. Ag katmani hala tek bir URL bekledigi icin [composeBaseUrl] ile
    // birlestirilir, kayitli deger [parseBaseUrl] ile geri ayristirilir.
    var host by remember { mutableStateOf("") }
    var port by remember { mutableStateOf("") }
    var useHttps by remember { mutableStateOf(false) }
    val baseUrl = composeBaseUrl(host, port, useHttps)
    var showPwd by remember { mutableStateOf(false) }
    var loading by remember { mutableStateOf(false) }
    var showServerSettings by remember { mutableStateOf(false) }

    // "Beni hatırla" — varsayılan AÇIK (bkz. LaunchedEffect(Unit) altta, kayıtlı son tercih
    // varsa onunla ezilir). AÇIKKEN: login başarılı olursa oturum çerezi + e-posta/displayName/
    // companyId/companyName kalıcı yazılır (bkz. doLogin) ve uygulama bir sonraki açılışta
    // otomatik giriş dener. KAPALIYSA hiçbiri persist edilmez.
    var rememberMe by remember { mutableStateOf(true) }

    // "Parolayi hatirla" — [rememberMe]'den AYRI ve varsayilani KAPALI (bkz.
    // SessionManager.isRememberPasswordEnabled KDoc'u: parola cerezden farkli bir risk sinifi).
    var rememberPassword by remember { mutableStateOf(false) }

    // Sunucu doğrulama ("Doğrula") akışının görsel durumu — login kilit-kadranından tamamen
    // bağımsız; kadrana hiç dokunmaz.
    var pingState by remember { mutableStateOf<ServerPingState>(ServerPingState.Idle) }
    var baseUrlFieldWasFocused by remember { mutableStateOf(false) }

    // Parola doğrulandıktan sonra dönen erişilebilir şirket listesi. Boş = kimlik bilgisi adımı
    // gösterilir; dolu = şirket seçim adımı gösterilir.
    var companyChoices by remember { mutableStateOf<List<CompanyDto>>(emptyList()) }

    // Kilit-kadranının bulmaca durumu — YALNIZ görsel katmanı sürer, login akış mantığına
    // karışmaz. Solved yalnız sunucu login onayıyla (loginCompanies >= 1 şirket veya doLogin
    // başarısı) tetiklenir.
    var dialState by remember { mutableStateOf(LockDialState.Idle) }

    LaunchedEffect(Unit) {
        val parsed = parseBaseUrl(session.currentBaseUrl())
        host = parsed.host
        port = parsed.port
        useHttps = parsed.https
        rememberMe = session.isRememberMeEnabled()
        session.rememberedEmail()?.let { email = it }
        rememberPassword = session.isRememberPasswordEnabled()
        session.rememberedPassword()?.let { password = it }
    }

    suspend fun doLogin(company: CompanyDto) {
        loading = true
        if (dialState != LockDialState.Solved) dialState = LockDialState.Loading
        session.setRememberMe(rememberMe)
        when (val result = session.login(email.trim(), password, company.id)) {
            is ApiResult.Success -> {
                val body = result.data
                if (body.ok) {
                    if (rememberMe) {
                        session.persistSessionDisplay(
                            email = email.trim(),
                            displayName = body.displayName,
                            companyId = company.id,
                            companyName = company.name,
                        )
                    }
                    // Menu suzgeci icin izinleri cek — /login yaniti izin tasimaz ve
                    // otomatik-giris probe'u yalniz uygulama acilisinda calisir. Bu cagri
                    // olmadan elle giris yapan kullanicinin menusu suzulmeden kalirdi.
                    // Sessizce basarisiz olabilir (izinler null -> hicbir sey gizlenmez).
                    session.refreshPermissions()
                    // Parola yalniz tercih ACIKKEN ve giris DOGRULANDIKTAN sonra yazilir —
                    // yanlis parola hicbir zaman diske dusmez.
                    session.persistPasswordIfEnabled(password)
                    if (dialState != LockDialState.Solved) {
                        dialState = LockDialState.Solved
                        delay(1600)
                    } else {
                        delay(250)
                    }
                    onLoggedIn()
                } else {
                    dialState = LockDialState.Failed
                    snackbarHostState.showSnackbar("Giriş başarısız: ${body.error ?: "bilinmeyen hata"}")
                }
            }
            is ApiResult.Failure -> {
                dialState = LockDialState.Failed
                snackbarHostState.showSnackbar("Giriş başarısız: ${result.message}")
            }
        }
        loading = false
    }

    /**
     * Sunucuyu dogrular; BASARILIYSA adresi kendiliginden kaydeder.
     *
     * Eskiden "Dogrula" yalniz kontrol ediyordu, kaydetmek icin ayrica "Kaydet"e basmak
     * gerekiyordu — dogrulayip kaydetmeden cikan kullanici bir sonraki acilista eski adresle
     * karsilasiyordu. Basarisiz dogrulama HICBIR SEY kaydetmez: calisan bir adres, calismayan
     * bir denemeyle ezilmemeli.
     */
    fun verifyServer() {
        pingState = ServerPingState.Checking
        scope.launch {
            val result = checkServer(session, baseUrl)
            pingState = result
            if (result is ServerPingState.Verified) {
                session.setBaseUrl(baseUrl)
                snackbarHostState.showSnackbar("Sunucu doğrulandı ve kaydedildi.")
            }
        }
    }

    fun onGirisClick() {
        loading = true
        dialState = LockDialState.Loading
        companyChoices = emptyList()
        scope.launch {
            // Erken teşhis: sunucu bu oturumda henüz doğrulanmadıysa login'i hiç denemeden
            // persisted base URL'e hızlı bir ping at. Ulaşılamıyorsa generic "kimlik geçersiz"
            // yerine net sunucu hatası erken gösterilir.
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

            when (val result = session.loginCompanies(email.trim(), password)) {
                is ApiResult.Success -> {
                    val list = result.data
                    when {
                        list.isEmpty() -> {
                            loading = false
                            dialState = LockDialState.Failed
                            snackbarHostState.showSnackbar("Kimlik geçersiz veya erişilebilir şirket yok")
                        }
                        list.size == 1 -> doLogin(list.first())
                        else -> {
                            loading = false
                            dialState = LockDialState.Solved
                            companyChoices = list
                        }
                    }
                }
                is ApiResult.Failure -> {
                    loading = false
                    dialState = LockDialState.Failed
                    snackbarHostState.showSnackbar("Kimlik geçersiz veya erişilebilir şirket yok")
                }
            }
        }
    }

    Scaffold(snackbarHost = { SnackbarHost(snackbarHostState) }) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(24.dp),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally,
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
                    modifier = Modifier.fillMaxWidth(),
                )
                Spacer(Modifier.height(12.dp))

                OutlinedTextField(
                    value = password,
                    onValueChange = {
                        password = it
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
                            else Icon(Icons.Default.Visibility, contentDescription = "Parolayı göster")
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                )
                Spacer(Modifier.height(4.dp))

                // "Beni hatırla" (checkbox DEĞİL, Switch — CLAUDE.md switchkey kuralı).
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(
                        text = "Beni hatırla",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f),
                    )
                    Switch(
                        checked = rememberMe,
                        onCheckedChange = { rememberMe = it },
                        enabled = !loading,
                    )
                }

                // "Parolayi hatirla" — AYRI ve varsayilani KAPALI. Kapatildiginda saklanan
                // parola ANINDA silinir (SessionManager.setRememberPassword), "kapattim ama
                // diskte duruyor" durumu olusmaz.
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(
                        text = "Parolayı hatırla",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f),
                    )
                    Switch(
                        checked = rememberPassword,
                        onCheckedChange = { enabled ->
                            rememberPassword = enabled
                            scope.launch { session.setRememberPassword(enabled) }
                        },
                        enabled = !loading,
                    )
                }
                Spacer(Modifier.height(20.dp))

                Button(
                    onClick = { onGirisClick() },
                    enabled = !loading && email.isNotBlank() && password.isNotBlank(),
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    if (loading) CircularProgressIndicator(modifier = Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary)
                    else Text("Giriş yap")
                }
            } else {
                // ── Adım 2: birden çok şirket erişimi varsa seçim ─────────
                Text(
                    text = "Şirket seçin",
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.fillMaxWidth(),
                )
                Spacer(Modifier.height(12.dp))

                companyChoices.forEach { company ->
                    OutlinedButton(
                        onClick = { scope.launch { doLogin(company) } },
                        enabled = !loading,
                        modifier = Modifier.fillMaxWidth(),
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
                        dialState = if (password.isEmpty()) LockDialState.Idle else LockDialState.Typing
                    },
                    enabled = !loading,
                ) { Text("Geri") }
            }

            Spacer(Modifier.height(32.dp))
            TextButton(onClick = { showServerSettings = !showServerSettings }) {
                Text(if (showServerSettings) "Sunucu ayarlarını gizle" else "Sunucu ayarları")
            }

            if (showServerSettings) {
                Spacer(Modifier.height(8.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    OutlinedTextField(
                        value = host,
                        onValueChange = {
                            host = it
                            pingState = ServerPingState.Idle
                        },
                        label = { Text("Sunucu adresi") },
                        supportingText = { Text("IP ya da alan adı") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                        modifier = Modifier
                            .weight(2f)
                            .onFocusChanged { focus ->
                                if (baseUrlFieldWasFocused && !focus.isFocused && host.isNotBlank()) {
                                    verifyServer()
                                }
                                baseUrlFieldWasFocused = focus.isFocused
                            },
                    )
                    OutlinedTextField(
                        value = port,
                        onValueChange = { input ->
                            // Yalniz rakam: port alanina yanlislikla ":" veya bosluk girilmesi
                            // en sik yapilan hataydi, giriste elenir.
                            port = input.filter { it.isDigit() }.take(5)
                            pingState = ServerPingState.Idle
                        },
                        label = { Text("Port") },
                        supportingText = { Text("örn. 61001") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.weight(1f),
                    )
                }

                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
                ) {
                    Text(
                        text = "HTTPS kullan",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f),
                    )
                    Switch(
                        checked = useHttps,
                        onCheckedChange = {
                            useHttps = it
                            pingState = ServerPingState.Idle
                        },
                    )
                }

                ServerPingStatusRow(state = pingState)

                Spacer(Modifier.height(8.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    OutlinedButton(
                        onClick = { verifyServer() },
                        enabled = host.isNotBlank() && pingState != ServerPingState.Checking,
                        modifier = Modifier.weight(1f),
                    ) { Text("Doğrula") }

                    OutlinedButton(
                        onClick = {
                            scope.launch {
                                session.setBaseUrl(baseUrl)
                                snackbarHostState.showSnackbar("Sunucu adresi kaydedildi.")
                            }
                        },
                        enabled = host.isNotBlank(),
                        modifier = Modifier.weight(1f),
                    ) { Text("Kaydet") }
                }
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Sunucu doğrulama ("Doğrula") — LoginScreen'deki Backend URL alanına ait küçük bir alt-sistem.
// Login kilit-kadranından TAMAMEN bağımsızdır. GET /api/mobile/ping (anonim) çağırır.
// ─────────────────────────────────────────────────────────────────────────

/** Parcali sunucu adresi — [parseBaseUrl] cikti tipi. */
private data class ParsedBaseUrl(val host: String, val port: String, val https: Boolean)

/**
 * Kayitli tek-parca base URL'i ("http://192.168.2.61:61001/") host + port + sema uclusune ayirir.
 * Ktor'un Url ayristiricisi yerine elle: girdi kullanicidan gelmis YARIM bir metin olabilir
 * ("192.168.2.61", "10.0.2.2:61001") ve Url() boyle degerlerde exception atar — burada
 * ayristirma HICBIR ZAMAN patlamamali, en kotu ihtimalle bos alanlar doner.
 */
private fun parseBaseUrl(raw: String?): ParsedBaseUrl {
    val text = raw?.trim().orEmpty()
    if (text.isEmpty()) return ParsedBaseUrl("", "", false)

    val https = text.startsWith("https://", ignoreCase = true)
    val withoutScheme = text
        .removePrefix("https://").removePrefix("HTTPS://")
        .removePrefix("http://").removePrefix("HTTP://")
    // Yol/sorgu kismini at — parcali UI yalniz host:port yonetir.
    val authority = withoutScheme.substringBefore('/').substringBefore('?')
    val host = authority.substringBefore(':').trim()
    val port = authority.substringAfter(':', "").filter { it.isDigit() }
    return ParsedBaseUrl(host = host, port = port, https = https)
}

/**
 * Parcalari tek bir base URL'e birlestirir. Port bos birakilirsa semanin varsayilan portu
 * kullanilir (URL'e hic yazilmaz) — kullanici 80/443 yazmak zorunda kalmasin. Sondaki "/"
 * KORUNUR: Ktor'un defaultRequest url'i goreli yol ekledigi icin eksikligi yol birlesmesini bozar.
 */
private fun composeBaseUrl(host: String, port: String, https: Boolean): String {
    val cleanHost = host.trim().trimEnd('/')
    if (cleanHost.isEmpty()) return ""
    val scheme = if (https) "https" else "http"
    val cleanPort = port.filter { it.isDigit() }
    return if (cleanPort.isEmpty()) "$scheme://$cleanHost/" else "$scheme://$cleanHost:$cleanPort/"
}

private sealed class ServerPingState {
    data object Idle : ServerPingState()
    data object Checking : ServerPingState()
    data class Verified(val version: String) : ServerPingState()
    data object NotCalibraHub : ServerPingState()
    data object Unreachable : ServerPingState()
}

private suspend fun checkServer(session: SessionManager, rawUrl: String): ServerPingState {
    if (rawUrl.isBlank()) return ServerPingState.Idle
    return session.ping(rawUrl).fold(
        onSuccess = { resp: PingResponse ->
            if (resp.ok && resp.product == "CalibraHub") ServerPingState.Verified(resp.version ?: "?")
            else ServerPingState.NotCalibraHub
        },
        onFailure = { ServerPingState.Unreachable },
    )
}

private val PingVerifiedGreen = Color(0xFF2FBF71)
private val PingWarningAmber = Color(0xFFF59E0B)

@Composable
private fun ServerPingStatusRow(state: ServerPingState) {
    if (state == ServerPingState.Idle) return

    val color = when (state) {
        is ServerPingState.Verified -> PingVerifiedGreen
        ServerPingState.NotCalibraHub -> PingWarningAmber
        ServerPingState.Unreachable -> MaterialTheme.colorScheme.error
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    val text = when (state) {
        ServerPingState.Checking -> "Sunucu kontrol ediliyor…"
        is ServerPingState.Verified -> "CalibraHub sunucusu doğrulandı (v${state.version})"
        ServerPingState.NotCalibraHub -> "Bu adres bir CalibraHub sunucusu değil"
        ServerPingState.Unreachable -> "Sunucuya ulaşılamadı"
        ServerPingState.Idle -> ""
    }

    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 6.dp),
    ) {
        if (state == ServerPingState.Checking) {
            CircularProgressIndicator(modifier = Modifier.size(14.dp), color = color)
        } else {
            val icon = when (state) {
                is ServerPingState.Verified -> Icons.Default.CheckCircle
                ServerPingState.NotCalibraHub -> Icons.Default.WarningAmber
                ServerPingState.Unreachable -> Icons.Default.CloudOff
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

/** Kilit-kadranının görsel durum makinesi — YALNIZ görsel katmanı sürer, login akış mantığını
 * etkilemez. Solved, client'ta parola kontrolüyle değil sadece sunucu onayıyla set edilir. */
private enum class LockDialState { Idle, Typing, Loading, Solved, Failed }

private val NeedleColorIndigo = Color(0xFF6366F1)
private val NeedleColorCyan = Color(0xFF06B6D4)
private val NeedleColorViolet = Color(0xFF8B5CF6)

private const val DialSlotCount = 12
private const val DialSlotDeg = 30f

private data class NeedleSpec(
    val restSlot: Int,
    val direction: Int,
    val color: Color,
    val lengthDp: Float,
    val strokeDp: Float,
)

private val DialNeedles = listOf(
    NeedleSpec(restSlot = 1, direction = +1, color = NeedleColorIndigo, lengthDp = 58f, strokeDp = 3.4f),
    NeedleSpec(restSlot = 5, direction = -1, color = NeedleColorCyan, lengthDp = 46f, strokeDp = 2.6f),
    NeedleSpec(restSlot = 9, direction = +1, color = NeedleColorViolet, lengthDp = 35f, strokeDp = 2.1f),
)

private const val DialTrackRadiusDp = 74f

private fun needleSteps(passwordLength: Int, r: Int): Int =
    if (passwordLength <= r) 0 else (passwordLength - r + 2) / 3

private fun needleTypingTarget(passwordLength: Int, r: Int): Float {
    val spec = DialNeedles[r]
    return spec.restSlot * DialSlotDeg + spec.direction * DialSlotDeg * needleSteps(passwordLength, r)
}

private fun needleSolvedTarget(passwordLength: Int, r: Int): Float {
    val current = needleTypingTarget(passwordLength, r)
    return (current / 360f).roundToInt() * 360f
}

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

private const val InnerSpinMinDeg = 14f
private const val InnerSpinMaxDeg = 52f

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

@Composable
private fun CalibraLoginBadge(
    passwordLength: Int,
    dialState: LockDialState,
) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        CalibrationLockDial(
            passwordLength = passwordLength,
            state = dialState,
            modifier = Modifier.size(200.dp),
        )
        Spacer(Modifier.height(20.dp))
        Text(
            text = "Mobil Companion",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun CalibrationLockDial(
    passwordLength: Int,
    state: LockDialState,
    modifier: Modifier = Modifier,
) {
    val needleAngles = remember { DialNeedles.map { Animatable(it.restSlot * DialSlotDeg) } }
    val innerAngles = remember { InnerDialSpecs.map { Animatable(0f) } }

    LaunchedEffect(state, passwordLength) {
        when (state) {
            LockDialState.Solved -> {
                DialNeedles.indices.forEach { r ->
                    launch {
                        delay(r * 150L)
                        needleAngles[r].animateTo(
                            targetValue = needleSolvedTarget(passwordLength, r),
                            animationSpec = spring(dampingRatio = 0.55f, stiffness = 260f),
                        )
                    }
                }
            }
            LockDialState.Failed -> {
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
                DialNeedles.indices.forEach { r ->
                    launch {
                        needleAngles[r].animateNeedleStepped(needleTypingTarget(passwordLength, r))
                    }
                }
            }
        }
    }

    var innerSpinArmed by remember { mutableStateOf(false) }
    LaunchedEffect(passwordLength) {
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
                    animationSpec = tween(durationMillis = spec.spinDurationMs, easing = EaseOutBackCustom),
                )
            }
        }
    }

    val measuringPulse = remember { Animatable(0f) }
    LaunchedEffect(state) {
        if (state == LockDialState.Loading) {
            measuringPulse.snapTo(0f)
            measuringPulse.animateTo(
                targetValue = 1f,
                animationSpec = infiniteRepeatable(
                    animation = tween(durationMillis = 1100, easing = FastOutSlowInEasing),
                    repeatMode = RepeatMode.Reverse,
                ),
            )
        } else {
            measuringPulse.animateTo(0f, tween(durationMillis = 200))
        }
    }

    val solveGlow = remember { Animatable(0f) }
    val failFlash = remember { Animatable(0f) }
    val shakeX = remember { Animatable(0f) }
    LaunchedEffect(state) {
        when (state) {
            LockDialState.Solved -> {
                failFlash.snapTo(0f)
                solveGlow.animateTo(1f, tween(durationMillis = 700, delayMillis = 300))
            }
            LockDialState.Failed -> {
                solveGlow.animateTo(0f, tween(durationMillis = 150))
                launch {
                    failFlash.animateTo(1f, tween(durationMillis = 90))
                    failFlash.animateTo(0f, tween(durationMillis = 650))
                }
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
                    },
                )
            }
            else -> {
                solveGlow.animateTo(0f, tween(durationMillis = 250))
                failFlash.snapTo(0f)
                shakeX.snapTo(0f)
            }
        }
    }

    val trackColor = MaterialTheme.colorScheme.outline
    val tickColor = MaterialTheme.colorScheme.onSurfaceVariant
    val accentColor = MaterialTheme.colorScheme.primary
    val errorColor = MaterialTheme.colorScheme.error
    val successGreen = Color(0xFF2FBF71)

    Canvas(modifier = modifier) {
        val mid = Offset(size.width / 2f, size.height / 2f)
        val p = solveGlow.value
        val q = failFlash.value
        val pulse = measuringPulse.value
        val shakePx = shakeX.value.dp.toPx()
        val trackR = DialTrackRadiusDp.dp.toPx()

        translate(left = shakePx) {
            val glowCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.7f)
            drawCircle(
                brush = Brush.radialGradient(
                    colors = listOf(
                        glowCol.copy(alpha = 0.10f + 0.16f * p + 0.06f * pulse + 0.10f * q),
                        Color.Transparent,
                    ),
                    center = mid,
                    radius = size.minDimension / 2f,
                ),
                radius = size.minDimension / 2f,
                center = mid,
            )

            val bandCol = lerp(lerp(trackColor, successGreen, p * 0.8f), errorColor, q * 0.5f)
            drawCircle(
                color = bandCol.copy(alpha = 0.5f),
                radius = trackR,
                center = mid,
                style = Stroke(width = 1.4.dp.toPx()),
            )

            for (i in 0 until DialSlotCount) {
                val isMajor = i % 3 == 0
                val outerR = trackR + 2.dp.toPx()
                val innerR = outerR - (if (isMajor) 11.dp.toPx() else 6.dp.toPx())
                val tickAlpha = if (isMajor) 0.65f else 0.32f
                rotate(degrees = i * DialSlotDeg, pivot = mid) {
                    drawLine(
                        color = tickColor.copy(alpha = tickAlpha),
                        start = mid + Offset(0f, -innerR),
                        end = mid + Offset(0f, -outerR),
                        strokeWidth = if (isMajor) 2.2.dp.toPx() else 1.4.dp.toPx(),
                        cap = StrokeCap.Round,
                    )
                }
            }

            InnerDialSpecs.forEachIndexed { idx, spec ->
                val radiusPx = spec.radiusDp.dp.toPx()
                drawCircle(
                    color = trackColor.copy(alpha = 0.33f),
                    radius = radiusPx,
                    center = mid,
                    style = Stroke(width = 0.9.dp.toPx()),
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
                            cap = StrokeCap.Round,
                        )
                    }
                }
                rotate(degrees = spin, pivot = mid) {
                    drawCircle(
                        color = spec.markColor,
                        radius = spec.markRadiusDp.dp.toPx(),
                        center = mid + Offset(0f, -radiusPx),
                    )
                }
            }

            if (p > 0.01f) {
                drawCircle(
                    color = successGreen.copy(alpha = 0.16f * p),
                    radius = trackR,
                    center = mid,
                    style = Stroke(width = 10.dp.toPx()),
                )
            }

            DialNeedles.forEachIndexed { r, spec ->
                val angle = needleAngles[r].value
                val needleCol = lerp(lerp(spec.color, successGreen, p), errorColor, q * 0.75f)
                val lengthPx = spec.lengthDp.dp.toPx()
                val strokePx = spec.strokeDp.dp.toPx()
                rotate(degrees = angle, pivot = mid) {
                    drawLine(
                        color = needleCol,
                        start = mid,
                        end = mid + Offset(0f, -lengthPx),
                        strokeWidth = strokePx,
                        cap = StrokeCap.Round,
                    )
                    if (r == 0) {
                        drawLine(
                            color = needleCol,
                            start = mid,
                            end = mid + Offset(0f, 7.dp.toPx()),
                            strokeWidth = strokePx,
                            cap = StrokeCap.Round,
                        )
                    }
                }
            }

            val hubCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.6f)
            drawCircle(color = hubCol, radius = 4.dp.toPx(), center = mid)

            val markerCol = lerp(lerp(accentColor, successGreen, p), errorColor, q * 0.6f)
            val up = Offset(0f, -1f)
            drawLine(
                color = markerCol.copy(alpha = 0.85f),
                start = mid + up * 90f.dp.toPx(),
                end = mid + up * 95f.dp.toPx(),
                strokeWidth = 2.5f.dp.toPx(),
                cap = StrokeCap.Round,
            )
        }
    }
}

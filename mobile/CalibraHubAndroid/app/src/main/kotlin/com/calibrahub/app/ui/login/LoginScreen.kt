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
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.calibrahub.app.R
import com.calibrahub.app.app
import com.calibrahub.app.data.CompanyDto
import kotlinx.coroutines.launch
import kotlin.math.PI
import kotlin.math.cos
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

    // Şirket dropdown — birden çok şirketli sistemler için
    var companies by remember { mutableStateOf<List<CompanyDto>>(emptyList()) }
    var selectedCompanyId by remember { mutableStateOf<Int?>(null) }
    var companyDropdownExpanded by remember { mutableStateOf(false) }

    // Mevcut base URL + şirket listesi
    LaunchedEffect(Unit) {
        baseUrl = session.currentBaseUrl()
        repo.companies().onSuccess { list ->
            companies = list
            if (list.size == 1) selectedCompanyId = list.first().id
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
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            CalibraLoginBadge()
            Spacer(Modifier.height(40.dp))

            // Şirket dropdown — sadece 2+ şirket varsa göster
            if (companies.size > 1) {
                ExposedDropdownMenuBox(
                    expanded = companyDropdownExpanded,
                    onExpandedChange = { companyDropdownExpanded = !companyDropdownExpanded },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    OutlinedTextField(
                        value = companies.firstOrNull { it.id == selectedCompanyId }?.name ?: "Şirket seçin",
                        onValueChange = {},
                        label = { Text("Şirket") },
                        readOnly = true,
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = companyDropdownExpanded) },
                        modifier = Modifier.menuAnchor().fillMaxWidth()
                    )
                    ExposedDropdownMenu(
                        expanded = companyDropdownExpanded,
                        onDismissRequest = { companyDropdownExpanded = false }
                    ) {
                        companies.forEach { c ->
                            DropdownMenuItem(
                                text = { Text(c.name) },
                                onClick = {
                                    selectedCompanyId = c.id
                                    companyDropdownExpanded = false
                                }
                            )
                        }
                    }
                }
                Spacer(Modifier.height(12.dp))
            }

            OutlinedTextField(
                value = email,
                onValueChange = { email = it },
                label = { Text("E-posta") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(12.dp))

            OutlinedTextField(
                value = password,
                onValueChange = { password = it },
                label = { Text("Parola") },
                singleLine = true,
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
                        repo.login(email.trim(), password, selectedCompanyId).fold(
                            onSuccess = { onLoggedIn() },
                            onFailure = { e ->
                                snackbarHostState.showSnackbar("Giriş başarısız: ${e.message ?: "bilinmeyen hata"}")
                            }
                        )
                        loading = false
                    }
                },
                enabled = !loading && email.isNotBlank() && password.isNotBlank() &&
                          (companies.size <= 1 || selectedCompanyId != null),
                modifier = Modifier.fillMaxWidth()
            ) {
                if (loading) CircularProgressIndicator(modifier = Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary)
                else         Text("Giriş yap")
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
 * Login formunun üstünde ortalı marka bloğu: kalibrasyon halkalı logo rozeti + alt başlık.
 * Tamamen stateless/dekoratif — dışarıya state sızdırmaz, parametre almaz.
 */
@Composable
private fun CalibraLoginBadge() {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            modifier = Modifier.size(184.dp),
            contentAlignment = Alignment.Center
        ) {
            CalibrationRing(modifier = Modifier.fillMaxSize())

            Image(
                painter = painterResource(id = R.drawable.calibrahub_logo),
                contentDescription = "CalibraHub logosu",
                contentScale = ContentScale.Crop,
                modifier = Modifier
                    .size(136.dp)
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
 * Marka kimliğine uygun "kalibrasyon halkası": çentikli (tick mark'lı) bir gösterge kadranı
 * + sürekli süpüren gradyan vurgu — sanki gösterge durmadan kalibre oluyormuş hissi verir.
 * Web login ekranındaki `cal-dial` SVG kadranıyla aynı ruh (ince kadran + hareketli ibre/iz),
 * renkler burada Material3 tema token'larından (`primary`/`secondary`) gelir — hardcoded hex
 * yok, dark/light temaya otomatik uyar.
 *
 * Hareket düşük genlikli/yumuşak tutulur (prefers-reduced-motion muadili): tek bir ince dönen
 * vurgu (9 sn/tur, linear) + hafif nefes alma (%80-%100 opaklık, 2.6 sn) — göz yormaz,
 * dikkat dağıtmadan sürekli döngüde kalır.
 */
@Composable
private fun CalibrationRing(modifier: Modifier = Modifier) {
    val infiniteTransition = rememberInfiniteTransition(label = "calibrationRing")

    val sweepAngle by infiniteTransition.animateFloat(
        initialValue = 0f,
        targetValue = 360f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 9000, easing = LinearEasing)
        ),
        label = "sweepAngle"
    )

    val breathe by infiniteTransition.animateFloat(
        initialValue = 0.8f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 2600, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "breathe"
    )

    val glowColor  = MaterialTheme.colorScheme.primary
    val trackColor = MaterialTheme.colorScheme.outline
    val tickColor  = MaterialTheme.colorScheme.onSurfaceVariant
    val sweepStart = MaterialTheme.colorScheme.secondary
    val sweepEnd   = MaterialTheme.colorScheme.primary

    Canvas(modifier = modifier) {
        val strokeW    = 2.dp.toPx()
        val ringRadius = size.minDimension / 2f - strokeW * 2f
        val mid        = Offset(size.width / 2f, size.height / 2f)

        // 0) Logo ile halka arasındaki boşlukta nefes alan yumuşak parıltı
        drawCircle(
            brush = Brush.radialGradient(
                colors = listOf(glowColor.copy(alpha = 0.14f * breathe), Color.Transparent),
                center = mid,
                radius = ringRadius * 1.05f
            ),
            radius = ringRadius * 1.05f,
            center = mid
        )

        // 1) Sabit gösterge izi (bezel)
        drawCircle(
            color = trackColor.copy(alpha = 0.25f),
            radius = ringRadius,
            center = mid,
            style = Stroke(width = strokeW)
        )

        // 2) Çentikli kadran — 48 ince tik, her 4'te bir majör tik (gösterge/kadran hissi)
        val tickCount = 48
        for (i in 0 until tickCount) {
            val isMajor  = i % 4 == 0
            val angleRad = i * (2.0 * PI / tickCount)
            val dir      = Offset(cos(angleRad).toFloat(), sin(angleRad).toFloat())
            val outerR   = ringRadius + strokeW * 0.6f
            val innerR   = outerR - (if (isMajor) strokeW * 3f else strokeW * 1.5f)
            drawLine(
                color = tickColor.copy(alpha = (if (isMajor) 0.55f else 0.26f) * breathe),
                start = mid + dir * innerR,
                end = mid + dir * outerR,
                strokeWidth = if (isMajor) strokeW * 0.9f else strokeW * 0.55f,
                cap = StrokeCap.Round
            )
        }

        // 3) Süpüren kalibrasyon vurgusu — ince "kuyruklu" gradyan (radar taraması gibi).
        //    Dairenin ~%66'sı tamamen saydam bırakılır ki hareket net bir "geçen ışık" olarak
        //    okunsun; geniş, her yeri kaplayan bir gradyan yerine dar/zarif bir kuyruk tercih edildi.
        rotate(degrees = sweepAngle, pivot = mid) {
            drawCircle(
                brush = Brush.sweepGradient(
                    0.00f to Color.Transparent,
                    0.66f to Color.Transparent,
                    0.80f to sweepStart.copy(alpha = 0.55f * breathe),
                    0.92f to sweepEnd.copy(alpha = 0.95f * breathe),
                    1.00f to Color.Transparent,
                    center = mid
                ),
                radius = ringRadius,
                center = mid,
                style = Stroke(width = strokeW * 1.7f, cap = StrokeCap.Round)
            )
        }
    }
}

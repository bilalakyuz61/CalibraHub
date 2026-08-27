package com.calibrahub.mobile.ui.common

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

/**
 * Acilis (oturum dogrulama) gostergesi — jenerik [androidx.compose.material3.CircularProgressIndicator]
 * yerine urun adiyla ortusen bir "kalibrasyon kadrani": sabit kadran + surekli donen yay +
 * ileri-geri suzulen ibre.
 *
 * Tamamen Compose Canvas ile cizilir — ek varlik (Lottie/GIF/vektor dosyasi) YOK, dolayisiyla
 * bundle buyumez ve iki platformda birebir ayni gorunur. Renkler tema token'larindan gelir
 * (light/dark otomatik).
 *
 * Bu bir ILERLEME gostergesi DEGIL (yuzde bilinmiyor); yalnizca "calisiyor" sinyali verir —
 * bu yuzden ibre hedefe varmaz, salinir.
 */
@Composable
fun CalibrationIndicator(
    modifier: Modifier = Modifier,
    size: Dp = 96.dp,
) {
    val transition = rememberInfiniteTransition(label = "kalibrasyon")

    // Ibre: -55° ile +55° arasi ileri-geri. FastOutSlowInEasing uclarda yavaslatir —
    // olcum aleti ibresinin oturma hissi.
    val needleAngle by transition.animateFloat(
        initialValue = -55f,
        targetValue = 55f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 900, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse,
        ),
        label = "ibre",
    )

    // Yay: kesintisiz donus — ibre uclarda yavaslarken bile "islem suruyor" sinyali kesilmesin.
    val arcRotation by transition.animateFloat(
        initialValue = 0f,
        targetValue = 360f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 2400, easing = LinearEasing),
            repeatMode = RepeatMode.Restart,
        ),
        label = "yay",
    )

    val accent = MaterialTheme.colorScheme.primary
    val track = MaterialTheme.colorScheme.surfaceVariant
    val needle = MaterialTheme.colorScheme.onSurface

    Canvas(modifier = modifier.size(size)) {
        val stroke = this.size.minDimension * 0.055f
        val radius = (this.size.minDimension - stroke) / 2f
        val center = Offset(this.size.width / 2f, this.size.height / 2f)

        // 1) Kadran halkasi (sabit zemin)
        drawCircle(color = track, radius = radius, center = center, style = Stroke(width = stroke))

        // 2) Kadran cizgileri — 12 adet, her ucuncusu uzun (saat kadrani mantigi)
        repeat(12) { i ->
            val angle = (i * 30f - 90f) * PI.toFloat() / 180f
            val long = i % 3 == 0
            val inner = radius * if (long) 0.74f else 0.83f
            drawLine(
                color = track,
                start = center + Offset(cos(angle) * inner, sin(angle) * inner),
                end = center + Offset(cos(angle) * (radius * 0.93f), sin(angle) * (radius * 0.93f)),
                strokeWidth = stroke * if (long) 0.55f else 0.35f,
                cap = StrokeCap.Round,
            )
        }

        // 3) Donen yay (90°) — halkanin uzerinde kayar
        drawArc(
            color = accent,
            startAngle = arcRotation,
            sweepAngle = 90f,
            useCenter = false,
            topLeft = Offset(center.x - radius, center.y - radius),
            size = Size(radius * 2, radius * 2),
            style = Stroke(width = stroke, cap = StrokeCap.Round),
        )

        // 4) Ibre — tepe noktasi 0° kabul edilir (-90° kaydirma), salinim ustune eklenir
        val needleRad = (needleAngle - 90f) * PI.toFloat() / 180f
        drawLine(
            color = needle,
            start = center,
            end = center + Offset(cos(needleRad) * radius * 0.62f, sin(needleRad) * radius * 0.62f),
            strokeWidth = stroke * 0.75f,
            cap = StrokeCap.Round,
        )

        // 5) Merkez gobek
        drawCircle(color = accent, radius = stroke * 0.9f, center = center)
    }
}

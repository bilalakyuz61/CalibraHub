package com.calibrahub.mobile.photo

import android.graphics.Bitmap
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.first
import java.io.ByteArrayOutputStream

/**
 * Android actual — bkz. commonMain [PhotoCapture] KDoc'u (neden `expect class` + composable
 * factory secildigi). [BarcodeScanner.android.kt] ile AYNI desen: launcher composition'da
 * kurulur, sonuc bir SharedFlow uzerinden [capture]'a tasinir.
 *
 * NEDEN `TakePicturePreview` (kucuk onizleme bitmap'i) ve `TakePicture` (tam cozunurluk,
 * FileProvider URI'si) DEGIL:
 *   - `TakePicture` bir `content://` URI ve dolayisiyla FileProvider tanimi + gecici dosya
 *     yasam dongusu yonetimi ister. Kanit fotografi icin bu karmasa gereksiz.
 *   - `TakePicturePreview` dogrudan bir `Bitmap` doner; JPEG'e sikistirip byte dizisine
 *     ceviriyoruz. Cozunurluk cihaza gore degisir ama uygunsuzluk kanidi icin YETERLI ve
 *     yukleme boyutu kucuk kalir (mobil veri).
 * Daha yuksek cozunurluk gerekirse dogru adim FileProvider'li `TakePicture`'a gecmektir;
 * seam degismez, yalniz bu actual degisir.
 */
actual class PhotoCapture internal constructor(
    private val launch: () -> Unit,
    private val results: MutableSharedFlow<Bitmap?>,
) {
    actual suspend fun capture(): CapturedPhoto? {
        launch()
        val bitmap = results.first() ?: return null

        val stream = ByteArrayOutputStream()
        // 85: kanit fotografi icin gorsel kalite ile dosya boyutu arasindaki dengeli nokta.
        bitmap.compress(Bitmap.CompressFormat.JPEG, 85, stream)
        return CapturedPhoto(bytes = stream.toByteArray(), fileName = "foto.jpg")
    }
}

@Composable
actual fun rememberPhotoCapture(): PhotoCapture {
    val results = remember { MutableSharedFlow<Bitmap?>(extraBufferCapacity = 1) }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { bitmap ->
        // Iptal/izin reddinde bitmap null gelir — tryEmit ile akisa aynen basilir,
        // capture() null doner ve cagiran sessizce devam eder.
        results.tryEmit(bitmap)
    }
    return remember(launcher) {
        PhotoCapture(launch = { launcher.launch(null) }, results = results)
    }
}

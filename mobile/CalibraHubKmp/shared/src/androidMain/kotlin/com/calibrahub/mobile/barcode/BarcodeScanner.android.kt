package com.calibrahub.mobile.barcode

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import com.journeyapps.barcodescanner.CaptureActivity
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.first

/**
 * ZXing embedded kutuphanesinin varsayilan [CaptureActivity]'si kendi AndroidManifest'inde
 * `screenOrientation="sensorLandscape"` ile yatay kilitlidir. Kutuphanenin belgeledigi yontem
 * bu alt sinifi (govde kasitli bos, tum davranis ust siniftan miras alinir) uygulamanin kendi
 * manifest'inde farkli bir orientation ile deklare edip [ScanOptions.setCaptureActivity] ile
 * devreye almaktir — bkz. `shared/src/androidMain/AndroidManifest.xml` (fullSensor) ve
 * [barcodeScanOptions] (setCaptureActivity + setOrientationLocked(false)). CalibraHubAndroid
 * `FlexibleCaptureActivity.kt` ile BIREBIR ayni (sadik port).
 */
class FlexibleCaptureActivity : CaptureActivity()

/**
 * Barkod tarama ekrani secenekleri — CalibraHubAndroid `MaterialPickerField.barcodeScanOptions()`
 * ile BIREBIR ayni (sadik port): malzeme etiketlerinde hangi semboloji kullanildigi bilinmediginden
 * tum yaygin 1D/2D formatlar acik birakilir; [FlexibleCaptureActivity] + `setOrientationLocked(false)`
 * ZXing'in varsayilan yatay-kilitli tarayicisinin yerine gecer.
 */
private fun barcodeScanOptions(): ScanOptions =
    ScanOptions()
        .setCaptureActivity(FlexibleCaptureActivity::class.java)
        .setDesiredBarcodeFormats(ScanOptions.ALL_CODE_TYPES)
        .setPrompt("Barkodu kare içine hizalayın")
        .setBeepEnabled(true)
        .setOrientationLocked(false)

/**
 * Android actual — bkz. commonMain [BarcodeScanner] sinif KDoc'u (neden `expect class` +
 * composable factory secildigi). [launch] her cagrida yeni bir tarama baslatir; sonuc
 * [results] SharedFlow'una ZXing'in ActivityResultCallback'i uzerinden (bkz.
 * [rememberBarcodeScanner]) basilir, [scan] bunun ILK degerini bekler.
 *
 * `extraBufferCapacity = 1` + `first()`: ayni anda tek bir [scan] cagrisi beklenir (picker
 * ekranlarinin kullanim deseni — bir seferde bir tarama), bu yuzden basit bir "sonraki emisyon"
 * yeterlidir; birden fazla es-zamanli [scan] cagrisi coklanmasi bu Faz'in kapsami DISINDADIR.
 */
actual class BarcodeScanner internal constructor(
    private val launch: (ScanOptions) -> Unit,
    private val results: MutableSharedFlow<String?>,
) {
    actual suspend fun scan(): String? {
        launch(barcodeScanOptions())
        return results.first()
    }
}

@Composable
actual fun rememberBarcodeScanner(): BarcodeScanner {
    val results = remember { MutableSharedFlow<String?>(extraBufferCapacity = 1) }
    val launcher = rememberLauncherForActivityResult(ScanContract()) { result ->
        // Iptal edilirse result.contents null'dur — cagiran taraf (picker ekrani) bunu
        // "kullanici vazgecti" olarak yorumlar (bkz. sinif KDoc'u).
        results.tryEmit(result.contents)
    }
    return remember { BarcodeScanner(launch = { options -> launcher.launch(options) }, results = results) }
}

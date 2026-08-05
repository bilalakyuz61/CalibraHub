package com.calibrahub.mobile.barcode

import androidx.compose.runtime.Composable

/**
 * Barkod tarayici SEAM (Faz 2a) — Faz 2b'deki depo malzeme picker ekranlari (bkz.
 * CalibraHubAndroid `ui/warehouse/MaterialPickerField.kt`) bu TEK arayuzu cagirir:
 * - androidMain: GERCEK ZXing (com.journeyapps:zxing-android-embedded) implementasyonu —
 *   [FlexibleCaptureActivity]'yi ActivityResultContract ile acar (bkz. BarcodeScanner.android.kt).
 * - iosMain: GERCEK AVFoundation (Faz 3, `AVCaptureSession` + `AVCaptureMetadataOutput`)
 *   implementasyonu — modal bir `UIViewController` acar, ilk okunan barkodda / kullanici
 *   iptalinde sonucu doner (bkz. BarcodeScanner.ios.kt).
 *
 * Boylece commonMain'deki picker ekranlari HER IKI platformda da DERLENIR ve HER IKISINDE de
 * tam fonksiyonel barkod okuma saglar.
 *
 * TASARIM KARARI — neden `expect suspend fun scanBarcode(): String?` DEGIL, `expect class` +
 * `@Composable expect fun rememberBarcodeScanner()`: Android tarafinda ZXing/CaptureActivity
 * acilisi bir `ActivityResultLauncher` gerektirir ve bu launcher'in bir @Composable icinde
 * (`rememberLauncherForActivityResult`) Activity STARTED olmadan ONCE kayitli olmasi ZORUNLUDUR
 * (bkz. androidx.activity.result kontrati). Duz, parametresiz bir top-level `suspend fun`
 * cagrildigi noktada composition disi bir yerden (ornegin bir ViewModel coroutine'inden)
 * tetiklenebilir olsaydi bu on-kayit sartini Android tarafinda karsilayamazdik. Bu yuzden
 * cagiran taraf (picker composable'i) once [rememberBarcodeScanner] ile bir [BarcodeScanner]
 * ORNEGI alir (composition'da kurulur/hatirlanir), sonra o ornegin [BarcodeScanner.scan]
 * suspend metodunu (ornegin bir buton onClick coroutine'inde) cagirir.
 */
expect class BarcodeScanner {
    /** Tarayiciyi acar ve kullanici bir barkod okutana/iptal edene kadar suspend olur.
     * Iptal edilirse, kamera izni reddedilirse veya kamera hic yoksa `null` doner. */
    suspend fun scan(): String?
}

/** [BarcodeScanner] ornegini composition'a bagli olarak kurar/hatirlar — bkz. sinif KDoc'u
 * (Android'de ActivityResultLauncher on-kaydi icin @Composable olmak ZORUNLU). */
@Composable
expect fun rememberBarcodeScanner(): BarcodeScanner

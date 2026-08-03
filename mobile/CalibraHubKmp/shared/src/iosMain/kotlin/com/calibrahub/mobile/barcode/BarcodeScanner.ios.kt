package com.calibrahub.mobile.barcode

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember

/**
 * iOS actual — Faz 2a STUB. Gercek kamera taramasi Faz 3'te AVFoundation
 * (`AVCaptureSession` + `AVCaptureMetadataOutput`) ile implemente edilecek (bkz. commonMain
 * [BarcodeScanner] sinif KDoc'u). Bu STUB, Faz 2b'deki depo picker ekranlarinin commonMain'de
 * DERLENEBILMESI icindir — iOS'ta bugun barkod ikonuna dokununca sadece "null" (tarama yok)
 * doner, ekran cokmez / derleme kirilmaz.
 */
actual class BarcodeScanner internal constructor() {
    actual suspend fun scan(): String? {
        // TODO Faz 3: AVFoundation AVCaptureMetadataOutput ile gercek barkod taramasi.
        return null
    }
}

@Composable
actual fun rememberBarcodeScanner(): BarcodeScanner = remember { BarcodeScanner() }

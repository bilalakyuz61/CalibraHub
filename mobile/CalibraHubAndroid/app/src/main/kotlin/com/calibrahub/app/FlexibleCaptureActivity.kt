package com.calibrahub.app

import com.journeyapps.barcodescanner.CaptureActivity

/**
 * ZXing embedded kütüphanesinin varsayılan CaptureActivity'si kendi AndroidManifest'inde
 * screenOrientation="sensorLandscape" ile yatay kilitlidir. Kütüphanenin belgelediği yöntem
 * bu alt sınıfı (gövde kasıtlı boş, tüm davranış üst sınıftan miras alınır) uygulamanın
 * kendi manifest'inde farklı bir orientation ile deklare edip ScanOptions.setCaptureActivity
 * ile devreye almaktır — bkz. AndroidManifest.xml (fullSensor) ve
 * MaterialPickerField.barcodeScanOptions() (setCaptureActivity + setOrientationLocked(false)).
 * Böylece tarayıcı ekranı hem dikey hem yatayda kullanılabilir.
 */
class FlexibleCaptureActivity : CaptureActivity()

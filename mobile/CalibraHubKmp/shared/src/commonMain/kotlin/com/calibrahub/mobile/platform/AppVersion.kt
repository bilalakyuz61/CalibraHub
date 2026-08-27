package com.calibrahub.mobile.platform

/**
 * Kurulu uygulamanin surum bilgisi.
 *
 * @param name   Kullaniciya gosterilen surum ("0.1.0"). Android `versionName`, iOS
 *               `CFBundleShortVersionString`.
 * @param build  Yapi numarasi ("12"). Android `versionCode`, iOS `CFBundleVersion` —
 *               iOS'ta bunu Codemagic her TestFlight yuklemesinde artirir, bu yuzden
 *               "hangi yapiyi test ediyorum" sorusunun GERCEK cevabi budur.
 */
data class AppVersionInfo(
    val name: String,
    val build: String,
) {
    /** Ekranda gosterilen tek satir: "0.1.0 (12)". */
    val display: String get() = if (build.isBlank()) name else "$name ($build)"
}

/**
 * Surum bilgisi SABIT bir string olarak commonMain'e YAZILMAZ; her platformun kendi paket
 * meta verisinden okunur. Sebep: cihazdaki yapinin gercek numarasi gorunmeli — iOS'ta yapi
 * numarasini CI (Codemagic $BUILD_NUMBER) uretim aninda Info.plist'e yaziyor, elle tutulan
 * bir sabit ilk yuklemede yanlis olurdu.
 *
 * Okuma basarisiz olursa (beklenmiyor) bos degerlerle doner — ekran "bilinmiyor" gosterir,
 * uygulama cokmez.
 */
expect fun currentAppVersion(): AppVersionInfo

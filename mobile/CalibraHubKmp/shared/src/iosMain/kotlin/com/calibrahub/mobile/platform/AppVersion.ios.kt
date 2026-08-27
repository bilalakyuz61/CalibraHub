package com.calibrahub.mobile.platform

import platform.Foundation.NSBundle

/**
 * iOS karsiligi — ana bundle'in Info.plist'inden `CFBundleShortVersionString` (surum) ve
 * `CFBundleVersion` (yapi numarasi). Yapi numarasini Codemagic her TestFlight yuklemesinde
 * $BUILD_NUMBER ile yeniden yazar (bkz. codemagic.yaml "Build numarasini ayarla" adimi),
 * bu yuzden ekranda gorunen deger "hangi yapiyi test ediyorum" sorusunun kesin cevabidir.
 */
actual fun currentAppVersion(): AppVersionInfo {
    val bundle = NSBundle.mainBundle
    val name = bundle.objectForInfoDictionaryKey("CFBundleShortVersionString") as? String ?: ""
    val build = bundle.objectForInfoDictionaryKey("CFBundleVersion") as? String ?: ""
    return AppVersionInfo(name = name, build = build)
}

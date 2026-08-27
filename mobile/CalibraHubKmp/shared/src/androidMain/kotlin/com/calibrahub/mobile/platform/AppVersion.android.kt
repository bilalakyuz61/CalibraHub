package com.calibrahub.mobile.platform

import android.os.Build
import com.calibrahub.mobile.storage.AndroidContextHolder

/**
 * Android karsiligi — paket yoneticisinden `versionName` + `versionCode`.
 * Context, [AndroidContextHolder]'dan alinir (SecureStorageFactory ile AYNI desen; bkz. oradaki KDoc).
 */
actual fun currentAppVersion(): AppVersionInfo = try {
    val context = AndroidContextHolder.require()
    val info = context.packageManager.getPackageInfo(context.packageName, 0)
    val code = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
        info.longVersionCode.toString()
    } else {
        @Suppress("DEPRECATION")
        info.versionCode.toString()
    }
    AppVersionInfo(name = info.versionName ?: "", build = code)
} catch (_: Exception) {
    AppVersionInfo(name = "", build = "")
}

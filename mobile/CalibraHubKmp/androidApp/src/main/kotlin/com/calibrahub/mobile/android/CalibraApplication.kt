package com.calibrahub.mobile.android

import android.app.Application
import com.calibrahub.mobile.storage.AndroidContextHolder

/**
 * Application-scoped giris noktasi — [AndroidContextHolder.init] BURADA cagrilir (Application
 * yasam suresi boyunca tek Context), boylece `shared` modulundeki [com.calibrahub.mobile.storage.SecureStorageFactory]
 * androidMain actual'i DataStore Preferences icin gecerli bir Context'e sahip olur. Android
 * Manifest'te `android:name=".CalibraApplication"` ile bagli (bkz. AndroidManifest.xml).
 */
class CalibraApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        AndroidContextHolder.init(this)
    }
}

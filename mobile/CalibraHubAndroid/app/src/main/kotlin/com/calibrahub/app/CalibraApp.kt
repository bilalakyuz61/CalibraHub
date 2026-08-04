package com.calibrahub.app

import android.app.Application
import android.content.Context
import com.calibrahub.app.data.AuthRepository
import com.calibrahub.app.data.ProductionRepository
import com.calibrahub.app.data.SessionManager
import com.calibrahub.app.data.WarehouseRepository

/**
 * Application class — uygulama lifetime'ı boyunca tek instance.
 * Singleton SessionManager + Repository init.
 *
 * 2026-08-04: WhatsApp bildirim kanalı (`ensureNotificationChannel`/`CHANNEL_WHATSAPP`) ve
 * arka plan polling işçisi kaldırıldı — WhatsApp/sohbet mobil kapsamdan TAMAMEN çıkarıldı
 * (kullanıcı kararı, bkz. CLAUDE.md). `WhatsAppPollingWorker` zaten hiçbir yerde
 * enqueueUniquePeriodicWork ile zamanlanmıyordu (ölü kod) — WorkManager bu app'te başka bir
 * amaçla kullanılmadığından `work-runtime-ktx` bağımlılığı da build.gradle.kts'ten kaldırıldı.
 */
class CalibraApp : Application() {

    val session: SessionManager by lazy { SessionManager(this) }
    val authRepository: AuthRepository by lazy { AuthRepository(session) }
    val warehouseRepository: WarehouseRepository by lazy { WarehouseRepository(session) }
    val productionRepository: ProductionRepository by lazy { ProductionRepository(session) }
}

/** Convenience getter — Activity / Composable içinden `context.app` */
val Context.app: CalibraApp
    get() = applicationContext as CalibraApp

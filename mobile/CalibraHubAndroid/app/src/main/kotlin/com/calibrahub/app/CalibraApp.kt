package com.calibrahub.app

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.os.Build
import com.calibrahub.app.data.SessionManager
import com.calibrahub.app.data.WhatsAppRepository

/**
 * Application class — uygulama lifetime'ı boyunca tek instance.
 * Singleton SessionManager + Repository init, notification channel hazırlığı.
 */
class CalibraApp : Application() {

    val session: SessionManager by lazy { SessionManager(this) }
    val repository: WhatsAppRepository by lazy { WhatsAppRepository(session) }

    override fun onCreate() {
        super.onCreate()
        ensureNotificationChannel()
    }

    private fun ensureNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            if (nm.getNotificationChannel(CHANNEL_WHATSAPP) == null) {
                val ch = NotificationChannel(
                    CHANNEL_WHATSAPP,
                    "WhatsApp mesajları",
                    NotificationManager.IMPORTANCE_DEFAULT
                ).apply {
                    description = "Yeni WhatsApp mesajları için bildirim kanalı"
                }
                nm.createNotificationChannel(ch)
            }
        }
    }

    companion object {
        const val CHANNEL_WHATSAPP = "calibra_whatsapp_v1"
    }
}

/** Convenience getter — Activity / Composable içinden `context.app` */
val Context.app: CalibraApp
    get() = applicationContext as CalibraApp

package com.calibrahub.app.work

import android.Manifest
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.ActivityCompat
import androidx.core.app.NotificationCompat
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.calibrahub.app.CalibraApp
import com.calibrahub.app.MainActivity
import com.calibrahub.app.R
import com.calibrahub.app.app

/**
 * Periyodik WorkManager task — uygulama background'da iken yeni mesajları çeker
 * ve unreadCount > 0 olan sohbetler için bildirim atar.
 *
 * Minimum interval: 15 dk (Android sistem kısıtı). Daha sık polling istenirse
 * FCM (Firebase Cloud Messaging) entegrasyonu V2'de yapılır.
 */
class WhatsAppPollingWorker(
    context: Context,
    params: WorkerParameters
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val app = applicationContext.app
        val whoAmI = app.repository.whoAmI().getOrNull()
        if (whoAmI == null) return Result.success()  // login yok — sessizce çık

        val result = app.repository.conversations()
        result.fold(
            onSuccess = { list ->
                val unread = list.filter { it.unreadCount > 0 }
                if (unread.isNotEmpty()) {
                    val totalUnread = unread.sumOf { it.unreadCount }
                    val title = if (unread.size == 1) {
                        "${unread.first().displayName ?: unread.first().phone}"
                    } else {
                        "${unread.size} sohbette $totalUnread yeni mesaj"
                    }
                    val text = unread.firstOrNull()?.lastMessage?.take(80) ?: ""
                    showNotification(title, text)
                }
            },
            onFailure = { /* network varsa sonraki cycle dener */ }
        )
        return Result.success()
    }

    private fun showNotification(title: String, text: String) {
        val ctx = applicationContext

        // Android 13+ POST_NOTIFICATIONS izni yoksa skip
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ActivityCompat.checkSelfPermission(ctx, Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED) {
            return
        }

        val intent = Intent(ctx, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val pending = PendingIntent.getActivity(
            ctx, 0, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val notif = NotificationCompat.Builder(ctx, CalibraApp.CHANNEL_WHATSAPP)
            .setSmallIcon(android.R.drawable.sym_action_chat)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setAutoCancel(true)
            .setContentIntent(pending)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()

        val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.notify(NOTIF_ID, notif)
    }

    companion object {
        const val WORK_NAME = "whatsapp_polling_v1"
        const val NOTIF_ID  = 1001
    }
}

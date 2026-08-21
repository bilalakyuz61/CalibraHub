package com.calibrahub.mobile

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.ComposeUIViewController
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.storage.SecureStorage
import com.calibrahub.mobile.storage.SecureStorageFactory
import com.calibrahub.mobile.ui.AppRoot

/**
 * GECICI TESHIS GIRIS NOKTASI (2026-08-21) — uygulama iOS'ta acilir acilmaz, UI hic cizilmeden
 * cokuyor ve ne cihazda (.ips) ne App Store Connect'te crash log'u ELDE EDILEMIYOR.
 * Log yoksa hatayi EKRANDA gosteririz: acilis, agir baslatma adimlarindan ARINDIRILMIS minimal
 * bir Compose agaciyla baslar; her adim ELLE, try/catch icinde tetiklenir ve sonuc/hata metni
 * ekrana basilir.
 *
 * Sonucun yorumu:
 *  - Bu ekran ACILIYORSA  -> CMP + statik framework + Xcode entegrasyonu SAGLAM; sorun bizim
 *                            baslatma zincirimizde (adim butonlari tam yeri gosterir).
 *  - Bu ekran BILE ACILMIYORSA -> sorun CMP/framework entegrasyonunun kendisinde (Compose hic
 *                            ayaga kalkmiyor); tamamen farkli bir yol izlenir.
 *
 * Teshis bitince bu dosya eski haline dondurulur:  fun MainViewController() =
 *     ComposeUIViewController { AppRoot() }        (bkz. git gecmisi / commit b619407 oncesi)
 */
fun MainViewController() = ComposeUIViewController { DiagnosticRoot() }

@Composable
private fun DiagnosticRoot() {
    var runFullApp by remember { mutableStateOf(false) }

    if (runFullApp) {
        // 3. adim: gercek uygulama kokunu calistir. Burada cokerse sorun AppRoot zincirindedir
        // (SessionManager + tema + NavHost + ekranlar).
        AppRoot()
    } else {
        var status by remember { mutableStateOf("Compose ayakta — bu ekrani goruyorsan CMP calisiyor.") }
        var storage by remember { mutableStateOf<SecureStorage?>(null) }
        var session by remember { mutableStateOf<SessionManager?>(null) }

        MaterialTheme {
            Surface(modifier = Modifier.fillMaxSize()) {
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(24.dp)
                        .verticalScroll(rememberScrollState())
                ) {
                    Text("CalibraHub iOS teshis", style = MaterialTheme.typography.titleLarge)
                    Spacer(modifier = Modifier.height(16.dp))
                    Text(status)
                    Spacer(modifier = Modifier.height(24.dp))

                    Button(onClick = {
                        status = try {
                            storage = SecureStorageFactory.create()
                            "1) SecureStorage OK (NSUserDefaults)"
                        } catch (t: Throwable) {
                            "1) HATA -> ${t::class.simpleName}: ${t.message}"
                        }
                    }) { Text("1) SecureStorage olustur") }

                    Spacer(modifier = Modifier.height(12.dp))

                    Button(onClick = {
                        status = try {
                            val s = storage ?: SecureStorageFactory.create()
                            storage = s
                            session = SessionManager(s)
                            "2) SessionManager OK (Ktor client + cookie jar kuruldu)"
                        } catch (t: Throwable) {
                            "2) HATA -> ${t::class.simpleName}: ${t.message}"
                        }
                    }) { Text("2) SessionManager olustur") }

                    Spacer(modifier = Modifier.height(12.dp))

                    Button(onClick = { runFullApp = true }) { Text("3) Tam uygulamayi baslat") }
                }
            }
        }
    }
}

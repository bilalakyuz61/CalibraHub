# CalibraHub Mobile

Android (Kotlin + Jetpack Compose) companion app for CalibraHub. Uses the existing CalibraHub.Web backend; no extra backend services to deploy.

## Klasör Yapısı

```
mobile/
├── CalibraHubAndroid/        # Android Studio projesi (Kotlin + Compose)
│   ├── app/
│   │   ├── build.gradle.kts
│   │   └── src/main/
│   │       ├── kotlin/com/calibrahub/app/
│   │       │   ├── MainActivity.kt
│   │       │   ├── data/        # Retrofit, CookieJar, SessionManager
│   │       │   ├── ui/          # Compose screens (login, chat)
│   │       │   ├── work/        # WorkManager polling
│   │       │   └── ui/theme/    # Material3 theme
│   │       ├── res/             # values, drawable, xml
│   │       └── AndroidManifest.xml
│   ├── build.gradle.kts        # Root project
│   ├── settings.gradle.kts
│   ├── gradle.properties
│   ├── gradle/wrapper/         # Gradle wrapper (otomatik indirme)
│   └── scripts/install-debug.sh
└── README.md (bu dosya)
```

## Geliştirme Ortamı Kurulumu

### Önkoşullar
- **Android Studio Hedgehog (2023.1)** veya üstü
- **JDK 17** (Android Studio paketinde gelir)
- **Android SDK API 34** (Android 14) — IDE üzerinden yüklenir
- **AVD** — Pixel 6, API 34, Google Play services dahil

### İlk açılış
1. Android Studio aç → **Open** → `D:\JetBrainsRider\Projeler\CalibraHub\mobile\CalibraHubAndroid\` seç
2. İlk gradle sync ~3-5 dk sürer (bağımlılıklar indirilir)
3. **AVD Manager** → **Create Virtual Device** → Pixel 6 → API 34 (Google Play image)
4. AVD adını `CalibraHub-Pixel6-API34` koy

### Backend'i başlat (host makinesinde)
```powershell
cd D:\JetBrainsRider\Projeler\CalibraHub\src\CalibraHub.Web
dotnet run --launch-profile http
```
Backend `http://localhost:61001` üzerinde dinler. Android emulator'dan bu adres `http://10.0.2.2:61001` olarak görünür (otomatik mapping).

### Build & install
```bash
cd mobile/CalibraHubAndroid
./gradlew :app:installDebug          # debug APK derler ve emulator'a kurar
adb shell am start -n com.calibrahub.app/.MainActivity
```

veya Android Studio'da **Run 'app'** (▶) butonu.

### Test akışı
1. AVD başlat
2. Backend çalıştığından emin ol (`curl http://localhost:61001/` → 302 dönmeli)
3. App'i çalıştır → Login ekranı açılır
4. Email + parola gir → ChatList'e geçer
5. Bir sohbete tıkla → Mesajlar yüklenir
6. Mesaj yaz, gönder → web `/Whatsapp` sayfasında da görünür (her iki taraf 3sn polling)

## Network Config

| Ortam | Base URL |
|-------|----------|
| AVD emulator → host backend | `http://10.0.2.2:61001/` |
| Fiziksel cihaz, aynı LAN | `http://<host-LAN-IP>:61001/` |
| Production | `https://erp.musteri.com/` |

`SessionManager.kt` içinde `BuildConfig.BASE_URL` ile flavor'a göre seçilir.

## WhatsApp Business app (opsiyonel)

Emulator'a Play Store üzerinden **WhatsApp Business** kurulabilir (API 34 Google Play image gerekli). Kurulumdan sonra ChatDetail ekranındaki **"WhatsApp Business'ta Aç"** butonu wa.me intent ile WB app'i açar, mesaj prefilled gelir.

```kotlin
val uri = Uri.parse("https://wa.me/${phone}?text=${URLEncoder.encode(text, "UTF-8")}")
startActivity(Intent(Intent.ACTION_VIEW, uri).apply { setPackage("com.whatsapp.w4b") })
```

## Production build

1. Keystore üret (bir kere):
```powershell
keytool -genkey -v -keystore calibrahub-release.jks -keyalg RSA -keysize 2048 -validity 10000 -alias calibrahub
```
2. `app/build.gradle.kts` içinde `signingConfigs.release` tanımla (env değişkenlerinden okur)
3. `./gradlew assembleRelease` → `app/build/outputs/apk/release/app-release.apk`
4. Cihaza yükle: `adb install app-release.apk` veya Google Play Console'a yükle

## Kapsam dışı (V2+)

- iOS companion app
- FCM push notifications (gerçek-zamanlı bildirim)
- HSM / template message gönderimi
- Offline mod (Room cache + WorkManager sync)
- Biometric auth

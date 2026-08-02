# CalibraHub Mobil — KMP / Compose Multiplatform Göç Planı

> **Karar (2026-08-02):** iOS + Android tek koddan, sağlam ortak altyapı için
> **Kotlin Multiplatform (KMP) + Compose Multiplatform (CMP)** seçildi.
> Mevcut Android app "at ve yeniden yaz" DEĞİL → "ortak katmana ayır + iOS target ekle".
> Bu döküman somut yol haritasıdır.

---

## 0. Neden KMP/CMP (özet gerekçe)

- Mevcut app zaten **Jetpack Compose** → CMP'de aynı UI iOS'ta da native (Skia) render eder.
- İş mantığı + ağ katmanı **bir kez** yazılır (`commonMain`), iki platform paylaşır.
- Platform-özel kısımlar (kamera/NFC/depolama) `expect`/`actual` ile temiz kapsüllenir.
- Tek dil (Kotlin), 1-3 kişilik ekip için tek zihinsel model.

**Değişmez kısıt:** iOS derlemesi/imzalaması için **macOS + Xcode zorunlu**. Windows'ta iOS
target'ı yazılabilir/derlenemez → build zinciri ayrı planlanmalı (bkz. §7).

---

## 1. Mevcut envanter (göç kapsamı)

| Katman | Bugün | Satır/Adet | Göç eforu |
|--------|-------|-----------|-----------|
| Compose UI ekranları | ~24 dosya | ~11.5k LOC toplam | **Düşük** — Compose kodu ~%90 aynen taşınır |
| Navigation | navigation-compose | 1 NavHost | Orta — CMP nav (Voyager / Decompose ya da navigation-compose MP) |
| Ağ istemcisi | Retrofit + Moshi + OkHttp | 33 endpoint / ~90 DTO | **Orta** — Ktor + kotlinx.serialization'a mekanik çeviri |
| Oturum/cookie | DataStore + OkHttp CookieJar | SessionManager 356 satır | Orta — depolama `expect/actual`, cookie mantığı ortak |
| Barkod | ZXing embedded (Android Activity) | 3 ekran kullanıyor | **Yüksek** — iOS native scanner yazılacak |
| NFC | **henüz kod yok** (planlı) | 0 | Baştan `expect/actual` tasarlanır |
| Arka plan polling | WorkManager (WhatsApp) | 1 worker | Orta — iOS BGTaskScheduler / push'a bak |
| Resim | Coil 2 (chat) | 1 ekran | Düşük — Coil 3 zaten multiplatform |

---

## 2. Hedef modül yapısı

```
CalibraHubMobile/                    # yeni KMP root (mevcut CalibraHubAndroid'in evrimi)
├── shared/                          # KMP kütüphane modülü — ASIL DEĞER burada
│   ├── src/commonMain/kotlin/
│   │   ├── data/                    # Ktor client, DTO'lar (kotlinx.serialization), repository'ler
│   │   ├── domain/                  # iş mantığı (FIFO özet, validasyon, delta hesap)
│   │   ├── session/                 # SessionManager ortak mantığı (cookie, remember-me, tema)
│   │   ├── ui/                      # Compose Multiplatform ekranları (login, depo, üretim...)
│   │   └── platform/                # expect declarations (barkod, NFC, secureStorage, http engine)
│   ├── src/androidMain/kotlin/      # actual: Android (DataStore, ZXing, OkHttp engine, NFC android)
│   └── src/iosMain/kotlin/          # actual: iOS (Keychain, AVFoundation scanner, Darwin engine, CoreNFC)
├── androidApp/                      # ince Android host (MainActivity + Compose entry)
└── iosApp/                          # ince iOS host (SwiftUI/UIKit wrapper + Xcode projesi)
```

**İlke:** `androidApp` ve `iosApp` mümkün olduğunca ince olur; tüm ekranlar ve mantık `shared/commonMain`'de.

---

## 3. Bağımlılık geçiş tablosu (Android → KMP karşılığı)

| Bugün (Android-only) | KMP karşılığı | Not |
|----------------------|---------------|-----|
| Retrofit 2.9 | **Ktor Client** | commonMain interface, platform engine (OkHttp/Darwin) |
| Moshi 1.15 | **kotlinx.serialization** | `@Serializable` data class; **enum string** (backend `JsonStringEnumConverter`) — mevcut string-sabit deseni korunur |
| OkHttp CookieJar | Ktor `HttpCookies` + custom storage | Cookie mantığı ortak, storage `expect/actual` |
| DataStore Preferences | `expect SecureStorage` → androidMain DataStore, iosMain Keychain | "Beni hatırla" + tema + cookie |
| navigation-compose | **navigation-compose (MP)** ya da **Voyager/Decompose** | Kararı POC'ta ver (§9) |
| ZXing embedded | `expect BarcodeScanner` → androidMain ZXing, iosMain AVFoundation | En büyük platform işi |
| WorkManager | `expect BackgroundSync` → androidMain WorkManager, iosMain BGTaskScheduler | iOS'ta arka plan kısıtlı; WhatsApp polling stratejisi gözden geçir |
| Coil 2 | **Coil 3 (multiplatform)** | commonMain AsyncImage |
| Compose BOM 2024.02 | **Compose Multiplatform** (JetBrains sürümü) | AndroidX Compose ≠ CMP; sürüm ayrı yönetilir |

---

## 4. Faz sırası (önerilen)

### Faz 0 — Altyapı iskeleti (kod öncesi + minimal)
- KMP root projesi kur (`shared` + `androidApp` + `iosApp`), Gradle KMP plugin, version catalog (`libs.versions.toml` — bugün yok, KMP'de zorunlu değil ama şart koşulur).
- Compose Multiplatform pluginini ekle; boş `shared` ekranı iki platformda "Merhaba" render etsin.
- **Çıkış kriteri:** aynı Compose ekranı hem Android emülatörde hem iOS simülatörde açılıyor. (iOS için Mac gerekir — bu fazdan ÖNCE §7 çözülmeli.)

### Faz 1 — Ağ + oturum katmanı (`commonMain`)
- 90+ DTO'yu `@Serializable`'a çevir (Moshi → kotlinx). Enum'lar string kalır.
- 33 endpoint'i Ktor client interface'lerine taşı (CalibraApi/Warehouse/Production).
- SessionManager: ortak mantık `commonMain`, depolama `expect SecureStorage`.
- Cookie jar: Ktor `HttpCookies` + `expect` storage.
- **Çıkış kriteri:** her iki platformda login + `warehouse/stock?code=` çağrısı gerçek 61001 backend'e gidip cevap dönüyor (headless test).

### Faz 2 — Platform-bağımsız ekranları taşı
- Sırayla: Login → Home/Drawer → Stok Sorgu → Giriş/Çıkış → Transfer/Sayım → Üretim → İrsaliye → Açık Sipariş → Ayarlar.
- Bu ekranlar barkod/NFC dışında saf Compose → çoğu **kes-yapıştır + import düzelt**.
- **Çıkış kriteri:** barkod/NFC gerektirmeyen tüm akışlar iki platformda çalışıyor.

### Faz 3 — Platform-özel entegrasyonlar (`expect/actual`)
- **Barkod:** `expect BarcodeScanner.scan(): String?`
  - androidMain → mevcut ZXing/FlexibleCaptureActivity sarmalanır.
  - iosMain → AVFoundation (`AVCaptureMetadataOutput`) native scanner.
- **NFC (yeni):** `expect NfcReader` → androidMain `android.nfc`, iosMain `CoreNFC`. (Bugün hiç yok → temiz tasarım fırsatı.)
- **Güvenli depolama:** Keychain (iOS) / DataStore (Android) — Faz 1'de zaten `expect`.
- **Çıkış kriteri:** barkod tarama + (varsa) NFC iki platformda.

### Faz 4 — Arka plan + cila
- WhatsApp polling: iOS'ta WorkManager yok → BGTaskScheduler ya da server push (FCM/APNs) stratejisi kararı. **Not:** iOS arka plan çok kısıtlı; belki iOS'ta polling yerine foreground-refresh + push.
- iOS özel: safe-area, klavye davranışı, geri-jesti, native scroll hissi ince ayar.
- İkon/splash/tema iki platform.
- **Çıkış kriteri:** iki platform feature-parity + mağaza-hazır.

---

## 5. Risk listesi

| Risk | Etki | Azaltma |
|------|------|---------|
| **Mac/Xcode yokluğu** | iOS hiç derlenemez | §7 — kod öncesi çöz (fiziksel Mac / Mac mini / bulut CI) |
| CMP-iOS olgunluğu (2025 stable) | Bazı native-hissi detaylar | POC'ta erken doğrula; kritik ekranı iOS'ta test et |
| Retrofit→Ktor DTO çevirisi hacmi (~90 DTO) | Zaman | Mekanik; script/şablonla hızlandır, enum deseni korunur |
| Barkod iOS native yazımı | Efor + test | ZXing yerine iOS'ta AVFoundation; izole `actual` |
| WhatsApp polling iOS arka plan | Fonksiyon kaybı | Push mimarisine geçiş değerlendir (ayrı karar) |
| Navigation kütüphane seçimi | Refactor riski | POC'ta netleştir (navigation-compose MP vs Voyager) |
| Compose sürüm ikilemi (AndroidX ≠ CMP) | Build karışıklığı | Tek CMP sürümü; AndroidX Compose bağımlılıklarını temizle |

---

## 6. Ne kadar iş? (kaba büyüklük, taahhüt değil)

- **En pahalı:** barkod iOS native + NFC (yeni) + iOS build zinciri kurulumu.
- **En ucuz (ama hacimli):** DTO/endpoint çevirisi (mekanik), Compose ekran taşıma (kes-yapıştır).
- **Sürpriz maliyet adayı:** iOS arka plan (WhatsApp), iOS native-hissi cila.

Faz 0→2 arası "iki platformda çalışan iskelet + çekirdek akışlar" en değerli eşik; oraya
kadar netlik yüksek. Faz 3-4 iOS-özel belirsizlik taşır → POC ile erken ölçülmeli.

---

## 7. iOS build zinciri — KARAR: Codemagic CI (2026-08-02)

**Karar:** Elimizde Mac yok; canlı uzak Mac de KULLANILMAYACAK. Kodlamayı Claude yaptığı
için interaktif simülatör ihtiyacı düşük → iOS **build + test + imzalama + dağıtım**
tamamen **Codemagic CI** üzerinden alınır (KMP'yi birinci sınıf destekler).

**Çalışma modeli (hibrit):**
- **Geliştirme + doğrulama:** Windows'ta, Android emülatör. Ortak `commonMain` kodunun
  ~%90'ı burada yazılıp doğrulanır. Android yeşilse iOS de büyük olasılıkla yeşildir
  (aynı Compose/Ktor kodu).
- **iOS build/test:** Codemagic pipeline — her push'ta `.ipa` derler, imzalar, TestFlight'a
  atar. iOS-özel doğrulama gerçek cihazda TestFlight ile yapılır.

**Bilinçli ödün (dürüst not):** İnteraktif iOS simülatör olmadığından, iOS-özel render/
native-hissi sorunları **anlık değil**, CI build + TestFlight döngüsüyle (daha yavaş
geri besleme) yakalanır. POC'ta bu risk kabul edildi; kritik iOS-UI sorunu çıkarsa o
noktada kısa süreli kiralık Mac tekrar değerlendirilebilir.

**Gerekli (iOS mağaza/TestFlight için):** **Apple Developer hesabı** ($99/yıl), imzalama
sertifikaları + provisioning, App Store Connect. (Simülatör imzasız çalışır ama bizde
simülatör yok → ilk gerçek iOS çıktısı için hesap Codemagic kurulmadan önce gerekli.)

---

## 8. YAPILMAYACAKLAR (kapsam koruması)

- Backend'e dokunma — `/api/mobile/*` sözleşmesi aynen kalır (iki platform ortak tüketir).
- Web (React) frontend'e dokunma.
- "İleride lazım olur" diye ekstra soyutlama ekleme (KISS/YAGNI — CLAUDE.md).
- Android app'i göç bitene kadar bozma → mevcut app çalışır kalır, KMP paralel olgunlaşır.

---

## 9. Önerilen ilk adım: küçük POC (karar pekiştirme)

Detaylı fazlara dalmadan önce **1 ekran POC** riski en çok düşürür:
- `shared/commonMain`'de **Login + Stok Sorgu** ekranı (basit state-machine nav; kütüphane
  kararı POC dışı bırakıldı — `when(screen)` yeterli).
- Ktor ile gerçek 61001 backend'e `login-companies` → `login` → `warehouse/stock`.
- **Windows'ta Android emülatörde** doğrulanır (base URL `http://10.0.2.2:61001/`).
- iOS `iosMain` actual'ları (Darwin engine) + `MainViewController()` hazır bırakılır →
  iOS build ilk Codemagic kurulumunda alınır (Mac/simülatör gerekmez).

Bu POC şunları erken doğrular: (1) KMP/CMP yapısı Android'de yeşil derleniyor mu,
(2) Ktor + in-memory cookie auth çalışıyor mu, (3) ortak Compose ekranı mount oluyor mu.
POC Android'de yeşilse tam göç güvenle başlar; iOS doğrulaması Codemagic'e devredilir.

**POC konumu:** mevcut `CalibraHubAndroid` DOKUNULMAZ → POC ayrı `mobile/CalibraHubKmp/`
dizininde kurulur (paralel olgunlaşma).

---

*İlgili memory: project_mobile_modules (mevcut Android envanteri), project_agent_team
(mobil↔backend /api/mobile sözleşmesi).*

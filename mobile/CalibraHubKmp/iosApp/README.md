# iOS App — Codemagic CI ile derlenir (bu makinede Mac/Xcode YOK)

Bu klasör artık **iskelet olarak hazır**. Windows'ta derlenemez (Kotlin/Native Apple
target'ları yalnız macOS host'ta derlenir) → iOS build'i **Codemagic CI** alır.
`.xcodeproj` elle yazılmaz; **XcodeGen** ile `project.yml`'den üretilir.

## Bu klasörde ne var
- `project.yml` — XcodeGen spec'i. `xcodegen generate` çalıştırılınca `iosApp.xcodeproj`
  üretir. KMP `shared` framework'ünü preBuildScript ile derler+linkler
  (`./gradlew :shared:embedAndSignAppleFrameworkForXcode`), ATS'yi dev-HTTP için gevşetir.
- `iosApp/iOSApp.swift` — `@main` SwiftUI giriş noktası.
- `iosApp/ContentView.swift` — `UIViewControllerRepresentable` ile
  `MainViewControllerKt.MainViewController()`'ı (Compose `App()`) barındırır.
- `Info.plist` — XcodeGen tarafından `project.yml`'deki `info:` bloğundan otomatik üretilir
  (elle tutulmaz).

## CI yapılandırması
Repo **kökündeki** `codemagic.yaml` iki workflow tanımlar:
- **`ios-compile-check`** — imzasız derleme kanıtı, Apple hesabı gerekmez. İlk çalıştırılacak.
- **`ios-testflight`** — imzalı IPA + TestFlight; Apple Developer + Codemagic ASC entegrasyonu gerekir.

## Kullanıcının yapması gerekenler (Claude yapamaz — hesap/dış servis)
1. **Codemagic hesabı aç** (codemagic.io) → GitHub ile giriş → `bilalakyuz61/CalibraHub` reposunu bağla.
2. `codemagic.yaml` + `mobile/CalibraHubKmp/` **GitHub'a push** edilmiş olmalı (branch: main).
3. Codemagic'te **`ios-compile-check`** workflow'unu ELLE başlat → iOS gerçekten derleniyor mu görülür.
4. (Sonra) TestFlight için: **Apple Developer hesabı** ($99/yıl) → App Store Connect API anahtarı
   → Codemagic'te **App Store Connect entegrasyonu** ("CalibraHub ASC" adıyla) + bundle id
   `com.calibrahub.mobile` kaydı → **`ios-testflight`** workflow'u.

## Doğrulanmamış (dürüst not)
Bu iskelet Windows'ta **hiç derlenemedi** — ilk gerçek doğrulama Codemagic'in ilk
`ios-compile-check` çalıştırmasında olacak. Blind CI konfigürasyonu tipik olarak 1-2 tur
düzeltme ister (task adı, framework arama yolu, XcodeGen sürüm farkları). İlk build
loglarına göre `project.yml`/`codemagic.yaml` düzeltilecek.

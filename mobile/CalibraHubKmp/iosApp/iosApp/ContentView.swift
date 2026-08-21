import SwiftUI
import shared

// KMP `shared` framework'ünün Compose UIViewController'ını SwiftUI'a köprüler.
// MainViewControllerKt.MainViewController() = commonMain'deki App() composable'ını
// barındıran ComposeUIViewController (bkz. shared/src/iosMain/.../MainViewController.kt).
struct ComposeView: UIViewControllerRepresentable {
    func makeUIViewController(context: Context) -> UIViewController {
        MainViewControllerKt.MainViewController()
    }

    func updateUIViewController(_ uiViewController: UIViewController, context: Context) {}
}

/// GECICI TESHIS (2026-08-21): uygulama iOS'ta acilir acilmaz cokuyor; crash log'u ne cihazdan
/// ne App Store Connect'ten alinabildi. Kotlin tarafindaki minimal Compose ekrani da acilmadi —
/// yani cokme, bizim uygulama kodumuzdan ONCE gerceklesiyor olabilir.
///
/// Bu sonda, Compose'u OTOMATIK baslatmaz: once SAF SwiftUI bir ekran cizer.
///  - Bu SwiftUI ekrani GORUNUYORSA -> uygulama kabugu (imzalama/plist/dyld/SwiftUI) SAGLAM;
///    cokme Kotlin framework'unun / Compose'un ayaga kalkmasinda. Butona basinca cokerse KESIN.
///  - Bu ekran BILE gorunmuyorsa   -> cokme daha erken (kabuk/yukleme) — tamamen farkli yol.
///
/// Teshis bitince ContentView tekrar dogrudan ComposeView() dondurecek sekilde sadelestirilir.
struct ContentView: View {
    @State private var showCompose = false
    @State private var kotlinResult = "Kotlin henuz test edilmedi"

    var body: some View {
        if showCompose {
            ComposeView()
                .ignoresSafeArea(.keyboard) // klavye Compose tarafından yönetilir
        } else {
            VStack(spacing: 24) {
                Text("SwiftUI calisiyor")
                    .font(.title2)
                Text("Uygulama kabugu ayakta. Compose'u baslatmak icin dokun.")
                    .font(.footnote)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 32)
                Text(kotlinResult)
                    .font(.footnote)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 24)
                Button("1) Kotlin'i test et (Compose YOK)") {
                    // Compose'a hic dokunmadan Kotlin/Native calisma zamanini dener.
                    kotlinResult = MainViewControllerKt.diagnosticPing()
                }
                Button("3) Compose VC olustur (sunma)") {
                    // Compose VC'sini olusturur ama SUNMAZ; Kotlin istisnasi varsa metni gosterir.
                    kotlinResult = MainViewControllerKt.diagnosticComposeInit()
                }
                Button("2) Compose'u baslat") {
                    showCompose = true
                }
            }
        }
    }
}

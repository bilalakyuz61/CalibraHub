import SwiftUI
import shared

// KMP `shared` framework'ünün Compose UIViewController'ını SwiftUI'a köprüler.
// MainViewControllerKt.MainViewController() = commonMain'deki AppRoot() composable'ını
// barındıran ComposeUIViewController (bkz. shared/src/iosMain/.../MainViewController.kt).
struct ComposeView: UIViewControllerRepresentable {
    func makeUIViewController(context: Context) -> UIViewController {
        MainViewControllerKt.MainViewController()
    }

    func updateUIViewController(_ uiViewController: UIViewController, context: Context) {}
}

/// Compose DOGRUDAN acilir (sonda butonlari kaldirildi): cokme, Codemagic'in iOS Simulator
/// kosumunda ANINDA olusur ve konsol/crash raporu build log'una dusrulur — teshis artik
/// telefon/TestFlight turu gerektirmiyor (bkz. codemagic.yaml `ios-sim-run`).
struct ContentView: View {
    var body: some View {
        ComposeView()
            .ignoresSafeArea(.keyboard) // klavye Compose tarafından yönetilir
    }
}

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

struct ContentView: View {
    var body: some View {
        ComposeView()
            .ignoresSafeArea(.keyboard) // klavye Compose tarafından yönetilir
    }
}

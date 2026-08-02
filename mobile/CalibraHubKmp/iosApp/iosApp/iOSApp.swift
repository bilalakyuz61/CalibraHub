import SwiftUI

// iOS uygulama giriş noktası. Tüm UI, KMP `shared` framework'ündeki Compose
// Multiplatform ağacından gelir (bkz. ContentView → MainViewController).
@main
struct iOSApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
                .ignoresSafeArea(.all)
        }
    }
}

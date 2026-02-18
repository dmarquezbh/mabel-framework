import SwiftUI

// =============================================================================
// Mabel Hello World — iOS
//
// Primeiro app real do Mabel Framework no iPhone.
// Renderiza via Core Graphics (CGContext) — SEM WebView.
//
// Build e deploy do Linux/WSL:
//   cd samples/hello-world-ios
//   xtool dev                    # compila + instala + abre no iPhone
//
// Nao precisa de Mac. Nao precisa de Xcode instalado localmente.
// O xtool compila via Swift toolchain no Linux e deploya via USB.
// =============================================================================

@main
struct MabelHelloApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}

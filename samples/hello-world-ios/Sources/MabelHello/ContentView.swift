import SwiftUI

// =============================================================================
// ContentView — entry point da UI
// =============================================================================

struct ContentView: View {
    @StateObject private var engine = MabelEngine()

    var body: some View {
        MabelCanvasView(commands: $engine.commands)
            .edgesIgnoringSafeArea(.all)
            .statusBarHidden()
            .onAppear {
                engine.loadHelloWorld()
            }
    }
}

// Preview nao disponivel em cross-compilation (Linux → iOS)
// #Preview { ContentView() }

import SwiftUI

// =============================================================================
// Spike WasmKit-on-device (iOS) — ver README.md deste diretório para o
// objetivo completo. Resumo: provar (ou refutar) que dá pra carregar e
// EXECUTAR um módulo .wasm via WasmKit (interpretador puro-Swift) no iPhone
// físico, e trocar esse módulo por outro em runtime, sem reiniciar o
// processo host — o pré-requisito de docs/hmr-e-estado.md e docs/ota.md.
//
// Todo o resultado é impresso via print() (stdout — capturado por
// `devicectl device process launch --console`) E mostrado na tela (pra
// confirmar visualmente / print de tela no device físico).
// =============================================================================

@main
struct WasmKitIOSSpikeApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}

struct ContentView: View {
    @State private var log: String = "Rodando spike...\n"

    var body: some View {
        ScrollView {
            Text(log)
                .font(.system(size: 13, design: .monospaced))
                .foregroundColor(.green)
                .padding()
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .background(Color.black)
        .onAppear {
            let result = SpikeRunner.run()
            log = result
            // Espelha no stdout — capturado por
            // `devicectl device process launch --console`.
            print("===== WASMKIT-IOS-SPIKE-RESULT-BEGIN =====")
            print(result)
            print("===== WASMKIT-IOS-SPIKE-RESULT-END =====")
        }
    }
}

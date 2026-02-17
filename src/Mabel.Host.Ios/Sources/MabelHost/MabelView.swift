import SwiftUI

// =============================================================================
// Mabel Host - SwiftUI integration
// Wrapper SwiftUI que exibe o MabelCanvasView e envia touch events pro WASM.
// =============================================================================

public struct MabelView: UIViewRepresentable {
    @Binding var commands: [RenderCommand]

    public init(commands: Binding<[RenderCommand]>) {
        self._commands = commands
    }

    public func makeUIView(context: Context) -> MabelCanvasView {
        let view = MabelCanvasView()
        view.isMultipleTouchEnabled = true
        view.backgroundColor = .black
        return view
    }

    public func updateUIView(_ view: MabelCanvasView, context: Context) {
        view.commands = commands
        view.setNeedsDisplay()
    }
}

/// ViewModel que carrega o WASM e gerencia o loop de render.
@MainActor
public class MabelEngine: ObservableObject {
    @Published public var commands: [RenderCommand] = []

    public init() {}

    /// Carrega um .wasm da bundle e executa mabel_init.
    /// TODO: integrar wasmtime-swift quando disponivel.
    public func load(wasmName: String) {
        // Placeholder: gera um Hello World estatico
        commands = Self.helloWorld()
    }

    /// Demo: renderiza um Hello World sem WASM.
    static func helloWorld() -> [RenderCommand] {
        [
            RenderCommand(op: .beginFrame, x: 0, y: 0, w: 0, h: 0,
                          color: 0x1A1A2EFF, text: nil, radius: 0, fontSize: 0),

            RenderCommand(op: .roundRect, x: 40, y: 100, w: 300, h: 200,
                          color: 0x16213EFF, text: nil, radius: 16, fontSize: 0),

            RenderCommand(op: .text, x: 80, y: 170, w: 0, h: 0,
                          color: 0xE94560FF, text: "Mabel Framework", radius: 0, fontSize: 28),

            RenderCommand(op: .text, x: 80, y: 210, w: 0, h: 0,
                          color: 0x0F3460FF, text: "Hello from WASI!", radius: 0, fontSize: 18),

            RenderCommand(op: .circle, x: 190, y: 400, w: 0, h: 0,
                          color: 0xE94560FF, text: nil, radius: 40, fontSize: 0),

            RenderCommand(op: .endFrame, x: 0, y: 0, w: 0, h: 0,
                          color: 0, text: nil, radius: 0, fontSize: 0),
        ]
    }
}

/// ContentView pronto pra uso.
public struct MabelContentView: View {
    @StateObject private var engine = MabelEngine()

    public init() {}

    public var body: some View {
        MabelView(commands: $engine.commands)
            .edgesIgnoringSafeArea(.all)
            .onAppear { engine.load(wasmName: "app") }
    }
}

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
                          color: 0x1A1A2EFF),

            RenderCommand(op: .roundRect, x: 40, y: 100, w: 300, h: 200,
                          color: 0x16213EFF, radius: 16),

            RenderCommand(op: .text, x: 80, y: 170, w: 0, h: 0,
                          color: 0xE94560FF, text: "Mabel Framework", fontSize: 28),

            RenderCommand(op: .text, x: 80, y: 210, w: 0, h: 0,
                          color: 0x0F3460FF, text: "Hello from WASI!", fontSize: 18),

            RenderCommand(op: .circle, x: 190, y: 400, w: 0, h: 0,
                          color: 0xE94560FF, radius: 40),

            RenderCommand(op: .endFrame, x: 0, y: 0, w: 0, h: 0,
                          color: 0),
        ]
    }

    /// Demo: renderiza um Glass Card com efeitos modernos (iOS 26 style).
    static func glassDemo() -> [RenderCommand] {
        [
            RenderCommand(op: .beginFrame, x: 0, y: 0, w: 0, h: 0,
                          color: 0x0A0A1AFF),

            // Background gradient
            RenderCommand(op: .linearGrad, x: 0, y: 0, w: 0, h: 812,
                          color: 0x1A1A3EFF, color2: 0x0A0A1AFF),
            RenderCommand(op: .rect, x: 0, y: 0, w: 390, h: 812,
                          color: 0x1A1A3EFF),

            // Glass card with shadow
            RenderCommand(op: .shadow, x: 0, y: 8, w: 0, h: 0,
                          color: 0x00000060, radius: 24),
            RenderCommand(op: .blur, x: 0, y: 0, w: 0, h: 0,
                          color: 0, radius: 40),
            RenderCommand(op: .linearGrad, x: 40, y: 120, w: 40, h: 320,
                          color: 0xFFFFFF30, color2: 0xFFFFFF10),
            RenderCommand(op: .roundRect, x: 40, y: 120, w: 310, h: 200,
                          color: 0xFFFFFF18, radius: 28),

            // Glass card border (stroke)
            RenderCommand(op: .stroke, x: 40, y: 120, w: 310, h: 200,
                          color: 0xFFFFFF20, radius: 28, fontSize: 0.5),

            // Card text
            RenderCommand(op: .text, x: 64, y: 160, w: 0, h: 0,
                          color: 0xFFFFFFFF, text: "Mabel Glass", fontSize: 32),
            RenderCommand(op: .text, x: 64, y: 200, w: 0, h: 0,
                          color: 0xFFFFFF99, text: "iOS 26 Design Language", fontSize: 16),

            RenderCommand(op: .endFrame, x: 0, y: 0, w: 0, h: 0,
                          color: 0),
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

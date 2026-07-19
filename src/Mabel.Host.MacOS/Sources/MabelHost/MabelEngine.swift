import AppKit

// =============================================================================
// Mabel Host - macOS engine
// Owns the display-list and (later) the WASM guest. For now, static demos
// mirroring Mabel.Host.Ios so both hosts render pixel-identical frames.
// =============================================================================

@MainActor
public final class MabelEngine {

    public private(set) var commands: [RenderCommand] = []

    /// Callback fired whenever the display-list changes (host re-renders).
    public var onChange: (() -> Void)?

    public init() {}

    /// Load a .wasm from disk and run mabel_init.
    /// TODO: integrate WasmKit/wasmtime-swift when the guest ABI lands.
    public func load(wasmPath: String) {
        // Placeholder until the WASM guest is wired: render the static hello.
        loadHelloWorld()
    }

    public func loadHelloWorld() {
        commands = Self.helloWorld()
        onChange?()
    }

    public func loadGlassDemo() {
        commands = Self.glassDemo()
        onChange?()
    }

    // MARK: - Hello World

    public static func helloWorld() -> [RenderCommand] {
        [
            RenderCommand(op: .beginFrame, color: 0x1A1A2EFF),
            RenderCommand(op: .roundRect, x: 40, y: 100, w: 300, h: 200, color: 0x16213EFF, radius: 16),
            RenderCommand(op: .text, x: 80, y: 140, color: 0xE94560FF, text: "Mabel Framework", fontSize: 28),
            RenderCommand(op: .text, x: 80, y: 180, color: 0x0F3460FF, text: "Hello from macOS!", fontSize: 18),
            RenderCommand(op: .text, x: 80, y: 220, color: 0xFFFFFF80, text: "AppKit • Core Graphics • No WebView", fontSize: 14),
            RenderCommand(op: .circle, x: 190, y: 400, color: 0xE94560FF, radius: 40),
            RenderCommand(op: .text, x: 130, y: 500, color: 0xFFFFFF40, text: "Mabel v0.1.0", fontSize: 12),
            RenderCommand(op: .endFrame),
        ]
    }

    // MARK: - Glass Demo

    public static func glassDemo() -> [RenderCommand] {
        [
            RenderCommand(op: .beginFrame, color: 0x0A0A1AFF),

            // Background gradient
            RenderCommand(op: .linearGrad, x: 0, y: 0, w: 0, h: 812, color: 0x1A1A3EFF, color2: 0x0A0A1AFF),
            RenderCommand(op: .rect, x: 0, y: 0, w: 390, h: 812, color: 0x1A1A3EFF),

            // Decorative radial glow
            RenderCommand(op: .radialGrad, x: 195, y: 200, color: 0xE9456040, radius: 120, color2: 0xE9456000),
            RenderCommand(op: .circle, x: 195, y: 200, color: 0xE9456020, radius: 120),

            // Glass card with shadow + blur
            RenderCommand(op: .shadow, x: 0, y: 8, color: 0x00000060, radius: 24),
            RenderCommand(op: .blur, radius: 40),
            RenderCommand(op: .linearGrad, x: 40, y: 300, w: 40, h: 500, color: 0xFFFFFF30, color2: 0xFFFFFF10),
            RenderCommand(op: .roundRect, x: 40, y: 300, w: 310, h: 200, color: 0xFFFFFF18, radius: 28),

            // Card border
            RenderCommand(op: .stroke, x: 40, y: 300, w: 310, h: 200, color: 0xFFFFFF20, radius: 28, fontSize: 0.5),

            // Content
            RenderCommand(op: .text, x: 64, y: 340, color: 0xFFFFFFFF, text: "Mabel Glass", fontSize: 32),
            RenderCommand(op: .text, x: 64, y: 380, color: 0xFFFFFF99, text: "macOS Design Language", fontSize: 16),
            RenderCommand(op: .text, x: 64, y: 420, color: 0xFFFFFF60, text: "Shadow • Blur • Gradient • Stroke", fontSize: 12),

            // Accent line
            RenderCommand(op: .linearGrad, x: 40, y: 0, w: 350, h: 0, color: 0xE94560FF, color2: 0x0F3460FF),
            RenderCommand(op: .rect, x: 40, y: 480, w: 310, h: 2, color: 0xE94560FF),

            RenderCommand(op: .text, x: 120, y: 550, color: 0xFFFFFF30, text: "Built with Mabel • No WebView", fontSize: 11),

            RenderCommand(op: .endFrame),
        ]
    }
}

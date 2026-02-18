import SwiftUI
import UIKit

// =============================================================================
// Mabel Hello World - iOS Renderer
//
// Autocontido: RenderOp, RenderCommand, MabelCanvasUIView (Core Graphics),
// MabelCanvasView (SwiftUI wrapper), MabelEngine.
//
// Renderiza via Core Graphics (CGContext) — SEM WebView.
// Suporta todas as operacoes Glass / modern UI (iOS 26 style).
// =============================================================================

// MARK: - Protocol (mirrors Mabel.Wasi.Protocol)

enum RenderOp: UInt8 {
    // Primitives
    case rect      = 0x01
    case roundRect = 0x02
    case circle    = 0x03
    case line      = 0x04
    case text      = 0x05
    case image     = 0x06

    // Effects (Glass / modern UI)
    case shadow     = 0x07
    case blur       = 0x08
    case linearGrad = 0x09
    case radialGrad = 0x0A
    case stroke     = 0x0B
    case path       = 0x0C

    // State
    case pushClip    = 0x10
    case popClip     = 0x11
    case pushOpacity = 0x12
    case popOpacity  = 0x13
    case translate   = 0x14
    case scale       = 0x15
    case rotate      = 0x16

    // Frame
    case beginFrame = 0xF0
    case endFrame   = 0xF1
}

struct RenderCommand {
    let op: RenderOp
    let x: Float
    let y: Float
    let w: Float
    let h: Float
    let color: UInt32
    let text: String?
    let radius: Float
    let fontSize: Float
    let color2: UInt32

    init(op: RenderOp, x: Float = 0, y: Float = 0, w: Float = 0, h: Float = 0,
         color: UInt32 = 0, text: String? = nil, radius: Float = 0,
         fontSize: Float = 14, color2: UInt32 = 0) {
        self.op = op; self.x = x; self.y = y; self.w = w; self.h = h
        self.color = color; self.text = text; self.radius = radius
        self.fontSize = fontSize; self.color2 = color2
    }
}

// MARK: - Core Graphics Canvas (UIView)

/// UIView que renderiza RenderCommands via Core Graphics (CGContext).
/// Leve, funciona em iOS 15+, sem WebView.
final class MabelCanvasUIView: UIView {
    var commands: [RenderCommand] = [] {
        didSet { setNeedsDisplay() }
    }

    // Effect state
    private var pendingShadow: (ox: CGFloat, oy: CGFloat, blur: CGFloat, color: UIColor)?
    private var pendingBlurRadius: CGFloat?
    private var pendingGradient: GradientState?

    private enum GradientState {
        case linear(x1: CGFloat, y1: CGFloat, x2: CGFloat, y2: CGFloat,
                    start: CGColor, end: CGColor)
        case radial(cx: CGFloat, cy: CGFloat, radius: CGFloat,
                    center: CGColor, edge: CGColor)
    }

    override func draw(_ rect: CGRect) {
        guard let ctx = UIGraphicsGetCurrentContext() else { return }

        pendingShadow = nil
        pendingBlurRadius = nil
        pendingGradient = nil

        for cmd in commands {
            switch cmd.op {

            // -- Frame --
            case .beginFrame:
                ctx.saveGState()
                ctx.setFillColor(uiColor(cmd.color).cgColor)
                ctx.fill(rect)

            case .endFrame:
                ctx.restoreGState()
                pendingShadow = nil
                pendingBlurRadius = nil
                pendingGradient = nil

            // -- Primitives --
            case .rect:
                applyEffects(ctx)
                let r = CGRect(x: d(cmd.x), y: d(cmd.y), width: d(cmd.w), height: d(cmd.h))
                if let grad = pendingGradient {
                    drawGradient(ctx, in: r, gradient: grad)
                } else {
                    ctx.setFillColor(uiColor(cmd.color).cgColor)
                    ctx.fill(r)
                }
                clearEffects(ctx)

            case .roundRect:
                applyEffects(ctx)
                let r = CGRect(x: d(cmd.x), y: d(cmd.y), width: d(cmd.w), height: d(cmd.h))
                let path = UIBezierPath(roundedRect: r, cornerRadius: CGFloat(cmd.radius))
                if let grad = pendingGradient {
                    ctx.saveGState()
                    ctx.addPath(path.cgPath)
                    ctx.clip()
                    drawGradient(ctx, in: r, gradient: grad)
                    ctx.restoreGState()
                } else {
                    ctx.setFillColor(uiColor(cmd.color).cgColor)
                    ctx.addPath(path.cgPath)
                    ctx.fillPath()
                }
                clearEffects(ctx)

            case .circle:
                applyEffects(ctx)
                let r = CGFloat(cmd.radius)
                let circleRect = CGRect(x: d(cmd.x) - r, y: d(cmd.y) - r,
                                        width: r * 2, height: r * 2)
                ctx.setFillColor(uiColor(cmd.color).cgColor)
                ctx.fillEllipse(in: circleRect)
                clearEffects(ctx)

            case .line:
                ctx.setStrokeColor(uiColor(cmd.color).cgColor)
                ctx.setLineWidth(CGFloat(cmd.fontSize > 0 ? cmd.fontSize : 1))
                ctx.move(to: CGPoint(x: d(cmd.x), y: d(cmd.y)))
                ctx.addLine(to: CGPoint(x: d(cmd.w), y: d(cmd.h)))
                ctx.strokePath()

            case .text:
                let attrs: [NSAttributedString.Key: Any] = [
                    .font: UIFont.systemFont(ofSize: CGFloat(cmd.fontSize)),
                    .foregroundColor: uiColor(cmd.color)
                ]
                (cmd.text ?? "").draw(at: CGPoint(x: d(cmd.x), y: d(cmd.y)),
                                      withAttributes: attrs)

            case .image:
                break // TODO: asset manager

            // -- Effects --
            case .shadow:
                pendingShadow = (
                    ox: d(cmd.x), oy: d(cmd.y),
                    blur: CGFloat(cmd.radius),
                    color: uiColor(cmd.color)
                )

            case .blur:
                pendingBlurRadius = CGFloat(cmd.radius)

            case .linearGrad:
                pendingGradient = .linear(
                    x1: d(cmd.x), y1: d(cmd.y),
                    x2: d(cmd.w), y2: d(cmd.h),
                    start: uiColor(cmd.color).cgColor,
                    end: uiColor(cmd.color2).cgColor
                )

            case .radialGrad:
                pendingGradient = .radial(
                    cx: d(cmd.x), cy: d(cmd.y),
                    radius: CGFloat(cmd.radius),
                    center: uiColor(cmd.color).cgColor,
                    edge: uiColor(cmd.color2).cgColor
                )

            case .stroke:
                applyEffects(ctx)
                let strokeWidth = CGFloat(cmd.fontSize > 0 ? cmd.fontSize : 1)
                let cornerRadius = CGFloat(cmd.radius)
                let strokeRect = CGRect(x: d(cmd.x), y: d(cmd.y),
                                        width: d(cmd.w), height: d(cmd.h))
                ctx.setStrokeColor(uiColor(cmd.color).cgColor)
                ctx.setLineWidth(strokeWidth)
                if cornerRadius > 0 {
                    let path = UIBezierPath(roundedRect: strokeRect,
                                            cornerRadius: cornerRadius)
                    ctx.addPath(path.cgPath)
                    ctx.strokePath()
                } else {
                    ctx.stroke(strokeRect)
                }
                clearEffects(ctx)

            case .path:
                applyEffects(ctx)
                if let data = cmd.text, !data.isEmpty,
                   let bezier = parseSVGPath(data) {
                    ctx.setFillColor(uiColor(cmd.color).cgColor)
                    ctx.addPath(bezier.cgPath)
                    ctx.fillPath()
                }
                clearEffects(ctx)

            // -- State --
            case .pushClip:
                ctx.saveGState()
                ctx.clip(to: CGRect(x: d(cmd.x), y: d(cmd.y),
                                    width: d(cmd.w), height: d(cmd.h)))

            case .popClip:
                ctx.restoreGState()

            case .pushOpacity:
                ctx.saveGState()
                ctx.setAlpha(CGFloat(cmd.x))

            case .popOpacity:
                ctx.restoreGState()

            case .translate:
                ctx.translateBy(x: d(cmd.x), y: d(cmd.y))

            case .scale:
                ctx.scaleBy(x: d(cmd.x), y: d(cmd.y))

            case .rotate:
                ctx.rotate(by: d(cmd.x))
            }
        }
    }

    // MARK: - Effect helpers

    private func applyEffects(_ ctx: CGContext) {
        if let s = pendingShadow {
            ctx.setShadow(offset: CGSize(width: s.ox, height: s.oy),
                          blur: s.blur, color: s.color.cgColor)
        }
    }

    private func clearEffects(_ ctx: CGContext) {
        if pendingShadow != nil {
            ctx.setShadow(offset: .zero, blur: 0, color: nil)
        }
    }

    private func drawGradient(_ ctx: CGContext, in rect: CGRect,
                               gradient: GradientState) {
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        switch gradient {
        case .linear(let x1, let y1, let x2, let y2, let start, let end):
            if let g = CGGradient(colorsSpace: colorSpace,
                                  colors: [start, end] as CFArray,
                                  locations: [0, 1]) {
                ctx.drawLinearGradient(g,
                    start: CGPoint(x: x1, y: y1),
                    end: CGPoint(x: x2, y: y2),
                    options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
            }
        case .radial(let cx, let cy, let radius, let center, let edge):
            if let g = CGGradient(colorsSpace: colorSpace,
                                  colors: [center, edge] as CFArray,
                                  locations: [0, 1]) {
                let c = CGPoint(x: cx, y: cy)
                ctx.drawRadialGradient(g, startCenter: c, startRadius: 0,
                    endCenter: c, endRadius: radius,
                    options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
            }
        }
    }

    // MARK: - SVG Path parser (subset: M, L, C, Q, H, V, Z)

    private func parseSVGPath(_ data: String) -> UIBezierPath? {
        let path = UIBezierPath()
        let scanner = Scanner(string: data)
        scanner.charactersToBeSkipped = CharacterSet.whitespaces
            .union(.init(charactersIn: ","))
        var cur = CGPoint.zero

        func nextFloat() -> CGFloat? {
            guard let v = scanner.scanDouble() else { return nil }
            return CGFloat(v)
        }

        while !scanner.isAtEnd {
            guard let ch = scanner.scanCharacter() else { break }
            switch ch {
            case "M":
                guard let x = nextFloat(), let y = nextFloat() else { break }
                path.move(to: CGPoint(x: x, y: y)); cur = CGPoint(x: x, y: y)
            case "m":
                guard let dx = nextFloat(), let dy = nextFloat() else { break }
                cur = CGPoint(x: cur.x + dx, y: cur.y + dy); path.move(to: cur)
            case "L":
                guard let x = nextFloat(), let y = nextFloat() else { break }
                path.addLine(to: CGPoint(x: x, y: y)); cur = CGPoint(x: x, y: y)
            case "l":
                guard let dx = nextFloat(), let dy = nextFloat() else { break }
                cur = CGPoint(x: cur.x + dx, y: cur.y + dy); path.addLine(to: cur)
            case "H":
                guard let x = nextFloat() else { break }
                cur.x = x; path.addLine(to: cur)
            case "h":
                guard let dx = nextFloat() else { break }
                cur.x += dx; path.addLine(to: cur)
            case "V":
                guard let y = nextFloat() else { break }
                cur.y = y; path.addLine(to: cur)
            case "v":
                guard let dy = nextFloat() else { break }
                cur.y += dy; path.addLine(to: cur)
            case "C":
                guard let x1 = nextFloat(), let y1 = nextFloat(),
                      let x2 = nextFloat(), let y2 = nextFloat(),
                      let x = nextFloat(), let y = nextFloat() else { break }
                path.addCurve(to: CGPoint(x: x, y: y),
                              controlPoint1: CGPoint(x: x1, y: y1),
                              controlPoint2: CGPoint(x: x2, y: y2))
                cur = CGPoint(x: x, y: y)
            case "Q":
                guard let x1 = nextFloat(), let y1 = nextFloat(),
                      let x = nextFloat(), let y = nextFloat() else { break }
                path.addQuadCurve(to: CGPoint(x: x, y: y),
                                  controlPoint: CGPoint(x: x1, y: y1))
                cur = CGPoint(x: x, y: y)
            case "Z", "z":
                path.close()
            default:
                break
            }
        }
        return path.isEmpty ? nil : path
    }

    // MARK: - Helpers

    private func d(_ v: Float) -> CGFloat { CGFloat(v) }

    private func uiColor(_ rgba: UInt32) -> UIColor {
        let r = CGFloat((rgba >> 24) & 0xFF) / 255.0
        let g = CGFloat((rgba >> 16) & 0xFF) / 255.0
        let b = CGFloat((rgba >> 8)  & 0xFF) / 255.0
        let a = CGFloat(rgba & 0xFF) / 255.0
        return UIColor(red: r, green: g, blue: b, alpha: a)
    }
}

// MARK: - SwiftUI Wrapper (UIViewRepresentable)

struct MabelCanvasView: UIViewRepresentable {
    @Binding var commands: [RenderCommand]

    func makeUIView(context: Context) -> MabelCanvasUIView {
        let view = MabelCanvasUIView()
        view.isMultipleTouchEnabled = true
        view.backgroundColor = .black
        return view
    }

    func updateUIView(_ view: MabelCanvasUIView, context: Context) {
        view.commands = commands
    }
}

// MARK: - Engine

@MainActor
class MabelEngine: ObservableObject {
    @Published var commands: [RenderCommand] = []

    /// Carrega hello world estatico (sem WASM por enquanto).
    func loadHelloWorld() {
        commands = Self.helloWorld()
    }

    /// Carrega demo Glass UI.
    func loadGlassDemo() {
        commands = Self.glassDemo()
    }

    // MARK: - Hello World

    static func helloWorld() -> [RenderCommand] {
        [
            RenderCommand(op: .beginFrame, color: 0x1A1A2EFF),

            // Card background
            RenderCommand(op: .roundRect, x: 40, y: 100, w: 300, h: 200,
                          color: 0x16213EFF, radius: 16),

            // Title
            RenderCommand(op: .text, x: 80, y: 140, color: 0xE94560FF,
                          text: "Mabel Framework", fontSize: 28),

            // Subtitle
            RenderCommand(op: .text, x: 80, y: 180, color: 0x0F3460FF,
                          text: "Hello from WASI!", fontSize: 18),

            // Status
            RenderCommand(op: .text, x: 80, y: 220, color: 0xFFFFFF80,
                          text: "Core Graphics • No WebView", fontSize: 14),

            // Circle accent
            RenderCommand(op: .circle, x: 190, y: 400, color: 0xE94560FF, radius: 40),

            // Version label
            RenderCommand(op: .text, x: 130, y: 500, color: 0xFFFFFF40,
                          text: "Mabel v0.1.0", fontSize: 12),

            RenderCommand(op: .endFrame),
        ]
    }

    // MARK: - Glass Demo (iOS 26 style)

    static func glassDemo() -> [RenderCommand] {
        [
            RenderCommand(op: .beginFrame, color: 0x0A0A1AFF),

            // Background gradient
            RenderCommand(op: .linearGrad, x: 0, y: 0, w: 0, h: 812,
                          color: 0x1A1A3EFF, color2: 0x0A0A1AFF),
            RenderCommand(op: .rect, x: 0, y: 0, w: 390, h: 812,
                          color: 0x1A1A3EFF),

            // Decorative circle
            RenderCommand(op: .radialGrad, x: 195, y: 200, color: 0xE9456040,
                          radius: 120, color2: 0xE9456000),
            RenderCommand(op: .circle, x: 195, y: 200, color: 0xE9456020, radius: 120),

            // Glass card with shadow + blur
            RenderCommand(op: .shadow, x: 0, y: 8, color: 0x00000060, radius: 24),
            RenderCommand(op: .blur, radius: 40),
            RenderCommand(op: .linearGrad, x: 40, y: 300, w: 40, h: 500,
                          color: 0xFFFFFF30, color2: 0xFFFFFF10),
            RenderCommand(op: .roundRect, x: 40, y: 300, w: 310, h: 200,
                          color: 0xFFFFFF18, radius: 28),

            // Glass card border
            RenderCommand(op: .stroke, x: 40, y: 300, w: 310, h: 200,
                          color: 0xFFFFFF20, radius: 28, fontSize: 0.5),

            // Card content
            RenderCommand(op: .text, x: 64, y: 340, color: 0xFFFFFFFF,
                          text: "Mabel Glass", fontSize: 32),
            RenderCommand(op: .text, x: 64, y: 380, color: 0xFFFFFF99,
                          text: "iOS 26 Design Language", fontSize: 16),
            RenderCommand(op: .text, x: 64, y: 420, color: 0xFFFFFF60,
                          text: "Shadow • Blur • Gradient • Stroke", fontSize: 12),

            // Bottom accent line
            RenderCommand(op: .linearGrad, x: 40, y: 0, w: 350, h: 0,
                          color: 0xE94560FF, color2: 0x0F3460FF),
            RenderCommand(op: .rect, x: 40, y: 480, w: 310, h: 2,
                          color: 0xE94560FF),

            // Footer
            RenderCommand(op: .text, x: 120, y: 550, color: 0xFFFFFF30,
                          text: "Built with Mabel • No WebView", fontSize: 11),

            RenderCommand(op: .endFrame),
        ]
    }
}

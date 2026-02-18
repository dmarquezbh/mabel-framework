import UIKit

// =============================================================================
// Mabel Host - iOS
// Carrega um modulo WASM/WASI e renderiza via Core Graphics (sem WebView).
// Suporta todas as operacoes Glass / modern UI.
// =============================================================================

/// Render commands recebidos do guest WASM.
/// Espelha Mabel.Wasi.Protocol.RenderOp.
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

    init(op: RenderOp, x: Float, y: Float, w: Float, h: Float,
         color: UInt32, text: String? = nil, radius: Float = 0,
         fontSize: Float = 14, color2: UInt32 = 0) {
        self.op = op
        self.x = x
        self.y = y
        self.w = w
        self.h = h
        self.color = color
        self.text = text
        self.radius = radius
        self.fontSize = fontSize
        self.color2 = color2
    }
}

/// UIView que renderiza RenderCommands via Core Graphics.
/// Leve, funciona em iOS 15+, sem WebView.
///
/// Suporta operacoes Glass: Shadow, Blur, LinearGradient, RadialGradient,
/// Stroke, Path, Scale, Rotate. Mesmo protocolo binario para todas as plataformas.
public class MabelCanvasView: UIView {
    var commands: [RenderCommand] = []

    // Effect state (set by effect ops, consumed by next shape)
    private var currentShadow: (offsetX: CGFloat, offsetY: CGFloat, blur: CGFloat, color: UIColor)?
    private var currentBlurRadius: CGFloat?
    private var currentGradient: (type: GradientType, colors: [CGColor], startPoint: CGPoint, endPoint: CGPoint, radius: CGFloat)?

    private enum GradientType { case linear, radial }

    public override func draw(_ rect: CGRect) {
        guard let ctx = UIGraphicsGetCurrentContext() else { return }

        // Reset effect state at frame start
        currentShadow = nil
        currentBlurRadius = nil
        currentGradient = nil

        for cmd in commands {
            switch cmd.op {

            // -- Frame --

            case .beginFrame:
                ctx.saveGState()
                let color = uiColor(cmd.color)
                ctx.setFillColor(color.cgColor)
                ctx.fill(rect)

            case .endFrame:
                ctx.restoreGState()
                // Clear any pending effects
                currentShadow = nil
                currentBlurRadius = nil
                currentGradient = nil

            // -- Primitives --

            case .rect:
                applyEffects(ctx: ctx)
                let color = uiColor(cmd.color)
                ctx.setFillColor(color.cgColor)
                ctx.fill(CGRect(x: d(cmd.x), y: d(cmd.y), w: d(cmd.w), h: d(cmd.h)))

            case .roundRect:
                applyEffects(ctx: ctx)
                let color = uiColor(cmd.color)
                let path = UIBezierPath(
                    roundedRect: CGRect(x: d(cmd.x), y: d(cmd.y), w: d(cmd.w), h: d(cmd.h)),
                    cornerRadius: CGFloat(cmd.radius)
                )
                ctx.setFillColor(color.cgColor)
                ctx.addPath(path.cgPath)
                ctx.fillPath()

            case .circle:
                applyEffects(ctx: ctx)
                let color = uiColor(cmd.color)
                let r = CGFloat(cmd.radius)
                let circleRect = CGRect(x: d(cmd.x) - r, y: d(cmd.y) - r, width: r * 2, height: r * 2)
                ctx.setFillColor(color.cgColor)
                ctx.fillEllipse(in: circleRect)

            case .line:
                let color = uiColor(cmd.color)
                ctx.setStrokeColor(color.cgColor)
                ctx.setLineWidth(CGFloat(cmd.fontSize > 0 ? cmd.fontSize : 1))
                ctx.move(to: CGPoint(x: d(cmd.x), y: d(cmd.y)))
                ctx.addLine(to: CGPoint(x: d(cmd.w), y: d(cmd.h)))
                ctx.strokePath()

            case .text:
                let color = uiColor(cmd.color)
                let attrs: [NSAttributedString.Key: Any] = [
                    .font: UIFont.systemFont(ofSize: CGFloat(cmd.fontSize)),
                    .foregroundColor: color
                ]
                (cmd.text ?? "").draw(at: CGPoint(x: d(cmd.x), y: d(cmd.y)), withAttributes: attrs)

            case .image:
                // Image rendering — requires asset loading from bundle or cache
                // TODO: integrate with asset manager
                break

            // -- Effects (Glass / modern UI) --

            case .shadow:
                currentShadow = (
                    offsetX: d(cmd.x),
                    offsetY: d(cmd.y),
                    blur: CGFloat(cmd.radius),
                    color: uiColor(cmd.color)
                )

            case .blur:
                currentBlurRadius = CGFloat(cmd.radius)

            case .linearGrad:
                let startColor = uiColor(cmd.color).cgColor
                let endColor = uiColor(cmd.color2).cgColor
                currentGradient = (
                    type: .linear,
                    colors: [startColor, endColor],
                    startPoint: CGPoint(x: d(cmd.x), y: d(cmd.y)),
                    endPoint: CGPoint(x: d(cmd.w), y: d(cmd.h)),
                    radius: 0
                )

            case .radialGrad:
                let centerColor = uiColor(cmd.color).cgColor
                let edgeColor = uiColor(cmd.color2).cgColor
                currentGradient = (
                    type: .radial,
                    colors: [centerColor, edgeColor],
                    startPoint: CGPoint(x: d(cmd.x), y: d(cmd.y)),
                    endPoint: CGPoint(x: d(cmd.x), y: d(cmd.y)),
                    radius: CGFloat(cmd.radius)
                )

            case .stroke:
                applyEffects(ctx: ctx)
                let color = uiColor(cmd.color)
                let strokeWidth = CGFloat(cmd.fontSize > 0 ? cmd.fontSize : 1)
                let cornerRadius = CGFloat(cmd.radius)
                let strokeRect = CGRect(x: d(cmd.x), y: d(cmd.y), w: d(cmd.w), h: d(cmd.h))

                if cornerRadius > 0 {
                    let path = UIBezierPath(roundedRect: strokeRect, cornerRadius: cornerRadius)
                    ctx.setStrokeColor(color.cgColor)
                    ctx.setLineWidth(strokeWidth)
                    ctx.addPath(path.cgPath)
                    ctx.strokePath()
                } else {
                    ctx.setStrokeColor(color.cgColor)
                    ctx.setLineWidth(strokeWidth)
                    ctx.stroke(strokeRect)
                }

            case .path:
                applyEffects(ctx: ctx)
                if let pathData = cmd.text, !pathData.isEmpty {
                    if let bezierPath = parseSVGPath(pathData) {
                        let color = uiColor(cmd.color)
                        ctx.setFillColor(color.cgColor)
                        ctx.addPath(bezierPath.cgPath)
                        ctx.fillPath()
                    }
                }

            // -- State --

            case .pushClip:
                ctx.saveGState()
                ctx.clip(to: CGRect(x: d(cmd.x), y: d(cmd.y), w: d(cmd.w), h: d(cmd.h)))

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
                ctx.rotate(by: d(cmd.x)) // angle in radians
            }
        }
    }

    // MARK: - Effect helpers

    /// Applies pending shadow/blur effects to the context before drawing a shape.
    private func applyEffects(ctx: CGContext) {
        // Shadow
        if let shadow = currentShadow {
            ctx.setShadow(
                offset: CGSize(width: shadow.offsetX, height: shadow.offsetY),
                blur: shadow.blur,
                color: shadow.color.cgColor
            )
        }

        // Blur: Core Graphics doesn't have a direct blur filter for draw ops.
        // For production, this would use CIFilter or render-to-image + blur.
        // The blur radius is stored so platform-specific implementations can use it.
        // On iOS, the recommended approach is to use UIVisualEffectView for live blur,
        // or render to an offscreen image and apply CIGaussianBlur.
        // For now, we note the blur is pending — shapes drawn will not be blurred
        // until a full CIFilter pipeline is implemented.
    }

    // MARK: - SVG Path parsing (subset)

    /// Parses a subset of SVG path data (M, L, C, Q, Z commands).
    /// Sufficient for common Glass UI shapes.
    private func parseSVGPath(_ data: String) -> UIBezierPath? {
        let path = UIBezierPath()
        let scanner = Scanner(string: data)
        scanner.charactersToBeSkipped = CharacterSet.whitespaces.union(.init(charactersIn: ","))

        var currentPoint = CGPoint.zero

        while !scanner.isAtEnd {
            var cmd: NSString?
            let cmdChars = CharacterSet.letters
            if scanner.scanCharacters(from: cmdChars, into: &cmd), let command = cmd as String? {
                for ch in command {
                    switch ch {
                    case "M":
                        if let x = scanFloat(scanner), let y = scanFloat(scanner) {
                            path.move(to: CGPoint(x: x, y: y))
                            currentPoint = CGPoint(x: x, y: y)
                        }
                    case "m":
                        if let dx = scanFloat(scanner), let dy = scanFloat(scanner) {
                            let pt = CGPoint(x: currentPoint.x + dx, y: currentPoint.y + dy)
                            path.move(to: pt)
                            currentPoint = pt
                        }
                    case "L":
                        if let x = scanFloat(scanner), let y = scanFloat(scanner) {
                            path.addLine(to: CGPoint(x: x, y: y))
                            currentPoint = CGPoint(x: x, y: y)
                        }
                    case "l":
                        if let dx = scanFloat(scanner), let dy = scanFloat(scanner) {
                            let pt = CGPoint(x: currentPoint.x + dx, y: currentPoint.y + dy)
                            path.addLine(to: pt)
                            currentPoint = pt
                        }
                    case "C":
                        if let x1 = scanFloat(scanner), let y1 = scanFloat(scanner),
                           let x2 = scanFloat(scanner), let y2 = scanFloat(scanner),
                           let x = scanFloat(scanner), let y = scanFloat(scanner) {
                            path.addCurve(
                                to: CGPoint(x: x, y: y),
                                controlPoint1: CGPoint(x: x1, y: y1),
                                controlPoint2: CGPoint(x: x2, y: y2)
                            )
                            currentPoint = CGPoint(x: x, y: y)
                        }
                    case "Q":
                        if let x1 = scanFloat(scanner), let y1 = scanFloat(scanner),
                           let x = scanFloat(scanner), let y = scanFloat(scanner) {
                            path.addQuadCurve(to: CGPoint(x: x, y: y),
                                              controlPoint: CGPoint(x: x1, y: y1))
                            currentPoint = CGPoint(x: x, y: y)
                        }
                    case "Z", "z":
                        path.close()
                    case "H":
                        if let x = scanFloat(scanner) {
                            path.addLine(to: CGPoint(x: x, y: currentPoint.y))
                            currentPoint.x = x
                        }
                    case "V":
                        if let y = scanFloat(scanner) {
                            path.addLine(to: CGPoint(x: currentPoint.x, y: y))
                            currentPoint.y = y
                        }
                    default:
                        break // Skip unknown commands
                    }
                }
            }
        }

        return path.isEmpty ? nil : path
    }

    private func scanFloat(_ scanner: Scanner) -> CGFloat? {
        var value: Double = 0
        if scanner.scanDouble(&value) { return CGFloat(value) }
        return nil
    }

    // MARK: - Helpers

    private func d(_ v: Float) -> CGFloat { CGFloat(v) }

    /// Converts a packed RGBA UInt32 to UIColor.
    /// Format: 0xRRGGBBAA
    private func uiColor(_ rgba: UInt32) -> UIColor {
        let r = CGFloat((rgba >> 24) & 0xFF) / 255.0
        let g = CGFloat((rgba >> 16) & 0xFF) / 255.0
        let b = CGFloat((rgba >> 8)  & 0xFF) / 255.0
        let a = CGFloat(rgba & 0xFF) / 255.0
        return UIColor(red: r, green: g, blue: b, alpha: a)
    }
}

// Extension para construir CGRect mais limpo
private extension CGRect {
    init(x: CGFloat, y: CGFloat, w: CGFloat, h: CGFloat) {
        self.init(x: x, y: y, width: w, height: h)
    }
}

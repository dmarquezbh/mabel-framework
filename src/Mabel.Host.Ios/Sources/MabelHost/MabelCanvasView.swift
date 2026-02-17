import UIKit

// =============================================================================
// Mabel Host - iOS
// Carrega um modulo WASM/WASI e renderiza via Core Graphics (sem WebView).
// =============================================================================

/// Render commands recebidos do guest WASM.
/// Espelha Mabel.Wasi.Protocol.RenderOp.
enum RenderOp: UInt8 {
    case rect      = 0x01
    case roundRect = 0x02
    case circle    = 0x03
    case line      = 0x04
    case text      = 0x05
    case image     = 0x06

    case pushClip    = 0x10
    case popClip     = 0x11
    case pushOpacity = 0x12
    case popOpacity  = 0x13
    case translate   = 0x14

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
}

/// UIView que renderiza RenderCommands via Core Graphics.
/// Leve, funciona em iOS 15+, sem WebView.
public class MabelCanvasView: UIView {
    var commands: [RenderCommand] = []

    public override func draw(_ rect: CGRect) {
        guard let ctx = UIGraphicsGetCurrentContext() else { return }

        for cmd in commands {
            switch cmd.op {
            case .beginFrame:
                // Save state at frame start so all transforms/clips are undone at EndFrame
                ctx.saveGState()
                let color = uiColor(cmd.color)
                ctx.setFillColor(color.cgColor)
                ctx.fill(rect)

            case .rect:
                let color = uiColor(cmd.color)
                ctx.setFillColor(color.cgColor)
                ctx.fill(CGRect(x: d(cmd.x), y: d(cmd.y), w: d(cmd.w), h: d(cmd.h)))

            case .roundRect:
                let color = uiColor(cmd.color)
                let path = UIBezierPath(
                    roundedRect: CGRect(x: d(cmd.x), y: d(cmd.y), w: d(cmd.w), h: d(cmd.h)),
                    cornerRadius: CGFloat(cmd.radius)
                )
                ctx.setFillColor(color.cgColor)
                ctx.addPath(path.cgPath)
                ctx.fillPath()

            case .circle:
                let color = uiColor(cmd.color)
                let r = CGFloat(cmd.radius)
                let circleRect = CGRect(x: d(cmd.x) - r, y: d(cmd.y) - r, width: r * 2, height: r * 2)
                ctx.setFillColor(color.cgColor)
                ctx.fillEllipse(in: circleRect)

            case .line:
                let color = uiColor(cmd.color)
                ctx.setStrokeColor(color.cgColor)
                ctx.setLineWidth(1)
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

            case .endFrame:
                // Restore the state saved at BeginFrame, undoing all transforms
                ctx.restoreGState()

            case .image:
                // Image rendering not yet implemented — requires asset loading
                break
            }
        }
    }

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

// Extensao para construir CGRect mais limpo
private extension CGRect {
    init(x: CGFloat, y: CGFloat, w: CGFloat, h: CGFloat) {
        self.init(x: x, y: y, width: w, height: h)
    }
}

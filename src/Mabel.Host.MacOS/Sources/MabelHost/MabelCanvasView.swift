import AppKit

// =============================================================================
// Mabel Host - macOS (AppKit)
// Renders a Mabel display-list via Core Graphics (CGContext) into an NSView —
// no WebView, no SwiftUI. AppKit twin of Mabel.Host.Ios/MabelCanvasView.
//
// Coordinate space: isFlipped = true so the origin is top-left and Y grows
// down, matching the iOS/UIKit host and the board coordinate space exactly
// (no offset math between hosts).
//
// Paths use Core Graphics CGPath directly (not NSBezierPath) so the code is
// deployment-target-agnostic (NSBezierPath.cgPath is only macOS 14+).
// =============================================================================

public final class MabelCanvasView: NSView {

    /// Display-list to render. Setting it triggers a redraw.
    public var commands: [RenderCommand] = [] {
        didSet { needsDisplay = true }
    }

    /// Clickable regions (hit-test). Empty = render only, no interactivity.
    public var regions: [HitRegion] = []

    /// Fired on click: receives the hit region (or nil = miss).
    public var onTap: ((HitRegion?) -> Void)?

    /// Region highlighted by the last click (visual feedback). nil = none.
    private var highlighted: HitRegion?

    // Top-left origin, Y-down — mirror iOS/UIKit.
    public override var isFlipped: Bool { true }

    // Effect state (set by effect ops, consumed by the next shape).
    private var pendingShadow: (ox: CGFloat, oy: CGFloat, blur: CGFloat, color: NSColor)?
    private var pendingBlurRadius: CGFloat?
    private var pendingGradient: GradientState?

    private enum GradientState {
        case linear(x1: CGFloat, y1: CGFloat, x2: CGFloat, y2: CGFloat, start: CGColor, end: CGColor)
        case radial(cx: CGFloat, cy: CGFloat, radius: CGFloat, center: CGColor, edge: CGColor)
    }

    // MARK: - Draw

    public override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let rect = bounds

        pendingShadow = nil
        pendingBlurRadius = nil
        pendingGradient = nil

        for cmd in commands {
            switch cmd.op {

            // -- Frame --
            case .beginFrame:
                ctx.saveGState()
                ctx.setFillColor(nsColor(cmd.color).cgColor)
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
                    ctx.saveGState(); ctx.clip(to: r)
                    drawGradient(ctx, in: r, gradient: grad)
                    ctx.restoreGState()
                } else {
                    ctx.setFillColor(nsColor(cmd.color).cgColor)
                    ctx.fill(r)
                }
                clearEffects(ctx)

            case .roundRect:
                applyEffects(ctx)
                let r = CGRect(x: d(cmd.x), y: d(cmd.y), width: d(cmd.w), height: d(cmd.h))
                let path = CGPath(roundedRect: r, cornerWidth: CGFloat(cmd.radius),
                                  cornerHeight: CGFloat(cmd.radius), transform: nil)
                if let grad = pendingGradient {
                    ctx.saveGState()
                    ctx.addPath(path); ctx.clip()
                    drawGradient(ctx, in: r, gradient: grad)
                    ctx.restoreGState()
                } else {
                    ctx.setFillColor(nsColor(cmd.color).cgColor)
                    ctx.addPath(path); ctx.fillPath()
                }
                clearEffects(ctx)

            case .circle:
                applyEffects(ctx)
                let rr = CGFloat(cmd.radius)
                let circleRect = CGRect(x: d(cmd.x) - rr, y: d(cmd.y) - rr, width: rr * 2, height: rr * 2)
                ctx.setFillColor(nsColor(cmd.color).cgColor)
                ctx.fillEllipse(in: circleRect)
                clearEffects(ctx)

            case .line:
                ctx.setStrokeColor(nsColor(cmd.color).cgColor)
                ctx.setLineWidth(CGFloat(cmd.fontSize > 0 ? cmd.fontSize : 1))
                ctx.move(to: CGPoint(x: d(cmd.x), y: d(cmd.y)))
                ctx.addLine(to: CGPoint(x: d(cmd.w), y: d(cmd.h)))
                ctx.strokePath()

            case .text:
                drawText(cmd.text ?? "", at: CGPoint(x: d(cmd.x), y: d(cmd.y)),
                         size: CGFloat(cmd.fontSize), color: nsColor(cmd.color))

            case .image:
                break // TODO: asset manager

            // -- Effects --
            case .shadow:
                pendingShadow = (ox: d(cmd.x), oy: d(cmd.y), blur: CGFloat(cmd.radius),
                                 color: nsColor(cmd.color))

            case .blur:
                pendingBlurRadius = CGFloat(cmd.radius)

            case .linearGrad:
                pendingGradient = .linear(x1: d(cmd.x), y1: d(cmd.y), x2: d(cmd.w), y2: d(cmd.h),
                                          start: nsColor(cmd.color).cgColor,
                                          end: nsColor(cmd.color2).cgColor)

            case .radialGrad:
                pendingGradient = .radial(cx: d(cmd.x), cy: d(cmd.y), radius: CGFloat(cmd.radius),
                                          center: nsColor(cmd.color).cgColor,
                                          edge: nsColor(cmd.color2).cgColor)

            case .stroke:
                applyEffects(ctx)
                let strokeWidth = CGFloat(cmd.fontSize > 0 ? cmd.fontSize : 1)
                let cornerRadius = CGFloat(cmd.radius)
                let strokeRect = CGRect(x: d(cmd.x), y: d(cmd.y), width: d(cmd.w), height: d(cmd.h))
                ctx.setStrokeColor(nsColor(cmd.color).cgColor)
                ctx.setLineWidth(strokeWidth)
                if cornerRadius > 0 {
                    let path = CGPath(roundedRect: strokeRect, cornerWidth: cornerRadius,
                                      cornerHeight: cornerRadius, transform: nil)
                    ctx.addPath(path); ctx.strokePath()
                } else {
                    ctx.stroke(strokeRect)
                }
                clearEffects(ctx)

            case .path:
                applyEffects(ctx)
                if let data = cmd.text, !data.isEmpty, let cg = parseSVGPath(data) {
                    ctx.setFillColor(nsColor(cmd.color).cgColor)
                    ctx.addPath(cg); ctx.fillPath()
                }
                clearEffects(ctx)

            // -- State --
            case .pushClip:
                ctx.saveGState()
                ctx.clip(to: CGRect(x: d(cmd.x), y: d(cmd.y), width: d(cmd.w), height: d(cmd.h)))

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
                ctx.rotate(by: d(cmd.x)) // radians
            }
        }

        // Tap feedback (additive — drawn on top of the frame).
        if let h = highlighted {
            let r = CGRect(x: d(h.x), y: d(h.y), width: d(h.w), height: d(h.h)).insetBy(dx: 1, dy: 1)
            let path = CGPath(roundedRect: r, cornerWidth: 6, cornerHeight: 6, transform: nil)
            ctx.setStrokeColor(NSColor.systemBlue.cgColor)
            ctx.setLineWidth(2)
            ctx.addPath(path); ctx.strokePath()
        }
    }

    // MARK: - Mouse / hit-testing

    public override func mouseDown(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        let hit = region(at: p)
        if let h = hit {
            NSLog("[Mabel] click \(h.kind) id=\(h.id) @ (\(Int(p.x)),\(Int(p.y)))")
        } else {
            NSLog("[Mabel] click miss @ (\(Int(p.x)),\(Int(p.y)))")
        }
        highlighted = hit
        needsDisplay = true
        onTap?(hit)
    }

    /// Most specific region (smallest area) that contains the point.
    /// Order-independent — a card inside a column inside a group always wins.
    func region(at point: CGPoint) -> HitRegion? {
        var best: HitRegion?
        var bestArea = CGFloat.greatestFiniteMagnitude
        for r in regions {
            let rect = CGRect(x: CGFloat(r.x), y: CGFloat(r.y),
                              width: CGFloat(r.w), height: CGFloat(r.h))
            guard rect.contains(point) else { continue }
            let area = rect.width * rect.height
            if area < bestArea { bestArea = area; best = r }
        }
        return best
    }

    // MARK: - Effect helpers

    private func applyEffects(_ ctx: CGContext) {
        if let s = pendingShadow {
            ctx.setShadow(offset: CGSize(width: s.ox, height: s.oy),
                          blur: s.blur, color: s.color.cgColor)
        }
        // Blur: Core Graphics has no direct blur for draw ops. A production
        // path would render offscreen + CIGaussianBlur, or use NSVisualEffectView.
        // The radius is captured (pendingBlurRadius) for that future pipeline.
    }

    private func clearEffects(_ ctx: CGContext) {
        if pendingShadow != nil {
            ctx.setShadow(offset: .zero, blur: 0, color: nil)
        }
        pendingGradient = nil
    }

    private func drawGradient(_ ctx: CGContext, in rect: CGRect, gradient: GradientState) {
        let space = CGColorSpaceCreateDeviceRGB()
        switch gradient {
        case .linear(let x1, let y1, let x2, let y2, let start, let end):
            if let g = CGGradient(colorsSpace: space, colors: [start, end] as CFArray, locations: [0, 1]) {
                ctx.drawLinearGradient(g, start: CGPoint(x: x1, y: y1), end: CGPoint(x: x2, y: y2),
                                       options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
            }
        case .radial(let cx, let cy, let radius, let center, let edge):
            if let g = CGGradient(colorsSpace: space, colors: [center, edge] as CFArray, locations: [0, 1]) {
                let c = CGPoint(x: cx, y: cy)
                ctx.drawRadialGradient(g, startCenter: c, startRadius: 0, endCenter: c, endRadius: radius,
                                       options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
            }
        }
    }

    // MARK: - Text

    private func drawText(_ text: String, at point: CGPoint, size: CGFloat, color: NSColor) {
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: size),
            .foregroundColor: color,
        ]
        NSAttributedString(string: text, attributes: attrs).draw(at: point)
    }

    // MARK: - SVG path parser (subset: M, L, C, Q, H, V, Z)

    private func parseSVGPath(_ data: String) -> CGPath? {
        let path = CGMutablePath()
        let scanner = Scanner(string: data)
        scanner.charactersToBeSkipped = CharacterSet.whitespaces.union(.init(charactersIn: ","))
        var cur = CGPoint.zero
        var moved = false

        func f() -> CGFloat? {
            guard let v = scanner.scanDouble() else { return nil }
            return CGFloat(v)
        }

        while !scanner.isAtEnd {
            guard let ch = scanner.scanCharacter() else { break }
            switch ch {
            case "M":
                guard let x = f(), let y = f() else { break }
                cur = CGPoint(x: x, y: y); path.move(to: cur); moved = true
            case "m":
                guard let dx = f(), let dy = f() else { break }
                cur = CGPoint(x: cur.x + dx, y: cur.y + dy); path.move(to: cur); moved = true
            case "L":
                guard let x = f(), let y = f() else { break }
                cur = CGPoint(x: x, y: y); path.addLine(to: cur)
            case "l":
                guard let dx = f(), let dy = f() else { break }
                cur = CGPoint(x: cur.x + dx, y: cur.y + dy); path.addLine(to: cur)
            case "H":
                guard let x = f() else { break }
                cur.x = x; path.addLine(to: cur)
            case "h":
                guard let dx = f() else { break }
                cur.x += dx; path.addLine(to: cur)
            case "V":
                guard let y = f() else { break }
                cur.y = y; path.addLine(to: cur)
            case "v":
                guard let dy = f() else { break }
                cur.y += dy; path.addLine(to: cur)
            case "C":
                guard let x1 = f(), let y1 = f(), let x2 = f(), let y2 = f(),
                      let x = f(), let y = f() else { break }
                cur = CGPoint(x: x, y: y)
                path.addCurve(to: cur, control1: CGPoint(x: x1, y: y1), control2: CGPoint(x: x2, y: y2))
            case "Q":
                guard let x1 = f(), let y1 = f(), let x = f(), let y = f() else { break }
                cur = CGPoint(x: x, y: y)
                path.addQuadCurve(to: cur, control: CGPoint(x: x1, y: y1))
            case "Z", "z":
                path.closeSubpath()
            default:
                break
            }
        }
        return (moved && !path.isEmpty) ? path.copy() : nil
    }

    // MARK: - Helpers

    private func d(_ v: Float) -> CGFloat { CGFloat(v) }

    /// Packed RGBA UInt32 (0xRRGGBBAA) → NSColor (device RGB).
    private func nsColor(_ rgba: UInt32) -> NSColor {
        let r = CGFloat((rgba >> 24) & 0xFF) / 255.0
        let g = CGFloat((rgba >> 16) & 0xFF) / 255.0
        let b = CGFloat((rgba >> 8)  & 0xFF) / 255.0
        let a = CGFloat(rgba & 0xFF) / 255.0
        return NSColor(deviceRed: r, green: g, blue: b, alpha: a)
    }
}

import Foundation

// =============================================================================
// Mabel render protocol — platform-neutral.
// Mirrors Mabel.Wasi.Protocol.RenderOp (src/Mabel.Wasi.Protocol/Protocol.cs)
// and Mabel.Host.Ios op-for-op. Same binary contract across all hosts.
// =============================================================================

/// Render commands received from the guest WASM (or a static demo).
public enum RenderOp: UInt8 {
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

public struct RenderCommand {
    public let op: RenderOp
    public let x: Float
    public let y: Float
    public let w: Float
    public let h: Float
    public let color: UInt32
    public let text: String?
    public let radius: Float
    public let fontSize: Float
    public let color2: UInt32

    public init(op: RenderOp, x: Float = 0, y: Float = 0, w: Float = 0, h: Float = 0,
                color: UInt32 = 0, text: String? = nil, radius: Float = 0,
                fontSize: Float = 14, color2: UInt32 = 0) {
        self.op = op; self.x = x; self.y = y; self.w = w; self.h = h
        self.color = color; self.text = text; self.radius = radius
        self.fontSize = fontSize; self.color2 = color2
    }
}

/// Clickable region of the frame (hit-test), emitted in parallel to the
/// display-list by board_gen (kanban-regions.json). Same logical space as the
/// frame. ADDITIVE: absent → rendering is unchanged.
public struct HitRegion: Decodable {
    public let id: String
    public let kind: String
    public let x: Float
    public let y: Float
    public let w: Float
    public let h: Float
    public let meta: [String: String]?
}

/// kanban-regions.json envelope: kanban bounds + region list.
public struct BoardRegions: Decodable {
    public let width: Float
    public let height: Float
    public let regions: [HitRegion]
}

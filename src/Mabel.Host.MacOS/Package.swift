// swift-tools-version: 6.0
import PackageDescription

// =============================================================================
// Mabel.Host.MacOS
// AppKit host for the Mabel SDUI render protocol, cross-compiled on Linux via
// the xtool Darwin Swift SDK (no Mac needed to BUILD). Mirrors Mabel.Host.Ios
// (UIKit) op-for-op — same binary RenderOp contract (Mabel.Wasi.Protocol).
//
// Build (from Linux/WSL):
//   swift build --swift-sdk arm64-apple-macosx \
//     --package-path src/Mabel.Host.MacOS
//
// Products:
//   - MabelHost     library : NSView canvas + engine (reusable)
//   - MabelHostApp  exe     : NSApplication demo that renders the display-list
// =============================================================================

let package = Package(
    name: "MabelHostMacOS",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "MabelHost", targets: ["MabelHost"]),
        .executable(name: "MabelHostApp", targets: ["MabelHostApp"]),
    ],
    targets: [
        .target(
            name: "MabelHost"
        ),
        .executableTarget(
            name: "MabelHostApp",
            dependencies: ["MabelHost"]
        ),
    ]
)

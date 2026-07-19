// swift-tools-version: 6.0
import PackageDescription

// Harness app: exercita cada capability chamando o CapabilityHost direto (prova
// a impl nativa sem um guest .wasm). Buildável via xtool (`xtool dev build`).
let package = Package(
    name: "MabelCapabilitiesHarness",
    platforms: [.iOS(.v16)],
    products: [
        .library(name: "MabelCapabilitiesHarness", targets: ["MabelCapabilitiesHarness"])
    ],
    dependencies: [
        .package(path: "../../src/Mabel.Host.Ios")
    ],
    targets: [
        .target(
            name: "MabelCapabilitiesHarness",
            dependencies: [.product(name: "MabelHost", package: "Mabel.Host.Ios")]
        )
    ],
    swiftLanguageModes: [.v5]
)

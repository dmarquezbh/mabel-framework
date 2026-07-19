// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "MabelHost",
    platforms: [.iOS(.v15)],
    products: [
        .library(name: "MabelHost", targets: ["MabelHost"])
    ],
    targets: [
        .target(
            name: "MabelHost",
            resources: [.copy("Resources")]
        )
    ],
    // Swift 5 language mode: as capabilities usam delegates/closures de frameworks
    // (CoreBluetooth/CoreLocation/UIKit) ainda sem anotação Sendable; o modo v5
    // evita erros de concorrência estrita do Swift 6 sem afetar o runtime.
    swiftLanguageModes: [.v5]
)

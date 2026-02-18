// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "MabelHello",
    platforms: [.iOS(.v16)],
    products: [
        .library(name: "MabelHello", targets: ["MabelHello"])
    ],
    targets: [
        .target(
            name: "MabelHello",
            path: "Sources/MabelHello",
            resources: [.copy("Resources")]
        )
    ]
)

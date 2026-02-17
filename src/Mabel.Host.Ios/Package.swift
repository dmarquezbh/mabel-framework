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
    ]
)

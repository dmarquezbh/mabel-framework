// swift-tools-version: 6.0
import PackageDescription
let package = Package(
    name: "runner",
    dependencies: [
        .package(url: "https://github.com/swiftwasm/WasmKit.git", exact: "0.2.2"),
        .package(url: "https://github.com/apple/swift-system", exact: "1.5.0")
    ],
    targets: [
        .executableTarget(name: "runner", dependencies: [
            .product(name: "WasmKit", package: "WasmKit"),
            .product(name: "WasmKitWASI", package: "WasmKit")
        ])
    ]
)

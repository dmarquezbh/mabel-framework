// swift-tools-version: 6.0
import PackageDescription

// =============================================================================
// Spike: WasmKit rodando em runtime interpretado, no device físico iOS.
//
// Pré-requisito real bloqueando docs/hmr-e-estado.md e docs/ota.md — ambos
// dependem de "trocar o módulo WASM em runtime sem reiniciar o host" no
// iPhone. Este pacote prova (ou refuta) isso isoladamente, sem o resto do
// Mabel no caminho.
//
// Segue a mesma convenção de samples/hello-world-ios (xtool): produto
// `.library`, wrapper de app feito pelo `xtool`, bundle ID em xtool.yml.
// =============================================================================
let package = Package(
    name: "WasmKitIOSSpike",
    platforms: [.iOS(.v18)],  // WasmKit 0.3.1 exige iOS 18+ (device test-device-1 roda 18.7.9)
    products: [
        .library(name: "WasmKitIOSSpike", targets: ["WasmKitIOSSpike"])
    ],
    dependencies: [
        .package(url: "https://github.com/swiftwasm/WasmKit.git", exact: "0.3.1")
    ],
    targets: [
        .target(
            name: "WasmKitIOSSpike",
            dependencies: [
                .product(name: "WasmKit", package: "WasmKit")
            ],
            path: "Sources/WasmKitIOSSpike",
            resources: [.copy("Resources")]
        )
    ]
)

// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "MabelHost",
    platforms: [.iOS(.v15)],
    products: [
        .library(name: "MabelHost", targets: ["MabelHost"])
    ],
    targets: [
        // Sem `resources:` de propósito: a pasta Resources/ só tinha um
        // .gitkeep (nenhum asset real, nada em código usa Bundle.module) e um
        // bundle de recursos vazio quebra o CodeSign em builds de Simulador
        // ("bundle format unrecognized, invalid, or unsuitable") — só não
        // aparecia em build de device físico por usar identidade de
        // assinatura diferente. Reintroduzir `resources:` quando houver
        // asset real pra empacotar.
        .target(name: "MabelHost")
    ],
    // Swift 5 language mode: as capabilities usam delegates/closures de frameworks
    // (CoreBluetooth/CoreLocation/UIKit) ainda sem anotação Sendable; o modo v5
    // evita erros de concorrência estrita do Swift 6 sem afetar o runtime.
    swiftLanguageModes: [.v5]
)

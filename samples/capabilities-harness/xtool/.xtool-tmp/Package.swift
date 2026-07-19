// swift-tools-version: 6.0
import PackageDescription
let package = Package(
    name: "MabelCapabilitiesHarness-Builder",
    platforms: [
        .iOS("16.0"),
    ],
    dependencies: [
        .package(name: "RootPackage", path: "../.."),
    ],
    targets: [
        .executableTarget(
    name: "MabelCapabilitiesHarness-App",
    dependencies: [
        .product(name: "MabelCapabilitiesHarness", package: "RootPackage"),
    ],
    linkerSettings: [
    .unsafeFlags([
        "-Xlinker", "-rpath", "-Xlinker", "@executable_path/Frameworks",
    ]),
]
)
    ]
)

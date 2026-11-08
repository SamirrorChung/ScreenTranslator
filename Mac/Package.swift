// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ScreenTranslator",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "TranslatorCore", targets: ["TranslatorCore"]),
        .executable(name: "ScreenTranslator", targets: ["ScreenTranslator"]),
    ],
    targets: [
        .target(name: "TranslatorCore", path: "Core"),
        .testTarget(
            name: "TranslatorCoreTests",
            dependencies: ["TranslatorCore"],
            path: "Tests/TranslatorCoreTests"
        ),
        .executableTarget(
            name: "ScreenTranslator",
            dependencies: ["TranslatorCore"],
            path: "App"
        ),
    ]
)

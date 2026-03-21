// swift-tools-version: 6.0
import PackageDescription

let bridgeSearchPath = "../../../artifacts/mac/bridge"

let package = Package(
    name: "PopMacApp",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "PopMacApp", targets: ["PopMacApp"])
    ],
    targets: [
        .systemLibrary(
            name: "CPopMacBridge",
            path: "Sources/CPopMacBridge"
        ),
        .target(
            name: "PopMacSupport",
            path: "Sources/PopMacSupport"
        ),
        .executableTarget(
            name: "PopMacApp",
            dependencies: [
                "CPopMacBridge",
                "PopMacSupport"
            ],
            path: "Sources/PopMacApp",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("ApplicationServices"),
                .linkedFramework("CoreGraphics"),
                .unsafeFlags([
                    "-L", bridgeSearchPath,
                    "-Xlinker", "-rpath",
                    "-Xlinker", "@executable_path/../Frameworks"
                ])
            ]
        ),
        .testTarget(
            name: "PopMacSupportTests",
            dependencies: ["PopMacSupport"],
            path: "Tests/PopMacSupportTests"
        ),
        .testTarget(
            name: "PopMacAppTests",
            dependencies: ["PopMacApp"],
            path: "Tests/PopMacAppTests"
        )
    ]
)

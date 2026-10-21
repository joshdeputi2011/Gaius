// swift-tools-version: 5.9
import PackageDescription
let package = Package(
    name: "gaius_engine",
    platforms: [.iOS("15.0")],
    products: [.library(name: "gaius-engine", targets: ["gaius_engine"])],
    targets: [.target(name: "gaius_engine", publicHeadersPath: "include")],
    cxxLanguageStandard: .cxx14
)

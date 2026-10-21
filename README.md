# Gaius Office

A local-first office workspace with a React web app, Flutter Android/iOS app, WinUI/WebView2 desktop suite, and a custom implementation of a custom calculation and document-interchange engine.

**Status: expanded development release.** Windows and web share the same locally bundled editor. Flutter has matching tabbed ribbons with Home selected by default. This is not yet complete Microsoft Office, LibreOffice, iLovePDF or iLoveIMG parity. See [feature coverage and limits](FEATURES.md) and [verification evidence](VALIDATION.md).

## Workspace

| Folder | Contents |
| --- | --- |
| `Engine` | C++ calculation engine and C ABI; Dart FFI plugin; TypeScript document/formula/ZIP/OOXML implementation; common schema and Fluent design tokens |
| `Web` | React + TypeScript online editor; browser-only document storage; no cloud service |
| `Mobile` | Flutter all-in-one app; Android and iOS hosts; document tabs; native additional-window hooks |
| `WinUI` | C# / WinUI 3 host for the shared editor, plus the retained native file workspace and calculation binding |
| `MacOS` | Planned SwiftUI/AppKit integration, intentionally not implemented yet |

## Editing and file workflows

- A consistent ribbon, soft raised surfaces, restrained blur, and bottom status bars across the shared desktop/web editor and native phone interface.
- Save as, sharing, search, print preview, document tabs/windows and local persistence.
- Documents with rich text, pictures, editable tables, WordArt, layout and review tools.
- Worksheets with formulas, formatting, sorting, filters and bar/line/pie charts.
- Presentations with movable text, pictures and shapes, speaker notes, transitions, animations and matching-object morph.
- PDF selection/search, page operations, conversion, limited text replacement and compression with a requested target size.
- Image crop/resize/flip, adjustments, blur, watermark, conversion and target-size compression.

The precise platform differences, interchange limits and remaining features are recorded in [FEATURES.md](FEATURES.md). All processing stays on the device. The Windows shared workspace imports copies of native documents; it does not continuously synchronize those copies back to the native directory.

## Run the web application

Push automation builds APK/Windows releases with notes and deploys the default branch to GitHub Pages. See [.github/RELEASES.md](.github/RELEASES.md) for the one-time Pages setting and stable Android signing secrets. Mobile extension coverage and conversion limits are documented in [Mobile/FORMATS.md](Mobile/FORMATS.md).

Requires Node.js 22+.

```powershell
cd Web
npm.cmd ci
npm.cmd run dev
```

Open `http://127.0.0.1:5173`. Build static output with `npm.cmd run build`; files are emitted into `Web/dist`. Any static HTTP host can serve this output. No backend, account, remote document storage, analytics, external fonts, or API keys are required.

The verified static build is also packaged as `Web/gaius-web-preview.zip`. Extract it and serve the contents over HTTP; opening the HTML directly as a `file://` URL is not supported.

```powershell
npm.cmd test
npm.cmd run test:e2e
```

Browser tests use the installed Chrome path on Windows. Set `CHROME_PATH` to a Chrome/Chromium executable on another machine. They start and stop their own local dev server.

IndexedDB is local browser storage, not a permanent backup: clearing site data, private browsing, quota limits, or browser eviction can remove it. Export `.gaius` files for portable copies. Hosting on a different origin creates a separate workspace. Opening another window does not send documents to a server. Conflicting writes are rejected with an export/reopen recovery message.

## Build Android and iOS

The project was generated with Flutter 3.47.1 / Dart 3.13.1. It uses Android API 24+ and iOS 15+.

```powershell
cd Mobile
flutter pub get
flutter analyze
flutter build apk --debug
```

An Android SDK, command-line tools, the appropriate accepted SDK licenses, JDK, NDK, CMake, and Gradle dependencies are required. The local `gaius_engine` plugin compiles the same C++ source as the desktop engine. Release signing must be configured before distribution; the generated Android release setup is not production signing.

On macOS with Xcode and the required iOS tooling:

```sh
cd Mobile
flutter pub get
flutter build ios --debug --no-codesign
```

The engine plugin provides Swift Package Manager and CocoaPods definitions. Each iOS scene creates its own Flutter engine. Android uses separate document tasks. Android release packaging is verified; window behavior still needs device testing. iOS packaging requires macOS.

The PDF dependency builds native assets, so `flutter test` requires an appropriate host C++ compiler. Tests cover the model, phone ribbon, rich content, compression output and PDF generation. Use Flutter 3.47.1 / Dart 3.13.1; this checkout also has an ignored local SDK at `.tools/flutter`.

## Build WinUI

Install .NET 8, Visual Studio 2022 or compatible Build Tools with **Desktop development with C++**, Windows SDK, and CMake. Use an x64 developer environment.

```powershell
npm.cmd --prefix Web ci
npm.cmd --prefix Web run build
cmake -S Engine/native -B Engine/native/build -A x64
cmake --build Engine/native/build --config Release
ctest --test-dir Engine/native/build -C Release --output-on-failure
dotnet build WinUI/Gaius.Office.csproj -c Release -p:Platform=x64
```

The `.csproj` copies `Engine/native/build/Release/gaius_engine.dll` when it exists. The engine must match the app's x64 architecture. Missing/incompatible engines display `#ENGINE!`/`#ARCH!` rather than fabricated results. `Engine/build.ps1` and `WinUI/build.ps1` provide equivalent convenience commands where local PowerShell execution policy permits scripts.

The app is unpackaged and self-contained with respect to Windows App SDK. Documents live under `%LOCALAPPDATA%/Gaius/Documents`. Optimistic revisions and file locks prevent silent conflicting saves. Export remains available after a conflict. Platform compilation, installation, accessibility, and multi-monitor behavior need validation.

## Engine scope and compatibility

The calculation engine implements a tested formula subset in C++ and TypeScript, with a C ABI for Dart/C#. The portable v1 document format now carries optional worksheets, cell styles/charts, slide objects/effects, comments, layout and image settings without removing existing records.

Office interchange is a supported-content import/export, not a certified full-fidelity converter. Keep source Office files and export `.gaius` copies when using editor-specific features. Full Office layout, every formula, arbitrary PDF object editing, OCR, secure redaction/signing and advanced image processing remain product work. Compression targets are best-effort and the resulting size is reported; image-based PDF compression removes selectable text and forms. Generated Office printouts are simplified layouts with limited font coverage.

See [FEATURES.md](FEATURES.md) for the full coverage matrix, research sources and remaining work. See [VALIDATION.md](VALIDATION.md) for checks performed in this iteration.

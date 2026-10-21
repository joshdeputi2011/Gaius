# Gaius mobile

One Flutter app for Write, Sheets, Slides, Image, and PDF. Multiple documents stay open as tabs. Android supports additional document tasks; supported iPad environments can request additional native scenes, each with its own Flutter engine.

Run `flutter pub get`, `flutter analyze`, then `flutter run` on a configured Android or iOS device. The native C++ engine and Dart document model are local dependencies under `../Engine`. Fluent UI System Icons and application accent colors match the web and Windows apps.

Documents are stored in the application support directory and shared using `.gaius` export. CSV, plain text, images, and PDFs can also be opened. CSV, PNG, and PDF exports are available in their respective editors. DOCX/XLSX/PPTX conversion currently runs only in the web app.

Flutter analysis passes. A universal release APK is available at `../output/gaius-office.apk` (Android 7.0+, ARMv7/ARM64/x86_64), signed with the local Android test key. Release compilation and APK signature verification passed. Device runtime and multi-window behavior remain untested; iOS compilation requires macOS and Xcode. The PDF dependency requires a host C++ compiler even for the Flutter test runner; the standalone document-model tests under `../Engine/dart` run without it and pass.

See the root README for build requirements, current feature coverage, data behavior, and remaining work.


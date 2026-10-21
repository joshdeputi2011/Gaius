# Gaius mobile

One Flutter app for Write, Sheets, Slides, Image, and PDF. Multiple documents stay open as tabs. Android supports additional document tasks; supported iPad environments can request additional native scenes, each with its own Flutter engine.

Run `flutter pub get`, `flutter analyze`, then `flutter run` on a configured Android or iOS device. The native C++ engine and Dart document model are local dependencies under `../Engine`. Fluent UI System Icons and application accent colors match the web and Windows apps.

Documents are stored in the application support directory. Office, OpenDocument, text, images, PDFs and `.gaius` files can be imported locally; DOCX/XLSX/PPTX content exports and CSV/PNG/PDF exports are available. See [FORMATS.md](FORMATS.md) for exact extensions and fidelity limits.

Phones have Home/Create/All tabs navigation. Every top tab has a close button and a long-press menu. Wider windows use a desktop navigation rail and ribbon. PDF commands have Fluent icons and labels, destination choices and compression modes. Android/iOS register file-open handlers; additional windows can request a saved document by ID.

Flutter analysis passes. A universal release APK is available at `../output/gaius-office.apk` (Android 7.0+, ARMv7/ARM64/x86_64), signed with the local Android test key. Release compilation, signature verification, phone/tablet emulator layout, DOCX opening and PDF page extraction were checked. Physical device performance and multi-window behavior need further testing; iOS compilation requires macOS and Xcode. The PDF dependency requires a host C compiler for native tests. Portable document/import tests run under `../Engine/dart`; PDF regression checks are under `../Engine/pdf` and run on the Linux CI host.

See the root README for build requirements, current feature coverage, data behavior, and remaining work.


# Gaius Office

A local-first office workspace with a React web app, Flutter Android/iOS app, native WinUI desktop suite, and the first version of a custom calculation and document-interchange engine.

**Status: functional development preview, not a complete Microsoft Office replacement.** Web builds/tests pass. Android release packaging and signature verification pass; phone/tablet emulator checks include navigation, DOCX import, PDF viewing and page extraction. The WinUI Release build succeeds. Physical devices, iOS compilation and Windows UI behavior still need validation. See the precise limits below.

## Workspace

| Folder | Contents |
| --- | --- |
| `Engine` | C++ calculation engine and C ABI; Dart FFI plugin; TypeScript document/formula/ZIP/OOXML implementation; common schema and Fluent design tokens |
| `Web` | React + TypeScript online editor; browser-only document storage; no cloud service |
| `Mobile` | Flutter all-in-one app; Android and iOS hosts; document tabs; native additional-window hooks |
| `WinUI` | C# / WinUI 3 home launcher and separate document editor windows; local files and native engine binding |
| `MacOS` | Planned SwiftUI/AppKit integration, intentionally not implemented yet |

## Implemented workflows

| Capability | Web | Flutter source | WinUI source |
| --- | --- | --- | --- |
| Home, create/open, rename, recents, delete | Yes | Yes | Yes |
| Local persistence and `.gaius` import/export | IndexedDB | Application support directory | LocalAppData |
| Text editing | Rich text, headings, lists, tables, alignment, color | Quill rich text, headings, lists | Native RichEditBox, basic formatting |
| Spreadsheet | Editable grid, formulas, CSV | Editable grid, native formulas, CSV | Editable grid, native formulas, CSV |
| Presentations | Text slides, reorder, background, present, SVG | Text slides, add/delete, present | Text slides, add/delete, separate presentation window |
| Images | Rotate, percentage crop, brightness, contrast, grayscale, PNG | Rotate, center crop, brightness, contrast, grayscale, PNG | Rotate, brightness, contrast, grayscale, PNG |
| PDF | View, merge, keep/delete/rotate pages, export | PDFium view, merge, compress, extract/delete/rotate/reverse pages, images to PDF, output choices | Windows PDF view, PDFsharp page operations, export |
| Basic DOCX/XLSX/PPTX interchange | Yes, limited | Local content import/export; see Mobile/FORMATS.md | Not implemented |
| Multiple documents | Browser tabs/windows, conflict checks | In-app tabs; Android task and iPad scene launch hooks | Independent windows and processes; conflict checks |

The native-source columns describe implemented source features. The Android release APK is packaged; native feature behavior still needs device validation. Additional-window behavior needs testing on actual Android and iPad devices. iPhones may not support additional OS windows; document tabs remain available.

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

The PDF dependency builds native assets, so even model-only `flutter test` currently requires an appropriate host C++ compiler. Tests are in `Mobile/test/model_test.dart`; they were blocked here by missing Visual C++ tools.

## Build WinUI

Install .NET 8, Visual Studio 2022 or compatible Build Tools with **Desktop development with C++**, Windows SDK, and CMake. Use an x64 developer environment.

```powershell
cmake -S Engine/native -B Engine/native/build -A x64
cmake --build Engine/native/build --config Release
ctest --test-dir Engine/native/build -C Release --output-on-failure
dotnet build WinUI/Gaius.Office.csproj -c Release -p:Platform=x64
```

The `.csproj` copies `Engine/native/build/Release/gaius_engine.dll` when it exists. The engine must match the app's x64 architecture. Missing/incompatible engines display `#ENGINE!`/`#ARCH!` rather than fabricated results. `Engine/build.ps1` and `WinUI/build.ps1` provide equivalent convenience commands where local PowerShell execution policy permits scripts.

The app is unpackaged and self-contained with respect to Windows App SDK. Documents live under `%LOCALAPPDATA%/Gaius/Documents`. Optimistic revisions and file locks prevent silent conflicting saves. Export remains available after a conflict. Platform compilation, installation, accessibility, and multi-monitor behavior need validation.

## Engine scope and compatibility

The original engine code in this repository covers:

- The v1 `.gaius` JSON document format and cross-platform adapters.
- A parsed formula language: arithmetic, comparisons, powers, references, absolute-reference syntax, ranges, SUM, AVERAGE, MIN, MAX, COUNT, IF, ROUND, ABS; circular-reference and error handling.
- A C ABI callable from C# and Dart, plus a TypeScript implementation tested against shared cases.
- CSV interchange, a ZIP reader/writer, and basic Office Open XML import/export for the web.

It is **not** a new full-fidelity Word pagination engine, Excel calculation engine, PowerPoint animation engine, image compositing engine, or PDF content engine. Text editing uses browser editing, Flutter Quill, and Windows RichEdit; PDF processing uses existing libraries. Formula semantics are a defined subset and are not identical to every Excel rule. The native engine reparses JSON per call; large-sheet optimization remains future work.

Basic OOXML support imports document text and some inline formatting, the **first worksheet**, and presentation text. Export produces simple documents, a single-sheet workbook, and text-based slides. Complex tables, fonts/layout, shared/array formulas, charts, embedded media, themes, macros, animation, external links, and advanced metadata are not preserved. The files were tested by reopening them in Gaius, not certified in Microsoft Office. Always retain the original files. Legacy `.doc/.xls/.ppt` files are not supported.

Rich formatting is not yet a universal cross-platform model. Flutter stores optional Quill deltas and Windows optional RTF alongside text/HTML. Editing on another platform can simplify formatting. Plain text, cells/formulas, slide text/background, image settings, and embedded PDFs use the common schema.

PDF operations modify pages; they do not provide arbitrary existing-text editing, OCR, secure redaction, cryptographic signatures, PDF-to-Office reconstruction, or comprehensive compression. Image editing is basic, without layers, selection masks, RAW development, or PSD interchange. Web document layout is a continuous editing surface styled as a page, not true multipage pagination.

## Verification recorded for this iteration

- Web TypeScript/production build passed.
- Nine unit/parity tests passed, including 22 common formula cases executed through the C++ and TypeScript implementations.
- Six Chrome workflow tests passed: edit/save/reload and DOCX round trip; spreadsheet/XLSX; slides/PPTX; PDF page extraction; desktop/narrow layout; and protection against conflicting writes from another window.
- The C++ executable passed 15 direct assertions, including Unicode surrogate handling; the iOS wrapper translation unit also compiled with the available C++ compiler.
- Flutter analysis passed after API/icon fixes. Final checks and any additional results are reported in the task handoff.
- Seventeen standalone Dart model/import tests passed, including modern and legacy Office fixtures, malformed inputs and editable OOXML export round trips.
- Android universal release APK built successfully with API 36, NDK 28.2.13676358, and CMake 3.22.1. APK v2 signature verified; ARMv7, ARM64, and x86_64 native calculation and PDF libraries confirmed. Available at `output/gaius-office.apk`, signed with the local Android test key. Phone/tablet emulator checks passed for navigation, DOCX opening, PDF viewing and one-page extraction; physical-device performance remains untested.
- WinUI Release build and self-contained x64 publish passed with .NET 8.0.426; the Windows x64 engine passed 15 assertions and a DLL/PInvoke smoke check. iOS still requires macOS/Xcode; native Windows UI interaction remains untested.

## Remaining product work

Full Office compatibility, a unified rich document model and layout engine, virtualized large workbooks, multiple worksheets, charts, pivot tables, advanced slide objects/media, full PDF/image toolsets, recovery and conflict UI, file associations, installers, release signing, accessibility, localization, device performance testing, and native QA remain substantial work. This preview establishes the applications and working vertical features; it does not complete that roadmap.


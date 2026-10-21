# Changes

## 1.1.0

- Fixed Windows SDK BuildTools downgrade and WinUI/PDFsharp namespace conflicts.
- Added push-triggered APK and Windows EXE/portable releases with notes and checksums; default-branch GitHub Pages deployment.
- Added Android release-key configuration with an explicit test-signing fallback.
- Redesigned mobile chrome with Fluent icons, soft raised surfaces and restrained translucency.
- Added Home/Create/All tabs navigation, per-tab close buttons and long-press actions.
- Added desktop-style navigation/ribbon layouts for tablet-sized windows.
- Added local Office/OpenDocument import, basic legacy binary content readers, multi-sheet switching and editable DOCX/XLSX/PPTX export. See Mobile/FORMATS.md for conversion limits.
- Added Android/iOS file-open handlers and saved-document IDs for new-window requests.
- Added labeled PDF tools, multiple-file merge, compression modes, images to PDF, page reversal and explicit new-file/replacement destinations.
- Fixed leading-page extraction by assembling a fresh document and verifying the output page count.
- Cached PDF viewer input and debounced image adjustments and saves to reduce unnecessary work.

Development preview. Full Office layout/format fidelity, physical-device performance, iOS compilation and Windows UI testing remain incomplete. The local APK uses a test key; Windows distribution is unsigned. Enable GitHub Actions as the repository's Pages source and configure signing secrets as described in .github/RELEASES.md.

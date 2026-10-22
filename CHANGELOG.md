# Changes

## Windows 1.2.1 ? 22 October 2026

- Fixed Settings navigation and shutdown callbacks accessing destroyed native windows; coordinated dialogs and PDF rendering lifetimes.
- Added per-app templates, filtered recents, native satin decoration and real Office/image PDF conversion and compression tools.
- Added inline document/PDF ink and source-safe vector PDF annotation export.
- Added row/column sizing and AutoFit, nine chart types and source-preserving XLSX interchange.
- Added supported ODT and legacy DOC content import with original-file export and visible compatibility limits; expanded BMP/raster handling.
- Aligned the acrylic ribbon, added filled active tab icons, removed duplicate commands and improved display scaling and installer branding.
- Native assertions, interactive regressions, conversion checks and platform tests are recorded in [OFFICE_UPGRADE.md](OFFICE_UPGRADE.md).

## Office editing and native UI upgrade — 22 October 2026

- Added a 16-shape gallery and editable fill, outlines, dimensions and rotation controls.
- Added floating picture insertion and clipboard images in native Windows documents; improved browser picture wrapping and Flutter block images.
- Added document view controls, navigation, paper sizes, margins, column settings and page borders with supported Office export.
- Added counted phone tab switching and Save/Discard/Cancel for new drafts.
- Added persisted light/dark/system themes, a white-page preference, floating translucent ribbons and Windows acrylic surfaces.
- Expanded Flutter PPTX interchange to preserve supported picture, text and shape objects. Retained native WinUI and Flutter editor surfaces.
- Fixed native publishing to include the XAML resource index and added a smoke check against the published app.
- See [coverage and verification](OFFICE_UPGRADE.md) for platform differences and unfinished suite features.

## 1.2.0

- Added the shared Windows/web editor, bundled locally in WebView2, with the native workspace retained and existing native documents copied into the shared workspace on first import.
- Added tabbed ribbons across all mobile editors, Home selected initially, collapsible commands, and more room for phone editing.
- Added Save as, sharing, in-file search, command search, print preview and native mobile printing.
- Added richer document editing, images, editable table embeds, WordArt formatting, reference helpers, review comments and cross-platform HTML/Quill content.
- Added multiple web worksheets, formatting, filters, sorting, charting, row virtualization, and expanded native/TypeScript formulas with shared parity cases.
- Added slide objects, positioning, WordArt, pictures, shapes, notes, transition/effect playback and matching-object morph.
- Added PDF.js text selection/rendering, text search/extraction, page conversion, annotations, target compression and limited page-flattening text replacement. Mobile PDF tools gained selection/copy/search, text changes, conversion and target sizes.
- Added image transforms, resize, blur, watermarks, conversion and target-size output review.
- Expanded DOCX media/table interchange, XLSX worksheet/style interchange and web PPTX object interchange.
- Switched Windows to the WinUI component dependency to avoid unused machine-learning/runtime packages. Web editors and PDF resources load as separate local bundles.

This remains a development release. See [FEATURES.md](FEATURES.md) for the implemented scope and outstanding suite work, and [VALIDATION.md](VALIDATION.md) for current evidence.

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

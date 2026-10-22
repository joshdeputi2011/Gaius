# Office editing upgrade

Verified locally on 22 October 2026. Windows uses native WinUI 3 controls, Flutter supplies mobile editors, and React supplies the separate browser app. The Windows editor does not create a WebView or bundle/load the React editor. Windows App SDK's distribution still contains its standard optional WebView support libraries. Existing source changes and document stores were retained.

## Available workflows

| Area | WinUI Windows | Flutter mobile | React web |
| --- | --- | --- | --- |
| New files | Unsaved draft; Save/Discard/Cancel on close, including the window close button | Unsaved draft; Save/Discard/Cancel when closing a tab or using Back | Unsaved draft; Save/Discard/Cancel on in-app close; browser confirmation on tab close |
| Tabs | Separate native editor windows | Counted tab icon in the app bar on phones; switch/close in a bottom sheet; strip on wide screens | Existing editor/window navigation |
| Theme | Persisted system/light/dark; desktop acrylic; floating acrylic ribbon | Persisted system/light/dark; floating translucent ribbon | Persisted system/light/dark; floating blurred ribbon |
| White page | Separate preference in Settings; print paper stays white | Separate preference in Settings | Separate preference under File → Appearance; print paper stays white |
| Write views | Ruler, live document thumbnail and paragraph navigation, reading/print/web layout, gridlines, fit/page width and zoom | Ruler, paragraph overview cards, reading mode and text zoom | Horizontal/vertical rulers, page overview and headings, reading/print/web layout, gridlines, fit/page width and zoom |
| Page setup | A4/Letter/Legal, orientation, four margins, border, columns, native column preview and print-column flow | Orientation, paper size, four margins and page border; column count is used for Office export | A4/Letter/Legal, orientation, four margins, 1–4 columns, gap and page border |
| Shapes | 16 shapes in Write and Slides; move, resize, fill and outline controls; rotation and alternative text | 16 shapes; document embeds retain editable geometry and cached PNG; slide objects have fill/outline, numeric/handle resize, rotation and ordering | 16 shapes; editable drawing images in Write and vectors in Slides; fill/outline, resize and slide rotation/order |
| Pictures | Insert and paste as independent floating objects; maintain source ratio initially; drag and lower-right resize; outline/alt text | Paste through the native clipboard bridge; contain-fit block pictures with their own height; width/height, alignment, rotation, outline and alt text | Image-file clipboard paste and file insertion; contain-fit defaults; width/height, alt text and layout controls |
| Wrap | Front-of-text default; left/right paragraph exclusion and top/bottom spacing | Pictures occupy separate blocks; text continues above/below; left/center/right alignment | In line, square-left/right, top/bottom, front/behind; floating tables support left/right and top/bottom |
| Inline ink | Document/PDF pen, highlighter, eraser, color/thickness, undo/redo and editable saved strokes; PDF vector ink export | Existing handwriting insertion | Existing handwriting insertion |
| Spreadsheet sizing/charts | Persistent row/column drag, keyboard, numeric sizing and AutoFit; nine charts with native XLSX export | Existing sheet controls | Existing sheet controls |
| Text and tables | Native font colors/highlights, rich text and basic editable RTF tables | Font color/highlight; table dialog can add rows and columns | Font color/highlight, paragraph/page borders, table row/column operations, shading and borders |

The gallery contains rectangle, rounded rectangle, ellipse, triangle, right triangle, diamond, pentagon, hexagon, star, heart, right arrow, left arrow, double arrow, chevron, callout and line.

Select a drawing to format it. In Windows Write, double-click a drawing or use Insert → Format object. Drag its lower-right corner to resize. On mobile, tap a document drawing to edit its properties. Slide pictures and shapes have resize handles and numeric dimensions.

Save commits a draft to the local workspace. Save as/export creates a portable file. Existing files retain autosave and conflicting-write checks. Mobile operating systems can terminate an app without an interactive close event; unsaved drafts currently remain in memory, so save work before leaving it for an extended period.

Windows start pages now keep the app launcher on Home. Write, Sheets and Slides each show a blank starter and three editable templates, followed by recent files filtered to that app. Write includes report, letter and meeting-note starters; Sheets includes a working budget, expense tracker and project tracker; Slides includes four-slide pitch, lesson and review starters. The small satin decoration uses native XAML curves and the selected app's accent color.

PDF and Image start pages provide file-backed quick tools. Image compression saves a JPEG copy with a chosen size target; PNG, JPEG and BMP conversion preserves the source file. PDF compression opens the selected PDF directly in the compression flow. Word, Excel, PowerPoint and image conversion opens a native print preview with Save PDF. Document, workbook and presentation PDF exports render the reviewed pages as images; workbook pages preserve the supported cell styles, dimensions, worksheet names and charts. These PDFs do not retain selectable text or Office editing structure. PDF-to-PDF saves retain the original PDF objects and add supported ink as vectors.

The Windows installer uses the system's light/dark appearance, original satin artwork, a high-DPI app mark and a readable installation summary. Welcome, Ready and Cancel were exercised without installing over the existing app. The installer remains unsigned.

Windows 1.2.1 fixes Settings category selection, async callbacks after a window closes, competing modal dialogs, and PDF source lifetime during rendering. Preferences save atomically and Windows settings changes share one commit queue. The ribbon uses icon tabs with a filled selected state, aligned command groups and one zoom control; duplicate action aliases were removed. Initial window size accounts for display scaling. The document workspace, home content and status area allow the native backdrop through.

## File interchange

- `.gaius` retains the additional drawing metadata and preferences stored with a document. The schema remains version 1 with optional fields, and existing files remain readable.
- DOCX exports include supported pictures, tables, highlighting, paper size/orientation, margins and column settings. React additionally exports picture anchors/wrap and page borders. Document shapes are raster pictures in DOCX; preserve `.gaius` for editable Gaius shape metadata.
- Native Windows, React and Flutter PPTX exports retain the supported gallery geometry, object positions, fills, outlines and rotation. Flutter now includes picture media relationships and editable text objects instead of dropping slide objects.
- Native XLSX supports persistent column/row dimensions and nine editable charts: column, horizontal bar, line, area, scatter, pie, doughnut, stacked column and stacked bar. Source package parts are retained on supported edits, including unedited fonts, number formats and unsupported embedded parts. This preservation does not make advanced Excel features editable. The native editor is bounded to 10,000 rows and 256 columns; charts use up to 200 rows and 12 series.
- Native ODT supports text, common formatting, tables and supported raster pictures, with editable ODT export. An unchanged imported package can be exported byte for byte. Complex page styles, tracked changes and embedded objects remain outside the supported content model. A visible import notice explains these limits; Save original ODT copy retains the original bytes after edits.
- Native `.doc` imports Word 97?2003 text and recognizes RTF/HTML/DOCX content under a legacy suffix. Binary formatting, fields, headers and pictures are not fully editable. Save original DOC copy preserves its bytes; save edits as DOCX or Gaius.
- PNG, JPEG and BMP files open natively and convert through Windows codecs. Recognized raster bytes under `.img` also open; raw camera and disk image IMG formats are not supported. Document and slide insertions normalize raster pictures to PNG while retaining their proportions.
- Native document/PDF ink stays editable in Gaius; native print preview and PDF exports include it. DOCX/ODT export offers a clear choice because those exports currently omit overlay handwriting.
- Import/export has local round-trip checks. This does not establish full fidelity in Microsoft Office, LibreOffice or every external viewer. Keep source files.
- Print color preferences are separate from document formatting. The white-page setting does not change the saved document's colors.

## Verification

| Check | Result |
| --- | --- |
| React/TypeScript production build | Passed |
| Existing TypeScript unit tests | 13 passed |
| Browser workflow tests | 23 passed, including six new clipboard/drawing/layout/theme/draft checks |
| Flutter analysis | No issues |
| Flutter widget/model tests | 12 passed |
| Shared Dart document/interchange tests | 20 passed |
| Native Windows tests | 150 assertions passed, including legacy DOC, ODT, raster formats, templates, XLSX preservation and actual rendered PDF ink on rotated/cropped pages |
| WinUI published-app checks | Passed: gallery/floating clipboard/view/theme/print; repeated Settings and clean shutdown; inline pen/undo/redo/persistence; row/column resizing and nine chart previews; real Office/image PDF export, raster conversion/compression and PDF compression cancellation |
| Final Windows installer wizard | Welcome, Ready and Cancel passed on the exact 1.2.1 artifact; no installation over the existing app |
| Windows Release build and publish | Passed; includes .NET runtime and the compiled native XAML resource index |
| Android universal Release APK | Built; local test signing |
| Android x64 emulator | Release update installed without clearing app data; counted tabs, Save/Cancel/Discard and editable shape dialog passed |

Repeat the relevant checks with:

```powershell
npm.cmd --prefix Web test
npm.cmd --prefix Web run build
npm.cmd --prefix Web run test:e2e
dotnet run --project WinUI.Tests/Gaius.Office.Tests.csproj -c Release -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File WinUI.Tests/scripts/office-ui.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File WinUI.Tests/scripts/windows-regressions.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File WinUI.Tests/scripts/sheet-ui.ps1
```

From `Mobile`, run `flutter analyze --no-pub`, `flutter test`, and `flutter build apk --release`. From `Engine/dart`, run `dart test`. The Windows interaction checks use isolated app instances, restore previous preferences and remove only their own generated test documents. An installed Gaius instance can remain open.

The WinUI publish target explicitly includes `Gaius.Office.pri` and fails if it is missing. The packaged-app smoke check also verifies that resource file before launch. For a published directory, pass `-AppExecutable <path-to-Gaius.Office.exe>` to the UI script. `Mobile/test_tools/office_smoke.py` exercises the release app on the local Android emulator without clearing its documents.

`WinUI.Tests/scripts/quick-tools-ui.ps1` exercises real native file pickers, Word/Excel/PowerPoint/image PDF exports, PNG/JPEG/BMP conversion, image compression, PDF compression cancellation and clean shutdown in an isolated app instance. Generate its source samples with the native test command followed by `-- --write-samples`. Its independent artifact check uses Python with PyMuPDF and Pillow to inspect PDF page counts/content and image encoding/dimensions. Each run writes to its own folder under `.tools/office-ui/quick-tools`; it does not save imported documents into the user's workspace.

Windows 1.2.1 is packaged separately as `output/gaius-office-windows-fluent-setup.exe` and `output/gaius-office-windows-fluent-portable.zip`; `output/WINDOWS-FLUENT-BUILD.txt` records the final hashes and validation.

Earlier cross-platform test downloads remain in `output`: `gaius-office-native-upgrade-setup.exe`, `gaius-office-android-upgrade.apk` (universal), `gaius-office-android-x64-upgrade.apk` (emulator), and `gaius-office-web-upgrade.zip` (serve over HTTP). `OFFICE-UPGRADE-BUILD.txt` records SHA-256 hashes and test results. The Android source version remains 1.2.0; the universal test APK uses build number 4006, and Flutter adds the ABI offset for the x64 variant.

iOS packaging and physical-device behavior remain unverified in this Windows environment. Android test signing and the unsigned Windows installer are local distribution artifacts; production signing is still required for a public release.

## Remaining suite work

Full office-suite parity remains unfinished. The current document editors do not provide a Word-compatible live pagination engine, arbitrary section/page styles, contour/tight wrapping, linked text frames or complete tracked changes. Windows wrapping is paragraph-based; complex edits around several overlapping objects need a dedicated flow engine. Flutter block images do not float through surrounding paragraphs, and its editing/PDF views do not show newspaper-style columns. The browser page overview is a continuous-document preview rather than a fully paginated thumbnail list.

Native document ink and floating objects use layers attached to the document surface. Portable HTML and basic print/export rendering can place them differently from the editing canvas. Front/behind picture layering and table wrapping still need broader external-format fidelity. Document table merge/split, cell-specific border presets, repeating headers and floating native/mobile tables are unfinished.

Other substantial work includes slide masters/themes and full media timelines; complete formulas, dependency relocation, pivot tables, validation and advanced charts; collaborative editing, version recovery, mail merge and professional proofing; OCR, secure PDF redaction/signatures and full PDF object editing; and image layers/masks/RAW/PSD workflows. Controls for these unsupported operations were not added as placeholders.

## Research used

The implementation was compared with these primary sources. Their inventories are useful references, not a claim that every listed feature is implemented here.

- Microsoft Word: [picture layout and anchoring](https://support.microsoft.com/en-gb/word/wrap-text-and-move-pictures-in-word), [default picture wrapping](https://support.microsoft.com/en-au/word/change-default-text-wrap-setting-for-images), and [dark mode with a white-page preference](https://support.microsoft.com/en-us/word/dark-mode-in-word).
- Microsoft PowerPoint: [picture/text wrapping limitations](https://support.microsoft.com/en-us/powerpoint/wrap-text-around-a-picture-or-other-object-in-powerpoint). Slide objects use placement/layering; a Word-style paragraph flow engine is a separate feature.
- LibreOffice Writer: [wrap modes](https://help.libreoffice.org/latest/en-US/text/swriter/01/05060200.html), [page styles](https://help.libreoffice.org/latest/en-US/text/swriter/guide/pagestyles.html?DbPAR=SHARED&System=WIN), and [columns](https://help.libreoffice.org/latest/en-GB/text/swriter/01/05040500.html).
- Google Docs: [table editing and table text wrapping](https://support.google.com/docs/answer/1696711?co=GENIE.Platform%3DDesktop&hl=en).
- Apple Pages: [object formatting, layering and resizing](https://support.apple.com/en-ae/guide/pages/tanc422178d0/mac) and [document setup](https://support.apple.com/en-us/119887).
- ONLYOFFICE: [shape gallery and formatting](https://helpcenter.onlyoffice.com/docs/userguides/document_editor/InsertAutoshapes.aspx).
- Microsoft Excel: [PivotTables and PivotCharts](https://support.microsoft.com/en-us/excel/overview-of-pivottables-and-pivotcharts), which remain outside the implemented calculation subset.
- Microsoft WinUI: [system backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops), [in-app acrylic](https://learn.microsoft.com/en-us/windows/apps/develop/ui/in-app-acrylic) and [theme support](https://learn.microsoft.com/en-us/windows/apps/develop/ui/theming).
- Inno Setup: [dynamic Windows 11 wizard styling](https://jrsoftware.org/ishelp/topic_setup_wizardstyle.htm), [high-DPI wizard artwork](https://jrsoftware.org/ishelp/topic_setup_wizardimagefile.htm), and [separate dark-mode branding](https://jrsoftware.org/ishelp/topic_setup_wizardsmallimagefiledynamicdark.htm).
